using Rig.Cli.Rendering;
using Rig.Cli.Services;
using Rig.Cli.Web;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Cli;

// The lens FILTER: a predicate over the projection, never over the derivation. That is what makes arbitrary
// client-driven combinations free (one cached closure serves them all) and it is also the property these tests
// exist to defend — a filter must narrow WHAT you see without changing what any surviving badge SAYS.
public sealed class FileEffectLensFilterTests
{
    private const string File = "/repo/src/Demo/Orders.cs";
    private const string LoadId = "M:Demo.Orders.Load(System.Int32)";
    private const string QueryId = "M:Demo.Repository.Query(System.Int32)";

    [Test]
    public void Only_keeps_the_named_family_and_discloses_what_it_hid()
    {
        var lens = Project(new FileEffectLens.LensFilter(Only: ["db"]));

        lens.Methods.ShouldHaveSingleItem().Badges.ShouldHaveSingleItem().Family.ShouldBe("db");
        lens.Disclosure.Active.ShouldBeTrue();
        lens.Disclosure.HidSomething.ShouldBeTrue();
        // io was dropped from the method row and its own line disappeared entirely.
        lens.Disclosure.HiddenBadges.ShouldBeGreaterThan(0);
        lens.Lines.SelectMany(line => line.Badges).ShouldAllBe(badge => badge.Family == "db");
    }

    [Test]
    public void Exclude_drops_only_the_named_family()
    {
        var lens = Project(new FileEffectLens.LensFilter(Exclude: ["io"]));

        lens.Methods.ShouldHaveSingleItem().Badges.Select(badge => badge.Family).ShouldBe(["db"]);
    }

    // The one that matters: narrowing a view must not move a number. A filter that changed a distance would
    // make two views of the same file disagree, which is the failure the shared lens exists to prevent.
    [Test]
    public void A_surviving_badge_keeps_the_number_it_had_unfiltered()
    {
        var unfiltered = Project(FileEffectLens.LensFilter.None);
        var filtered = Project(new FileEffectLens.LensFilter(Only: ["db"]));

        var before = unfiltered.Methods.Single().Badges.Single(badge => badge.Family == "db");
        var after = filtered.Methods.Single().Badges.Single(badge => badge.Family == "db");
        after.NearestDepth.ShouldBe(before.NearestDepth);
        after.Label.ShouldBe(before.Label);
    }

    [Test]
    public void Direct_keeps_only_effects_performed_in_the_body()
    {
        var lens = Project(new FileEffectLens.LensFilter(DirectOnly: true));

        lens.Methods.SelectMany(method => method.Badges).ShouldAllBe(badge => badge.IsDirect);
        lens.Methods.SelectMany(method => method.Badges).Select(badge => badge.Family).ShouldBe(["io"]);
    }

    [Test]
    public void Min_and_max_depth_bound_the_rendered_distance()
    {
        Project(new FileEffectLens.LensFilter(MinDepth: 1))
            .Methods.SelectMany(method => method.Badges)
            .ShouldAllBe(badge => !badge.IsDirect);

        Project(new FileEffectLens.LensFilter(MaxDepth: 0))
            .Methods.SelectMany(method => method.Badges)
            .ShouldAllBe(badge => badge.IsDirect);
    }

    [Test]
    public void No_dispatch_drops_the_badges_that_rest_only_on_devirtualization()
    {
        var lens = Project(new FileEffectLens.LensFilter(HideDispatchOnly: true));

        lens.Methods.SelectMany(method => method.Badges)
            .Concat(lens.Lines.SelectMany(line => line.Badges))
            .ShouldAllBe(badge => !badge.ViaDispatchOnly);
    }

    [Test]
    public void Looped_keeps_only_the_amplification_tier()
    {
        var lens = Project(new FileEffectLens.LensFilter(LoopedOnly: true));

        lens.Methods.SelectMany(method => method.Badges).ShouldAllBe(badge => badge.Looped);
        lens.Methods.ShouldHaveSingleItem().Badges.ShouldHaveSingleItem().Label.ShouldBe("io!*");
    }

    // A filter that removed nothing still reports itself as ACTIVE: the reader must be able to tell a narrowed
    // view from an unnarrowed one even when the two happen to look identical.
    [Test]
    public void An_active_filter_that_hid_nothing_still_discloses_itself()
    {
        var lens = Project(new FileEffectLens.LensFilter(MaxDepth: 99));

        lens.Disclosure.Active.ShouldBeTrue();
        lens.Disclosure.HidSomething.ShouldBeFalse();
    }

    [Test]
    public void The_label_grammar_orders_distance_then_repetition_then_basis()
    {
        new FileEffectLens.LensBadge("db", 0).Label.ShouldBe("db!");
        new FileEffectLens.LensBadge("db", 0, Looped: true).Label.ShouldBe("db!*");
        new FileEffectLens.LensBadge("db", 5, ViaDispatchOnly: true).Label.ShouldBe("db:5?");
        new FileEffectLens.LensBadge("db", 5, ViaDispatchOnly: true, Looped: true).Label.ShouldBe("db:5*?");
    }

    // The web response builds its SITE rows from the raw read model while the terminal renders merged lines.
    // A per-badge filter on raw rows would resurrect a distance the merged line badge had already lost, so the
    // endpoint filters sites by what survived on the LINE. This pins that the two agree.
    [Test]
    public void The_web_response_drops_site_rows_whose_line_lost_the_family()
    {
        var response = FileEffectsEndpoint.ToResponse(Artifact(), new FileEffectLens.LensFilter(Only: ["db"]), []);

        response.Sites.SelectMany(site => site.Effects).ShouldAllBe(effect => effect.Family == "db");
        response.Filter.ShouldNotBeNull().Active.ShouldBeTrue();
        response.Filter.HiddenBadges.ShouldBeGreaterThan(0);
    }

    [Test]
    public void The_web_response_omits_the_filter_block_when_nothing_was_filtered()
    {
        FileEffectsEndpoint.ToResponse(Artifact()).Filter.ShouldBeNull();
    }

    // An --only whose tokens resolve to nothing must render NOTHING. It used to render the whole file (the
    // resolver returned an empty set, which the filter read as "no --only given"), so the command answered the
    // opposite of the question while printing a note saying the token matched nothing.
    [Test]
    public void An_only_that_resolves_to_nothing_matches_nothing()
    {
        var lens = Project(new FileEffectLens.LensFilter(Only: []));

        lens.Methods.ShouldBeEmpty();
        lens.Lines.ShouldBeEmpty();
        lens.Disclosure.Active.ShouldBeTrue();
    }

    // Both render paths must carry the caveat, and `--summary` renders no footer — so it lives in the header.
    [Test]
    public void The_header_discloses_an_active_filter()
    {
        var writer = new StringWriter();

        Rig.Cli.Commands.AnnotateCommand.RenderHeader(writer, Project(new FileEffectLens.LensFilter(Only: ["db"])));

        writer.ToString().ShouldContain("FILTERED:");
    }

    private static FileEffectLens.LensModel Project(FileEffectLens.LensFilter filter) => FileEffectLens.Project(Artifact(), filter);

    // One method that: performs an io effect in a loop in its own body (line 21), and reaches db two calls
    // away through a call on line 17 whose route is a dispatch guess.
    private static FileEffectsQueryService.Artifact Artifact()
    {
        var model = new FileEffectReadModel(
            File,
            ["db", "io"],
            [
                new FileEffectMethod(
                    LoadId,
                    [new FileEffectAggregate("db", 2, ViaDispatchOnly: true), new FileEffectAggregate("io", 0, Looped: true)]
                ),
            ],
            [
                new FileEffectCallSite(LoadId, QueryId, Line: 17, [new FileEffectAggregate("db", 1, ViaDispatchOnly: true)]),
                new FileEffectCallSite(LoadId, TargetSymbolId: "", Line: 21, [new FileEffectAggregate("io", 0, Looped: true)]),
            ]
        );
        return new FileEffectsQueryService.Artifact(
            model,
            new Dictionary<string, FileEffectsQueryService.MethodLocation>(StringComparer.Ordinal)
            {
                [LoadId] = new(LoadId, "Load", "Order Load(int id)", Line: 10, EndLine: 24),
            }
        );
    }
}
