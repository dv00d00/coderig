using Rig.Domain.Data;

namespace Rig.Domain.Functions;

// Reconnects a delegate-field invocation (`saveFunc(...)`) to the callable(s) assigned to that field —
// a hop the forward walk otherwise cuts at the mutable-field seam. Built from the extractor's per-site
// bind/invoke/escape facts; a field with ANY escape (an assignment outside its declaring type) is
// dropped entirely rather than trusted. One hop only — the joined callable's body walks normally but
// isn't itself re-dispatched. Applied identically in both FactGraphData builders to stay in parity.
public static class FactDelegateFieldJoin
{
    public static FactGraphData Apply(FactGraphData graph)
    {
        var extra = Edges(graph.MinedDispatch);
        return extra.Count == 0 ? graph : graph with { CallEdges = [.. graph.CallEdges, .. extra] };
    }

    public static IReadOnlyList<CallEdge> Edges(IReadOnlyList<DispatchFact>? mined)
    {
        if (mined is null or { Count: 0 })
        {
            return [];
        }

        var binds = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var invokes = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var escaped = new HashSet<string>(StringComparer.Ordinal);

        foreach (var fact in mined)
        {
            switch (fact.Kind)
            {
                case DispatchKinds.DelegateFieldBind:
                    Add(binds, fact.SourceMember, fact.TargetMember);
                    break;
                case DispatchKinds.DelegateFieldInvoke:
                    Add(invokes, fact.SourceMember, fact.TargetMember);
                    break;
                case DispatchKinds.DelegateFieldEscape:
                    escaped.Add(fact.SourceMember);
                    break;
            }
        }

        var edges = new List<CallEdge>();
        foreach (var slot in binds.Keys.OrderBy(s => s, StringComparer.Ordinal))
        {
            if (escaped.Contains(slot) || !invokes.TryGetValue(slot, out var invokers))
            {
                continue;
            }

            foreach (var invoker in invokers)
            foreach (var callable in binds[slot])
            {
                edges.Add(new CallEdge(Caller: invoker, Callee: callable, Kind: EdgeKinds.DelegateField, FilePath: "", Line: 0));
            }
        }

        return edges;
    }

    // Caller-local twin of Edges for a keyed resident graph. An invoke fact points from the delegate
    // slot to its invoking method, so reverse lookup discovers only this caller's slots; forward lookup
    // then supplies each slot's binds and any escape that suppresses the join.
    public static IReadOnlyList<CallEdge> EdgesFrom(IFactGraphView graph, string caller)
    {
        var slots = graph
            .DispatchTo(caller)
            .Where(fact => fact.Kind == DispatchKinds.DelegateFieldInvoke)
            .Select(fact => fact.SourceMember)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(slot => slot, StringComparer.Ordinal);
        var edges = new List<CallEdge>();
        foreach (var slot in slots)
        {
            var facts = graph.DispatchFrom(slot);
            if (
                facts.Any(fact => fact.Kind == DispatchKinds.DelegateFieldEscape)
                || !facts.Any(fact => fact.Kind == DispatchKinds.DelegateFieldInvoke && fact.TargetMember == caller)
            )
            {
                continue;
            }

            foreach (
                var callable in facts
                    .Where(fact => fact.Kind == DispatchKinds.DelegateFieldBind)
                    .Select(fact => fact.TargetMember)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(target => target, StringComparer.Ordinal)
            )
            {
                edges.Add(new CallEdge(Caller: caller, Callee: callable, Kind: EdgeKinds.DelegateField, FilePath: "", Line: 0));
            }
        }

        return edges;
    }

    private static void Add(Dictionary<string, SortedSet<string>> map, string key, string value)
    {
        if (!map.TryGetValue(key, out var set))
        {
            map[key] = set = new SortedSet<string>(StringComparer.Ordinal);
        }

        set.Add(value);
    }
}
