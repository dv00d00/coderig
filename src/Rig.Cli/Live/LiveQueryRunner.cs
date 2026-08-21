using Rig.Cli.CommandLine;
using Rig.Cli.Commands;

namespace Rig.Cli.Live;

// Parses and answers ONE query against ONE generation of live facts, returning the rendered text.
//
// The transport is deliberately trivial (a line of text in, a block of text out) because the interesting part
// is NOT the transport: it is that the answer comes out of the same ReachesCommand / PathCommand /
// CallersCommand body, through the same renderer, as the store-backed CLI. A real one-shot client/host
// protocol is a later slice; keeping this a pure string→string function means that slice can be built without
// unpicking anything here.
//
// Every verb here is the DEFAULT invocation of its command — no flags on the live surface yet. That is what
// makes each answer directly comparable to `rig <verb> …` against a store of the same tree, which is exactly
// what LiveReachesTests and LivePathCallersTests do. Adding a flag means widening the shaping check in
// LiveQueryFactSource.SameShapingAsMemo, not just parsing one more token.
//
// Nothing is written to Console: the caller owns presentation (and TUnit does not surface console output, a
// lesson this program has already paid for). Unrecognized input answers with what IS supported — an obscure
// failure on a mistyped query would make the whole live surface feel broken.
internal static class LiveQueryRunner
{
    // NOTE ON WORD ORDER: verbs are APPENDED to the list, before the `quit` clause, and WatchCommandTests pins
    // this sentence verbatim — so adding one means updating that one string literal too (the assertion's
    // SUBJECT is unchanged: the banner enumerates exactly the verbs that route). Keep doing it that way rather
    // than loosening the assertion to a substring: an inaccurate banner tells a user a feature doesn't exist.
    internal const string Usage =
        "supported live queries: `reaches <pattern>`, `path <from> <to>`, `callers <to>`, `tree <pattern>`; "
        + "`quit` (or EOF) exits.";

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
        // SINGLE-argument verbs take the WHOLE remainder as the pattern (only the surrounding quotes are
        // stripped), because a pattern may legitimately contain spaces — a full DocID signature does
        // (`M:Ns.T.M(System.Int32, System.String)`). Only `path`, which needs two, is tokenized; see PathAsync.
        var remainder = split < 0 ? "" : trimmed[(split + 1)..].Trim();
        var argument = remainder.Trim('"');

        if (string.Equals(verb, "reaches", StringComparison.OrdinalIgnoreCase))
        {
            return argument.Length == 0
                ? Rejected("`reaches` needs an entry-point pattern")
                : await ReachesAsync(argument, facts, workingDirectory);
        }

        if (string.Equals(verb, "callers", StringComparison.OrdinalIgnoreCase))
        {
            return argument.Length == 0 ? Rejected("`callers` needs a target pattern") : await CallersAsync(argument, facts, workingDirectory);
        }

        if (string.Equals(verb, "tree", StringComparison.OrdinalIgnoreCase))
        {
            return argument.Length == 0 ? Rejected("`tree` needs an entry-point pattern") : await TreeAsync(argument, facts, workingDirectory);
        }

        if (string.Equals(verb, "path", StringComparison.OrdinalIgnoreCase))
        {
            // The RAW remainder, not the quote-trimmed `argument`: `path` tokenizes it itself and needs the
            // quotes intact to group a pattern that contains spaces.
            return await PathAsync(remainder, facts, workingDirectory);
        }

        return new LiveAnswer(Exit: 2, Out: $"live: unsupported query '{verb}' — {Usage}{Environment.NewLine}", Err: "");
    }

    // A usage rejection: exit 2, the reason, and what IS supported. Exit 2 (not 1) keeps "you asked wrong"
    // distinguishable from "the answer is no results", the same way the CLI's parse errors are.
    private static LiveAnswer Rejected(string reason) => new LiveAnswer(Exit: 2, Out: $"live: {reason} — {Usage}{Environment.NewLine}", Err: "");

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

    // `callers <to>` — the REVERSE traversal, answered off the resident facts. Same shape as ReachesAsync: the
    // DEFAULT options record (no --orphans/--entrypoints/--async/--raw/--limit/--format on the live surface
    // yet), so the rendering is directly comparable to `rig callers <to>` against a store of the same tree.
    private static async Task<LiveAnswer> CallersAsync(string pattern, LiveFactSource facts, string workingDirectory)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var source = new LiveQueryFactSource(facts);
        var exit = await CallersCommand.RunAsync(
            new CallersCommand.Options(
                ToPattern: pattern,
                RootsOnly: false,
                EntrypointsOnly: false,
                IncludeReverseOnly: false,
                Async: false,
                IncludeDelivery: false,
                Raw: false,
                ExtraRules: [],
                Depth: null,
                Format: null,
                Limit: null,
                Time: false
            ),
            new CommandIo(new TextOutput(Output: output, Error: error), new WorkspaceLocation(WorkingDirectory: workingDirectory)),
            () => Task.FromResult<IQueryFactSource>(source)
        );

        return new LiveAnswer(Exit: exit, Out: output.ToString(), Err: error.ToString());
    }

    // `tree <pattern>` — the call TREE, answered off the resident facts. Same shape as ReachesAsync: the DEFAULT
    // options record, so this is `--view paths` with no filters and no --format, directly comparable to
    // `rig tree <pattern>` against a store of the same tree (LiveTreeTests compares exactly that, and drives the
    // command directly for the view/format matrix the live surface doesn't expose yet).
    //
    // NoCache is FALSE here, i.e. caching ON — which on this path means the fact generation's in-memory memo
    // (LiveQueryFactSource.OpenArtifactCache), never `.rig/cache.db`. Passing NoCache: true would be the wrong
    // "safe" choice: it would recompute the forest for every repeat question, which is precisely the cost a
    // resident host exists to avoid.
    private static async Task<LiveAnswer> TreeAsync(string pattern, LiveFactSource facts, string workingDirectory)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var source = new LiveQueryFactSource(facts);
        var exit = await TreeCommand.RunAsync(
            new TreeCommand.Options(
                FromPattern: pattern,
                View: "paths",
                Async: false,
                IncludeDelivery: false,
                Raw: false,
                Files: false,
                Signatures: false,
                Plain: false,
                Guards: false,
                ExtraRules: [],
                Depth: null,
                Limit: null,
                Only: CommonOptions.FilterSet(null),
                Exclude: CommonOptions.FilterSet(null),
                Intrinsic: false,
                ExcludeNamespaces: CommonOptions.NamespacePrefixes(null),
                NoCache: false,
                Gate: true,
                Amplification: true,
                Time: false,
                Format: null,
                Suppress: null
            ),
            new CommandIo(new TextOutput(Output: output, Error: error), new WorkspaceLocation(WorkingDirectory: workingDirectory)),
            () => Task.FromResult<IQueryFactSource>(source)
        );

        return new LiveAnswer(Exit: exit, Out: output.ToString(), Err: error.ToString());
    }

    // `path <from> <to>` — the only two-argument live verb, so the only one that tokenizes its remainder.
    // Whitespace separates the two patterns; double quotes group, so a DocID-shaped pattern whose signature
    // contains a space survives as ONE argument (`path "M:A.B(System.Int32, System.String)" C.D`). Anything
    // other than exactly two arguments is rejected rather than guessed at — silently pairing the wrong
    // endpoints would answer a question nobody asked.
    private static async Task<LiveAnswer> PathAsync(string remainder, LiveFactSource facts, string workingDirectory)
    {
        var endpoints = SplitArguments(remainder);
        if (endpoints.Count != 2)
        {
            return Rejected("`path` needs exactly two patterns, a from and a to (quote a pattern containing spaces)");
        }

        var output = new StringWriter();
        var error = new StringWriter();
        var source = new LiveQueryFactSource(facts);
        var exit = await PathCommand.RunAsync(
            new PathCommand.Options(
                FromPattern: endpoints[0],
                ToPattern: endpoints[1],
                Async: false,
                IncludeDelivery: false,
                Raw: false,
                ExtraRules: [],
                Depth: null,
                Format: null,
                Time: false
            ),
            new CommandIo(new TextOutput(Output: output, Error: error), new WorkspaceLocation(WorkingDirectory: workingDirectory)),
            () => Task.FromResult<IQueryFactSource>(source)
        );

        return new LiveAnswer(Exit: exit, Out: output.ToString(), Err: error.ToString());
    }

    // Whitespace-separated positional arguments with double-quote grouping. Deliberately minimal (no escapes,
    // no single quotes): this is a query line typed at a terminal, not a shell.
    private static List<string> SplitArguments(string remainder)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var ch in remainder)
        {
            if (ch == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (!quoted && char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            args.Add(current.ToString());
        }

        return args;
    }
}
