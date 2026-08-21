using Rig.Domain.Data;

namespace Rig.Domain.Functions;

// The SINGLE source of truth for `reference_facts` (RefKind=invocation|methodGroup|ctor) -> CallEdge: the
// row->record mapping every raw-fact call-graph load funnels through. Two paths build these edges — the EF
// whole-store loader (Reads.LoadFactGraphAsync, which reconstructs the graph from a saved .rig store) and the
// in-memory twin (FactGraphProjection.FromAnalysis, which builds it straight off a freshly-extracted
// AnalysisResult and is what `rig index` MATERIALIZES call_edges from) — and until now each carried its own
// hand-written copy of the 16-field construction, kept in parity by nothing but a pair of comments asserting
// they must stay "field-for-field identical". A field silently missing from ONE of them means the persisted
// call_edges disagree with the in-memory oracle over the same facts, which is the effect-path divergence those
// comments warn about. Same class of bug as the FactInvocation drift (see FactInvocationProjection) — so the
// mapping lives HERE, once, and the comments now point at shared code instead of asserting an invariant.
//
// The precedent is FactInvocationProjection / DeliverySiteProjection: the pure projection core in the domain,
// the row SUPPLY left to each caller (EF projection / in-memory AnalysisResult). Deliberately NOT shared: the
// row FILTERS and the redirect lookup. Those genuinely differ per path (the EF loader pins TargetInSource in
// SQL and fetches each redirect rule's external rows in a separate indexed query; the in-memory twin filters
// and redirects in one pass), and they are the caller's business — this function is only the record.
//
// `redirectTo` is the external-virtual-override redirect (RedirectClassifier): when set, the edge's CALLEE is
// the virtual hatch the rule points at instead of the ref's own target. Null (the overwhelmingly common case)
// keeps the ref's target.
//
// Kept honest by FactGraphProjectionParityTests, which compares the two projections edge by edge on a real
// solution, and by ReachInputProjectionTests / LiveFactSourceParityTests downstream.
public static class CallEdgeProjection
{
    // The reference_facts columns a CallEdge is a function of: TargetSymbolId, RefKind, EnclosingSymbolId,
    // FilePath, Line, EnclosingLoopKind, EnclosingLoopDetail, ReceiverType, TypeArguments, DelegateConsumer,
    // DeclaringTypeArgBinding, MethodTypeArgBinding, NonVirtual, EnclosingGuards. A loader that supplies
    // placeholders for the REST of the ReferenceFact (TargetAssembly / TargetInSource — consumed by the
    // caller's own WHERE, not by this mapping — and the invocation-only argument/scope columns) is correct.
    //
    // HandoffDispatcher and DeliveryPrecision are always null here BY CONSTRUCTION: neither is a fact. The
    // handoff dispatcher is attached later by HandoffClassifier.Classify (which both callers run over the
    // projected edges), and DeliveryPrecision only exists on the synthetic edges FactPathFinder.AddDeliveryEdges
    // creates.
    public static CallEdge Project(ReferenceFact r, string? redirectTo = null) =>
        new CallEdge(
            Caller: r.EnclosingSymbolId!,
            Callee: redirectTo ?? r.TargetSymbolId,
            Kind: r.RefKind,
            FilePath: r.FilePath,
            Line: r.Line,
            LoopKind: r.EnclosingLoopKind,
            LoopDetail: r.EnclosingLoopDetail,
            ReceiverType: r.ReceiverType,
            HandoffDispatcher: null,
            TypeArguments: r.TypeArguments,
            DelegateConsumer: r.DelegateConsumer,
            DeclaringTypeArgBinding: r.DeclaringTypeArgBinding,
            MethodTypeArgBinding: r.MethodTypeArgBinding,
            DeliveryPrecision: null,
            NonVirtual: r.NonVirtual,
            EnclosingGuards: r.EnclosingGuards
        );
}
