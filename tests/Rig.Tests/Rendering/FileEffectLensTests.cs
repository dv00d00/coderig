using Rig.Cli.Rendering;
using Rig.Cli.Services;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Rendering;

// The lens is the ONE projection `rig annotate`, the web endpoint and (later) the Rider plugin render, so
// these tests pin the three decisions a surface must not re-make: ORDER, per-line MERGE, direct-vs-distant.
// Shapes verified against the real store via `rig annotate PersonCoursesRepository.cs --summary`.
public sealed class FileEffectLensTests
{
    private static FileEffectAggregate Effect(string family, int depth) => new(family, depth);

    private static FileEffectsQueryService.Artifact Artifact(
        IReadOnlyList<FileEffectMethod> methods,
        IReadOnlyList<FileEffectCallSite> sites,
        IReadOnlyDictionary<string, FileEffectsQueryService.MethodLocation>? locations = null,
        IReadOnlyList<string>? families = null
    ) =>
        new(
            new FileEffectReadModel("src/Repo.cs", families ?? ["db"], methods, sites),
            locations ?? new Dictionary<string, FileEffectsQueryService.MethodLocation>(StringComparer.Ordinal)
        );

    [Test]
    public void Depth_zero_is_direct_and_anything_else_carries_the_distance()
    {
        new FileEffectLens.LensBadge("db", 0).IsDirect.ShouldBeTrue();
        new FileEffectLens.LensBadge("db", 0).Label.ShouldBe("db!");
        new FileEffectLens.LensBadge("db", 5).IsDirect.ShouldBeFalse();
        new FileEffectLens.LensBadge("db", 5).Label.ShouldBe("db:5");
    }

    [Test]
    public void Methods_order_by_declaration_line_then_docid()
    {
        var locations = new Dictionary<string, FileEffectsQueryService.MethodLocation>(StringComparer.Ordinal)
        {
            ["M:N.T.Late()"] = new("M:N.T.Late()", "Late", "()", 80, 90),
            ["M:N.T.Early()"] = new("M:N.T.Early()", "Early", "()", 10, 20),
            ["M:N.T.Bbb()"] = new("M:N.T.Bbb()", "Bbb", "()", 10, 12),
        };
        var methods = new FileEffectMethod[]
        {
            new("M:N.T.Late()", [Effect("db", 0)]),
            new("M:N.T.Early()", [Effect("db", 1)]),
            new("M:N.T.Bbb()", [Effect("db", 2)]),
        };

        var lens = FileEffectLens.Project(Artifact(methods, [], locations));

        lens.Methods.Select(method => method.Name).ShouldBe(["Bbb", "Early", "Late"]);
        lens.Methods[0].Line.ShouldBe(10);
    }

    [Test]
    public void A_method_with_no_location_still_projects_under_its_short_name()
    {
        var lens = FileEffectLens.Project(Artifact([new FileEffectMethod("M:N.T.Orphan()", [Effect("cache", 3)])], []));

        lens.Methods.Single().Name.ShouldBe("T.Orphan");
        lens.Methods.Single().Line.ShouldBe(0);
        lens.Methods.Single().Signature.ShouldBe("");
    }

    [Test]
    public void Sites_on_one_line_merge_keeping_the_shortest_distance_per_family()
    {
        var sites = new FileEffectCallSite[]
        {
            new("M:N.T.M()", "M:N.Db.A()", 44, [Effect("db", 4), Effect("cache", 1)]),
            new("M:N.T.M()", "M:N.Db.B()", 44, [Effect("db", 0)]),
            new("M:N.T.M()", "", 61, [Effect("db", 2)]),
        };

        var lens = FileEffectLens.Project(Artifact([], sites));

        lens.Lines.Select(line => line.Line).ShouldBe([44, 61]);
        FileEffectLens.LabelLine(lens.Lines[0].Badges).ShouldBe("cache:1 db!");
        lens.Lines[0].Targets.ShouldBe(["Db.A", "Db.B"]);
    }

    [Test]
    public void A_site_with_no_in_solution_callee_names_no_target()
    {
        var lens = FileEffectLens.Project(Artifact([], [new FileEffectCallSite("M:N.T.M()", "", 12, [Effect("db", 0)])]));

        lens.Lines.Single().Targets.ShouldBeEmpty();
        FileEffectLens.LabelLine(lens.Lines.Single().Badges).ShouldBe("db!");
    }

    [Test]
    public void Families_are_the_selectors_asked_about_not_the_ones_found()
    {
        var lens = FileEffectLens.Project(Artifact([], [], families: ["cache", "db", "echo"]));

        lens.Families.ShouldBe(["cache", "db", "echo"]);
        lens.Lines.ShouldBeEmpty();
    }

    // Extraction mines no column and no surface asks for witness paths, so both flags must read FALSE — a
    // surface that flipped one would promise precision the facts do not have.
    [Test]
    public void The_precision_flags_are_false_by_construction()
    {
        var lens = FileEffectLens.Project(Artifact([], []));

        lens.ColumnsAvailable.ShouldBeFalse();
        lens.WitnessPathsIncluded.ShouldBeFalse();
    }
}
