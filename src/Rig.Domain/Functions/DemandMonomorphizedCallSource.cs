using System.Collections.Immutable;
using Rig.Domain.Data;

namespace Rig.Domain.Functions;

public sealed record ForwardCallProjectionRules(
    IReadOnlyList<FactHandoffRule>? Handoff = null,
    IReadOnlyList<FactRedirectRule>? Redirect = null,
    IReadOnlyList<FactGenericFactoryRule>? Factory = null
);

public sealed record DemandMonomorphizationLimits(int MaxInstantiationsPerMethod = 50, int MaxWorkUnits = 100_000);

public readonly record struct DemandReadMetric(int Calls, int Rows);

public sealed record DemandReadDiagnostics(
    DemandReadMetric ForwardCallers,
    DemandReadMetric Symbols,
    DemandReadMetric ContainingMethods,
    DemandReadMetric Dispatch
);

public sealed record DemandAdjacencyDiagnostics(int CacheHits, int CacheMisses, int ProjectedBaseEdges);

public sealed record DemandPrecisionDiagnostics(
    int DistinctInstantiations,
    int MonomorphizedCallers,
    int MonomorphizedEdges,
    int PerMethodFallbackEdges,
    ImmutableArray<string> CappedMethodIds,
    int BudgetFallbackEdges
);

public sealed record DemandBudgetDiagnostics(int Limit, int Reserved, int Attempted, int AtomicOvershoot, bool Exceeded);

public sealed record DemandMonomorphizationDiagnostics(
    DemandReadDiagnostics Reads,
    DemandAdjacencyDiagnostics Adjacency,
    DemandPrecisionDiagnostics Precision,
    DemandBudgetDiagnostics Budget
);

// A short-lived, query-local adjacency source. It reads only the requested caller partitions, admits only
// instantiations reached by this query, and delegates dispatch entirely to the existing traversal engine.
public sealed class DemandMonomorphizedCallSource : IForwardCallSource
{
    private readonly CountingView countedGraph;
    private readonly IReadOnlyList<FactHandoffRule> handoffRules;
    private readonly IReadOnlyList<FactRedirectRule> redirectRules;
    private readonly IReadOnlyList<FactGenericFactoryRule> factoryRules;
    private readonly DemandMonomorphizationLimits limits;
    private readonly Dictionary<string, IReadOnlyList<CallEdge>> baseAdjacency = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<CallEdge>> resultCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SymbolFact?> canonicalSymbols = new(StringComparer.Ordinal);
    private readonly HashSet<string> admittedIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> admittedByMethod = new(StringComparer.Ordinal);
    private readonly SortedSet<string> cappedMethods = new(StringComparer.Ordinal);

    private int forwardCallerLookupCalls;
    private int forwardCallerRowsRead;
    private int symbolLookupCalls;
    private int symbolRowsRead;
    private int containingMethodLookupCalls;
    private int containingMethodRowsRead;
    private int dispatchLookupCalls;
    private int dispatchRowsRead;
    private int baseAdjacencyCacheHits;
    private int baseAdjacencyCacheMisses;
    private int projectedBaseEdges;
    private int monomorphizedCallers;
    private int monomorphizedEdges;
    private int workUnitsReserved;
    private int workUnitsAttempted;
    private int atomicOvershoot;
    private int perMethodFallbackEdges;
    private int budgetFallbackEdges;
    private bool budgetExceeded;

    public DemandMonomorphizedCallSource(
        IFactGraphView graph,
        ForwardCallProjectionRules? rules = null,
        DemandMonomorphizationLimits? limits = null
    )
    {
        ArgumentNullException.ThrowIfNull(graph);
        var configuredRules = rules ?? new ForwardCallProjectionRules();
        handoffRules = configuredRules.Handoff ?? [];
        redirectRules = configuredRules.Redirect ?? [];
        factoryRules = configuredRules.Factory ?? [];
        this.limits = limits ?? new DemandMonomorphizationLimits();
        if (this.limits.MaxInstantiationsPerMethod <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "The per-method instantiation limit must be positive.");
        }
        if (this.limits.MaxWorkUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "The work limit must be positive.");
        }

        countedGraph = new CountingView(this, graph);
    }

    public IReadOnlyList<CallEdge> CallsFrom(string caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (resultCache.TryGetValue(caller, out var cached))
        {
            return cached;
        }

        IReadOnlyList<CallEdge> result = MonomorphizedNodeId.TryParse(caller, out var parsed)
            ? CallsFromMonomorphized(caller, parsed)
            : CallsFromBase(caller);
        resultCache[caller] = result;
        return result;
    }

    public DemandMonomorphizationDiagnostics DiagnosticsSnapshot() =>
        new(
            Reads: new DemandReadDiagnostics(
                ForwardCallers: new DemandReadMetric(forwardCallerLookupCalls, forwardCallerRowsRead),
                Symbols: new DemandReadMetric(symbolLookupCalls, symbolRowsRead),
                ContainingMethods: new DemandReadMetric(containingMethodLookupCalls, containingMethodRowsRead),
                Dispatch: new DemandReadMetric(dispatchLookupCalls, dispatchRowsRead)
            ),
            Adjacency: new DemandAdjacencyDiagnostics(baseAdjacencyCacheHits, baseAdjacencyCacheMisses, projectedBaseEdges),
            Precision: new DemandPrecisionDiagnostics(
                DistinctInstantiations: admittedIds.Count,
                MonomorphizedCallers: monomorphizedCallers,
                MonomorphizedEdges: monomorphizedEdges,
                PerMethodFallbackEdges: perMethodFallbackEdges,
                CappedMethodIds: cappedMethods.Count == 0 ? ImmutableArray<string>.Empty : cappedMethods.ToImmutableArray(),
                BudgetFallbackEdges: budgetFallbackEdges
            ),
            Budget: new DemandBudgetDiagnostics(
                Limit: limits.MaxWorkUnits,
                Reserved: workUnitsReserved,
                Attempted: workUnitsAttempted,
                AtomicOvershoot: atomicOvershoot,
                Exceeded: budgetExceeded
            )
        );

    private IReadOnlyList<CallEdge> CallsFromBase(string caller)
    {
        var edges = BaseEdges(caller);
        if (budgetExceeded)
        {
            budgetFallbackEdges += edges.Count(edge => HasGenericBinding(edge));
            return edges;
        }

        var rewritten = new List<CallEdge>(edges.Count);
        foreach (var edge in edges)
        {
            var instantiation = GenericMonomorphizer.ResolveInstantiation(edge, [], []);
            if (instantiation is null || !TryAdmit(instantiation))
            {
                rewritten.Add(edge);
                continue;
            }

            rewritten.Add(
                edge with
                {
                    Callee = MonomorphizedNodeId.For(instantiation.MethodId, instantiation.DeclaringBinding, instantiation.MethodBinding),
                }
            );
        }

        return rewritten;
    }

    private IReadOnlyList<CallEdge> CallsFromMonomorphized(string caller, MonomorphizedNodeId.Parsed parsed)
    {
        var precisionAllowed = ReserveWork(1);
        monomorphizedCallers++;

        var baseCaller = parsed.BaseMethodId;
        var bindingOwner = LambdaOwner(baseCaller);
        var baseEdges = BaseEdges(baseCaller);
        if (!precisionAllowed || budgetExceeded)
        {
            budgetFallbackEdges += baseEdges.Count;
            return baseEdges.Select(edge => edge with { Caller = caller }).ToArray();
        }

        var binding = new GenericInstantiationInventory.GenericInstantiation(bindingOwner, parsed.DeclaringBinding, parsed.MethodBinding);
        var map = BuildTypeParameterMap(binding);
        if (map is null || budgetExceeded)
        {
            budgetFallbackEdges += baseEdges.Count;
            return baseEdges.Select(edge => edge with { Caller = caller }).ToArray();
        }

        var cloned = GenericMonomorphizer.CloneCallerEdges(baseEdges, caller, binding, map, TryAdmit);
        monomorphizedEdges += cloned.Count;
        return cloned;
    }

    private IReadOnlyList<CallEdge> BaseEdges(string caller)
    {
        if (baseAdjacency.TryGetValue(caller, out var cached))
        {
            baseAdjacencyCacheHits++;
            return cached;
        }

        baseAdjacencyCacheMisses++;
        var projected = FactGraphProjection.CallsFrom(countedGraph, caller, handoffRules, redirectRules);
        var shaped = new List<CallEdge>(projected.Count);
        foreach (var edge in projected)
        {
            if (factoryRules.Count == 0)
            {
                shaped.Add(edge);
                continue;
            }

            shaped.AddRange(
                FactPathFinder.RewriteGenericFactoryEdge(
                    edge,
                    factoryRules,
                    (constructType, methodName) => MethodsByConstructedType(constructType, methodName)
                )
            );
        }

        projectedBaseEdges += shaped.Count;
        ReserveWork(shaped.Count);
        cached = shaped.ToArray();
        baseAdjacency[caller] = cached;
        return cached;
    }

    private IReadOnlyList<MethodRef> MethodsByConstructedType(string constructType, string methodName)
    {
        var containingType = FactPathFinder.FactoryConstructTypeId(constructType);
        if (containingType is null)
        {
            return [];
        }
        var rows = countedGraph.MethodsByContainingSymbol(containingType);
        return SymbolFactProjections
            .SelectCanonicalMethodFacts(rows)
            .Where(symbol => symbol.Name == methodName)
            .Select(SymbolFactProjections.ToMethodRef)
            .ToArray();
    }

    private bool TryAdmit(GenericInstantiationInventory.GenericInstantiation instantiation)
    {
        var id = MonomorphizedNodeId.For(instantiation.MethodId, instantiation.DeclaringBinding, instantiation.MethodBinding);
        if (admittedIds.Contains(id))
        {
            return true;
        }

        if (budgetExceeded)
        {
            budgetFallbackEdges++;
            return false;
        }

        if (!HasCompleteSignatures(instantiation))
        {
            return false;
        }
        if (budgetExceeded)
        {
            budgetFallbackEdges++;
            return false;
        }

        if (!admittedByMethod.TryGetValue(instantiation.MethodId, out var methodInstantiations))
        {
            admittedByMethod[instantiation.MethodId] = methodInstantiations = new HashSet<string>(StringComparer.Ordinal);
        }
        if (methodInstantiations.Count >= limits.MaxInstantiationsPerMethod)
        {
            cappedMethods.Add(instantiation.MethodId);
            perMethodFallbackEdges++;
            return false;
        }

        methodInstantiations.Add(id);
        admittedIds.Add(id);
        return true;
    }

    private bool HasCompleteSignatures(GenericInstantiationInventory.GenericInstantiation instantiation)
    {
        var method = CanonicalSymbol(instantiation.MethodId);
        if (method is null || method.Kind != SymbolKinds.Method)
        {
            return false;
        }

        if (
            instantiation.MethodBinding.Count > 0
            && GenericSubstitution.ParseTypeParameterNames(method.Signature).Count < instantiation.MethodBinding.Count
        )
        {
            return false;
        }

        if (instantiation.DeclaringBinding.Count == 0)
        {
            return true;
        }

        if (method.ContainingSymbolId is null)
        {
            return false;
        }

        var type = CanonicalSymbol(method.ContainingSymbolId);
        return type is not null
            && type.Kind == SymbolKinds.Type
            && GenericSubstitution.ParseTypeParameterNames(type.Signature).Count >= instantiation.DeclaringBinding.Count;
    }

    private IReadOnlyDictionary<string, string>? BuildTypeParameterMap(GenericInstantiationInventory.GenericInstantiation instantiation)
    {
        if (!HasCompleteSignatures(instantiation))
        {
            return null;
        }

        var method = CanonicalSymbol(instantiation.MethodId)!;
        return GenericMonomorphizer.BuildTypeParameterMap(
            instantiation,
            id => GenericSubstitution.ParseTypeParameterNames(CanonicalSymbol(id)?.Signature),
            method.ContainingSymbolId
        );
    }

    private SymbolFact? CanonicalSymbol(string symbolId)
    {
        if (canonicalSymbols.TryGetValue(symbolId, out var cached))
        {
            return cached;
        }

        var rows = countedGraph.SymbolsById(symbolId);
        cached = SymbolFactProjections.SelectCanonicalFacts(rows).SingleOrDefault();
        canonicalSymbols[symbolId] = cached;
        return cached;
    }

    private static string LambdaOwner(string caller)
    {
        var marker = caller.IndexOf("~λ", StringComparison.Ordinal);
        return marker < 0 ? caller : caller[..marker];
    }

    private static bool HasGenericBinding(CallEdge edge) =>
        GenericSubstitution.ParseBindingTokens(edge.DeclaringTypeArgBinding).Count > 0
        || GenericSubstitution.ParseBindingTokens(edge.MethodTypeArgBinding).Count > 0;

    private bool ReserveWork(int units)
    {
        if (units == 0)
        {
            return !budgetExceeded;
        }

        workUnitsAttempted += units;
        if (budgetExceeded || workUnitsReserved + units > limits.MaxWorkUnits)
        {
            budgetExceeded = true;
            return false;
        }

        workUnitsReserved += units;
        return true;
    }

    private void ChargeAtomic(int rows)
    {
        if (rows == 0)
        {
            return;
        }

        workUnitsAttempted += rows;
        workUnitsReserved += rows;
        if (workUnitsReserved > limits.MaxWorkUnits)
        {
            budgetExceeded = true;
            atomicOvershoot = Math.Max(atomicOvershoot, workUnitsReserved - limits.MaxWorkUnits);
        }
    }

    private sealed class CountingView(DemandMonomorphizedCallSource owner, IFactGraphView inner) : IFactGraphView
    {
        public IReadOnlyList<ReferenceFact> ReferencesFrom(string enclosingSymbolId)
        {
            var rows = inner.ReferencesFrom(enclosingSymbolId);
            owner.forwardCallerLookupCalls++;
            owner.forwardCallerRowsRead += rows.Count;
            owner.ChargeAtomic(rows.Count);
            return rows;
        }

        public IReadOnlyList<ReferenceFact> ReferencesTo(string targetSymbolId) => inner.ReferencesTo(targetSymbolId);

        public IReadOnlyList<SymbolFact> SymbolsById(string symbolId)
        {
            var rows = inner.SymbolsById(symbolId);
            owner.symbolLookupCalls++;
            owner.symbolRowsRead += rows.Count;
            owner.ChargeAtomic(rows.Count);
            return rows;
        }

        public IReadOnlyList<SymbolFact> SymbolsByContainingSymbol(string containingSymbolId) =>
            inner.SymbolsByContainingSymbol(containingSymbolId);

        public IReadOnlyCollection<string> MethodSymbolIds => inner.MethodSymbolIds;

        public IReadOnlyList<SymbolFact> MethodsById(string symbolId) => inner.MethodsById(symbolId);

        public IReadOnlyList<SymbolFact> MethodsByContainingSymbol(string containingSymbolId)
        {
            var rows = inner.MethodsByContainingSymbol(containingSymbolId);
            owner.containingMethodLookupCalls++;
            owner.containingMethodRowsRead += rows.Count;
            owner.ChargeAtomic(rows.Count);
            return rows;
        }

        public IReadOnlyList<TypeRelationFact> TypeRelationsFrom(string typeSymbolId) => inner.TypeRelationsFrom(typeSymbolId);

        public IReadOnlyList<TypeRelationFact> TypeRelationsTo(string relatedSymbolId) => inner.TypeRelationsTo(relatedSymbolId);

        public IReadOnlyList<DispatchFact> DispatchFrom(string sourceMember)
        {
            var rows = inner.DispatchFrom(sourceMember);
            owner.dispatchLookupCalls++;
            owner.dispatchRowsRead += rows.Count;
            owner.ChargeAtomic(rows.Count);
            return rows;
        }

        public IReadOnlyList<DispatchFact> DispatchTo(string targetMember)
        {
            var rows = inner.DispatchTo(targetMember);
            owner.dispatchLookupCalls++;
            owner.dispatchRowsRead += rows.Count;
            owner.ChargeAtomic(rows.Count);
            return rows;
        }
    }
}
