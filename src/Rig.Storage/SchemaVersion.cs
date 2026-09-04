namespace Rig.Storage;

// The DB-FILE-level schema version stamped into the `meta` table (see SchemaMeta / SchemaGate). The
// `.rig` store is DERIVED + DISPOSABLE — rebuilt by re-index — so this is a TRIPWIRE, not a migration
// system: bumping a constant makes an old store fail fast at open with "re-index", it never transforms
// a store in place.
public static class SchemaVersion
{
    // Bump when a fact / extraction table or column SHAPE changes (the index write path: symbol_facts /
    // reference_facts / type_relation_facts / dispatch_facts / the run+assembly registry).
    // v1->v2: persist compiler-owned allocation facts.
    // v2->v3: persist structured allocation mechanism, cardinality, and shallow-size evidence.
    // v3->v4: persist the resolved ELEMENT TYPE of an iteration context — reference_facts
    //         .EnclosingLoopElementType, plus a 6th LambdaParameterType field inside the encoded
    //         EnclosingInvocations. The semantic half of the self-keyed read question: the loop DETAIL is
    //         source text and cannot say what a "row" is, and a lambda context has no detail worth reading.
    // v4->v5: persist reference_facts.EnclosingLoopBindType (the declaring type of a `query` context's bind
    //         method — the only signal separating a real comprehension loop from a single-shot monadic bind)
    //         and reference_facts.InExpressionTree (the reference is QUOTED code — an Expression<> lambda or
    //         IQueryable clause — which never executes as C# and must derive no effect / anchor no iteration).
    // v5->v6: persist the exact emitter FilePath on type_relation_facts and dispatch_facts, making resident
    //         overlays replace these emissions per file instead of retaining deleted edges as ghosts.
    // v6->v7: persist symbol SurfaceHash/IsIterator and the project aggregate assemblies.SurfaceHash.
    // v7->v8: persist reference_facts.Column (1-based start column of the reference, the same convention as
    //         Line) — the coordinate that separates two call sites sharing ONE source line, which Line alone
    //         collapses into indistinguishable facts.
    // v8->v9: persist source_files.Dirty — the per-FILE record of whether this run indexed that file from
    //         uncommitted source (git status at index start and end, unioned). The run-level SourceDirty
    //         flag cannot say WHICH file is off-commit, so a review could not scope the caveat.
    // v9->v10: persist each run's extraction-semantics version and producing rig build so two-store queries
    //          can reject provenance-skewed facts instead of comparing them as if they meant the same thing.
    // v10->v11: persist located compilation diagnostics on source files and partial-compilation rollups on runs.
    public const int Index = 11;

    // Bump whenever FactExtractor / extraction semantics change, even when the persisted DB shape does not.
    // This is fact provenance, deliberately independent of Index (shape) and Graph (materialization shape).
    public const int Extraction = 1;

    // Bump when the GRAPH shape changes (call_edges / dispatch_edges / nodes / the symbol_fts /
    // ref_target_fts virtual tables — all built by GraphMaterializer).
    // v1->v2: stamp the effective rules fingerprint that shaped the materialized call graph.
    public const int Graph = 2;
}
