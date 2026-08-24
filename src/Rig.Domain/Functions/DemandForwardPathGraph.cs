using System.Collections.Immutable;
using Rig.Domain.Data;

namespace Rig.Domain.Functions;

public sealed record DemandForwardGraphRules(
    ForwardCallProjectionRules Projection,
    IReadOnlyList<FactTraversalCutRule> Cut,
    IReadOnlyList<FactContextDispatchRule> Context,
    IReadOnlyList<DeliveryRule>? Delivery = null
);

public sealed record DemandForwardGraphRequest(
    string FromPattern,
    int MaxDepth,
    FactPathFinder.TraversalMode Mode,
    DemandMonomorphizationLimits? Monomorphization = null,
    int MaxNodes = 250_000
);

public sealed record DemandForwardStructureReads(
    DemandReadMetric MethodsById,
    DemandReadMetric MethodsByContainingType,
    DemandReadMetric TypeRelations,
    DemandReadMetric Dispatch
);

public sealed record DemandForwardClosureDiagnostics(int MatchedSeeds, int ExpandedCallers, int FixedPointPasses);

public sealed record DemandDeliveryDiagnostics(DemandReadMetric ReferencePartitions, int ChannelsProjected, int SitesProjected);

public sealed record DemandGraphOwnershipHints(ImmutableArray<string> SymbolIds, ImmutableArray<string> EmitterFilePaths)
{
    public static DemandGraphOwnershipHints Empty { get; } = new([], []);
}

public enum DemandForwardLoadMode
{
    KeyedDemand,
    LegacyWholeGraphFallback,

    // The whole projected call graph, materialized ONCE per fact generation and traversed by the shared
    // FactPathFinder — the same edge set the store walks out of `call_edges`, tagged by kind and filtered
    // by traversal MODE rather than projected per query. Distinct from LegacyWholeGraphFallback: that arm
    // is a flattened compatibility graph with NO delivery edges (hence its async decline), this one carries
    // them, so an async traversal over it is exact.
    MaterializedWholeGraph,
}

public sealed record DemandForwardLoadDiagnostics(DemandForwardLoadMode Mode)
{
    public bool UsedLegacyFallback => Mode == DemandForwardLoadMode.LegacyWholeGraphFallback;
}

public sealed record DemandForwardGraphDiagnostics(
    DemandMonomorphizationDiagnostics Calls,
    DemandForwardStructureReads Structure,
    DemandForwardClosureDiagnostics Closure,
    DemandForwardLoadDiagnostics Load,
    DemandDeliveryDiagnostics? Delivery = null
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

    // The same all-zero counters as LegacyFallback — no keyed read, no closure pass, no monomorphization
    // budget was spent, because nothing was projected on demand — under the honest load mode.
    public static DemandForwardGraphDiagnostics Materialized() =>
        LegacyFallback() with
        {
            Load = new DemandForwardLoadDiagnostics(DemandForwardLoadMode.MaterializedWholeGraph),
        };
}

public sealed record DemandForwardGraphResult(
    FactGraphData Graph,
    DemandForwardGraphDiagnostics Diagnostics,
    bool EventSubscriptionsClassified,
    DemandGraphOwnershipHints? Ownership = null
);

// A keyed, query-local projection of delivery sites. Event channels are read by exact E: identity;
// argument-addressed mechanisms read only the configured endpoint method partitions and then apply the
// shared DeliverySiteProjection filter. No whole-snapshot reference or event-site enumeration occurs.
internal sealed class DemandDeliverySiteSource
{
    private readonly IFactGraphView view;
    private readonly IReadOnlyList<DeliveryRule> rules;
    private readonly Dictionary<string, IReadOnlyList<ReferenceFact>> from = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<ReferenceFact>> to = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<ReferenceFact>> toMethod = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Tag, string Token), IReadOnlyList<DeliverySite>> channels = [];
    private readonly HashSet<string> ownershipSymbols = new(StringComparer.Ordinal);
    private readonly HashSet<string> ownershipPaths = new(StringComparer.Ordinal);
    private int lookupCalls;
    private int rowsRead;
    private int sitesProjected;

    internal DemandDeliverySiteSource(IFactGraphView view, IReadOnlyList<DeliveryRule>? rules)
    {
        this.view = view;
        this.rules = rules ?? [];
        ValidateRules(this.rules);
    }

    internal bool Enabled => rules.Count > 0;

    internal bool HasEventRules => DeliverySiteProjection.EventRules(rules).Count > 0;

    internal IReadOnlyList<DeliverySite> SitesFromCaller(string caller) => Project(ReadFrom(caller));

    internal IReadOnlyList<DeliverySite> SitesForChannel(DeliverySite seed)
    {
        var key = (seed.Tag, seed.IdentityToken);
        if (channels.TryGetValue(key, out var cached))
        {
            return cached;
        }

        IReadOnlyList<ReferenceFact> rows;
        if (seed.IdentityToken.StartsWith("E:", StringComparison.Ordinal))
        {
            rows = ReadTo(seed.IdentityToken);
        }
        else
        {
            var gathered = new List<ReferenceFact>();
            foreach (var rule in rules.Where(rule => string.Equals(rule.Tag, seed.Tag, StringComparison.Ordinal)))
            {
                AddEndpointRows(rule.Producer, gathered);
                AddEndpointRows(rule.Registration, gathered);
            }
            rows = gathered;
        }

        cached = Project(rows)
            .Where(site =>
                string.Equals(site.Tag, seed.Tag, StringComparison.Ordinal)
                && string.Equals(site.IdentityToken, seed.IdentityToken, StringComparison.Ordinal)
            )
            .Distinct()
            .ToArray();
        channels[key] = cached;
        sitesProjected += cached.Count;
        return cached;
    }

    internal DemandDeliveryDiagnostics Diagnostics() => new(new DemandReadMetric(lookupCalls, rowsRead), channels.Count, sitesProjected);

    internal DemandGraphOwnershipHints Ownership() =>
        new(
            ownershipSymbols.OrderBy(id => id, StringComparer.Ordinal).ToImmutableArray(),
            ownershipPaths.OrderBy(path => path, StringComparer.Ordinal).ToImmutableArray()
        );

    private IReadOnlyList<DeliverySite> Project(IReadOnlyList<ReferenceFact> rows)
    {
        var eventReads = rows.Where(row =>
                row.RefKind == RefKinds.Read
                && row.TargetSymbolId.StartsWith("E:", StringComparison.Ordinal)
                && row.EnclosingSymbolId is not null
            )
            .Select(row => new DeliverySiteProjection.EventRead(row.EnclosingSymbolId, row.FilePath, row.Line, row.TargetSymbolId))
            .ToArray();
        var argInvocations = rows.Where(row =>
                row.RefKind == RefKinds.Invocation && row.EnclosingSymbolId is not null && row.FirstArgumentName is not null
            )
            .Select(row => new DeliverySiteProjection.ArgInvocation(
                row.EnclosingSymbolId,
                row.FilePath,
                row.Line,
                row.FirstArgumentName,
                row.TargetSymbolId
            ))
            .ToArray();
        return DeliverySiteProjection.Project(rules, eventReads, argInvocations);
    }

    private IReadOnlyList<ReferenceFact> ReadFrom(string caller)
    {
        if (!from.TryGetValue(caller, out var rows))
        {
            rows = view.ReferencesFrom(caller);
            from[caller] = rows;
            Count(rows);
        }
        return rows;
    }

    private IReadOnlyList<ReferenceFact> ReadTo(string target)
    {
        if (!to.TryGetValue(target, out var rows))
        {
            rows = view.ReferencesTo(target);
            to[target] = rows;
            Count(rows);
        }
        return rows;
    }

    private IReadOnlyList<ReferenceFact> ReadToMethod(string key)
    {
        if (!toMethod.TryGetValue(key, out var rows))
        {
            rows = view.ReferencesToMethodKey(key);
            toMethod[key] = rows;
            Count(rows);
        }
        return rows;
    }

    private void AddEndpointRows(DeliveryEndpoint endpoint, List<ReferenceFact> destination)
    {
        if (!string.Equals(endpoint.Source, "arg", StringComparison.Ordinal))
        {
            return;
        }
        foreach (var type in endpoint.DeclaringTypes ?? [])
        {
            foreach (var method in endpoint.Methods ?? [])
            {
                destination.AddRange(ReadToMethod(ReferenceTargetMethodKey.Normalize($"{type}.{method}")));
            }
        }
    }

    private void Count(IReadOnlyList<ReferenceFact> rows)
    {
        lookupCalls++;
        rowsRead += rows.Count;
        foreach (var row in rows)
        {
            if (!string.IsNullOrWhiteSpace(row.TargetSymbolId))
            {
                ownershipSymbols.Add(row.TargetSymbolId);
            }
            if (!string.IsNullOrWhiteSpace(row.EnclosingSymbolId))
            {
                ownershipSymbols.Add(row.EnclosingSymbolId!);
            }
            if (!string.IsNullOrWhiteSpace(row.FilePath))
            {
                ownershipPaths.Add(row.FilePath);
            }
        }
    }

    private static void ValidateRules(IReadOnlyList<DeliveryRule> configured)
    {
        foreach (var rule in configured)
        {
            var sources = new[] { rule.Producer.Source, rule.Registration.Source };
            if (sources.Any(source => source is not ("event-symbol" or "arg")))
            {
                throw new DemandForwardGraphUnavailableException($"delivery rule '{rule.Id}' uses an unsupported source");
            }
            if (sources.Contains("event-symbol", StringComparer.Ordinal) && !sources.All(source => source == "event-symbol"))
            {
                throw new DemandForwardGraphUnavailableException($"delivery rule '{rule.Id}' mixes incompatible channel sources");
            }
            if (
                new[] { rule.Producer, rule.Registration }.Any(endpoint =>
                    string.Equals(endpoint.Source, "arg", StringComparison.Ordinal) && endpoint.ArgumentIndex != 0
                )
            )
            {
                throw new DemandForwardGraphUnavailableException(
                    $"delivery rule '{rule.Id}' requests a nonzero argument; resident facts expose only argument 0"
                );
            }
            foreach (var endpoint in new[] { rule.Producer, rule.Registration })
            {
                var supported = endpoint.Source switch
                {
                    "event-symbol" => endpoint.Resolve == "symbol",
                    "arg" => endpoint.Resolve is "path" or "leaf",
                    _ => false,
                };
                if (!supported)
                {
                    throw new DemandForwardGraphUnavailableException(
                        $"delivery rule '{rule.Id}' uses unsupported resolve '{endpoint.Resolve}' for source '{endpoint.Source}'"
                    );
                }
            }
        }
    }
}

public sealed class DemandForwardGraphUnavailableException(string message) : InvalidOperationException(message);

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
        private readonly DemandDeliverySiteSource delivery;
        private readonly bool deferEventClassification;
        private readonly Dictionary<string, MethodRef> methods = new(StringComparer.Ordinal);
        private readonly HashSet<CallEdge> edges = [];
        private readonly HashSet<DeliverySite> deliverySites = [];
        private readonly HashSet<ImplementsEdge> implementations = [];
        private readonly HashSet<BaseEdge> bases = [];
        private readonly HashSet<DispatchFact> dispatch = [];
        private readonly HashSet<string> expandedCallers = new(StringComparer.Ordinal);
        private readonly HashSet<string> loadedMethodIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> loadedContainingTypes = new(StringComparer.Ordinal);
        private readonly HashSet<string> loadedRelationTypes = new(StringComparer.Ordinal);
        private readonly HashSet<string> loadedDispatchMembers = new(StringComparer.Ordinal);
        private readonly HashSet<string> materializedNodes = new(StringComparer.Ordinal);
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
            if (request.MaxDepth < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "Maximum depth must be non-negative.");
            }
            if (request.MaxNodes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "Maximum node count must be positive.");
            }
            delivery = new DemandDeliverySiteSource(view, rules.Delivery);
            deferEventClassification =
                request.Mode != FactPathFinder.TraversalMode.SyncCut
                && rules.Projection.ClassifyEventSubscriptions
                && delivery.HasEventRules;
            calls = new DemandMonomorphizedCallSource(
                view,
                deferEventClassification ? rules.Projection with { ClassifyEventSubscriptions = false } : rules.Projection,
                request.Monomorphization
            );
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
                    maxNodes: request.MaxNodes,
                    mode: request.Mode
                );
                if (reachable.Count >= request.MaxNodes)
                {
                    throw new DemandForwardGraphUnavailableException(
                        $"Keyed forward closure reached {reachable.Count} nodes, at the {request.MaxNodes} node cap. Raise it with --max-nodes <n> (0 = uncapped), or narrow the query with --depth <n>."
                    );
                }
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
                        AddEdge(edge);
                        AddMethod(MonomorphizedNodeId.BaseOf(edge.Callee));
                        AddDispatchNeighborhood(edge.Callee, edge.ReceiverType);
                    }
                    ExpandDeliveryChannels(caller);
                }
            }

            var callDiagnostics = calls.DiagnosticsSnapshot();
            if (
                callDiagnostics.Budget.Exceeded
                || callDiagnostics.Precision.CappedMethodIds.Length > 0
                || callDiagnostics.Precision.BudgetFallbackEdges > 0
            )
            {
                throw new DemandForwardGraphUnavailableException(
                    "Keyed forward generic projection exceeded its exact monomorphization limits. Raise it with --max-generic-work <n> (0 = uncapped), or narrow the query with --depth <n>."
                );
            }

            return new DemandForwardGraphResult(
                Graph: Snapshot(),
                Diagnostics: new DemandForwardGraphDiagnostics(
                    Calls: callDiagnostics,
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
                    Load: new DemandForwardLoadDiagnostics(DemandForwardLoadMode.KeyedDemand),
                    Delivery: delivery.Diagnostics()
                ),
                EventSubscriptionsClassified: rules.Projection.ClassifyEventSubscriptions,
                Ownership: delivery.Ownership()
            );
        }

        private FactGraphData Snapshot()
        {
            FactGraphData graph = new(
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
            if (deliverySites.Count > 0)
            {
                graph = FactPathFinder.AddDeliveryEdges(graph, deliverySites.ToArray());
            }
            if (deferEventClassification)
            {
                graph = FactPathFinder.MarkEventSubscriptionHandoffs(
                    graph,
                    deliverySites
                        .Where(site => site.IdentityToken.StartsWith("E:", StringComparison.Ordinal))
                        .Select(site => new EventSubscriptionSite(site.Caller, site.FilePath, site.Line))
                        .ToHashSet()
                );
            }
            return graph;
        }

        private void ExpandDeliveryChannels(string caller)
        {
            if (request.Mode == FactPathFinder.TraversalMode.SyncCut || !delivery.Enabled)
            {
                return;
            }
            foreach (var local in delivery.SitesFromCaller(MonomorphizedNodeId.BaseOf(caller)))
            {
                foreach (var site in delivery.SitesForChannel(local))
                {
                    deliverySites.Add(site);
                    AddMethod(MonomorphizedNodeId.BaseOf(site.Caller));
                    if (site.Role != DeliveryRole.Producer)
                    {
                        MaterializeDeliveryRegistration(site);
                    }
                }
            }
        }

        private void MaterializeDeliveryRegistration(DeliverySite site)
        {
            var handlers = calls
                .CallsFrom(site.Caller)
                .Where(edge =>
                    string.Equals(edge.FilePath, site.FilePath, StringComparison.OrdinalIgnoreCase)
                    && edge.Line == site.Line
                    && (
                        site.HandlerDispatcher is { } dispatcher
                            ? edge.Kind == EdgeKinds.Handoff && string.Equals(edge.HandoffDispatcher, dispatcher, StringComparison.Ordinal)
                            : edge.Kind == EdgeKinds.MethodGroup
                    )
                )
                .ToArray();
            foreach (var edge in handlers)
            {
                AddEdge(edge);
                if (view.MethodsById(MonomorphizedNodeId.BaseOf(edge.Callee)).Count == 0)
                {
                    throw new DemandForwardGraphUnavailableException(
                        $"delivery registration target '{edge.Callee}' has no resident method declaration"
                    );
                }
                AddMethod(MonomorphizedNodeId.BaseOf(edge.Callee));
                AddDispatchNeighborhood(edge.Callee, edge.ReceiverType);
            }
        }

        private void AddEdge(CallEdge edge)
        {
            AdmitNode(edge.Caller);
            AdmitNode(edge.Callee);
            edges.Add(edge);
        }

        private void AdmitNode(string node)
        {
            if (materializedNodes.Contains(node))
            {
                return;
            }
            if (materializedNodes.Count >= request.MaxNodes)
            {
                throw new DemandForwardGraphUnavailableException(
                    $"Keyed forward graph materialization admitted {materializedNodes.Count} nodes, hitting the {request.MaxNodes} node cap. Raise it with --max-nodes <n> (0 = uncapped), or narrow the query with --depth <n>."
                );
            }
            materializedNodes.Add(node);
        }

        private void AddMethod(string methodId)
        {
            AdmitNode(methodId);
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
                AdmitNode(symbol.SymbolId);
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
