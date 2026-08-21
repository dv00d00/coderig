using Rig.Domain.Functions;
using Rig.Storage.Queries;
using Rig.Storage.Storage;

namespace Rig.Cli.CommandLine;

// Correctness-of-disclosure for the SEED of a traversal, the sibling of AmbiguityNotice. A pattern argument
// has THREE distinguishable outcomes and the user must be able to tell them apart:
//
//   1. NO MATCH        — the pattern named nothing in the store/graph. `No symbol matches '<pattern>'.` + exit 1
//                        (the wording and exit code TreeCommand already uses; `reaches`/`path` passed the raw
//                        pattern straight into the traversal, so this surfaced as an EMPTY RESULT instead —
//                        "Reachable methods: 0", indistinguishable from outcome 2, which cost a session of
//                        misdiagnosis on 2026-07-27).
//   2. MATCHED, ZERO OUT-EDGES — a genuine leaf. Zero reach is the CORRECT ANSWER, so it stays exit 0 and gets
//                        a stderr note naming what the pattern resolved to, so the zero reads as an answer
//                        rather than a resolution failure.
//   3. AMBIGUOUS       — AmbiguityNotice's job, untouched.
//
// Disclosure only: no traversal semantics change here, and stdout in machine formats (tsv/llm) stays clean —
// outcome 2's note goes to stderr, exactly like AmbiguityNotice's.
internal static class SeedResolutionNotice
{
    // How many hits the no-node probe looks at AND lists. Internal, not private, because the LIVE fact source
    // runs the same probe in memory and must truncate identically — the "is there a non-node hit" decision is
    // made over the truncated list, so a different cap would give a different disclosure.
    internal const int MaxListed = 5;

    // Outcome 1. `endpoint` names WHICH pattern failed when a command has more than one seed (`path`'s
    // from/to, which may even be the same text); omitted, the message is byte-identical to TreeCommand's.
    // Written to STDOUT (where tree writes it) — it is the command's answer, not a side remark.
    internal static void ReportNoMatch(TextWriter output, string pattern, string? endpoint = null) =>
        output.WriteLine($"No symbol matches '{pattern}'{(endpoint is null ? "" : $" (the '{endpoint}' endpoint)")}.");

    // Outcome 2. Fires only when the seed DID resolve (reachable is non-empty) and every reachable node is the
    // seed itself at depth 0 — i.e. the traversal found no out-edges to walk. Suppressed under `--depth 0`,
    // where a depth-0-only result is the bound the user asked for, not a property of the symbol.
    internal static void NoteIfNoOutEdges(TextWriter error, IReadOnlyDictionary<string, FactPathFinder.ReachInfo> reachable, int maxDepth)
    {
        if (maxDepth <= 0 || reachable.Count == 0 || reachable.Values.Any(r => r.Depth > 0))
        {
            return;
        }

        var seeds = reachable.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        var listed = string.Join(", ", seeds.Take(MaxListed));
        var more = seeds.Count > MaxListed ? $", +{seeds.Count - MaxListed} more" : "";
        error.WriteLine($"note: resolved to {listed}{more}; it makes no in-solution calls (0 call edges).");
    }

    // Outcome 1, REFINED: "named nothing" vs "named something that CANNOT BE A CALL-GRAPH NODE".
    //
    // Call-graph nodes are methods, bodied accessors, lambdas and ctors — all `M:`/synthetic ids. Properties
    // (`P:`), fields (`F:`) and events (`E:`) are NEVER nodes (only their bodied accessors are; see the
    // effect/reachability invariant in CLAUDE.md). So `reaches "PerformanceLogger.Factory"` names a REAL
    // indexed property and still matches no node — and reporting the bare "No symbol matches" for it is
    // misleading in the same way the old empty result was: it says "no such thing" when the truth is "that
    // kind of thing is not traversable; use its accessor". Verified on the MedDBase store:
    // `PerformanceLogger.Factory` is `P:` (no nodes) while `PerformanceLogger.get_Factory` reaches 16.
    //
    // One store-wide probe, only on the already-failed path, so the success path pays nothing.
    internal static async Task ReportNoNodeMatchAsync(TextWriter output, RigDbContext context, string pattern, string? endpoint = null)
    {
        var hits = await Reads.SearchSymbolsAsync(context, pattern: pattern, kind: null, limit: MaxListed);
        ReportNoNodeMatch(output, hits.Select(h => (h.SymbolId, h.Kind)).ToList(), pattern, endpoint);
    }

    // The MESSAGE half of outcome 1, split from the store probe so the LIVE fact source (which probes the
    // in-memory symbol facts instead of the store) emits byte-identical text. `hits` must already be truncated
    // to MaxListed by the probe — the non-node decision is deliberately made over the truncated list, on both
    // paths. Kept as a plain (SymbolId, Kind) pair list so it carries no storage type into the live path.
    internal static void ReportNoNodeMatch(
        TextWriter output,
        IReadOnlyList<(string SymbolId, string Kind)> hits,
        string pattern,
        string? endpoint = null
    )
    {
        var nonNode = hits.Where(h => !h.SymbolId.StartsWith("M:", StringComparison.Ordinal)).ToList();
        if (hits.Count == 0 || nonNode.Count == 0)
        {
            ReportNoMatch(output, pattern, endpoint); // genuinely nothing by that name (outcome 1 proper)
            return;
        }

        var listed = string.Join(", ", nonNode.Take(MaxListed).Select(h => $"{h.Kind} {h.SymbolId}"));
        var where = endpoint is null ? "" : $" (the '{endpoint}' endpoint)";
        output.WriteLine(
            $"'{pattern}'{where} is indexed but is not a call-graph node: {listed}."
                + " Properties, fields and events are never nodes — only methods, bodied accessors, lambdas and ctors are."
                + " Try the accessor (e.g. `get_X`/`set_X`) or a method."
        );
    }

    // Does the pattern name ANY indexed symbol, anywhere in the store? Needed for `path`'s TO endpoint: the
    // graph `path` walks is the FROM node's forward slice, so a `to` that exists but is simply UNREACHABLE is
    // absent from it — deciding no-match off that graph would report "No symbol matches" for a symbol that
    // does exist. This is the same store-wide search `rig symbols`/`rig show` resolve against, and it is only
    // consulted on the negative path (no path found), so it costs nothing in the normal case. Deliberately
    // conservative: ANY hit (method, field, type) counts as "exists", so the no-match claim is only made when
    // the store genuinely has nothing by that name.
    internal static async Task<bool> ExistsInStoreAsync(RigDbContext context, string pattern) =>
        (await Reads.SearchSymbolsAsync(context, pattern: pattern, kind: null, limit: 1)).Count > 0;
}
