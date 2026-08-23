using Rig.Domain.Data;

namespace Rig.Domain.Functions;

// Transparent whole-program method metrics for refactoring/hotspot ranking. There is deliberately no
// blended score: every column is an independently explainable count, and callers choose the sort axis.
public static class FactHotspotReport
{
    public sealed record Method(
        string Id,
        string Name,
        string File,
        int Line,
        int EndLine,
        bool IsGenerated,
        bool IsLambda
    );

    // Graph-tier hazards are not attached to DerivedEffect, so the query layer flattens them into this
    // domain-neutral site shape before composing the report.
    public sealed record FindingSite(string Enclosing, string Kind, string File, int Line);

    public sealed record Row(
        string Id,
        string Name,
        string File,
        int Line,
        int Lines,
        int CallerMethods,
        int IncomingCallSites,
        int CalleeMethods,
        int OutgoingCallSites,
        int EffectSites,
        int EffectKinds,
        double EffectSitesPer100Lines,
        int HazardSites,
        int HazardKinds,
        int AmplificationSites,
        int ResidualDispatchFan,
        int DispatchIncomingEdges,
        long DispatchRank,
        bool IsGenerated,
        bool IsLambda
    );

    public static IReadOnlyList<Row> Build(
        FactGraphData graph,
        IReadOnlyList<Method> methods,
        IReadOnlyList<DerivedEffect> effects,
        IReadOnlyList<FindingSite> hazards,
        IReadOnlyList<FactAmplificationRule> amplificationScope
    )
    {
        var incoming = graph.CallEdges.GroupBy(e => e.Callee, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList());
        var outgoing = graph.CallEdges.GroupBy(e => e.Caller, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList());
        var effectsByMethod = effects
            .Where(e => !string.IsNullOrEmpty(e.EnclosingSymbolId))
            .GroupBy(e => e.EnclosingSymbolId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var hazardsByMethod = hazards
            .Where(h => h.Enclosing.Length > 0)
            .GroupBy(h => h.Enclosing, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var dispatchByHub = FactPathFinder.DispatchFanReport(graph).ToDictionary(r => r.Hub, StringComparer.Ordinal);

        var rows = new List<Row>(methods.Count);
        foreach (var method in methods.GroupBy(m => m.Id, StringComparer.Ordinal).Select(g => g.First()))
        {
            var inEdges = incoming.GetValueOrDefault(method.Id) ?? [];
            var outEdges = outgoing.GetValueOrDefault(method.Id) ?? [];
            var methodEffects = effectsByMethod.GetValueOrDefault(method.Id) ?? [];
            var methodHazards = hazardsByMethod.GetValueOrDefault(method.Id) ?? [];

            var effectSites = methodEffects
                // Resource is part of the effect-site identity: two distinct writes on one physical source
                // line must not collapse merely because their provider/operation/location agree.
                .Select(e => (e.Provider, e.Operation, e.ResourceType, e.FilePath, e.Line))
                .Distinct()
                .Count();
            var effectKinds = methodEffects.Select(e => (e.Provider, e.Operation)).Distinct().Count();

            var hazardSites = methodHazards
                .Select(h => (Type: h.Kind, FilePath: h.File, h.Line))
                .Distinct()
                .ToList();

            var amplificationSites = methodEffects
                .Where(e => AmplificationScope.Includes(amplificationScope, e.Provider, e.Operation))
                .Where(e => (e.Observations ?? []).Any(o => HazardKinds.IsAmplification(o.Type)))
                .Select(e => (e.Provider, e.Operation, e.ResourceType, e.FilePath, e.Line))
                .Distinct()
                .Count();

            var lines = method.EndLine >= method.Line && method.Line > 0 ? method.EndLine - method.Line + 1 : 1;
            dispatchByHub.TryGetValue(method.Id, out var dispatch);
            rows.Add(
                new Row(
                    Id: method.Id,
                    Name: method.Name,
                    File: method.File,
                    Line: method.Line,
                    Lines: lines,
                    CallerMethods: inEdges.Select(e => e.Caller).Distinct(StringComparer.Ordinal).Count(),
                    IncomingCallSites: inEdges.Select(e => (e.Caller, e.FilePath, e.Line)).Distinct().Count(),
                    CalleeMethods: outEdges.Select(e => e.Callee).Distinct(StringComparer.Ordinal).Count(),
                    OutgoingCallSites: outEdges.Select(e => (e.Callee, e.FilePath, e.Line)).Distinct().Count(),
                    EffectSites: effectSites,
                    EffectKinds: effectKinds,
                    EffectSitesPer100Lines: effectSites * 100d / lines,
                    HazardSites: hazardSites.Count,
                    HazardKinds: hazardSites.Select(h => h.Type).Distinct(StringComparer.Ordinal).Count(),
                    AmplificationSites: amplificationSites,
                    ResidualDispatchFan: dispatch?.ResidualFan ?? 0,
                    DispatchIncomingEdges: dispatch?.IncomingEdges ?? 0,
                    DispatchRank: dispatch?.Rank ?? 0,
                    IsGenerated: method.IsGenerated,
                    IsLambda: method.IsLambda
                )
            );
        }

        return rows;
    }
}
