using Rig.Analysis.Rules;
using Rig.Domain.Data;

namespace Rig.Cli.Rendering;

// Resolves the `--only` / `--exclude` vocabulary every other rig surface already speaks (provider, or
// provider:operation) into the FAMILY tokens the file lens can actually match today.
//
// Why a resolver instead of just documenting "families only": `--only llblgen` is what a reader who has used
// `rig reaches` will type, and a filter that silently matched nothing would render an empty overlay — the one
// output shape indistinguishable from "this file is clean". So every token resolves, and everything the
// resolution had to WIDEN or IGNORE comes back as text the command prints. When the lens moves to provider
// grain (label chunking past the 64-label bitmask ceiling, ~66 providers on the MedDBase rule set) this type
// keeps its shape and stops widening; nothing above it changes.
internal static class FileEffectFilterTokens
{
    internal sealed record Resolution(IReadOnlyCollection<string> Families, IReadOnlyList<string> Notes)
    {
        internal static Resolution Empty { get; } = new([], []);
    }

    internal static Resolution Resolve(RuleSet rules, IReadOnlyList<string>? tokens, string flag)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (tokens is null || tokens.Count == 0)
        {
            return Resolution.Empty;
        }

        var declaredFamilies = ProviderCatalog.DeclaredFamilies(rules).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownProviders = rules.Effects.Select(rule => rule.Provider).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var notes = new List<string>();

        foreach (var raw in tokens)
        {
            var token = raw.Trim();
            if (token.Length == 0)
            {
                continue;
            }

            // provider:operation — the operation cannot survive a family-grain match, and pretending it did
            // would answer a narrower question than the badge was computed for.
            var provider = token;
            var colon = token.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                provider = token[..colon];
                notes.Add($"{flag} {token}: operation grain is not available in the file lens — matched on '{provider}' alone.");
            }

            if (declaredFamilies.Contains(provider))
            {
                families.Add(provider);
                continue;
            }

            if (knownProviders.Contains(provider))
            {
                var family = ProviderCatalog.FamilyOf(rules, provider);
                families.Add(family);
                if (!string.Equals(family, provider, StringComparison.OrdinalIgnoreCase))
                {
                    var siblings = rules
                        .ProviderFamilies.Where(pair => string.Equals(pair.Value, family, StringComparison.OrdinalIgnoreCase))
                        .Select(pair => pair.Key)
                        .Where(sibling => !string.Equals(sibling, provider, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(sibling => sibling, StringComparer.Ordinal)
                        .ToArray();
                    notes.Add(
                        siblings.Length == 0
                            ? $"{flag} {provider}: widened to family '{family}'."
                            : $"{flag} {provider}: widened to family '{family}' — also matches {string.Join(", ", siblings)}."
                    );
                }

                continue;
            }

            notes.Add($"{flag} {token}: no rule declares this provider or family — it matches nothing in this store.");
        }

        return new Resolution(families, notes);
    }
}
