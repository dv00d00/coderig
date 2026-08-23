using Rig.Cli.Services;
using Rig.Cli.Web;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Cli;

public sealed class EffectsDiffQueryServiceTests
{
    [Test]
    public void Exact_fqn_wins_over_prefix_twin_and_preserves_the_cli_finding_contract()
    {
        var graph = Graph(
            ["M:N.A.Save", "M:N.A.SaveFinal", "M:N.B.Run"],
            Edge("M:N.A.Save", "M:N.A.Helper")
        );
        var effects = new[]
        {
            Effect("db", "write", "N.CommonEntityCollection", "M:N.A.Save"),
            Effect("db", "write", "N.AlphaEntityCollection", "M:N.A.Helper"),
            Effect("db", "write", "N.PrefixTwinEntityCollection", "M:N.A.SaveFinal"),
            Effect("db", "write", "N.CommonEntityCollection", "M:N.B.Run"),
            Effect("db", "write", "N.BetaEntityCollection", "M:N.B.Run"),
        };

        var result = EffectsDiffQueryService.Compute(graph, effects, "N.A.Save", "M:N.B.Run", label: "pair-label");

        result.Matched.ShouldBeTrue();
        result.A.Matches.ShouldBe(["N.A.Save"]);
        result.A.ResolvedId.ShouldBe("M:N.A.Save");
        result.Common.Select(x => x.ResourceKey).ShouldBe(["Common"]);
        result.AOnly.Select(x => x.ResourceKey).ShouldBe(["Alpha"]);
        result.BOnly.Select(x => x.ResourceKey).ShouldBe(["Beta"]);
        result.Findings.Count.ShouldBe(2);
        result.Findings[0].Label.ShouldBe("pair-label");
        result.Findings[0].PresentEpId.ShouldBe("M:N.A.Save");
        result.Findings[0].AbsentEpId.ShouldBe("M:N.B.Run");
        result.Findings.ShouldNotContain(f => f.ResourceKey == "PrefixTwin");
    }

    [Test]
    public void Full_open_generic_docid_selects_the_canonical_member_and_its_monomorphs_as_one_target()
    {
        const string open = "M:N.Runner.Run``1(System.String)";
        var mono = MonomorphizedNodeId.For(open, [], ["System.Int32"]);
        var graph = Graph([open, mono, "M:N.Other.Run"]);

        var result = EffectsDiffQueryService.Compute(graph, [], open, "M:N.Other.Run");

        result.Matched.ShouldBeTrue();
        result.A.Matches.ShouldBe(["N.Runner.Run``1"]);
        result.A.ResolvedId.ShouldBe(open);
    }

    [Test]
    public void Partial_pattern_discloses_distinct_conceptual_ambiguity_without_comparing()
    {
        var graph = Graph(["M:N.Left.Run", "M:N.Right.Run", "M:N.Other.Stop"]);

        var result = EffectsDiffQueryService.Compute(graph, [], "Run", "Stop");

        result.Matched.ShouldBeFalse();
        result.A.Status.ShouldBe(EffectsDiffQueryService.TargetStatus.Ambiguous);
        result.A.Matches.ShouldBe(["N.Left.Run", "N.Right.Run"]);
        result.Findings.ShouldBeEmpty();
    }

    [Test]
    public void Only_filter_is_shared_by_common_and_both_difference_sets()
    {
        var graph = Graph(["M:N.A.Run", "M:N.B.Run"]);
        var effects = new[]
        {
            Effect("db", "write", "N.AOnlyEntityCollection", "M:N.A.Run"),
            Effect("cache", "write", "N.CacheOnlyEntityCollection", "M:N.A.Run"),
            Effect("db", "write", "N.BOnlyEntityCollection", "M:N.B.Run"),
        };

        var result = EffectsDiffQueryService.Compute(graph, effects, "N.A.Run", "N.B.Run", only: ["db:write"]);

        result.AOnly.Select(x => x.ResourceKey).ShouldBe(["AOnly"]);
        result.BOnly.Select(x => x.ResourceKey).ShouldBe(["BOnly"]);
        result.AOnly.ShouldNotContain(x => x.ResourceKey == "CacheOnly");
    }

    [Test]
    public void Web_contract_has_stable_target_status_and_grouped_resource_shape()
    {
        var graph = Graph(["M:N.A.Run", "M:N.B.Run"]);
        var effects = new[]
        {
            Effect("db", "write", "N.CommonEntityCollection", "M:N.A.Run"),
            Effect("db", "write", "N.CommonEntityCollection", "M:N.B.Run"),
            Effect("queue", "publish", "N.OutboxEntityCollection", "M:N.A.Run"),
        };
        var result = EffectsDiffQueryService.Compute(graph, effects, "N.A.Run", "N.B.Run");

        var dto = EffectsDiffEndpoint.ToResponse(result);

        dto.Matched.ShouldBeTrue();
        dto.A.Status.ShouldBe("matched");
        dto.A.Matches.ShouldBe(["N.A.Run"]);
        dto.Common.Single().ResourceKey.ShouldBe("Common");
        dto.AOnly.Single().ResourceKey.ShouldBe("Outbox");
        dto.AOnly.Single().Categories.ShouldBe(["queue:publish"]);
        dto.BOnly.ShouldBeEmpty();
        dto.Error.ShouldBeNull();
    }

    [Test]
    public void Web_contract_carries_ambiguity_candidates_for_a_400_response()
    {
        var result = EffectsDiffQueryService.Compute(
            Graph(["M:N.Left.Run", "M:N.Right.Run", "M:N.Other.Stop"]),
            [],
            "Run",
            "Stop"
        );

        var dto = EffectsDiffEndpoint.ToResponse(result);

        dto.Matched.ShouldBeFalse();
        dto.A.Status.ShouldBe("ambiguous");
        dto.A.Matches.ShouldBe(["N.Left.Run", "N.Right.Run"]);
        dto.Error.ShouldNotBeNull().ShouldContain("ambiguous");
    }

    private static FactGraphData Graph(IReadOnlyList<string> nodes, params CallEdge[] edges)
    {
        var all = nodes
            .Concat(edges.SelectMany(e => new[] { e.Caller, e.Callee }))
            .Distinct(StringComparer.Ordinal)
            .Select(id => new MethodRef(id, id, null))
            .ToList();
        return new FactGraphData(edges, [], all);
    }

    private static CallEdge Edge(string caller, string callee) => new(caller, callee, "invocation", "f.cs", 1);

    private static DerivedEffect Effect(string provider, string operation, string resource, string enclosing) =>
        new(provider, operation, resource, enclosing, "f.cs", 1);
}
