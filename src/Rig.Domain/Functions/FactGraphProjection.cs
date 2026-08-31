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
// is the row FILTERING (the RefKind set + the TargetInSource/redirect/external-admission predicate) and the
// dedup keys. FactGraphProjectionParityTests asserts the two agree on a real solution; keep them in lockstep.
//
// EXTERNAL-NODE ADMISSION arrives as `externalNodes` (ExternalNodeAdmission): the SAME policy object the
// SQL loader consults, so the admitted set cannot differ between the two. This projection is what the live
// `rig watch` host runs and what `rig index` materializes call_edges from, so the Rider plugin and the
// persisted edge view see the admitted leaves too.
public static class FactGraphProjection
{
    public static FactGraphData FromAnalysis(
        AnalysisResult result,
        IReadOnlyList<FactHandoffRule>? handoffRules = null,
        IReadOnlyList<FactRedirectRule>? redirectRules = null,
        ExternalNodeAdmission? externalNodes = null
    ) => FromView(result, handoffRules, redirectRules, externalNodes);

    public static FactGraphData FromView(
        IFactSnapshotView result,
        IReadOnlyList<FactHandoffRule>? handoffRules = null,
        IReadOnlyList<FactRedirectRule>? redirectRules = null,
        ExternalNodeAdmission? externalNodes = null
    )
    {
        var (classifiedEdges, externalLeaves) = ProjectCallsWithExternals(
            result.EnumerateReferences(),
            handoffRules,
            redirectRules,
            externalNodes
        );

        var implEdges = result
            .EnumerateTypeRelations()
            .Where(t => t.RelationKind == RelationKinds.Interface)
            .Select(t => new ImplementsEdge(ImplType: t.TypeSymbolId, InterfaceType: t.RelatedSymbolId))
            .Distinct()
            .ToList();

        var baseEdges = result
            .EnumerateTypeRelations()
            .Where(t => t.RelationKind == RelationKinds.Base)
            .Select(t => new BaseEdge(SubType: t.TypeSymbolId, BaseType: t.RelatedSymbolId))
            .Distinct()
            .ToList();

        // First-party methods from symbol_facts, then the synthesized EXTERNAL LEAVES. The externals are
        // appended AFTER and the dedup is first-wins, so a DocID that is somehow both (in source AND
        // referenced as external) keeps its real, source-located node. Same order as the store loader's.
        var methods = result
            .EnumerateSymbols()
            .Where(s => s.Kind == SymbolKinds.Method)
            .Select(SymbolFactProjections.ToMethodRef)
            .Concat(externalLeaves)
            .GroupBy(m => m.SymbolId, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        // DispatchFact now carries its emitter path for live per-file replacement, but graph identity
        // remains the semantic (source,target,kind) triple: several files may legitimately emit the
        // same exact edge.
        var minedDispatch = result.EnumerateDispatchFacts().Select(d => d with { FilePath = "" }).Distinct().ToList();

        // Applied here AND in LoadFactGraphAsync so the two projections match.
        return FactDelegateFieldJoin.Apply(new FactGraphData(classifiedEdges, implEdges, methods, baseEdges, minedDispatch));
    }

    // Projects the semantic call edges owned by one caller without enumerating the rest of the graph.
    // The keyed resident view preserves raw row multiplicity; ProjectCalls owns the same semantic dedupe
    // and caller-local handoff classification used by the full snapshot projection above.
    public static IReadOnlyList<CallEdge> CallsFrom(
        IFactGraphView graph,
        string caller,
        IReadOnlyList<FactHandoffRule>? handoffRules = null,
        IReadOnlyList<FactRedirectRule>? redirectRules = null,
        bool classifyEventSubscriptions = false,
        // EXTERNAL-NODE ADMISSION on the DEMAND path. Currently always null from production: this
        // projection returns EDGES only, and the demand graph builds its node set from per-caller
        // symbol_facts reads, so an admitted external callee would get an edge but no IsExternal MethodRef
        // — i.e. no dispatch suppression, which is exactly the external-interface CHA fan-out that change
        // put out of scope. The seam exists so the demand node materialization can opt in as a follow-on.
        ExternalNodeAdmission? externalNodes = null
    )
    {
        var references = graph.ReferencesFrom(caller);
        var calls = ProjectCalls(references, handoffRules, redirectRules, externalNodes);
        var delegateFieldEdges = FactDelegateFieldJoin.EdgesFrom(graph, caller);
        IReadOnlyList<CallEdge> combined = delegateFieldEdges.Count == 0 ? calls : [.. calls, .. delegateFieldEdges];
        if (!classifyEventSubscriptions)
        {
            return combined;
        }

        var eventSites = references
            .Where(reference =>
                reference.RefKind == RefKinds.Read
                && reference.TargetSymbolId.StartsWith("E:", StringComparison.Ordinal)
                && reference.EnclosingSymbolId is not null
            )
            .Select(reference => new EventSubscriptionSite(reference.EnclosingSymbolId!, reference.FilePath, reference.Line))
            .ToHashSet();
        return eventSites.Count == 0
            ? combined
            : FactPathFinder
                .MarkEventSubscriptionHandoffs(
                    new FactGraphData(combined, Array.Empty<ImplementsEdge>(), Array.Empty<MethodRef>()),
                    eventSites
                )
                .CallEdges;
    }

    private static IReadOnlyList<CallEdge> ProjectCalls(
        IEnumerable<ReferenceFact> references,
        IReadOnlyList<FactHandoffRule>? handoffRules,
        IReadOnlyList<FactRedirectRule>? redirectRules,
        ExternalNodeAdmission? externalNodes = null
    ) => ProjectCallsWithExternals(references, handoffRules, redirectRules, externalNodes).Edges;

    // The row FILTER + the shared row->CallEdge mapping, plus the external LEAF nodes that filter admitted.
    // Returned together because they are ONE decision: a callee is an external node exactly when the
    // admission arm — not the first-party arm and not the redirect arm — is what kept its row.
    private static (IReadOnlyList<CallEdge> Edges, IReadOnlyList<MethodRef> ExternalLeaves) ProjectCallsWithExternals(
        IEnumerable<ReferenceFact> references,
        IReadOnlyList<FactHandoffRule>? handoffRules,
        IReadOnlyList<FactRedirectRule>? redirectRules,
        ExternalNodeAdmission? externalNodes
    )
    {
        // Three admission arms, in PRECEDENCE order (each row takes exactly ONE, so a single row can never
        // produce two edges):
        //   1. TargetInSource — the first-party graph, unchanged.
        //   2. a REDIRECT rule matched the row (external convenience overload → virtual hatch): the row is
        //      KEPT and its callee REWRITTEN to the hatch — the external-virtual-override-orphan fix
        //      (docs/backlog.md). Receiver-narrowed dispatch then resolves the kept hatch to the first-party
        //      override, so a redirect target is deliberately NOT an external leaf (it must keep its
        //      dispatch) and is excluded from the synthesized node set below.
        //   3. EXTERNAL-NODE ADMISSION (ExternalNodeAdmission.Admits): the out-of-source target becomes an
        //      ordinary CallEdge to a synthesized LEAF node. A null policy leaves this arm OFF (the shape
        //      before this change), which is what the synthetic/test projections passing no rules get.
        // Mirrors LoadFactGraphAsync's WHERE + its external scan exactly.
        var redirectTargets = new HashSet<string>((redirectRules ?? []).Select(rule => rule.RedirectTo), StringComparer.Ordinal);
        var externalIds = new HashSet<string>(StringComparer.Ordinal);
        var callEdges = references
            .Where(r =>
                r.EnclosingSymbolId != null
                && (r.RefKind == RefKinds.Invocation || r.RefKind == RefKinds.MethodGroup || r.RefKind == RefKinds.Ctor)
            )
            .Select(r => (r, redirect: RedirectClassifier.Redirect(r.TargetSymbolId, redirectRules)))
            .Where(x =>
            {
                if (x.r.TargetInSource || x.redirect != null)
                {
                    return true;
                }

                if (externalNodes is null || !externalNodes.Admits(x.r.TargetAssembly, x.r.TargetSymbolId))
                {
                    return false;
                }

                if (!redirectTargets.Contains(x.r.TargetSymbolId))
                {
                    externalIds.Add(x.r.TargetSymbolId);
                }

                return true;
            })
            // The one shared row->CallEdge mapping (see CallEdgeProjection) — also used by the store loader,
            // so the two cannot differ by a field.
            .Select(x => CallEdgeProjection.Project(x.r, redirectTo: x.redirect))
            .Distinct()
            .ToList();
        return (HandoffClassifier.Classify(callEdges, handoffRules), [.. externalIds.Select(ExternalNodeAdmission.SynthesizeNode)]);
    }
}
