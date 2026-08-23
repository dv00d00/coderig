using System.Globalization;
using System.IO.Pipes;
using System.Text.Json;
using static Rig.Cli.Live.LiveQueryTransport;

namespace Rig.Cli.Live;

// Which source answered, from the CLIENT's point of view. Three states and not two, because "there is no
// resident host" and "there is one and it failed" are different facts and the user is owed different things:
// the first is the ordinary case and must be indistinguishable from rig before this slice existed; the second
// is a surprise and must be said out loud.
internal enum LiveRouteStatus
{
    // No host for this working directory. The store path runs exactly as it always has, silently.
    NoHost,

    // A host was there and could not serve this question — dead pipe, timeout, protocol mismatch, wrong
    // tree, unroutable verb. The store path runs and the reason is DISCLOSED, because the user asked a
    // question expecting live facts and did not get them.
    Failed,

    Served,
}

// A routed answer: the command's own two streams and exit code, plus the host's source/staleness line.
internal sealed record LiveRoutedAnswer(int Exit, string Out, string Err, string Disclosure);

internal sealed record LiveRouteOutcome(LiveRouteStatus Status, LiveRoutedAnswer? Answer, string? Reason)
{
    internal static readonly LiveRouteOutcome NoHost = new(LiveRouteStatus.NoHost, null, null);

    internal static LiveRouteOutcome Failed(string reason) => new(LiveRouteStatus.Failed, null, reason);

    internal static LiveRouteOutcome Served(LiveRoutedAnswer answer) => new(LiveRouteStatus.Served, answer, null);
}

// The client half of the transport: ask the resident host for this directory, or report that we cannot.
//
// EVERY failure path returns an OUTCOME, never an exception. That is the contract that makes routing safe to
// have on by default: a transport fault degrades to the answer rig would have given anyway, and the only
// visible consequence is a line on stderr saying so. Nothing here can fail a query, and nothing here can
// hang one — the deadline covers connect, write and read together.
internal static class LiveQueryClient
{
    // The RETRY budget, spent only once we know an endpoint is there but had no free instance. Deliberately
    // small: waiting longer to reach a busy host is worse than answering from the store, which is what the
    // fallback does anyway. The FIRST attempt uses a zero timeout — see TryConnectAsync.
    private const int RetryConnectTimeoutMilliseconds = 500;

    // The whole-request deadline, and the only number here that is a judgement call rather than a mechanism.
    //
    // 30s, chosen against the MEASURED live cost of the slowest thing this transport carries. On MedDBase
    // (227 projects) a first query in a generation pays ~4.0s of derived layer, and `tree --view hazards`
    // adds the three derive-shaped artifacts at ~12s more; the host WARMS the query set in the background
    // after every edit, so the common case is milliseconds. 30s therefore sits comfortably above the worst
    // real answer and far below anything a user would tolerate as a hang.
    //
    // The deadline is a backstop for a WEDGED host, not a performance knob — a host that DIES is detected by
    // the OS (the read returns EOF and we fall back at once), which is strictly better than any timer. Note
    // which way the failure costs: expiring here means the store then re-does the whole query, so a deadline
    // tuned tight would double the cost of exactly the queries that are already the slowest.
    private static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(30);

    // Test/escape hatch for the deadline. Not documented as a user knob: the only reason to move it is to
    // exercise the wedged-host path without waiting 30s for it.
    private const string DeadlineEnvironmentVariable = "RIG_LIVE_TIMEOUT_MS";

    internal static async Task<LiveRouteOutcome> TryAskAsync(
        string verb,
        object options,
        string workingDirectory,
        TimeSpan? deadline = null,
        CancellationToken cancellationToken = default
    )
    {
        // Client-side allowlist check. Belt to the host's braces: the host validates the verb server-side
        // regardless (that is where it MUST be enforced), but a client that knows a verb is not routable
        // should not spend a connect discovering it.
        if (!LiveQueryVerbs.Routable.Contains(verb))
        {
            return LiveRouteOutcome.NoHost;
        }

        var pipeName = PipeNameFor(workingDirectory);
        var budget = deadline ?? DeadlineFromEnvironment() ?? DefaultDeadline;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(budget);
        try
        {
            await using var pipe = new NamedPipeClientStream(
                serverName: ".",
                pipeName: pipeName,
                direction: PipeDirection.InOut,
                options: PipeOptions.Asynchronous
            );
            if (!await TryConnectAsync(pipe, pipeName, timeout.Token))
            {
                return LiveRouteOutcome.NoHost;
            }

            // The full path, not the normalised form: the host does its own normalising for the comparison,
            // and an unmangled path is what makes its decline message readable.
            var request = new LiveQueryRequest(
                Protocol: Protocol,
                Verb: verb,
                WorkingDirectory: Path.GetFullPath(workingDirectory),
                Options: JsonSerializer.Serialize(options, options.GetType(), Json)
            );
            await WriteFrameAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(request, Json), timeout.Token);

            var frame = await ReadFrameAsync(pipe, timeout.Token);
            if (frame is null)
            {
                return LiveRouteOutcome.Failed("the resident index closed the connection without answering");
            }

            var response = JsonSerializer.Deserialize<LiveQueryResponse>(frame, Json);
            if (response is null)
            {
                return LiveRouteOutcome.Failed("the resident index sent an unreadable response");
            }

            if (response.Protocol != Protocol)
            {
                return LiveRouteOutcome.Failed(
                    $"protocol mismatch (host speaks {response.Protocol}, this rig speaks {Protocol}) — the resident host is from a different build"
                );
            }

            if (!string.Equals(response.Status, StatusOk, StringComparison.Ordinal))
            {
                return LiveRouteOutcome.Failed(string.IsNullOrEmpty(response.Reason) ? "the resident index declined" : response.Reason);
            }

            return LiveRouteOutcome.Served(
                new LiveRoutedAnswer(Exit: response.Exit, Out: response.Out, Err: response.Err, Disclosure: response.Disclosure)
            );
        }
        catch (TimeoutException)
        {
            // The endpoint existed a moment ago but nothing accepted — a host shutting down, or one whose
            // only listener instance is occupied.
            return LiveRouteOutcome.Failed("no resident index accepted the connection");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LiveRouteOutcome.Failed(
                $"the resident index did not answer within {budget.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)}s"
            );
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or ObjectDisposedException
                        or JsonException
                        or NotSupportedException
            )
        {
            // IOException covers the pipe breaking mid-request (the host process died); UnauthorizedAccess
            // covers an endpoint owned by another user, which is exactly what the server's ACL produces.
            return LiveRouteOutcome.Failed(exception.Message);
        }
    }

    // DISCOVERY IS THE CONNECT ATTEMPT ITSELF — two-stage, and the staging is the whole reason routing is
    // free when nobody is watching.
    //
    // Stage one is ConnectAsync(0): it succeeds instantly when a listener instance is free, and fails
    // instantly when there is nothing there (39us measured, including the exception). Note what is NOT done
    // here: an existence PROBE before connecting. On Windows a File.Exists against a live pipe consumes the
    // server's pending accept (measured — see LiveQueryTransport.ServerExists), so probing first would burn
    // one accept per query and could turn "a host is running" into "the connect timed out".
    //
    // Stage two only runs when stage one failed AND an endpoint does exist, i.e. the host was mid-answer for
    // someone else. Then, and only then, is it worth spending real time.
    //
    // Returns false for "no host". A host that exists and still will not accept lets the TimeoutException
    // out, which the caller reports as a DISCLOSED fallback — being unable to reach a running host is a
    // surprise, and silently reading the (stale) store instead is exactly what must not happen quietly.
    private static async Task<bool> TryConnectAsync(NamedPipeClientStream pipe, string pipeName, CancellationToken cancellationToken)
    {
        try
        {
            await pipe.ConnectAsync(0, cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            if (!ServerExists(pipeName))
            {
                return false;
            }
        }

        await pipe.ConnectAsync(RetryConnectTimeoutMilliseconds, cancellationToken);
        return true;
    }

    private static TimeSpan? DeadlineFromEnvironment() =>
        int.TryParse(Environment.GetEnvironmentVariable(DeadlineEnvironmentVariable), CultureInfo.InvariantCulture, out var ms) && ms > 0
            ? TimeSpan.FromMilliseconds(ms)
            : null;
}
