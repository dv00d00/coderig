using Rig.Domain.Data;

namespace Rig.Domain.Functions;

// The IN-MEMORY TWIN of the query-side `Reads` surface: every artifact a query path loads out of a saved
// .rig store, projected instead straight off a freshly-extracted AnalysisResult — no SQLite round-trip.
// This is the query-serving foundation for the resident live index (`rig watch`, ResidentIndex), which keeps
// an AnalysisResult current ~0.75s after a file edit but has, until now, had nothing that can QUERY it: the
// whole query surface (reaches/tree/callers/derive) reads facts through RigDbContext.
//
// It extends exactly the pattern FactGraphProjection already established for the call graph. Each twin below
// names the Reads method it mirrors and reproduces its filters, projection fields and client-side dedup
// EXACTLY — including the `GroupBy(...).Select(g => g.First())` first-wins dedups (the store holds duplicate
// fact rows across runs, and dropping the dedup would change counts) and the `Modifiers.Split(' ')` /
// `Modifiers.Contains(...)` token logic that has no SQL translation.
//
// These MUST stay field-for-field identical to their store counterparts, or a live-served answer would
// differ from a cold-indexed one and `rig` would stop being a fact tool. LiveFactSourceParityTests is what
// keeps them in lockstep: it saves a real analyzed solution to a temp store, reads it back through `Reads`,
// and asserts each projection here is SET-EQUAL to it. Change one side, change the other.
//
// All synchronous: these are pure functions over in-memory lists, so there is nothing to await.
//
// NOTE on prefix matching: the store side's `TargetSymbolId.StartsWith("E:")` becomes a SQLite `LIKE 'E:%'`,
// which is ASCII-case-INSENSITIVE; the ordinal comparisons here are case-SENSITIVE. No divergence is
// reachable — DocID prefixes are Roslyn-generated and always upper-case (T:/M:/P:/F:/E:/N:).
//
// AllocationFacts has no twin ON PURPOSE: Reads.LoadAllocationFactsAsync (whole-store, no enclosing scope)
// applies no filter and no dedup, and projects the AllocationFact record field-for-field off the entity — so
// `result.AllocationFacts ?? []` already IS its return value. The parity test asserts that rather than
// wrapping it in a pointless pass-through.
public static class LiveReads
{
    // Mirrors Reads.LoadMonomorphizationSignaturesAsync (which is Reads.LoadSymbolSignaturesAsync verbatim):
    // the `id -> Signature` map over every METHOD and TYPE symbol, the type-param-name source ShapeGraph's
    // `monomorphizeSignatures` mines. First-wins on SymbolId (TryAdd), mirroring the method dedupe in
    // LoadFactGraphAsync. Kept in lockstep with Reads by LiveFactSourceParityTests.
    public static IReadOnlyDictionary<string, string> MonomorphizationSignatures(AnalysisResult result)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in result.Symbols ?? [])
        {
            if (s.Kind != SymbolKinds.Method && s.Kind != SymbolKinds.Type)
            {
                continue;
            }

            map.TryAdd(s.SymbolId, s.Signature ?? "");
        }

        return map;
    }

    // Mirrors Reads.EventSubscriptionSitesAsync: call SITES containing an EVENT read (a `read` ref whose
    // target is an event DocID). Intersected with method-group edges by MarkEventSubscriptionHandoffs.
    // Kept in lockstep with Reads by LiveFactSourceParityTests.
    public static ISet<EventSubscriptionSite> EventSubscriptionSites(AnalysisResult result) =>
        (result.References ?? [])
            .Where(r => r.EnclosingSymbolId != null && r.RefKind == RefKinds.Read && r.TargetSymbolId.StartsWith("E:", StringComparison.Ordinal))
            .Select(r => new EventSubscriptionSite(Caller: r.EnclosingSymbolId!, FilePath: r.FilePath, Line: r.Line))
            .ToHashSet();

    // Mirrors Reads.LoadDeliverySitesAsync — and shares its ENTIRE rule-driven core: both halves project the
    // raw rows (here off the in-memory reference facts, there by two SQL scans) and hand them to the one
    // DeliverySiteProjection.Project. The row filters below are that loader's two WHERE clauses verbatim.
    // Kept in lockstep with Reads by LiveFactSourceParityTests.
    public static IReadOnlyList<DeliverySite> DeliverySites(AnalysisResult result, IReadOnlyList<DeliveryRule> deliveryRules)
    {
        var references = result.References ?? [];

        var eventReads =
            DeliverySiteProjection.EventRules(deliveryRules).Count > 0
                ? references
                    .Where(r =>
                        r.EnclosingSymbolId != null
                        && r.RefKind == RefKinds.Read
                        && r.TargetSymbolId.StartsWith("E:", StringComparison.Ordinal)
                    )
                    .Select(r => new DeliverySiteProjection.EventRead(r.EnclosingSymbolId, r.FilePath, r.Line, r.TargetSymbolId))
                    .ToList()
                : [];

        var argInvocations =
            DeliverySiteProjection.ArgMethods(deliveryRules).Count > 0
                ? references
                    .Where(r =>
                        r.EnclosingSymbolId != null
                        && r.FirstArgumentName != null
                        && r.RefKind == RefKinds.Invocation
                        && r.TargetSymbolId.StartsWith("M:", StringComparison.Ordinal)
                    )
                    .Select(r => new DeliverySiteProjection.ArgInvocation(
                        r.EnclosingSymbolId,
                        r.FilePath,
                        r.Line,
                        r.FirstArgumentName,
                        r.TargetSymbolId
                    ))
                    .ToList()
                : [];

        return DeliverySiteProjection.Project(deliveryRules, eventReads, argInvocations);
    }

    // Mirrors Reads.LoadFactEntryPointDataAsync: base/interface type-relation edges, ALL method symbols, type
    // symbols, and ctor refs (attribute applications included). The dedups are that loader's, key-for-key —
    // baseEdges/interfaceEdges by value (Distinct), methods and ctorRefs by (FilePath, Line), types by
    // SymbolId. The MethodSymbol/TypeSymbol record mappings are the SHARED SymbolFactProjections (so is the
    // `Modifiers.Split(' ').Contains("abstract")` that gives TypeSymbol its IsAbstract — String.Split has no
    // SQL translation, so it runs in memory on BOTH sides, now through one function).
    // Kept in lockstep with Reads by LiveFactSourceParityTests.
    public static FactEntryPointDeriver.FactEntryPointData FactEntryPointData(AnalysisResult result)
    {
        var typeRelations = result.TypeRelations ?? [];
        var symbols = result.Symbols ?? [];
        var references = result.References ?? [];

        var baseEdges = typeRelations
            .Where(t => t.RelationKind == RelationKinds.Base)
            .Select(t => (t.TypeSymbolId, t.RelatedSymbolId))
            .Distinct()
            .ToList();

        var interfaceEdges = typeRelations
            .Where(t => t.RelationKind == RelationKinds.Interface)
            .Select(t => (t.TypeSymbolId, t.RelatedSymbolId))
            .Distinct()
            .ToList();

        var methods = symbols
            .Where(s => s.Kind == SymbolKinds.Method)
            .Select(SymbolFactProjections.ToMethodSymbol)
            .GroupBy(m => (m.FilePath, m.Line))
            .Select(g => g.First())
            .ToList();

        var types = symbols
            .Where(s => s.Kind == SymbolKinds.Type)
            .GroupBy(s => s.SymbolId, StringComparer.Ordinal)
            .Select(g => g.First())
            .Select(SymbolFactProjections.ToTypeSymbol)
            .ToList();

        var ctorRefs = references
            .Where(r => r.RefKind == RefKinds.Ctor && r.EnclosingSymbolId != null)
            .Select(r => new SymbolRef(Target: r.TargetSymbolId, Enclosing: r.EnclosingSymbolId, FilePath: r.FilePath, Line: r.Line))
            .GroupBy(r => (r.FilePath, r.Line))
            .Select(g => g.First())
            .ToList();

        return new FactEntryPointDeriver.FactEntryPointData(
            BaseEdges: baseEdges,
            Methods: methods,
            Types: types,
            CtorRefs: ctorRefs,
            InterfaceEdges: interfaceEdges
        );
    }

    // Mirrors Reads.LoadInvocationRefsAsync: every `invocation` reference fact, projected to FactInvocation
    // with its full structural context. NO first-party filter and NO dedup — deliberately, on both sides: the
    // effect deriver keys a BCL call to its first-party ENCLOSING method, so filtering here would lose effects.
    // Kept in lockstep with Reads by LiveFactSourceParityTests — and now by CONSTRUCTION as well: the
    // projection itself is FactInvocationProjection.Project, the same function both store loaders map through.
    public static IReadOnlyList<FactInvocation> InvocationRefs(AnalysisResult result) =>
        (result.References ?? []).Where(r => r.RefKind == RefKinds.Invocation).Select(FactInvocationProjection.Project).ToList();

    // Mirrors Reads.LoadThrowRefsAsync: `throw` reference facts (Target is the thrown exception type DocID),
    // deduped by (FilePath, Line, Target) exactly as the loader does.
    // Kept in lockstep with Reads by LiveFactSourceParityTests.
    public static IReadOnlyList<SymbolRef> ThrowRefs(AnalysisResult result) =>
        (result.References ?? [])
            .Where(r => r.RefKind == RefKinds.Throw && r.EnclosingSymbolId != null)
            .Select(r => new SymbolRef(
                Target: r.TargetSymbolId,
                Enclosing: r.EnclosingSymbolId,
                FilePath: r.FilePath,
                Line: r.Line,
                EnclosingGuards: r.EnclosingGuards
            ))
            .GroupBy(r => (r.FilePath, r.Line, r.Target))
            .Select(g => g.First())
            .ToList();

    // Mirrors Reads.LoadStaticFieldAccessRefsByKindAsync: BOTH static-field-access arms from one pass over the
    // read/write refs, joined to the symbol facts on a STATIC target (the fact layer's only source of the
    // accessed slot's modifiers), partitioned by kind and deduped per partition by (FilePath, Line, Target).
    // The `readonly` asymmetry is the loader's: only the READ arm drops readonly targets (an immutable cell
    // can't be a TOCTOU "check"; ~99k static-readonly logger reads of noise on the real store) — the WRITE arm
    // keeps them. The join is written as a real LINQ Join, not a HashSet lookup, so a duplicated symbol fact
    // row fans out exactly as the SQL inner join does before the dedup collapses it. The row->record mapping
    // is the SHARED FactFieldAccessProjection, the same one both store loaders map through.
    // Kept in lockstep with Reads by LiveFactSourceParityTests.
    public static (IReadOnlyList<FactFieldAccess> Writes, IReadOnlyList<FactFieldAccess> Reads) StaticFieldAccessRefsByKind(AnalysisResult result)
    {
        var staticSymbols = (result.Symbols ?? []).Where(s => s.Modifiers.Contains("static", StringComparison.Ordinal));

        var rows = (result.References ?? [])
            .Where(r => (r.RefKind == RefKinds.Write || r.RefKind == RefKinds.Read) && r.TargetInSource && r.EnclosingSymbolId != null)
            .Join(
                staticSymbols,
                r => r.TargetSymbolId,
                s => s.SymbolId,
                (r, s) => new { Row = r, IsReadonly = s.Modifiers.Contains("readonly", StringComparison.Ordinal) }
            )
            .ToList();

        var writes = rows.Where(x => string.Equals(x.Row.RefKind, RefKinds.Write, StringComparison.Ordinal))
            .Select(x => FactFieldAccessProjection.Project(x.Row))
            .GroupBy(r => (r.FilePath, r.Line, r.Target))
            .Select(g => g.First())
            .ToList();
        var reads = rows.Where(x => string.Equals(x.Row.RefKind, RefKinds.Read, StringComparison.Ordinal) && !x.IsReadonly)
            .Select(x => FactFieldAccessProjection.Project(x.Row))
            .GroupBy(r => (r.FilePath, r.Line, r.Target))
            .Select(g => g.First())
            .ToList();

        return (writes, reads);
    }

    // Mirrors Reads.LoadThreadStaticFieldIdsAsync: the field/auto-property DocIDs carrying [ThreadStatic],
    // found the same way — an attribute application IS a ctor reference whose ENCLOSING is the decorated
    // field's DocID and whose TARGET is the attribute's ctor. Same exact ctor DocID match, same Distinct.
    // Kept in lockstep with Reads by LiveFactSourceParityTests.
    public static IReadOnlySet<string> ThreadStaticFieldIds(AnalysisResult result)
    {
        const string threadStaticCtor = "M:System.ThreadStaticAttribute.#ctor";
        return (result.References ?? [])
            .Where(r =>
                r.RefKind == RefKinds.Ctor
                && string.Equals(r.TargetSymbolId, threadStaticCtor, StringComparison.Ordinal)
                && r.EnclosingSymbolId != null
            )
            .Select(r => r.EnclosingSymbolId!)
            .ToHashSet(StringComparer.Ordinal);
    }

    // Mirrors Reads.LoadVolatileFieldIdsAsync: field symbols whose Modifiers carry `volatile` — one of the two
    // signals hard-suppressing a lock-enclosed lazy-init as a safe DCL.
    // Kept in lockstep with Reads by LiveFactSourceParityTests.
    public static IReadOnlySet<string> VolatileFieldIds(AnalysisResult result) =>
        (result.Symbols ?? [])
            .Where(s => s.Kind == SymbolKinds.Field && s.Modifiers.Contains("volatile", StringComparison.Ordinal))
            .Select(s => s.SymbolId)
            .ToHashSet(StringComparer.Ordinal);

    // Mirrors Reads.LoadDeadCodeMethodsAsync: metadata for EVERY method symbol (no first-party filter — the
    // finder applies its own), deduped by SymbolId, with the same generated-file heuristic. (Also the `async` modifier source the sync_over_async
    // hazard feed filters on.)
    // Kept in lockstep with Reads by LiveFactSourceParityTests.
    public static IReadOnlyList<DeadCodeFinder.MethodMeta> DeadCodeMethods(AnalysisResult result) =>
        (result.Symbols ?? [])
            .Where(s => s.Kind == SymbolKinds.Method)
            .GroupBy(s => s.SymbolId, StringComparer.Ordinal)
            .Select(g => g.First())
            .Select(SymbolFactProjections.ToMethodMeta)
            .ToList();

    // Mirrors Reads.LoadShapedGraphAsync: the FULLY shaped graph — handoff-classified load → ShapeGraph
    // (factory rewrite + monomorphization + cut/context metadata) → AddDeliveryEdges →
    // MarkEventSubscriptionHandoffs. The single entry point for in-memory consumers needing the complete
    // shaped graph, so the sequence is defined once rather than hand-rolled per caller.
    //
    // ORDER IS LOAD-BEARING (copied from LoadShapedGraphAsync, not re-derived): AddDeliveryEdges resolves an
    // event's handlers by joining event-read sites to co-located `methodGroup` subscription edges
    // (`someEvent += H`), so it MUST run while those edges are still methodGroup. MarkEventSubscriptionHandoffs
    // reclassifies exactly those subscription edges to `handoff` — run it AFTER, or AddDeliveryEdges finds zero
    // handlers and event delivery (event_raise) edges vanish (event_cycle drops to 0).
    // Kept in lockstep with Reads by LiveFactSourceParityTests.
    public static FactGraphData ShapedGraph(AnalysisResult result, RuleSet rules)
    {
        var graph = FactGraphProjection.FromAnalysis(result, handoffRules: rules.Handoff, redirectRules: rules.Redirect);
        graph = FactPathFinder.ShapeGraph(
            graph: graph,
            factoryRules: rules.Factory,
            cutRules: rules.Cut,
            contextRules: rules.Context,
            monomorphizeSignatures: MonomorphizationSignatures(result)
        );
        graph = FactPathFinder.AddDeliveryEdges(graph: graph, sites: DeliverySites(result, rules.Delivery));
        return FactPathFinder.MarkEventSubscriptionHandoffs(graph: graph, eventSites: EventSubscriptionSites(result));
    }
}
