using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Rig.Cli.Live;

// The WIRE between a one-shot `rig` invocation and the resident host that `rig watch` runs — naming,
// discovery, framing, and the two message records. Nothing here knows what a query IS; the verb dispatch
// lives in LiveQueryRunner and the routing decision in LiveRoute.
//
// WHY A NAMED PIPE, and not the 127.0.0.1+token+`.rig/live.json` shape the design note sketched:
//
//   * DISCOVERY IS A PURE FUNCTION. The pipe NAME is derived from the host's working directory, so a client
//     computes the same name from its OWN cwd and "is there a host?" is answered by TRYING TO CONNECT to it
//     (LiveQueryClient; a zero-timeout attempt, 39us when nothing is there).
//     A port file is the opposite: a piece of mutable metadata that must be written, kept fresh, and
//     reconciled against a PID — and every failure mode of it (stale port, dead pid, half-written file,
//     a crashed host that never cleaned up) is a way to answer confidently about the wrong process. The
//     pipe name cannot go stale because it is not stored anywhere; the OS removes the endpoint when the
//     host dies. That deletes a whole class of bug rather than handling it.
//   * NO TOKEN, BECAUSE NO NETWORK. A pipe is not reachable off-box and its ACL is a first-class OS object,
//     so access control is an ACE granting exactly the current user (LiveQueryServer sets it EXPLICITLY
//     rather than inheriting the process default DACL). A loopback socket needs a shared secret precisely
//     because any local process may connect to it; here the kernel does that check before we see a byte.
//     No port allocation, no firewall prompt, no secret at rest.
//
// The transport is BYTE mode with an explicit 4-byte little-endian length prefix, not PipeTransmissionMode
// .Message: message mode is Windows-only (it throws on Unix, where .NET backs named pipes with a Unix
// domain socket), and a length prefix costs four bytes to make the framing identical on both.
internal static class LiveQueryTransport
{
    // Bumped only on an INCOMPATIBLE change to the two records below. A mismatch is declined rather than
    // guessed at: a client and host from different builds must fall back to the store, which is always
    // correct, instead of misreading each other's fields.
    internal const int Protocol = 1;

    internal const string StatusOk = "ok";
    internal const string StatusDeclined = "declined";

    // 256 MB. A rendered `tree --format tsv` on a large entry point is the biggest thing that crosses this
    // wire; the cap exists so a corrupt length prefix allocates nothing rather than an arbitrary array.
    private const int MaxFrameBytes = 256 * 1024 * 1024;

    // Web defaults => camelCase + case-insensitive property matching, so a field renamed in casing only can
    // never silently read as its default on the other side.
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // `rig-live-<16 hex of sha256(normalised working directory)>`.
    //
    // DERIVED, not allocated: the client computes it from its own cwd and connects, so there is no registry,
    // no file, and no negotiation. Truncated to 64 bits because this is a collision-avoidance name, not a
    // security boundary — and a collision is CAUGHT anyway: the host re-checks the client's working
    // directory against its own before it answers (LiveQueryServer), so the worst a collision can do is
    // decline and fall back to the store.
    internal static string PipeNameFor(string workingDirectory)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeDirectory(workingDirectory)));
        return $"rig-live-{Convert.ToHexString(hash)[..16].ToLowerInvariant()}";
    }

    // The one normalisation both sides use, so `C:\Git\x`, `C:/Git/x\` and `c:\git\x` are ONE host.
    // Path.TrimEndingDirectorySeparator is used rather than a hand-rolled TrimEnd because it deliberately
    // leaves a ROOT alone (`C:\` must not become `C:`). Case is folded on Windows only — the filesystems
    // rig runs on elsewhere are case-sensitive, and folding there would merge two genuinely different trees.
    internal static string NormalizeDirectory(string directory)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        return OperatingSystem.IsWindows() ? full.ToLowerInvariant() : full;
    }

    internal static bool SameDirectory(string left, string right) =>
        string.Equals(NormalizeDirectory(left), NormalizeDirectory(right), StringComparison.Ordinal);

    // Is there an ENDPOINT under this name? One filesystem stat: 3.5us for a name that does not exist.
    //
    // *** MEASURED TRAP, and the reason this is NOT the primary discovery mechanism: on Windows, a
    // File.Exists against a LIVE pipe CONSUMES the server's pending accept. *** Proved by experiment — a
    // NamedPipeServerStream with WaitForConnectionAsync outstanding has that task complete the moment this
    // runs, because GetFileAttributesEx on the pipe namespace opens and closes an instance. So probing
    // before connecting would burn one accept per query, and against a host with no listener re-armed it
    // turns "there is a host" into "the connect timed out" — which is exactly the bug this cost the author.
    //
    // So it is used only to CLASSIFY a failed connect (see LiveQueryClient): a name that does not exist is
    // "no host" (cheap, and consumes nothing because there is nothing to consume), a name that does exist is
    // "a host that would not accept", which is a disclosed fallback rather than a silent one.
    internal static bool ServerExists(string pipeName)
    {
        try
        {
            return File.Exists(EndpointPath(pipeName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A probe that cannot answer means "no host we can use" — never an error a query pays for.
            return false;
        }
    }

    // Where the OS exposes the endpoint. Windows: the pipe namespace. Unix: .NET maps a named pipe onto a
    // Unix domain socket at $TMPDIR/CoreFxPipe_<name>, which is an implementation detail of the runtime but
    // a stable one, and the only way to probe existence without a connect attempt.
    internal static string EndpointPath(string pipeName) =>
        OperatingSystem.IsWindows() ? $@"\\.\pipe\{pipeName}" : Path.Combine(Path.GetTempPath(), $"CoreFxPipe_{pipeName}");

    internal static async Task WriteFrameAsync(Stream stream, byte[] payload, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    // Null = the peer went away, or sent a frame we refuse to allocate for. Both are "no usable answer",
    // which every caller turns into a store fallback — never into an exception a user sees.
    internal static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        if (!await ReadExactlyAsync(stream, header, cancellationToken))
        {
            return null;
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > MaxFrameBytes)
        {
            return null;
        }

        var payload = new byte[length];
        return await ReadExactlyAsync(stream, payload, cancellationToken) ? payload : null;
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var chunk = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken);
            if (chunk == 0)
            {
                return false; // EOF mid-frame: the host died or closed on us
            }

            read += chunk;
        }

        return true;
    }
}

// The four verbs a resident host will answer — the ALLOWLIST, named once so the client's request and the
// host's switch cannot drift apart (LiveTransportRoutingTests asserts the switch covers exactly this set).
//
// This is the whole surface `docs/backlog/progress/live-background-index.md` records as live-servable:
// `derive` stays on the store path by decision, `impact` is a pure function of two IMMUTABLE stores by
// definition, and `dead` is disabled repo-wide. A verb outside this set is not "unimplemented" — it is not
// routable, and the client does not try.
internal static class LiveQueryVerbs
{
    internal const string Reaches = "reaches";
    internal const string Path = "path";
    internal const string Callers = "callers";
    internal const string Tree = "tree";

    internal static readonly IReadOnlySet<string> Routable = new HashSet<string>(StringComparer.Ordinal) { Reaches, Path, Callers, Tree };
}

// A query request. NOT an argv: `Verb` is matched against the allowlist above by the HOST, and `Options` is
// the JSON of that verb's own strongly-typed options record — so nothing a client sends can name a command,
// a file, or a process. The host decodes into the type its own switch arm chose, which means an unknown or
// mistyped verb has no code path at all rather than a rejected one.
//
// WorkingDirectory is carried even though the pipe name is derived from it, and that redundancy is the
// point: it is what lets the host REFUSE to answer for a tree that is not the one it is watching (a name
// collision, or a host booted elsewhere). Discovery being a hash means the client cannot know which tree
// answered; stating the tree and having the host check it means it never has to guess.
internal sealed record LiveQueryRequest(int Protocol, string Verb, string WorkingDirectory, string Options);

// The rendered answer, exactly as the command produced it: the two streams kept SEPARATE (stdout stays
// machine-parseable, disclosures stay on stderr) plus the exit code, so `--format tsv`, `--limit` and every
// other rendering flag work with no transport involvement at all.
//
// `Disclosure` is the host's own source+staleness line, computed under the same lock as the fact generation
// that produced the answer. It is a separate field rather than baked into `Err` so the client can place it
// FIRST, ahead of the command's own stderr, without parsing anything.
internal sealed record LiveQueryResponse(int Protocol, string Status, int Exit, string Out, string Err, string Disclosure, string Reason);
