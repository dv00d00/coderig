using Rig.Domain.Data;

namespace Rig.Domain.Functions;

// The DISPLAY GATE of the amplification finding tier: "is a looped `<provider>:<operation>` effect in scope to
// be shown as a finding?". Generic infrastructure only — the provider list itself is DATA
// (`observations.amplification` in the rules JSON, projected to FactAmplificationRule); nothing here names a
// provider, so widening the scope never touches C#.
//
// Why a scope exists at all, when looped_effect is a sound structural fact: the fact is cheap to derive for
// every provider, but not every provider's ×N is equally actionable. The shipped default admits the
// NETWORK-CROSSING providers — where a loop means N round trips over a socket — and leaves the local ones
// (in-process caches, locks, allocations, permission checks) out of the default DISPLAY, because their ×N is
// CPU/contention, a different conversation. The observation is still derived and still in the tsv `effect` row
// either way; this only decides what gets a section, a mark, and an impact row.
//
// Matching mirrors the n_plus_1 read gate exactly (FactNPlusOneRule): ANY rule may admit the effect, and within
// a rule an EMPTY list means "any" for that dimension — so `{ "providers": ["http"] }` admits every http
// operation, while `{ "providers": ["llblgen"], "operations": ["read"] }` admits only that pair. Ordinal
// comparison throughout: provider/operation tokens are rule-authored identifiers, not prose.
public static class AmplificationScope
{
    // True when a looped effect of this provider:operation is in the DISPLAYED amplification scope. An empty
    // rule list admits NOTHING (a declared-scope-only contract: no rules ⇒ no findings), which is also what
    // makes `--no-amplification` and "rules didn't ship the section" behave identically.
    public static bool Includes(IReadOnlyList<FactAmplificationRule> rules, string? provider, string? operation)
    {
        if (rules.Count == 0)
        {
            return false;
        }

        foreach (var rule in rules)
        {
            var providerOk = rule.Providers.Count == 0 || rule.Providers.Contains(provider ?? "", StringComparer.Ordinal);
            var operationOk = rule.Operations.Count == 0 || rule.Operations.Contains(operation ?? "", StringComparer.Ordinal);
            if (providerOk && operationOk)
            {
                return true;
            }
        }

        return false;
    }
}
