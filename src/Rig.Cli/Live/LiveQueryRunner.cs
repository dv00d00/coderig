using Rig.Cli.CommandLine;
using Rig.Cli.Commands;

namespace Rig.Cli.Live;

// Parses and answers ONE query against ONE generation of live facts, returning the rendered text.
//
// The transport is deliberately trivial in this slice (a line of text in, a block of text out) because the
// interesting part is NOT the transport: it is that the answer comes out of the same ReachesCommand body,
// through the same renderer, as the store-backed CLI. A real one-shot client/host protocol is a later slice;
// keeping this a pure string→string function means that slice can be built without unpicking anything here.
//
// Nothing is written to Console: the caller owns presentation (and TUnit does not surface console output, a
// lesson this program has already paid for). Unrecognized input answers with what IS supported — an obscure
// failure on a mistyped query would make the whole live surface feel broken.
internal static class LiveQueryRunner
{
    internal const string Usage = "supported live queries: `reaches <pattern>`; `quit` (or EOF) exits.";

    // One answer, with the command's two streams kept SEPARATE. Splitting them is not fussiness: the CLI puts
    // the answer on stdout and disclosures (ambiguity, seed notes) on stderr, and a test that claims live
    // output equals store output has to compare like with like — a concatenated blob would let a disclosure
    // moving between streams pass unnoticed. `Text` is the single-string view a text transport wants.
    internal sealed record LiveAnswer(int Exit, string Out, string Err)
    {
        public string Text => Err.Length == 0 ? Out : Out + Err;
    }

    internal static async Task<LiveAnswer> AnswerAsync(string query, LiveFactSource facts, string workingDirectory)
    {
        var trimmed = (query ?? "").Trim();
        if (trimmed.Length == 0)
        {
            return new LiveAnswer(Exit: 2, Out: $"live: empty query — {Usage}{Environment.NewLine}", Err: "");
        }

        var split = trimmed.IndexOf(' ', StringComparison.Ordinal);
        var verb = split < 0 ? trimmed : trimmed[..split];
        var argument = split < 0 ? "" : trimmed[(split + 1)..].Trim().Trim('"');

        if (!string.Equals(verb, "reaches", StringComparison.OrdinalIgnoreCase))
        {
            return new LiveAnswer(Exit: 2, Out: $"live: unsupported query '{verb}' — {Usage}{Environment.NewLine}", Err: "");
        }

        if (argument.Length == 0)
        {
            return new LiveAnswer(Exit: 2, Out: $"live: `reaches` needs an entry-point pattern — {Usage}{Environment.NewLine}", Err: "");
        }

        return await ReachesAsync(argument, facts, workingDirectory);
    }

    // `reaches <pattern>`, answered off the resident facts. The Options record is the DEFAULT one the CLI
    // builds when only the positional pattern is given — no --async/--raw/--depth/--limit/--format on the live
    // surface yet, so the output is the default human rendering and is directly comparable to
    // `rig reaches <pattern>` against a store of the same tree (which is exactly what LiveReachesTests does).
    private static async Task<LiveAnswer> ReachesAsync(string pattern, LiveFactSource facts, string workingDirectory)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var source = new LiveQueryFactSource(facts);
        var exit = await ReachesCommand.RunAsync(
            new ReachesCommand.Options(
                FromPattern: pattern,
                Async: false,
                IncludeDelivery: false,
                Raw: false,
                ExtraRules: [],
                Depth: null,
                Format: null,
                Only: CommonOptions.FilterSet(null),
                Exclude: CommonOptions.FilterSet(null),
                Intrinsic: false,
                Limit: null,
                Time: false
            ),
            new CommandIo(new TextOutput(Output: output, Error: error), new WorkspaceLocation(WorkingDirectory: workingDirectory)),
            // The live source is owned by the HOST (it is one fact generation, shared by every query against
            // it), so the adapter's DisposeAsync is a no-op and the command's `await using` cannot close
            // anything out from under the next query.
            () => Task.FromResult<IQueryFactSource>(source)
        );

        return new LiveAnswer(Exit: exit, Out: output.ToString(), Err: error.ToString());
    }
}
