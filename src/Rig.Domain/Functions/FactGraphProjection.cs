using Rig.Domain.Data;

namespace Rig.Domain.Functions;

// Builds the FactGraphData directly from a freshly-extracted AnalysisResult — the SAME graph
// Reads.LoadFactGraphAsync reconstructs from a saved .rig store, but without the round-trip through
// SQLite. `rig index` uses this so the graph phase materializes call_edges from facts already in memory
// instead of re-reading the whole fact store off disk (the second 3.8GB cold read).
//
// This projection must produce the same graph as Reads.LoadFactGraphAsync — same first-party call filter,
// same edge fields, same method dedup, same handoff classification — or the persisted call_edges would
// diverge from what the in-memory oracle computes over a re-read store (the effect-path divergence). The
// RECORD MAPPINGS are no longer duplicated to achieve that: both paths build their edges through
// CallEdgeProjection and their methods through SymbolFactProjections, so no edge or method field can be
// present on one side and missing on the other. What is still written twice — and so still needs to agree —
// is the row FILTERING (the RefKind set + TargetInSource/redirect predicate) and the dedup keys.
// FactGraphProjectionParityTests asserts the two agree on a real solution; keep them in lockstep.
public static class FactGraphProjection
{
    public static FactGraphData FromAnalysis(
        AnalysisResult result,
        IReadOnlyList<FactHandoffRule>? handoffRules = null,
        IReadOnlyList<FactRedirectRule>? redirectRules = null
    )
    {
        // First-party callees only (TargetInSource): BCL/runtime targets are leaves that add width, not
        // reach, and have no source symbol. Mirrors LoadFactGraphAsync's WHERE exactly. EXCEPTION: a call
        // matched by a redirect rule (external convenience overload → virtual hatch) is KEPT despite being
        // out-of-source and its callee rewritten to the hatch — the external-virtual-override-orphan fix
        // (docs/backlog.md); receiver-narrowed dispatch then resolves the kept hatch to the first-party override.
        var callEdges = (result.References ?? [])
            .Where(r =>
                r.EnclosingSymbolId != null
                && (r.RefKind == RefKinds.Invocation || r.RefKind == RefKinds.MethodGroup || r.RefKind == RefKinds.Ctor)
            )
            .Select(r => (r, redirect: RedirectClassifier.Redirect(r.TargetSymbolId, redirectRules)))
            .Where(x => x.r.TargetInSource || x.redirect != null)
            // The one shared row->CallEdge mapping (see CallEdgeProjection) — also used by the store loader,
            // so the two cannot differ by a field.
            .Select(x => CallEdgeProjection.Project(x.r, redirectTo: x.redirect))
            .Distinct()
            .ToList();
        var classifiedEdges = HandoffClassifier.Classify(callEdges, handoffRules);

        var implEdges = (result.TypeRelations ?? [])
            .Where(t => t.RelationKind == RelationKinds.Interface)
            .Select(t => new ImplementsEdge(ImplType: t.TypeSymbolId, InterfaceType: t.RelatedSymbolId))
            .Distinct()
            .ToList();

        var baseEdges = (result.TypeRelations ?? [])
            .Where(t => t.RelationKind == RelationKinds.Base)
            .Select(t => new BaseEdge(SubType: t.TypeSymbolId, BaseType: t.RelatedSymbolId))
            .Distinct()
            .ToList();

        var methods = (result.Symbols ?? [])
            .Where(s => s.Kind == SymbolKinds.Method)
            .Select(SymbolFactProjections.ToMethodRef)
            .GroupBy(m => m.SymbolId, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        var minedDispatch = (result.DispatchFacts ?? []).Distinct().ToList();

        // Applied here AND in LoadFactGraphAsync so the two projections match.
        return FactDelegateFieldJoin.Apply(new FactGraphData(classifiedEdges, implEdges, methods, baseEdges, minedDispatch));
    }
}
