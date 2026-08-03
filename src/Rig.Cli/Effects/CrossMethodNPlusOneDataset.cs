using Rig.Domain.Data;
using Rig.Domain.Functions;

namespace Rig.Cli.Effects;

// `n_plus_1_cross_method` — the PRESENCE instance of the generic effect-correlation deriver:
// presence-join(iteration_fanout, read, fwd-tree <= maxDepth). It answers "is a read reachable at or beneath a
// call that is issued once per element", which the shipped lexical n_plus_1 structurally cannot see (the
// iteration context and the read live in different frames).
//
// STEP 1 OF THREE, and that governs every choice here: this is a DATA-GATHERING INSTRUMENT, not a review
// surface. It emits one row per (anchor, witness) PAIR — the full cross product — with the key token, the
// iterated source, the guards, the depth and the dispatch basis all as COLUMNS, so that the analysis pass can
// cross-tab them and DERIVE the cache/amortization rules from the shapes the codebase actually has. No
// amortization heuristic is encoded, no tier is assigned, no archetype is suppressed: every one of those is a
// decision to be made ON this evidence, not before it. The eventual FINDING grain will be one row per anchor
// (the cross product is ~40x larger), which is why these rows carry their own type string and are NOT hazard
// findings — they must not dilute the calibrated intra-method n_plus_1 or move `rig impact` attribution.
internal static class CrossMethodNPlusOneDataset
{
    // The row type, deliberately DISTINCT from HazardKinds.NPlusOne. Not a member of HazardKinds.All: these are
    // dataset rows, and admitting them to the hazard catalog would put tens of thousands of them into the
    // Hazards view and into every `rig impact` hazard delta.
    internal const string RowType = "n_plus_1_cross_method";

    // TSV column reference (tab-separated, one row per (anchor, witness) pair):
    //   n_plus_1_cross_method
    //     \t anchorFile \t anchorLine \t anchorMethod(CALLER — the human site) \t callee
    //     \t iterationKind \t iterationDetail \t keyToken \t argIndex \t iteratedSource
    //     \t witnessMethod \t witnessFile \t witnessLine \t witnessProvider \t witnessOperation
    //     \t witnessResource \t witnessDepth \t anchorGuards
    //     \t dispatchBasis \t dispatchVia \t dispatchDegree \t recursive
    internal static IReadOnlyList<string> TsvRows(
        IReadOnlyList<FactInvocation> invocations,
        FactGraphData graph,
        IReadOnlyList<DerivedEffect> effects,
        FactObservationRules observationRules,
        FactCrossMethodNPlusOneRule rule
    )
    {
        var fanouts = FactIterationFanoutDeriver.Derive(invocations, observationRules);

        // (file, line, callee) identifies an anchor — the fanout deriver emits at most one event per call site —
        // so the finding can be joined back to the evidence the correlation operator does not carry.
        var anchorOf = new Dictionary<string, FactIterationFanoutDeriver.IterationFanout>(StringComparer.Ordinal);
        foreach (var f in fanouts)
        {
            anchorOf[AnchorId(f.Event.FilePath, f.Event.Line, f.Event.EnclosingSymbolId)] = f;
        }

        var findings = FactCorrelationDeriver.Derive(
            graph: graph,
            // The pseudo-events ride in the SAME event list as the real effects: the operator takes one event
            // stream and tells anchors from companions by predicate, which is the whole reason synthesizing a
            // DerivedEffect (rather than widening the anchor type) buys the reach step for free.
            effects: [.. fanouts.Select(f => f.Event), .. effects],
            spec: new CorrelationSpec(
                Anchor: new EffectPredicate(Provider: FactIterationFanoutDeriver.Provider, Operation: FactIterationFanoutDeriver.Operation),
                // Unused: the presence path matches companions through `Companions` below.
                Companion: new EffectPredicate(Provider: FactIterationFanoutDeriver.Provider),
                AnchorNormalize: new NormalizeSpec(),
                CompanionNormalize: new NormalizeSpec(),
                Polarity: CorrelationPolarity.Presence,
                KeyMatch: CorrelationKeyMatch.PropagatedKeyToken,
                Companions: ReadPredicates(rule),
                ExcludeEnclosingNamespaceSuffix: rule.ExcludeEnclosingNamespaceSuffix,
                MaxDepth: rule.MaxDepth,
                MaxWitnessesPerAnchor: rule.MaxWitnessesPerAnchor
            )
        );

        var rows = new List<string>(findings.Count);
        foreach (var f in findings)
        {
            if (!anchorOf.TryGetValue(AnchorId(f.FilePath, f.Line, f.Method), out var anchor))
            {
                continue; // unreachable in practice: every anchor came from a fanout event
            }

            rows.Add(
                string.Join(
                    "\t",
                    RowType,
                    Clean(f.FilePath),
                    f.Line,
                    Clean(anchor.Caller),
                    Clean(f.Method),
                    Clean(anchor.IterationKind),
                    Clean(anchor.IterationDetail),
                    Clean(anchor.KeyToken),
                    anchor.ArgumentIndex,
                    Clean(anchor.IteratedSource),
                    Clean(f.WitnessMethod),
                    Clean(f.WitnessFilePath),
                    f.WitnessLine,
                    Clean(f.WitnessProvider),
                    Clean(f.WitnessOperation),
                    Clean(f.WitnessResourceKey),
                    f.WitnessDepth,
                    Clean(anchor.Event.EnclosingGuards),
                    Clean(f.WitnessDispatchBasis),
                    Clean(f.WitnessDispatchVia),
                    f.WitnessDispatchDegree,
                    anchor.Recursive ? "1" : "0"
                )
            );
        }

        return rows;
    }

    // The read gate as correlation predicates: the provider x operation cross product, or provider-only (any
    // operation) when the rule names no operations.
    private static IReadOnlyList<EffectPredicate> ReadPredicates(FactCrossMethodNPlusOneRule rule)
    {
        if (rule.ReadOperations.Count == 0)
        {
            return rule.ReadProviders.Select(p => new EffectPredicate(p)).ToList();
        }

        return rule.ReadProviders.SelectMany(p => rule.ReadOperations.Select(o => new EffectPredicate(p, o))).ToList();
    }

    private static string AnchorId(string filePath, int line, string? callee) => $"{filePath}{line}{callee}";

    // A tab or newline inside a mined value (an iteration detail is source text) would split a row, so the
    // emitted dataset normalizes both to a space. Null -> "".
    private static string Clean(string? value) =>
        string.IsNullOrEmpty(value) ? "" : value!.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
