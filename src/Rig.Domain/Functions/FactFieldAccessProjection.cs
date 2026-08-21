using Rig.Domain.Data;

namespace Rig.Domain.Functions;

// The SINGLE source of truth for `reference_facts` (RefKind=read|write, static target) -> FactFieldAccess: the
// row->record mapping for the FR-1 shared-state arms. THREE paths built this record by hand — the combined
// two-arm store loader (Reads.LoadStaticFieldAccessRefsByKindAsync), the single-kind store loader
// (Reads.LoadStaticFieldAccessRefsAsync, which the enclosing-scope-bounded `tree --hazards` path uses) and the
// in-memory twin (LiveReads.StaticFieldAccessRefsByKind) — each with its own copy of the field list.
//
// That list is the same STRUCTURAL-CONTEXT set whose loss on one path silently deleted every lexical-scope
// observation from reaches/tree/path (see FactInvocationProjection): drop EnclosingScopes here and a
// static-field mutation inside a `lock` stops carrying lock_held_across_effect, on that path only. Hence one
// mapping, called by all three.
//
// The precedent is FactInvocationProjection / DeliverySiteProjection: the pure projection core in the domain,
// the row SUPPLY left to each caller. Deliberately NOT shared: the static/readonly JOIN to symbol_facts, the
// enclosing-scope bound, the read-vs-write partition and the per-partition dedup by (FilePath, Line, Target) —
// those are the loaders' own, and they differ. Kept honest by LiveFactSourceParityTests.
public static class FactFieldAccessProjection
{
    // The reference_facts columns a FactFieldAccess is a function of: TargetSymbolId, EnclosingSymbolId,
    // FilePath, Line, EnclosingLoopKind, EnclosingLoopDetail, EnclosingInvocations, EnclosingCatchTypes,
    // EnclosingScopes. RefKind is NOT part of the record (the caller's WHERE pins it, and the combined loader
    // partitions on it), and a loader that supplies placeholders for the rest of the ReferenceFact is correct.
    public static FactFieldAccess Project(ReferenceFact r) =>
        new FactFieldAccess(
            Target: r.TargetSymbolId,
            Enclosing: r.EnclosingSymbolId,
            FilePath: r.FilePath,
            Line: r.Line,
            LoopKind: r.EnclosingLoopKind,
            LoopDetail: r.EnclosingLoopDetail,
            EnclosingInvocations: r.EnclosingInvocations,
            CatchTypes: r.EnclosingCatchTypes,
            EnclosingScopes: r.EnclosingScopes
        );
}
