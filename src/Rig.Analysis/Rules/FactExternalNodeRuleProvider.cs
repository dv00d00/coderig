using Rig.Domain.Data;

namespace Rig.Analysis.Rules;

// Projects the merged `externalNodes` rule section to the fact-matchable FactExternalNodeRule the Domain
// ExternalNodeAdmission consumes (external-node admission). The generic policy lives in Domain; rule data
// flows in through RuleSetLoader. Mirrors FactRedirectRuleProvider.
//
// An ABSENT section projects to null, not to an empty rule: null means "the built-in defaults" (the
// framework deny-list plus the type patterns the loaded effect rules mention), which is what makes the
// feature default-ON with no section authored anywhere. A section present but empty is equivalent.
internal static class FactExternalNodeRuleProvider
{
    internal static FactExternalNodeRule? Project(AnalysisRulesDocument doc)
    {
        var section = doc.ExternalNodes;
        if (section is null)
        {
            return null;
        }

        return new FactExternalNodeRule(AllowAssemblies: section.AllowAssemblies ?? [], DenyAssemblies: section.DenyAssemblies ?? []);
    }
}
