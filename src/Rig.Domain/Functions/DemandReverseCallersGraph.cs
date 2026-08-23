using System.Collections.Immutable;
using Rig.Domain.Data;

namespace Rig.Domain.Functions;

public sealed record DemandReverseCallersGraphRequest(
    string ToPattern,
    int MaxDepth,
    FactPathFinder.TraversalMode DiscoveryMode,
    int MaxNodes = 20_000,
    DemandMonomorphizationLimits? Monomorphization = null,
    FactPathFinder.TraversalMode? ExecutionMode = null
)
{
    // Compatibility for planner/build callers that historically supplied only the discovery lens. Query
    // execution supplies this explicitly because sync human --entrypoints deliberately discovers AsyncExact.
    public FactPathFinder.TraversalMode EffectiveExecutionMode => ExecutionMode ?? DiscoveryMode;
}

public sealed record DemandReverseKeyedReads(
    DemandReadMetric ReferencesTo,
    DemandReadMetric MethodsById,
    DemandReadMetric MethodsByContainingType,
    DemandReadMetric TypeRelations,
    DemandReadMetric Dispatch
);

public sealed record DemandReverseClosureDiagnostics(
    int MatchedTargets,
    int ExpandedNodes,
    int MaterializedCallerPartitions,
    int FixedPointPasses
);

public sealed record DemandReverseOwnershipHints(ImmutableArray<string> SymbolIds, ImmutableArray<string> EmitterFilePaths);

public enum DemandReverseLoadMode
{
    KeyedDemand,
    LegacyWholeGraphFallback,
}

public sealed record DemandReverseLoadDiagnostics(DemandReverseLoadMode Mode)
{
    public bool UsedLegacyFallback => Mode == DemandReverseLoadMode.LegacyWholeGraphFallback;
}

public sealed record DemandReverseCallersGraphDiagnostics(
    DemandMonomorphizationDiagnostics Calls,
    DemandReverseKeyedReads Reverse,
    DemandReverseClosureDiagnostics Closure,
    DemandReverseLoadDiagnostics Load,
    bool DeliverySitesSynthesized,
    DemandDeliveryDiagnostics? Delivery = null
)
{
    public static DemandReverseCallersGraphDiagnostics LegacyFallback() =>
        new(
            new DemandMonomorphizationDiagnostics(
                new DemandReadDiagnostics(default, default, default, default),
                new DemandAdjacencyDiagnostics(0, 0, 0),
                new DemandPrecisionDiagnostics(0, 0, 0, 0, [], 0),
                new DemandBudgetDiagnostics(0, 0, 0, 0, false)
            ),
            new DemandReverseKeyedReads(default, default, default, default, default),
            new DemandReverseClosureDiagnostics(0, 0, 0, 0),
            new DemandReverseLoadDiagnostics(DemandReverseLoadMode.LegacyWholeGraphFallback),
            DeliverySitesSynthesized: false
        );
}

public sealed record DemandReverseCallersGraphResult(
    FactGraphData Graph,
    DemandReverseCallersGraphDiagnostics Diagnostics,
    ImmutableArray<string> TargetIds,
    DemandReverseOwnershipHints Ownership,
    bool EventSubscriptionsClassified
);

// A cap or unsupported traversal shape must never escape as a partial graph that a caller could mistake
// for exact. The exception is deliberately typed so a live host can decline/fallback without parsing text.
public sealed class DemandReverseCallersGraphUnavailableException(string message) : InvalidOperationException(message);

// Materializes the keyed reverse candidate closure for callers. Discovery reads reverse fact partitions,
// but reachability, roots, receiver-narrowed dispatch, cuts and forward confirmation remain exclusively in
// FactPathFinder over the returned graph. Async delivery is synthesized from keyed channel and endpoint
// partitions; the resident graph and the whole event-site corpus are never flattened.
public static class DemandReverseCallersGraph
{
    public static DemandReverseCallersGraphResult Build(
        IFactGraphView view,
        DemandForwardGraphRules rules,
        DemandReverseCallersGraphRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxDepth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Maximum depth must be non-negative.");
        }
        if (request.MaxNodes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Maximum node count must be positive.");
        }
        return new Builder(view, rules, request).Build();
    }

    private sealed class Builder
    {
        private readonly IFactGraphView view;
        private readonly DemandForwardGraphRules rules;
        private readonly DemandReverseCallersGraphRequest request;
        private readonly DemandMonomorphizedCallSource calls;
        private readonly DemandDeliverySiteSource delivery;
        private readonly bool deferEventClassification;
        private readonly Dictionary<string, MethodRef> methods = new(StringComparer.Ordinal);
        private readonly HashSet<CallEdge> edges = [];
        private readonly HashSet<DeliverySite> deliverySites = [];
        private readonly HashSet<ImplementsEdge> implementations = [];
        private readonly HashSet<BaseEdge> bases = [];
        private readonly HashSet<DispatchFact> dispatch = [];
        private readonly HashSet<string> materializedNodes = new(StringComparer.Ordinal);
        private readonly HashSet<string> ownershipSymbols = new(StringComparer.Ordinal);
        private readonly HashSet<string> ownershipPaths = new(StringComparer.Ordinal);
        private readonly HashSet<string> expandedNodes = new(StringComparer.Ordinal);
        private readonly HashSet<string> loadedCallerPartitions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<ReferenceFact>> incomingRows = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<ReferenceFact>> incomingMethodRows = new(StringComparer.Ordinal);
        private readonly HashSet<(string LookupKind, string RawTarget, string ProjectedDestination)> processedIncoming = [];
        private readonly HashSet<string> loadedMethodIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> loadedContainingTypes = new(StringComparer.Ordinal);
        private readonly HashSet<string> loadedRelationTypes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<DispatchFact>> dispatchFromRows = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<DispatchFact>> dispatchToRows = new(StringComparer.Ordinal);
        private int referencesToCalls;
        private int referencesToRows;
        private int methodByIdCalls;
        private int methodByIdRows;
        private int containingMethodCalls;
        private int containingMethodRows;
        private int typeRelationCalls;
        private int typeRelationRows;
        private int dispatchCalls;
        private int dispatchRows;
        private int fixedPointPasses;

        internal Builder(IFactGraphView view, DemandForwardGraphRules rules, DemandReverseCallersGraphRequest request)
        {
            this.view = view;
            this.rules = rules;
            this.request = request;
            delivery = new DemandDeliverySiteSource(view, rules.Delivery);
            deferEventClassification =
                request.DiscoveryMode != FactPathFinder.TraversalMode.SyncCut
                && rules.Projection.ClassifyEventSubscriptions
                && delivery.HasEventRules;
            calls = new DemandMonomorphizedCallSource(
                view,
                deferEventClassification ? rules.Projection with { ClassifyEventSubscriptions = false } : rules.Projection,
                request.Monomorphization
            );
        }

        internal DemandReverseCallersGraphResult Build()
        {
            var targets = FactPathFinder
                .MatchNodes(view.MethodSymbolIds, request.ToPattern)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToImmutableArray();
            foreach (var target in targets)
            {
                AddMethod(MonomorphizedNodeId.BaseOf(target));
            }

            while (true)
            {
                fixedPointPasses++;
                var graph = Snapshot();
                var reached = FactPathFinder.ReachedBy(
                    graph,
                    request.ToPattern,
                    maxDepth: request.MaxDepth,
                    maxNodes: int.MaxValue,
                    narrowDispatch: true,
                    mode: request.DiscoveryMode
                );
                if (reached.Count > request.MaxNodes)
                {
                    throw new DemandReverseCallersGraphUnavailableException(
                        $"Keyed reverse closure exceeded the {request.MaxNodes} node cap."
                    );
                }

                // Expand the depth boundary as well. EntryRootsReaching asks whether a returned boundary
                // node has a predecessor one hop beyond the closure, so that edge must be present even though
                // its caller is not returned at depth <= MaxDepth.
                var next = reached.Keys.Where(id => !expandedNodes.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToArray();
                if (next.Length == 0)
                {
                    break;
                }

                foreach (var node in next)
                {
                    expandedNodes.Add(node);
                    ExpandIncoming(node);
                }
            }

            var callDiagnostics = calls.DiagnosticsSnapshot();
            if (
                callDiagnostics.Budget.Exceeded
                || callDiagnostics.Precision.CappedMethodIds.Length > 0
                || callDiagnostics.Precision.BudgetFallbackEdges > 0
            )
            {
                throw new DemandReverseCallersGraphUnavailableException(
                    "Keyed reverse generic projection exceeded its exact monomorphization limits."
                );
            }

            return new DemandReverseCallersGraphResult(
                Snapshot(),
                new DemandReverseCallersGraphDiagnostics(
                    callDiagnostics,
                    new DemandReverseKeyedReads(
                        new DemandReadMetric(referencesToCalls, referencesToRows),
                        new DemandReadMetric(methodByIdCalls, methodByIdRows),
                        new DemandReadMetric(containingMethodCalls, containingMethodRows),
                        new DemandReadMetric(typeRelationCalls, typeRelationRows),
                        new DemandReadMetric(dispatchCalls, dispatchRows)
                    ),
                    new DemandReverseClosureDiagnostics(
                        targets.Length,
                        expandedNodes.Count,
                        loadedCallerPartitions.Count,
                        fixedPointPasses
                    ),
                    new DemandReverseLoadDiagnostics(DemandReverseLoadMode.KeyedDemand),
                    DeliverySitesSynthesized: request.DiscoveryMode != FactPathFinder.TraversalMode.SyncCut && delivery.Enabled,
                    Delivery: delivery.Diagnostics()
                ),
                targets,
                MergeOwnership(delivery.Ownership()),
                rules.Projection.ClassifyEventSubscriptions
            );
        }

        private DemandReverseOwnershipHints MergeOwnership(DemandGraphOwnershipHints deliveryOwnership) =>
            new(
                ownershipSymbols
                    .Concat(deliveryOwnership.SymbolIds)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .Distinct(StringComparer.Ordinal)
                    .ToImmutableArray(),
                ownershipPaths
                    .Concat(deliveryOwnership.EmitterFilePaths)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .Distinct(StringComparer.Ordinal)
                    .ToImmutableArray()
            );

        private void ExpandIncoming(string node)
        {
            var baseNode = MonomorphizedNodeId.BaseOf(node);
            AddProjectedIncoming(baseNode);

            foreach (var hub in ReverseDispatchSources(baseNode))
            {
                // Redirect/factory projection happens before dispatch in the full graph. A discovered
                // external hatch therefore needs the same family inverse as the original target; an
                // exact ReferencesTo(hub) probe cannot see convenience-overload rows rewritten to it.
                AddProjectedIncoming(hub);
                // A registration can bind to the interface/base member while the reverse seed is a
                // concrete implementation. Delivery is co-located with that registration row, so probe
                // every one-hop dispatch hub as well as the concrete seed. This is discovery only; the
                // caller-local forward confirmation below still decides which projected edge is admitted.
                ExpandDeliveryIncoming(hub);
            }

            ExpandDeliveryIncoming(baseNode);
        }

        private void ExpandDeliveryIncoming(string handler)
        {
            if (request.DiscoveryMode == FactPathFinder.TraversalMode.SyncCut || !delivery.Enabled)
            {
                return;
            }

            if (!incomingRows.TryGetValue(handler, out var incoming))
            {
                incoming = view.ReferencesTo(handler);
                incomingRows[handler] = incoming;
                referencesToCalls++;
                referencesToRows += incoming.Count;
            }
            foreach (
                var handlerReference in incoming.Where(row =>
                    row.RefKind == RefKinds.MethodGroup && !string.IsNullOrWhiteSpace(row.EnclosingSymbolId)
                )
            )
            {
                var registrationSites = delivery
                    .SitesFromCaller(handlerReference.EnclosingSymbolId!)
                    .Where(site =>
                        string.Equals(site.FilePath, handlerReference.FilePath, StringComparison.OrdinalIgnoreCase)
                        && site.Line == handlerReference.Line
                        && site.Role != DeliveryRole.Producer
                    )
                    .ToArray();
                foreach (var registration in registrationSites)
                {
                    foreach (var site in delivery.SitesForChannel(registration))
                    {
                        deliverySites.Add(site);
                        AddMethod(MonomorphizedNodeId.BaseOf(site.Caller));
                        if (site.Role != DeliveryRole.Producer)
                        {
                            MaterializeCallerPartition(site.Caller, calls.CallsFrom(site.Caller));
                        }
                    }
                }
            }
        }

        private void AddProjectedIncoming(string baseNode)
        {
            AddIncomingKey(baseNode, baseNode);

            foreach (var redirect in rules.Projection.Redirect ?? [])
            {
                if (string.Equals(redirect.RedirectTo, baseNode, StringComparison.Ordinal))
                {
                    AddIncomingMethodKey(ReferenceTargetMethodKey.Normalize(redirect.Method), baseNode);
                }
            }

            // A factory-rewritten edge is stored under a generic overload family, not the constructed
            // destination. The normalized inverse supplies that bounded family; caller-local projection
            // filters unrelated overload rows before graph materialization.
            foreach (var factory in rules.Projection.Factory ?? [])
            {
                AddIncomingMethodKey(ReferenceTargetMethodKey.Normalize(factory.Method), baseNode);
            }
        }

        private void AddIncomingKey(string target, string projectedDestination)
        {
            if (!processedIncoming.Add(("exact", target, projectedDestination)))
            {
                return;
            }

            if (!incomingRows.TryGetValue(target, out var rows))
            {
                rows = view.ReferencesTo(target);
                incomingRows[target] = rows;
                referencesToCalls++;
                referencesToRows += rows.Count;
            }
            foreach (var group in CallerGroups(rows))
            {
                if (AddCallerPartition(group.Key, projectedDestination))
                {
                    AddRawOwnership(group);
                }
            }
        }

        private void AddIncomingMethodKey(string methodKey, string projectedDestination)
        {
            if (!processedIncoming.Add(("method", methodKey, projectedDestination)))
            {
                return;
            }

            if (!incomingMethodRows.TryGetValue(methodKey, out var rows))
            {
                rows = view.ReferencesToMethodKey(methodKey);
                incomingMethodRows[methodKey] = rows;
                referencesToCalls++;
                referencesToRows += rows.Count;
            }
            foreach (var group in CallerGroups(rows))
            {
                if (AddCallerPartition(group.Key, projectedDestination))
                {
                    AddRawOwnership(group);
                }
            }
        }

        private bool AddCallerPartition(string caller, string expectedCallee)
        {
            var projected = calls.CallsFrom(caller);
            if (!projected.Any(edge => string.Equals(MonomorphizedNodeId.BaseOf(edge.Callee), expectedCallee, StringComparison.Ordinal)))
            {
                return false;
            }
            MaterializeCallerPartition(caller, projected);
            return true;
        }

        private void MaterializeCallerPartition(string caller, IReadOnlyList<CallEdge> projected)
        {
            if (!loadedCallerPartitions.Add(caller))
            {
                return;
            }

            AddMethod(MonomorphizedNodeId.BaseOf(caller));
            var synthetic = new List<string>();
            foreach (var edge in projected)
            {
                AddEdge(edge);
                var callee = MonomorphizedNodeId.BaseOf(edge.Callee);
                AddMethod(callee);
                AddDispatchNeighborhood(callee, edge.ReceiverType);
                // ReferencesTo is keyed by the base raw symbol and can never be queried with a synthetic
                // ~mono id. Once a caller admits an instantiation, expand that synthetic caller immediately
                // so its cloned body (and any nested mono/lambda variants) is available to reverse traversal
                // and forward confirmation.
                if (MonomorphizedNodeId.IsMonomorphized(edge.Callee))
                {
                    synthetic.Add(edge.Callee);
                }
            }
            AddSyntheticCallerPartitions(synthetic);
        }

        private static IEnumerable<IGrouping<string, ReferenceFact>> CallerGroups(IReadOnlyList<ReferenceFact> rows) =>
            rows.Where(row => !string.IsNullOrWhiteSpace(row.EnclosingSymbolId))
                .GroupBy(row => row.EnclosingSymbolId!, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal);

        private void AddRawOwnership(IEnumerable<ReferenceFact> rows)
        {
            foreach (var row in rows)
            {
                AddOwnershipSymbol(row.TargetSymbolId);
                AddOwnershipPath(row.FilePath);
            }
        }

        private void AddSyntheticCallerPartitions(IEnumerable<string> seeds)
        {
            var pending = new Queue<string>(seeds);
            while (pending.Count > 0)
            {
                var caller = pending.Dequeue();
                if (!loadedCallerPartitions.Add(caller))
                {
                    continue;
                }

                AdmitNode(caller);
                AddMethod(MonomorphizedNodeId.BaseOf(caller));
                foreach (var edge in calls.CallsFrom(caller))
                {
                    AddEdge(edge);
                    var callee = MonomorphizedNodeId.BaseOf(edge.Callee);
                    AddMethod(callee);
                    AddDispatchNeighborhood(callee, edge.ReceiverType);
                    if (MonomorphizedNodeId.IsMonomorphized(edge.Callee))
                    {
                        pending.Enqueue(edge.Callee);
                    }
                }
            }
        }

        private IReadOnlyList<string> ReverseDispatchSources(string target)
        {
            var sources = new SortedSet<string>(StringComparer.Ordinal);
            var pendingMembers = new Queue<(string Member, string? Kind)>();
            var visited = new HashSet<(string Member, string? Kind)>();
            pendingMembers.Enqueue((target, null));
            while (pendingMembers.Count > 0)
            {
                var (member, requiredKind) = pendingMembers.Dequeue();
                if (!visited.Add((member, requiredKind)))
                {
                    continue;
                }
                foreach (var fact in DispatchTo(member))
                {
                    switch (fact.Kind)
                    {
                        case DispatchKinds.Impl:
                        case DispatchKinds.Override:
                            if (requiredKind is not null && fact.Kind != requiredKind)
                            {
                                continue;
                            }
                            AddDispatchFact(fact);
                            AddMethod(fact.SourceMember);
                            sources.Add(fact.SourceMember);
                            pendingMembers.Enqueue((fact.SourceMember, fact.Kind));
                            break;
                        case DispatchKinds.DelegateBind:
                            AddDispatchFact(fact);
                            AddMethod(fact.SourceMember);
                            sources.Add(fact.SourceMember);
                            break;
                        case DispatchKinds.DelegateFieldBind:
                            AddDelegateFieldCallers(fact.SourceMember, member);
                            break;
                    }
                }
            }

            AddHeuristicDispatchSources(target, sources);
            foreach (var source in sources)
            {
                AddDispatchNeighborhood(source, receiverType: null);
            }
            return sources.ToArray();
        }

        private void AddDelegateFieldCallers(string slot, string callable)
        {
            foreach (var fact in DispatchFrom(slot))
            {
                if (
                    fact.Kind
                    is not (DispatchKinds.DelegateFieldBind or DispatchKinds.DelegateFieldInvoke or DispatchKinds.DelegateFieldEscape)
                )
                {
                    continue;
                }

                AddDispatchFact(fact);
                if (fact.Kind == DispatchKinds.DelegateFieldInvoke)
                {
                    AddCallerPartition(fact.TargetMember, callable);
                }
            }
        }

        private void AddHeuristicDispatchSources(string target, SortedSet<string> sources)
        {
            var targetMethod = methods.GetValueOrDefault(target);
            if (targetMethod?.ContainingTypeId is not { } targetType)
            {
                return;
            }

            var pending = new Queue<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            pending.Enqueue(targetType);
            while (pending.Count > 0)
            {
                var type = pending.Dequeue();
                if (!seen.Add(type))
                {
                    continue;
                }

                foreach (var relation in TypeRelationsFrom(type))
                {
                    AddRelation(relation);
                    pending.Enqueue(relation.RelatedSymbolId);
                    if (relation.RelatedSymbolId.StartsWith("!:", StringComparison.Ordinal))
                    {
                        AddErrorInterfaceSources(relation.RelatedSymbolId, target, sources);
                    }
                    foreach (var candidate in MethodsByContainingType(relation.RelatedSymbolId))
                    {
                        if (SameDispatchShape(target, candidate.SymbolId))
                        {
                            sources.Add(candidate.SymbolId);
                        }
                    }
                }
            }
        }

        private void AddErrorInterfaceSources(string unresolvedInterface, string target, SortedSet<string> sources)
        {
            var simpleName = DispatchRelationKeys.SimpleTypeName(unresolvedInterface);
            foreach (var candidateId in view.MethodSymbolIds)
            {
                if (
                    SameDispatchShape(target, candidateId)
                    && string.Equals(
                        DispatchRelationKeys.SimpleTypeName(DeclaringTypeId(candidateId)),
                        simpleName,
                        StringComparison.Ordinal
                    )
                )
                {
                    AddMethod(candidateId);
                    sources.Add(candidateId);
                }
            }
        }

        private void AddDispatchNeighborhood(string memberId, string? receiverType)
        {
            var baseMember = MonomorphizedNodeId.BaseOf(memberId);
            AddExactDispatchClosure(baseMember);
            if (methods.TryGetValue(baseMember, out var method) && method.ContainingTypeId is { } declaringType)
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
            var pending = new Queue<(string Member, string? Kind)>();
            var visited = new HashSet<(string Member, string? Kind)>();
            pending.Enqueue((sourceMember, null));
            while (pending.Count > 0)
            {
                var (source, requiredKind) = pending.Dequeue();
                if (!visited.Add((source, requiredKind)))
                {
                    continue;
                }

                foreach (var fact in DispatchFrom(source))
                {
                    if (fact.Kind == DispatchKinds.DelegateBind)
                    {
                        AddDispatchFact(fact);
                        AddMethod(fact.TargetMember);
                        continue;
                    }
                    if (fact.Kind is not (DispatchKinds.Impl or DispatchKinds.Override))
                    {
                        continue;
                    }
                    if (requiredKind is not null && fact.Kind != requiredKind)
                    {
                        continue;
                    }

                    AddDispatchFact(fact);
                    AddMethod(fact.TargetMember);
                    pending.Enqueue((fact.TargetMember, fact.Kind));
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

                MethodsByContainingType(type);
                foreach (var relation in TypeRelationsFrom(type))
                {
                    AddRelation(relation);
                }
                foreach (var relation in DispatchRelationsTo(type))
                {
                    AddRelation(relation);
                    pending.Enqueue(relation.TypeSymbolId);
                }
            }
        }

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
                AdmitNode(symbol.SymbolId);
                AddOwnershipSymbol(symbol.SymbolId);
                AddOwnershipPath(symbol.FilePath);
                methods[symbol.SymbolId] = SymbolFactProjections.ToMethodRef(symbol);
            }
        }

        private IReadOnlyList<MethodRef> MethodsByContainingType(string type)
        {
            if (!loadedContainingTypes.Add(type))
            {
                return methods.Values.Where(method => method.ContainingTypeId == type).ToArray();
            }

            var rows = view.MethodsByContainingSymbol(type);
            containingMethodCalls++;
            containingMethodRows += rows.Count;
            var projected = SymbolFactProjections.SelectCanonicalMethodFacts(rows).Select(SymbolFactProjections.ToMethodRef).ToArray();
            foreach (var method in projected)
            {
                AdmitNode(method.SymbolId);
                AddOwnershipSymbol(method.SymbolId);
                AddOwnershipPath(method.FilePath);
                methods[method.SymbolId] = method;
            }
            return projected;
        }

        private IReadOnlyList<TypeRelationFact> TypeRelationsFrom(string type)
        {
            var rows = view.TypeRelationsFrom(type);
            typeRelationCalls++;
            typeRelationRows += rows.Count;
            return rows;
        }

        private IReadOnlyList<TypeRelationFact> DispatchRelationsTo(string type)
        {
            var rows = view.DispatchRelationsTo(type);
            typeRelationCalls++;
            typeRelationRows += rows.Count;
            return rows;
        }

        private IReadOnlyList<DispatchFact> DispatchFrom(string source)
        {
            if (!dispatchFromRows.TryGetValue(source, out var rows))
            {
                rows = view.DispatchFrom(source);
                dispatchFromRows[source] = rows;
                dispatchCalls++;
                dispatchRows += rows.Count;
            }
            return rows;
        }

        private IReadOnlyList<DispatchFact> DispatchTo(string target)
        {
            if (!dispatchToRows.TryGetValue(target, out var rows))
            {
                rows = view.DispatchTo(target);
                dispatchToRows[target] = rows;
                dispatchCalls++;
                dispatchRows += rows.Count;
            }
            return rows;
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
            else
            {
                return;
            }

            AddOwnershipSymbol(relation.TypeSymbolId);
            AddOwnershipSymbol(relation.RelatedSymbolId);
            AddOwnershipPath(relation.FilePath);
        }

        private void AddEdge(CallEdge edge)
        {
            AdmitNode(edge.Caller);
            AdmitNode(edge.Callee);
            AddOwnershipSymbol(edge.Caller);
            AddOwnershipSymbol(edge.Callee);
            AddOwnershipPath(edge.FilePath);
            edges.Add(edge);
        }

        private void AddDispatchFact(DispatchFact fact)
        {
            AdmitNode(fact.SourceMember);
            AdmitNode(fact.TargetMember);
            AddOwnershipSymbol(fact.SourceMember);
            AddOwnershipSymbol(fact.TargetMember);
            AddOwnershipPath(fact.FilePath);
            dispatch.Add(fact with { FilePath = "" });
        }

        private void AddOwnershipSymbol(string symbolId)
        {
            if (!string.IsNullOrWhiteSpace(symbolId))
            {
                ownershipSymbols.Add(MonomorphizedNodeId.BaseOf(symbolId));
            }
        }

        private void AddOwnershipPath(string? filePath)
        {
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                ownershipPaths.Add(filePath);
            }
        }

        private void AdmitNode(string node)
        {
            if (materializedNodes.Contains(node))
            {
                return;
            }
            if (materializedNodes.Count >= request.MaxNodes)
            {
                throw new DemandReverseCallersGraphUnavailableException(
                    $"Keyed reverse graph materialization exceeded the {request.MaxNodes} node cap."
                );
            }
            materializedNodes.Add(node);
        }

        private FactGraphData Snapshot()
        {
            FactGraphData graph = new(
                edges.OrderBy(edge => edge.Caller, StringComparer.Ordinal).ThenBy(edge => edge.Line).ToArray(),
                implementations.OrderBy(edge => edge.ImplType, StringComparer.Ordinal).ThenBy(edge => edge.InterfaceType).ToArray(),
                methods.Values.OrderBy(method => method.SymbolId, StringComparer.Ordinal).ToArray(),
                bases.OrderBy(edge => edge.SubType, StringComparer.Ordinal).ThenBy(edge => edge.BaseType).ToArray(),
                dispatch
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

        private static bool SameDispatchShape(string left, string right) =>
            string.Equals(MethodName(left), MethodName(right), StringComparison.Ordinal) && ParameterArity(left) == ParameterArity(right);

        private static string MethodName(string id)
        {
            var head = id.AsSpan();
            var open = head.IndexOf('(');
            if (open >= 0)
            {
                head = head[..open];
            }
            var dot = head.LastIndexOf('.');
            return dot < 0 ? head.ToString() : head[(dot + 1)..].ToString();
        }

        private static string DeclaringTypeId(string methodId)
        {
            var head = methodId.StartsWith("M:", StringComparison.Ordinal) ? methodId[2..] : methodId;
            var open = head.IndexOf('(');
            if (open >= 0)
            {
                head = head[..open];
            }
            var dot = head.LastIndexOf('.');
            return dot < 0 ? "T:" + head : "T:" + head[..dot];
        }

        private static int ParameterArity(string id)
        {
            var open = id.IndexOf('(');
            var close = id.LastIndexOf(')');
            if (open < 0 || close <= open + 1)
            {
                return 0;
            }

            var count = 1;
            var depth = 0;
            for (var i = open + 1; i < close; i++)
            {
                switch (id[i])
                {
                    case '{':
                    case '[':
                        depth++;
                        break;
                    case '}':
                    case ']':
                        depth--;
                        break;
                    case ',' when depth == 0:
                        count++;
                        break;
                }
            }
            return count;
        }
    }
}
