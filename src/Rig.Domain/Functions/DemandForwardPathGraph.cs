using Rig.Domain.Data;

namespace Rig.Domain.Functions;

public sealed record DemandForwardGraphRules(
    ForwardCallProjectionRules Projection,
    IReadOnlyList<FactTraversalCutRule> Cut,
    IReadOnlyList<FactContextDispatchRule> Context
);

public sealed record DemandForwardGraphRequest(
    string FromPattern,
    int MaxDepth,
    FactPathFinder.TraversalMode Mode,
    DemandMonomorphizationLimits? Monomorphization = null
);

public sealed record DemandForwardStructureReads(
    DemandReadMetric MethodsById,
    DemandReadMetric MethodsByContainingType,
    DemandReadMetric TypeRelations,
    DemandReadMetric Dispatch
);

public sealed record DemandForwardClosureDiagnostics(int MatchedSeeds, int ExpandedCallers, int FixedPointPasses);

public enum DemandForwardLoadMode
{
    KeyedDemand,
    LegacyWholeGraphFallback,
}

public sealed record DemandForwardLoadDiagnostics(DemandForwardLoadMode Mode)
{
    public bool UsedLegacyFallback => Mode == DemandForwardLoadMode.LegacyWholeGraphFallback;
}

public sealed record DemandForwardGraphDiagnostics(
    DemandMonomorphizationDiagnostics Calls,
    DemandForwardStructureReads Structure,
    DemandForwardClosureDiagnostics Closure,
    DemandForwardLoadDiagnostics Load
)
{
    public static DemandForwardGraphDiagnostics LegacyFallback() =>
        new(
            Calls: new DemandMonomorphizationDiagnostics(
                Reads: new DemandReadDiagnostics(default, default, default, default),
                Adjacency: new DemandAdjacencyDiagnostics(0, 0, 0),
                Precision: new DemandPrecisionDiagnostics(0, 0, 0, 0, [], 0),
                Budget: new DemandBudgetDiagnostics(0, 0, 0, 0, false)
            ),
            Structure: new DemandForwardStructureReads(default, default, default, default),
            Closure: new DemandForwardClosureDiagnostics(0, 0, 0),
            Load: new DemandForwardLoadDiagnostics(DemandForwardLoadMode.LegacyWholeGraphFallback)
        );
}

public sealed record DemandForwardGraphResult(
    FactGraphData Graph,
    DemandForwardGraphDiagnostics Diagnostics,
    bool EventSubscriptionsClassified
);

// Materializes only the forward closure needed by one path query. Direct adjacency comes from the demand
// source; each fixed-point pass delegates reachability and dispatch to FactPathFinder, so there is no second
// dispatch implementation hidden in the loader.
public static class DemandForwardPathGraph
{
    public static DemandForwardGraphResult Build(IFactGraphView view, DemandForwardGraphRules rules, DemandForwardGraphRequest request)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(request);
        return new Builder(view, rules, request).Build();
    }

    private sealed class Builder
    {
        private readonly IFactGraphView view;
        private readonly DemandForwardGraphRules rules;
        private readonly DemandForwardGraphRequest request;
        private readonly DemandMonomorphizedCallSource calls;
        private readonly Dictionary<string, MethodRef> methods = new(StringComparer.Ordinal);
        private readonly HashSet<CallEdge> edges = [];
        private readonly HashSet<ImplementsEdge> implementations = [];
        private readonly HashSet<BaseEdge> bases = [];
        private readonly HashSet<DispatchFact> dispatch = [];
        private readonly HashSet<string> expandedCallers = new(StringComparer.Ordinal);
        private readonly HashSet<string> loadedMethodIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> loadedContainingTypes = new(StringComparer.Ordinal);
        private readonly HashSet<string> loadedRelationTypes = new(StringComparer.Ordinal);
        private readonly HashSet<string> loadedDispatchMembers = new(StringComparer.Ordinal);
        private int methodByIdCalls;
        private int methodByIdRows;
        private int containingMethodCalls;
        private int containingMethodRows;
        private int typeRelationCalls;
        private int typeRelationRows;
        private int dispatchCalls;
        private int dispatchRows;
        private int fixedPointPasses;

        internal Builder(IFactGraphView view, DemandForwardGraphRules rules, DemandForwardGraphRequest request)
        {
            this.view = view;
            this.rules = rules;
            this.request = request;
            calls = new DemandMonomorphizedCallSource(view, rules.Projection, request.Monomorphization);
        }

        internal DemandForwardGraphResult Build()
        {
            var catalog = view.MethodSymbolIds;
            var seeds = FactPathFinder.MatchNodes(catalog, request.FromPattern);
            foreach (var seed in seeds)
            {
                AddMethod(seed);
                // A matched interface/base method is itself a valid traversal seed. Load the same keyed
                // dispatch neighborhood an incoming call edge would have supplied so the existing engine
                // can dispatch that seed before walking an implementation body.
                AddDispatchNeighborhood(seed, receiverType: null);
            }

            while (seeds.Count > 0)
            {
                fixedPointPasses++;
                var graph = Snapshot();
                var reachable = FactPathFinder.Reaches(
                    graph,
                    request.FromPattern,
                    maxDepth: request.MaxDepth,
                    maxNodes: int.MaxValue,
                    mode: request.Mode
                );
                var next = reachable
                    .Where(item => item.Value < request.MaxDepth && !expandedCallers.Contains(item.Key))
                    .Select(item => item.Key)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray();
                if (next.Length == 0)
                {
                    break;
                }

                foreach (var caller in next)
                {
                    expandedCallers.Add(caller);
                    foreach (var edge in calls.CallsFrom(caller))
                    {
                        edges.Add(edge);
                        AddMethod(MonomorphizedNodeId.BaseOf(edge.Callee));
                        AddDispatchNeighborhood(edge.Callee, edge.ReceiverType);
                    }
                }
            }

            return new DemandForwardGraphResult(
                Graph: Snapshot(),
                Diagnostics: new DemandForwardGraphDiagnostics(
                    Calls: calls.DiagnosticsSnapshot(),
                    Structure: new DemandForwardStructureReads(
                        MethodsById: new DemandReadMetric(methodByIdCalls, methodByIdRows),
                        MethodsByContainingType: new DemandReadMetric(containingMethodCalls, containingMethodRows),
                        TypeRelations: new DemandReadMetric(typeRelationCalls, typeRelationRows),
                        Dispatch: new DemandReadMetric(dispatchCalls, dispatchRows)
                    ),
                    Closure: new DemandForwardClosureDiagnostics(
                        MatchedSeeds: seeds.Count,
                        ExpandedCallers: expandedCallers.Count,
                        FixedPointPasses: fixedPointPasses
                    ),
                    Load: new DemandForwardLoadDiagnostics(DemandForwardLoadMode.KeyedDemand)
                ),
                EventSubscriptionsClassified: rules.Projection.ClassifyEventSubscriptions
            );
        }

        private FactGraphData Snapshot() =>
            new(
                CallEdges: edges.OrderBy(edge => edge.Caller, StringComparer.Ordinal).ThenBy(edge => edge.Line).ToArray(),
                ImplementsEdges: implementations
                    .OrderBy(edge => edge.ImplType, StringComparer.Ordinal)
                    .ThenBy(edge => edge.InterfaceType, StringComparer.Ordinal)
                    .ToArray(),
                Methods: methods.Values.OrderBy(method => method.SymbolId, StringComparer.Ordinal).ToArray(),
                BaseEdges: bases
                    .OrderBy(edge => edge.SubType, StringComparer.Ordinal)
                    .ThenBy(edge => edge.BaseType, StringComparer.Ordinal)
                    .ToArray(),
                MinedDispatch: dispatch
                    .Select(fact => fact with { FilePath = "" })
                    .Distinct()
                    .OrderBy(fact => fact.SourceMember, StringComparer.Ordinal)
                    .ThenBy(fact => fact.TargetMember, StringComparer.Ordinal)
                    .ThenBy(fact => fact.Kind, StringComparer.Ordinal)
                    .ToArray(),
                CutRules: rules.Cut.Count == 0 ? null : rules.Cut,
                ContextRules: rules.Context.Count == 0 ? null : rules.Context
            );

        private void AddMethod(string methodId)
        {
            if (!loadedMethodIds.Add(methodId))
            {
                return;
            }

            var rows = view.MethodsById(methodId);
            methodByIdCalls++;
            methodByIdRows += rows.Count;
            foreach (var symbol in SymbolFactProjections.SelectCanonicalMethodFacts(rows))
            {
                methods[symbol.SymbolId] = SymbolFactProjections.ToMethodRef(symbol);
            }
        }

        private void AddDispatchNeighborhood(string memberId, string? receiverType)
        {
            var baseMember = MonomorphizedNodeId.BaseOf(memberId);
            AddExactDispatchClosure(baseMember);
            var method = methods.TryGetValue(baseMember, out var known) ? known : null;
            if (method?.ContainingTypeId is { } declaringType)
            {
                AddDescendantFamily(declaringType);
            }

            if (!string.IsNullOrWhiteSpace(receiverType))
            {
                var receiverId =
                    FactPathFinder.FactoryConstructTypeId(receiverType!)
                    ?? (receiverType!.StartsWith("T:", StringComparison.Ordinal) ? receiverType : "T:" + receiverType);
                AddDescendantFamily(receiverId);
            }
        }

        private void AddExactDispatchClosure(string sourceMember)
        {
            var pending = new Queue<string>();
            pending.Enqueue(sourceMember);
            while (pending.Count > 0)
            {
                var source = pending.Dequeue();
                if (!loadedDispatchMembers.Add(source))
                {
                    continue;
                }

                var rows = view.DispatchFrom(source);
                dispatchCalls++;
                dispatchRows += rows.Count;
                foreach (var fact in rows)
                {
                    if (fact.Kind == DispatchKinds.DelegateBind)
                    {
                        dispatch.Add(fact with { FilePath = "" });
                        AddMethod(fact.TargetMember);
                        continue;
                    }

                    if (fact.Kind is not (DispatchKinds.Impl or DispatchKinds.Override))
                    {
                        continue;
                    }

                    dispatch.Add(fact with { FilePath = "" });
                    AddMethod(fact.TargetMember);
                    pending.Enqueue(fact.TargetMember);
                }
            }
        }

        private void AddDescendantFamily(string rootType)
        {
            var pending = new Queue<string>();
            pending.Enqueue(rootType);
            while (pending.Count > 0)
            {
                var type = pending.Dequeue();
                if (!loadedRelationTypes.Add(type))
                {
                    continue;
                }

                AddMethodsByContainingType(type);
                var from = view.TypeRelationsFrom(type);
                typeRelationCalls++;
                typeRelationRows += from.Count;
                foreach (var relation in from)
                {
                    AddRelation(relation);
                }

                var to = view.DispatchRelationsTo(type);
                typeRelationCalls++;
                typeRelationRows += to.Count;
                foreach (var relation in to)
                {
                    AddRelation(relation);
                    pending.Enqueue(relation.TypeSymbolId);
                }
            }
        }

        private void AddMethodsByContainingType(string type)
        {
            if (!loadedContainingTypes.Add(type))
            {
                return;
            }

            var rows = view.MethodsByContainingSymbol(type);
            containingMethodCalls++;
            containingMethodRows += rows.Count;
            foreach (var symbol in SymbolFactProjections.SelectCanonicalMethodFacts(rows))
            {
                methods[symbol.SymbolId] = SymbolFactProjections.ToMethodRef(symbol);
            }
        }

        private void AddRelation(TypeRelationFact relation)
        {
            if (relation.RelationKind == RelationKinds.Interface)
            {
                implementations.Add(new ImplementsEdge(relation.TypeSymbolId, relation.RelatedSymbolId));
            }
            else if (relation.RelationKind == RelationKinds.Base)
            {
                bases.Add(new BaseEdge(relation.TypeSymbolId, relation.RelatedSymbolId));
            }
        }
    }
}
