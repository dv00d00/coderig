using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using static Rig.Cli.Live.LiveQueryTransport;

namespace Rig.Cli.Live;

// What the host produced for one request: the command's two streams and exit code plus the source/staleness
// disclosure — or a DECLINE, which is the host refusing to answer at all: an unroutable verb, undecodable
// options, a demand projection that failed during execution, or a PREPARATION that could not establish
// exactness (failed refinement, watcher overflow, topology change, repeated supersession). A decline is not
// an error answer: it carries no rendered output, because the client must fall back to the store rather than
// print an empty result.
internal sealed record LiveServeResult(int Exit, string Out, string Err, string Disclosure, string? DeclineReason)
{
    internal static LiveServeResult Answered(int exit, string standardOut, string standardError, string disclosure) =>
        new(exit, standardOut, standardError, disclosure, DeclineReason: null);

    internal static LiveServeResult Declined(string reason) => new(Exit: 2, Out: "", Err: "", Disclosure: "", DeclineReason: reason);
}

// The host half of the transport: a named-pipe listener whose NAME is derived from the working directory it
// serves, so a one-shot `rig reaches` in that directory finds it without any published metadata.
//
// Started by `rig watch` (the resident host) and by nothing else. Deliberately NOT started by
// WatchHost.StartAsync: a test — and there are many — that boots a resident host must not thereby publish an
// endpoint that a concurrently-running CLI test could route to. Publishing is a decision of the long-lived
// COMMAND, not a property of holding live facts.
//
// THREE THINGS THIS CLASS ENFORCES, all of them server-side because a client's word is not evidence:
//
//   1. THE ACL. Set EXPLICITLY to the current user only, rather than inheriting the process default DACL.
//      The default is usually equivalent, but "usually" is not an access-control statement, and the whole
//      no-token argument for a pipe rests on the kernel checking access before we read a byte.
//   2. THE PROTOCOL VERSION. A mismatch is declined, never guessed at — a client from another build must
//      fall back to the store, which is always a correct answer, rather than misread our fields.
//   3. THE WORKING DIRECTORY. The pipe name is a 64-bit hash, so a collision is possible in principle and a
//      host booted in the wrong place is possible in practice. Answering a client about a DIFFERENT tree
//      than it asked about is the worst failure this transport could have, so the tree is re-checked here,
//      against the host's own directory, before any query runs.
internal sealed class LiveQueryServer : IAsyncDisposable
{
    // Small. Queries are handled one at a time (see the accept loop); the extra instances exist so a
    // listener is always ARMED while one is being served, which is what removes the accept gap a client
    // would otherwise fall back to the store through.
    private const int MaxServerInstances = 4;

    private readonly string _workingDirectory;
    private readonly Func<LiveQueryRequest, CancellationToken, Task<LiveServeResult>> _serve;
    private readonly TextWriter _log;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource _armed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _loop;

    private LiveQueryServer(
        string pipeName,
        string workingDirectory,
        Func<LiveQueryRequest, CancellationToken, Task<LiveServeResult>> serve,
        TextWriter log,
        NamedPipeServerStream first
    )
    {
        PipeName = pipeName;
        _workingDirectory = workingDirectory;
        _serve = serve;
        _log = log;
        _loop = Task.Run(() => AcceptLoopAsync(first, _shutdown.Token));
    }

    internal string PipeName { get; }

    // The first listener instance is created SYNCHRONOUSLY here, before Start returns, so on Windows the
    // endpoint is discoverable the moment the caller has the object — no readiness handshake, and no window
    // in which `rig watch` has printed its banner but a query would still miss.
    //
    // `pipeName` is an override for tests only: it is the one way to construct the pipe-name COLLISION that
    // the working-directory guard exists for (a host serving directory A under the name derived from B).
    internal static LiveQueryServer Start(
        string workingDirectory,
        Func<LiveQueryRequest, CancellationToken, Task<LiveServeResult>> serve,
        TextWriter log,
        string? pipeName = null
    )
    {
        var name = pipeName ?? PipeNameFor(workingDirectory);
        if (ServerExists(name))
        {
            // Not fatal and not silent: two hosts on one directory means whichever listener accepts answers,
            // and if their rule sets differ so will their answers. Say so rather than let it be a mystery.
            // (This probe consumes one pending accept from the OTHER host — see ServerExists — which is
            // harmless because that host re-arms, and it happens once at boot.)
            log.WriteLine(
                $"live: an endpoint for this directory already exists ({EndpointPath(name)}) — another `rig watch` may already be serving it."
            );
        }

        return new LiveQueryServer(name, workingDirectory, serve, log, CreateServerStream(name));
    }

    // Start the endpoint, or DON'T — and either way keep watching. The transport is an addition to the
    // resident loop, not a precondition for it: if the pipe cannot be created (an exhausted name, a security
    // policy, a platform that refuses), the right outcome is a host that still maintains live facts and still
    // answers its own stdin, with the loss stated. Failing `rig watch` outright over a query channel would
    // trade the whole feature for one of its conveniences.
    internal static LiveQueryServer? TryStart(
        string workingDirectory,
        Func<LiveQueryRequest, CancellationToken, Task<LiveServeResult>> serve,
        TextWriter log,
        string? pipeName = null
    )
    {
        try
        {
            return Start(workingDirectory, serve, log, pipeName);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException)
        {
            log.WriteLine(
                $"live: could NOT publish a query endpoint for this directory ({exception.Message}) — one-shot `rig reaches/path/callers/tree` "
                    + "will read the .rig store instead of these live facts. Watching continues."
            );
            return null;
        }
    }

    // Wait until this server can actually be connected to. Used by callers that must not race the accept
    // loop — tests, mainly, since `rig watch` simply prints its banner and carries on.
    //
    // The signal is the loop announcing it is about to accept, NOT an existence probe: File.Exists against a
    // live pipe CONSUMES the pending accept on Windows (see LiveQueryTransport.ServerExists), so a readiness
    // check built on it would disturb the very thing it is checking. On Unix the socket is bound inside
    // WaitForConnectionAsync, so there the probe is still needed — and harmless, because it is a plain stat
    // of a socket file rather than an open of a pipe instance.
    internal async Task<bool> WaitUntilReadyAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        if (await Task.WhenAny(_armed.Task, Task.Delay(timeout, cancellationToken)) != _armed.Task)
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        while (DateTime.UtcNow < deadline && !ServerExists(PipeName))
        {
            await Task.Delay(20, cancellationToken);
        }

        return ServerExists(PipeName);
    }

    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException) { }

        _shutdown.Dispose();
    }

    private static NamedPipeServerStream CreateServerStream(string pipeName)
    {
        if (!OperatingSystem.IsWindows())
        {
            // Unix: .NET backs this with a Unix domain socket created under the user's own TMPDIR. There is
            // no PipeSecurity to set (the type is Windows-only) — the filesystem is the access control.
            return new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                MaxServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous
            );
        }

        return CreateWindowsServerStream(pipeName);
    }

    // The ACL: ONE allow ACE, for the SID that owns this process, and nothing else — not SYSTEM, not
    // Administrators, not Everyone. A pipe carries a request that the host executes against the live index,
    // so the set of principals that may send one is exactly the set that could have run `rig` themselves.
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static NamedPipeServerStream CreateWindowsServerStream(string pipeName)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user =
            identity.User
            ?? throw new InvalidOperationException("The current Windows identity has no user SID; cannot secure the live query pipe.");
        var security = new PipeSecurity();
        security.SetOwner(user);
        security.AddAccessRule(
            new PipeAccessRule(user, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow)
        );
        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            MaxServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: security
        );
    }

    // One connection at a time, but never a moment without a LISTENER: the next instance is armed before the
    // current connection is handled. The alternative (dispose, then create) leaves a window in which a client
    // finds the endpoint, fails to connect, and silently falls back to the STORE — i.e. answers from stale
    // facts because the host happened to be busy. That is the exact failure mode this whole program exists to
    // remove, so the four bytes of an extra instance are worth it.
    //
    // Handling is SEQUENTIAL by choice: the resident index hands out an immutable per-generation fact source,
    // so concurrent answers would be safe, but a single-user dev tool asking one question at a time does not
    // need the second failure surface. A client that arrives mid-answer waits on the armed listener and is
    // covered by its own deadline.
    private async Task AcceptLoopAsync(NamedPipeServerStream first, CancellationToken cancellationToken)
    {
        NamedPipeServerStream? listener = first;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                listener ??= CreateServerStream(PipeName);
                _armed.TrySetResult();
                try
                {
                    await listener.WaitForConnectionAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException)
                {
                    // A client that connected and vanished, or a broken instance: drop it and re-arm.
                    await listener.DisposeAsync();
                    listener = null;
                    continue;
                }

                var connected = listener;
                listener = TryCreateServerStream();
                try
                {
                    await HandleAsync(connected, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // A malformed request must never take the host down — it is a background process someone
                    // is depending on. Log and keep listening.
                    _log.WriteLine($"live: query transport error: {exception.Message}");
                }
                finally
                {
                    await connected.DisposeAsync();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            // The loop itself failed (a listener that cannot be re-created, for instance). The endpoint is
            // gone from here on, so every client falls back to the store — which is correct, and is exactly
            // what the client's own failure handling produces. What must NOT happen is this escaping into
            // DisposeAsync and taking `rig watch` down with it: the resident index is still good, and losing
            // the query channel is not a reason to lose the facts.
            _log.WriteLine(
                $"live: the query endpoint STOPPED ({exception.Message}) — one-shot queries will read the .rig store from now on. Watching continues."
            );
        }
        finally
        {
            if (listener is not null)
            {
                await listener.DisposeAsync();
            }
        }
    }

    private NamedPipeServerStream? TryCreateServerStream()
    {
        try
        {
            return CreateServerStream(PipeName);
        }
        catch (IOException)
        {
            // All instances busy — re-armed on the next loop iteration instead.
            return null;
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        var frame = await ReadFrameAsync(pipe, cancellationToken);
        if (frame is null)
        {
            return; // client gave up before sending a whole request; nothing to answer
        }

        LiveQueryRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<LiveQueryRequest>(frame, Json);
        }
        catch (JsonException exception)
        {
            await RespondAsync(pipe, LiveServeResult.Declined($"unreadable request ({exception.Message})"), cancellationToken);
            return;
        }

        if (request is null)
        {
            await RespondAsync(pipe, LiveServeResult.Declined("empty request"), cancellationToken);
            return;
        }

        if (request.Protocol != Protocol)
        {
            await RespondAsync(
                pipe,
                LiveServeResult.Declined($"protocol mismatch (client speaks {request.Protocol}, this host speaks {Protocol})"),
                cancellationToken
            );
            return;
        }

        // THE WRONG-TREE GUARD. Checked before the verb, because a request about another directory must not
        // reach the query layer at all — not even to be rejected there.
        if (!SameDirectory(request.WorkingDirectory, _workingDirectory))
        {
            await RespondAsync(
                pipe,
                LiveServeResult.Declined($"this resident index is watching '{_workingDirectory}', not '{request.WorkingDirectory}'"),
                cancellationToken
            );
            return;
        }

        await RespondAsync(pipe, await _serve(request, cancellationToken), cancellationToken);
    }

    private static async Task RespondAsync(NamedPipeServerStream pipe, LiveServeResult result, CancellationToken cancellationToken)
    {
        var response = new LiveQueryResponse(
            Protocol: Protocol,
            Status: result.DeclineReason is null ? StatusOk : StatusDeclined,
            Exit: result.Exit,
            Out: result.Out,
            Err: result.Err,
            Disclosure: result.Disclosure,
            Reason: result.DeclineReason ?? ""
        );
        await WriteFrameAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(response, Json), cancellationToken);
    }
}
