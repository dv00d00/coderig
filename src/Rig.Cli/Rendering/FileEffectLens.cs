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
    internal sealed record LensBadge(string Family, int NearestDepth, bool ViaDispatchOnly = false, bool Looped = false)
    {
        internal bool IsDirect => NearestDepth == 0;

        // The grammar, in one sentence: FAMILY, then how far (`!` here / `:N` calls away), then how OFTEN
        // (`*` = once per loop iteration), then on what BASIS (`?` = only reachable through a dispatch hop with
        // several candidate implementations, so it may not be a real call at all; a one-implementation hop is
        // deterministic and carries no mark).
        //
        //   db!     the effect is in this body
        //   db!*    …and it runs once per iteration of an enclosing loop
        //   db:5    the nearest one is five calls away
        //   db:5?   …and that reach exists only through a polymorphic dispatch hop — a lead, not a fact
        //
        // `*` appears only on `!` rows by construction (repetition is a lexical fact about the effect's own
        // body — see FileEffectAggregate.Looped), so `db:5*` is not a shape this grammar can produce.
        // The suffixes are deliberately part of the shared label rather than each surface's own decoration: a
        // reader who learns them once reads them the same way in the terminal, the browser and the editor.
        internal string Label =>
            $"{(IsDirect ? $"{Family}!" : $"{Family}:{NearestDepth}")}{(Looped ? "*" : "")}{(ViaDispatchOnly ? "?" : "")}";
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

    // What the reader asked to SEE, applied to the projection rather than to the derivation. That split is the
    // whole design: the reverse closure is keyed on (store, rules, schema) and costs ~47s cold on MedDBase, so
    // a filter that changed it would re-pay that per combination. Everything here is a predicate over an
    // already-computed badge — microseconds — which is why a client may drive arbitrary combinations.
    //
    // The two filters that CANNOT live here, and must stay derivation-side flags with their own cache key:
    // `--intrinsic` (adds alloc/throw, ~91% of all effects — a different, far larger artifact) and `--async`
    // (a different reverse edge set). `max-depth` looks like a closure bound but is exact as a predicate:
    // nearest-depth is monotone, so filtering `<= N` off an unbounded closure equals bounding the walk at N.
    internal sealed record LensFilter(
        // Family tokens. A caller that was given PROVIDER tokens resolves them to families first (see
        // ProviderCatalog.FamilyOf) and reports the widening — this model is family-grain, and silently
        // dropping a provider token no badge can ever match would read as "this file is clean".
        IReadOnlyCollection<string>? Only = null,
        IReadOnlyCollection<string>? Exclude = null,
        int? MinDepth = null,
        int? MaxDepth = null,
        bool DirectOnly = false,
        bool LoopedOnly = false,
        bool HideDispatchOnly = false
    )
    {
        internal static LensFilter None { get; } = new();

        // NULL means "the reader did not ask", EMPTY means "the reader asked and nothing resolved". They must
        // not behave alike: an `--only` whose tokens all failed to resolve has to match NOTHING, because
        // rendering the whole file instead answers the opposite of the question that was asked. Observed on
        // the real store — `--only nosuchthing` printed the "matches nothing" note and then listed all 20
        // methods.
        internal bool IsActive =>
            Only is not null
            || Exclude is { Count: > 0 }
            || MinDepth is not null
            || MaxDepth is not null
            || DirectOnly
            || LoopedOnly
            || HideDispatchOnly;

        internal bool Keeps(LensBadge badge)
        {
            if (Only is not null && !Only.Contains(badge.Family, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            if (Exclude is { Count: > 0 } && Exclude.Contains(badge.Family, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            return (MinDepth is null || badge.NearestDepth >= MinDepth)
                && (MaxDepth is null || badge.NearestDepth <= MaxDepth)
                && (!DirectOnly || badge.IsDirect)
                && (!LoopedOnly || badge.Looped)
                && (!HideDispatchOnly || !badge.ViaDispatchOnly);
        }
    }

    // What a filter REMOVED. Mandatory rather than nice-to-have: a filtered view and a clean file look
    // identical, and a reader who cannot tell them apart draws the wrong conclusion from an empty overlay.
    internal sealed record LensFilterDisclosure(bool Active, int HiddenBadges, int HiddenMethods, int HiddenLines)
    {
        internal static LensFilterDisclosure Inactive { get; } = new(false, 0, 0, 0);

        internal bool HidSomething => HiddenBadges > 0 || HiddenMethods > 0 || HiddenLines > 0;
    }

    internal sealed record LensModel(
        string FilePath,
        IReadOnlyList<string> RequestedFamilies,
        IReadOnlyList<string> PresentFamilies,
        IReadOnlyList<string> AbsentRequestedFamilies,
        IReadOnlyList<LensMethod> Methods,
        IReadOnlyList<LensLine> Lines,
        bool ColumnsAvailable,
        bool WitnessPathsIncluded,
        // Defaulted so every existing construction site and test keeps compiling with the unfiltered meaning.
        LensFilterDisclosure? Filtered = null
    )
    {
        // Compatibility name for existing lens consumers: it retains the complete selector-set meaning while
        // richer surfaces can distinguish what was requested from what this file actually contains.
        internal IReadOnlyList<string> Families => RequestedFamilies;

        internal LensFilterDisclosure Disclosure => Filtered ?? LensFilterDisclosure.Inactive;
    }

    internal static LensModel Project(FileEffectsQueryService.Artifact artifact) => Project(artifact, LensFilter.None);

    internal static LensModel Project(FileEffectsQueryService.Artifact artifact, LensFilter filter)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(filter);
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

        // The filter runs HERE — over the projected badges, after the merge. Applying it before the merge
        // would change which row wins (a `--only db` view would pick a different nearest distance than the
        // same line shows unfiltered), and a view whose numbers move when you narrow it is not a filter, it
        // is a different measurement.
        var disclosure = LensFilterDisclosure.Inactive;
        if (filter.IsActive)
        {
            var badgesBefore = methods.Sum(method => method.Badges.Count) + lines.Sum(line => line.Badges.Count);
            var methodsBefore = methods.Length;
            var linesBefore = lines.Length;

            methods = methods
                .Select(method => method with { Badges = method.Badges.Where(filter.Keeps).ToArray() })
                .Where(method => method.Badges.Count > 0)
                .ToArray();
            lines = lines
                .Select(line => line with { Badges = line.Badges.Where(filter.Keeps).ToArray() })
                .Where(line => line.Badges.Count > 0)
                .ToArray();

            var badgesAfter = methods.Sum(method => method.Badges.Count) + lines.Sum(line => line.Badges.Count);
            disclosure = new LensFilterDisclosure(
                Active: true,
                HiddenBadges: badgesBefore - badgesAfter,
                HiddenMethods: methodsBefore - methods.Length,
                HiddenLines: linesBefore - lines.Length
            );
        }

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
            WitnessPathsIncluded: false,
            disclosure
        );
    }

    internal static string LabelLine(IReadOnlyList<LensBadge> badges) => string.Join(" ", badges.Select(badge => badge.Label));

    private static IReadOnlyList<LensBadge> Badges(IEnumerable<FileEffectAggregate> effects) =>
        effects
            .Select(effect => new LensBadge(effect.Family, effect.NearestDepth, effect.ViaDispatchOnly, effect.Looped))
            .OrderBy(badge => badge.Family, StringComparer.Ordinal)
            .ToArray();

    // Collapsing several rows on one line keeps the SHORTEST distance per family — and must keep the basis
    // with it. A plain `Min(NearestDepth)` rebuilt the aggregate with the default (real) flag, so a line whose
    // only route to the family was a dispatch guess printed as a fact: `cache:18` sat under a `cache:19?`
    // method badge, and the two disagreed about the same route. Mirrors FileEffectReadModelIndex.Best: a real
    // row beats a dispatch-only one, then distance decides within the surviving basis.
    private static IReadOnlyList<FileEffectAggregate> Merge(IEnumerable<FileEffectAggregate> effects) =>
        effects
            .GroupBy(effect => effect.Family, StringComparer.Ordinal)
            .Select(group =>
            {
                var real = group.Where(effect => !effect.ViaDispatchOnly).ToArray();
                var basis = real.Length > 0 ? real : group.ToArray();
                var nearest = basis.Min(effect => effect.NearestDepth);
                return new FileEffectAggregate(
                    group.Key,
                    nearest,
                    real.Length == 0,
                    basis.Any(effect => effect.NearestDepth == nearest && effect.Looped)
                );
            })
            .ToArray();

    private static IReadOnlyList<string> OrderedFamilies(IEnumerable<string> families) =>
        families.Distinct(StringComparer.Ordinal).OrderBy(family => family, StringComparer.Ordinal).ToArray();
}
