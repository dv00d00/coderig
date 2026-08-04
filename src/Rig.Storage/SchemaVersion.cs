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
    public const int Index = 5;

    // Bump when the GRAPH shape changes (call_edges / dispatch_edges / nodes / the symbol_fts /
    // ref_target_fts virtual tables — all built by GraphMaterializer).
    public const int Graph = 1;
}
