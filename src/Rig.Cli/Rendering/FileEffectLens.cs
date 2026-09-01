using Rig.Cli.Services;
using Rig.Domain.Functions;
using static Rig.Cli.Rendering.SymbolNameFormatter;

namespace Rig.Cli.Rendering;

// The FILE LENS presentation model — one projection of FileEffectReadModel that every surface renders:
// `rig annotate` (text/tsv), the web endpoint's DTOs, and, when the transport carries it, the Rider plugin.
//
// It exists because the same three decisions were about to be made three times, and a reader who moves
// between the browser, the terminal and the editor must not have to relearn them:
//
//   1. ORDER — methods by declaration line, then DocID; badges by family name; lines ascending.
//   2. MERGE — several call sites on one line collapse into that line, keeping the SHORTEST distance per
//      family (extraction mines lines, not columns, so the line is the finest honest anchor).
//   3. DIRECT vs DISTANT — nearest depth 0 means this body performs the effect; anything else is a distance.
//      Every surface marks that difference, and only the marker differs (`db!` in text, a filled glyph in the
//      browser, a bold adornment in Rider).
//
// Nothing here formats for a specific surface beyond `Label`, which is the text form; a glyph table belongs
// to the surface that has glyphs, keyed on Family + IsDirect + Rank so the three cannot drift in ORDER.
internal static class FileEffectLens
{
    internal sealed record LensBadge(string Family, int NearestDepth)
    {
        internal bool IsDirect => NearestDepth == 0;

        // `db!` = the effect is in this body; `db:5` = the nearest one is five calls away.
        internal string Label => IsDirect ? $"{Family}!" : $"{Family}:{NearestDepth}";
    }

    internal sealed record LensMethod(
        string SymbolId,
        string Name,
        string Signature,
        int Line,
        int EndLine,
        IReadOnlyList<LensBadge> Badges
    );

    // One annotated source line. Targets are the in-solution callees the store could name; an effect at a call
    // into external code leaves it empty, which is also why such a row must never claim a specific call.
    internal sealed record LensLine(int Line, IReadOnlyList<LensBadge> Badges, IReadOnlyList<string> Targets);

    internal sealed record LensModel(
        string FilePath,
        IReadOnlyList<string> RequestedFamilies,
        IReadOnlyList<string> PresentFamilies,
        IReadOnlyList<string> AbsentRequestedFamilies,
        IReadOnlyList<LensMethod> Methods,
        IReadOnlyList<LensLine> Lines,
        bool ColumnsAvailable,
        bool WitnessPathsIncluded
    )
    {
        // Compatibility name for existing lens consumers: it retains the complete selector-set meaning while
        // richer surfaces can distinguish what was requested from what this file actually contains.
        internal IReadOnlyList<string> Families => RequestedFamilies;
    }

    internal static LensModel Project(FileEffectsQueryService.Artifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var model = artifact.Model;
        var methods = model
            .Methods.Select(method =>
            {
                var location = artifact.Methods.GetValueOrDefault(method.SymbolId);
                return new LensMethod(
                    method.SymbolId,
                    location?.Name ?? ShortName(method.SymbolId),
                    location?.Signature ?? "",
                    location?.Line ?? 0,
                    location?.EndLine ?? 0,
                    Badges(method.Effects)
                );
            })
            .OrderBy(method => method.Line)
            .ThenBy(method => method.SymbolId, StringComparer.Ordinal)
            .ToArray();

        var lines = model
            .CallSites.GroupBy(site => site.Line)
            .OrderBy(group => group.Key)
            .Select(group => new LensLine(
                group.Key,
                Badges(Merge(group.SelectMany(site => site.Effects))),
                group
                    .Select(site => site.TargetSymbolId)
                    .Where(id => id.Length > 0)
                    .Select(ShortName)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray()
            ))
            .ToArray();

        // Family coverage belongs to the shared projection rather than each surface. Derive PRESENT from the
        // projected badges (not from the request), so web, CLI and editor agree even if a store contains an
        // observed family outside the current selector set. All three lists are stable regardless of store order.
        var requestedFamilies = OrderedFamilies(model.EffectSelectors);
        var presentFamilies = OrderedFamilies(
            methods.SelectMany(method => method.Badges).Concat(lines.SelectMany(line => line.Badges)).Select(badge => badge.Family)
        );
        var presentSet = presentFamilies.ToHashSet(StringComparer.Ordinal);
        var absentRequestedFamilies = requestedFamilies.Where(family => !presentSet.Contains(family)).ToArray();

        // Both flags are FALSE by construction today and are carried rather than hidden: extraction mines no
        // column, and no surface asks for witness paths yet. A surface that renders them as available would
        // be promising precision the facts do not have.
        return new LensModel(
            model.FilePath,
            requestedFamilies,
            presentFamilies,
            absentRequestedFamilies,
            methods,
            lines,
            ColumnsAvailable: false,
            WitnessPathsIncluded: false
        );
    }

    internal static string LabelLine(IReadOnlyList<LensBadge> badges) => string.Join(" ", badges.Select(badge => badge.Label));

    private static IReadOnlyList<LensBadge> Badges(IEnumerable<FileEffectAggregate> effects) =>
        effects
            .Select(effect => new LensBadge(effect.Family, effect.NearestDepth))
            .OrderBy(badge => badge.Family, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<FileEffectAggregate> Merge(IEnumerable<FileEffectAggregate> effects) =>
        effects
            .GroupBy(effect => effect.Family, StringComparer.Ordinal)
            .Select(group => new FileEffectAggregate(group.Key, group.Min(effect => effect.NearestDepth)))
            .ToArray();

    private static IReadOnlyList<string> OrderedFamilies(IEnumerable<string> families) =>
        families.Distinct(StringComparer.Ordinal).OrderBy(family => family, StringComparer.Ordinal).ToArray();
}
