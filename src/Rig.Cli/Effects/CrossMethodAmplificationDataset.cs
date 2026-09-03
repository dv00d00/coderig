using Rig.Domain.Data;
using Rig.Domain.Functions;

namespace Rig.Cli.Effects;

// `cross_method_amplification` — the PRESENCE instance of the generic effect-correlation deriver:
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
internal static class CrossMethodAmplificationDataset
{
    // The row type — TIER 3 of the findings catalog (HazardKinds.CrossMethodAmplification), still deliberately
    // DISTINCT from HazardKinds.NPlusOne and NOT a member of HazardKinds.All: the tsv dataset stays at
    // (anchor x witness) grain, the DISPLAYED finding is the AnchorFinding grain, and neither may dilute the
    // calibrated intra-method hazards or move `rig impact`'s hazard deltas.
    internal const string RowType = HazardKinds.CrossMethodAmplification;

    // TSV column reference (tab-separated, one row per (anchor, witness) pair):
    //   cross_method_amplification
    //     \t anchorFile \t anchorLine \t anchorMethod(CALLER — the human site) \t callee
    //     \t iterationKind \t iterationDetail \t keyToken \t argIndex \t iteratedSource
    //     \t witnessMethod \t witnessFile \t witnessLine \t witnessProvider \t witnessOperation
    //     \t witnessResource \t witnessDepth \t anchorGuards
    //     \t dispatchBasis \t dispatchVia \t dispatchDegree \t recursive
    //     \t keyPath \t elementType
    // keyPath and elementType are APPENDED rather than slotted beside keyToken so the 1..22 positions of the
    // first dataset stay valid — the two runs are meant to be comparable column-for-column.
    // One DISPLAYED finding per anchor CALL SITE — the grain a human reviews (see
    // docs/backlog/todo/amplification-context-propagation.md). Dedupes the (anchor x witness) rows twice over:
    // the witness cap comes from the rule's MaxWitnessesPerAnchor, and CHA fan-out (one row per candidate
    // callee of the SAME call site) collapses here to the nearest-depth representative, because graph
    // multiplicity is plumbing, not findings. Confidence is DEPTH-tiered: a depth-0/1 witness (the read is in
    // the callee's own body or one hop down) is a far stronger claim under path-insensitive reach than a
    // depth-5 one — this exact surface calibrated 2026-08-04 at 93% TP+TP-weak on a stratified hand audit.
    internal sealed record AnchorFinding(
        string Caller,
        string FilePath,
        int Line,
        string IterationKind,
        string WitnessProvider,
        string WitnessOperation,
        string WitnessResource,
        int WitnessDepth,
        // The three evidence columns the (anchor x witness) dataset always carried and the displayed grain
        // used to drop (TSV columns 18, 19, 21). Without them every anchor renders under one flat hedge, so a
        // depth-0 unconditional call reached through real calls reads exactly like a depth-5 witness found
        // only through a name-guessed virtual hop. Guards are the ANCHOR call site's control-dependence set
        // (null/"" == the call is unconditional in its loop); DispatchBasis is the reach provenance
        // (null == no dispatch hop at all, "roslyn" == exact mined hops, "heuristic" == at least one guess);
        // DispatchDegree is >1 only when one source method fanned out to N targets on the shortest path.
        string? Guards,
        string? DispatchBasis,
        int DispatchDegree
    )
    {
        public string Confidence =>
            WitnessDepth <= 1 ? "high"
            : WitnessDepth <= 4 ? "medium"
            : "low";

        // How much of the claim the STATIC evidence actually supports — a strict refinement of Confidence,
        // not a second independent axis: "direct" implies Confidence is "high", and the two can never
        // disagree about which anchors are the strong ones. Confidence stays depth-only because that is what
        // the 2026-08-04 hand audit calibrated (93% TP+TP-weak); this adds the two doubts that audit could
        // not see from depth alone.
        //
        //   direct    — the call is unconditional inside its loop, the witness is in the callee's own body or
        //               one hop down, and the reach crossed no dispatch inference. Per-iteration ISSUANCE of
        //               the call is established. Still NOT a query count: N is data-dependent and nothing
        //               here says the witness effect is unconditional in ITS own frame (the correlation
        //               carries the anchor's guards, never the witness's).
        //   inferred  — a name/arity-guessed dispatch hop, or a fan-out to N>1 targets, sits on the path, so
        //               the witness may live in an implementation this anchor never reaches.
        //   candidate — everything else: reachable and looped, with more frames between than direct allows.
        public string Evidence =>
            DispatchBasis == "heuristic" || DispatchDegree > 1 ? "inferred"
            : WitnessDepth <= 1 && DispatchBasis is null && string.IsNullOrEmpty(Guards) ? "direct"
            : "candidate";
    }

    internal static IReadOnlyList<AnchorFinding> AnchorFindings(
        IReadOnlyList<(CorrelationFinding Finding, FactIterationFanoutDeriver.IterationFanout Anchor)> pairs
    ) =>
        pairs
            .GroupBy(p => (p.Finding.FilePath, p.Finding.Line))
            .Select(g => g.OrderBy(p => p.Finding.WitnessDepth).ThenBy(p => p.Finding.Method, StringComparer.Ordinal).First())
            .Select(p => new AnchorFinding(
                Caller: p.Anchor.Caller,
                FilePath: p.Finding.FilePath,
                Line: p.Finding.Line,
                IterationKind: p.Anchor.IterationKind,
                WitnessProvider: p.Finding.WitnessProvider ?? "",
                WitnessOperation: p.Finding.WitnessOperation ?? "",
                WitnessResource: p.Finding.WitnessResourceKey ?? "",
                // Always populated on presence findings; 0 (the strongest tier) only as a defensive fallback.
                WitnessDepth: p.Finding.WitnessDepth ?? 0,
                Guards: p.Anchor.Event.EnclosingGuards,
                DispatchBasis: p.Finding.WitnessDispatchBasis,
                DispatchDegree: p.Finding.WitnessDispatchDegree ?? 0
            ))
            .OrderBy(f => f.FilePath, StringComparer.Ordinal)
            .ThenBy(f => f.Line)
            .ToList();

    // The correlation run, ONCE — TsvRows and AnchorFindings are both pure projections of these pairs, so a
    // caller that wants both (derive) never pays the reach join twice.
    internal static IReadOnlyList<(CorrelationFinding Finding, FactIterationFanoutDeriver.IterationFanout Anchor)> Pairs(
        IReadOnlyList<FactInvocation> invocations,
        FactGraphData graph,
        IReadOnlyList<DerivedEffect> effects,
        FactObservationRules observationRules,
        FactCrossMethodAmplificationRule rule
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
                Companions: WitnessPredicates(rule, effects),
                ExcludeEnclosingNamespaceSuffix: rule.ExcludeEnclosingNamespaceSuffix,
                MaxDepth: rule.MaxDepth,
                MaxWitnessesPerAnchor: rule.MaxWitnessesPerAnchor
            )
        );

        var pairs = new List<(CorrelationFinding, FactIterationFanoutDeriver.IterationFanout)>(findings.Count);
        foreach (var f in findings)
        {
            if (!anchorOf.TryGetValue(AnchorId(f.FilePath, f.Line, f.Method), out var anchor))
            {
                continue; // unreachable in practice: every anchor came from a fanout event
            }

            pairs.Add((f, anchor));
        }

        return pairs;
    }

    internal static IReadOnlyList<string> TsvRows(
        IReadOnlyList<(CorrelationFinding Finding, FactIterationFanoutDeriver.IterationFanout Anchor)> pairs
    )
    {
        var rows = new List<string>(pairs.Count);
        foreach (var (f, anchor) in pairs)
        {
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
                    anchor.Recursive ? "1" : "0",
                    // The two discriminator columns: WHICH per-element value crosses the boundary (the member
                    // path, not the bare loop variable), and WHAT is being iterated (the resolved element type).
                    // Both exist so the self-keyed-vs-foreign-keyed question is measurable — the lexical test
                    // reads the path, the semantic test reads the element type, and neither was expressible
                    // from the bare token alone.
                    Clean(anchor.KeyPath),
                    Clean(anchor.ElementType)
                )
            );
        }

        return rows;
    }

    // The witness gate as correlation predicates. Each witness group expands to its provider x operation
    // cross product (empty operations = any operation of that provider). An EMPTY witness list is the all-IO
    // mode: one provider-only predicate per DISTINCT provider present in the effect stream, minus the rule's
    // exclusions — discovered from the data rather than hardcoded, so a new effect rule is in scope the day
    // it ships. A group with operations but NO providers expands across the same discovered provider set.
    // The iteration-fanout pseudo-provider is always excluded: anchors must never witness each other.
    private static IReadOnlyList<EffectPredicate> WitnessPredicates(
        FactCrossMethodAmplificationRule rule,
        IReadOnlyList<DerivedEffect> effects
    )
    {
        var excluded = new HashSet<string>(rule.ExcludeWitnessProviders, StringComparer.Ordinal) { FactIterationFanoutDeriver.Provider };
        var discovered = effects
            .Select(e => e.Provider)
            .Where(p => !string.IsNullOrEmpty(p) && !excluded.Contains(p))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (rule.Witnesses.Count == 0)
        {
            return discovered.Select(p => new EffectPredicate(p)).ToList();
        }

        var predicates = new List<EffectPredicate>();
        foreach (var w in rule.Witnesses)
        {
            var providers = w.Providers.Count > 0 ? w.Providers : discovered;
            predicates.AddRange(
                w.Operations.Count == 0
                    ? providers.Select(p => new EffectPredicate(p))
                    : providers.SelectMany(p => w.Operations.Select(o => new EffectPredicate(p, o)))
            );
        }

        return predicates;
    }

    private static string AnchorId(string filePath, int line, string? callee) => $"{filePath}{line}{callee}";

    // A tab or newline inside a mined value (an iteration detail is source text) would split a row, so the
    // emitted dataset normalizes both to a space. Null -> "".
    private static string Clean(string? value) =>
        string.IsNullOrEmpty(value) ? "" : value!.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
