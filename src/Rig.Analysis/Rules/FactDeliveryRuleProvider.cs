using Rig.Domain.Data;

namespace Rig.Analysis.Rules;

// Projects the merged `deliveryRules` rule section to the DeliveryRule the Storage loader
// (Reads.LoadDeliverySitesAsync) consumes to emit the uniform DeliverySite the framework-blind
// FactPathFinder.AddDeliveryEdges joins. Mirrors FactHandoffRuleProvider; rule data flows in through RuleSetLoader.
internal static class FactDeliveryRuleProvider
{
    internal static IReadOnlyList<DeliveryRule> Project(AnalysisRulesDocument doc) => (doc.DeliveryRules ?? []).Select(Project).ToArray();

    private static DeliveryRule Project(DeliveryRuleDocument rule) =>
        new(
            Id: rule.Id,
            Tag: rule.Tag,
            Confidence: rule.Confidence,
            Producer: Project(rule.Producer),
            Registration: Project(rule.Registration),
            CycleDelivery: rule.CycleDelivery,
            // A cycleDelivery rule that omits joinConfidence is treated as an EXACT join ("high"): the
            // heuristic case is the one worth declaring, and defaulting the other way would silently downgrade
            // every exact mechanism's cycles.
            JoinConfidence: string.IsNullOrWhiteSpace(rule.JoinConfidence) ? null : rule.JoinConfidence
        );

    private static DeliveryEndpoint Project(DeliveryEndpointDocument endpoint) =>
        new(
            Source: endpoint.Source,
            Resolve: endpoint.Resolve,
            ArgumentIndex: endpoint.ArgumentIndex,
            Methods: endpoint.Methods,
            DeclaringTypes: endpoint.DeclaringTypes,
            HandlerDispatcher: endpoint.HandlerDispatcher
        );
}
