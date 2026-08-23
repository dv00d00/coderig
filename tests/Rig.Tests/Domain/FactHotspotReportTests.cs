using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

public sealed class FactHotspotReportTests
{
    private const string Hub = "M:N.Base.Run";

    [Test]
    public void Computes_each_metric_independently_without_a_blended_score()
    {
        var graph = Graph();
        var methods = new[]
        {
            new FactHotspotReport.Method(Hub, "Run", "Base.cs", 1, 10, IsGenerated: false, IsLambda: false),
            new FactHotspotReport.Method("M:N.Generated.G", "G", "Generated/G.cs", 20, 20, IsGenerated: true, IsLambda: false),
        };
        var hazard = new EffectObservationInfo("dual_write", "cell", "detail", "high", "facts", "test");
        var looped = new EffectObservationInfo("looped_effect", "item in items", "foreach", "high", "facts", "test");
        var effects = new[]
        {
            new DerivedEffect("db", "write", "Row", Hub, "Base.cs", 5, [hazard]),
            new DerivedEffect("db", "write", "Row", Hub, "Base.cs", 5, [hazard]), // duplicate site
            new DerivedEffect("db", "write", "OtherRow", Hub, "Base.cs", 5, [hazard]), // distinct same-line resource
            new DerivedEffect("http", "POST", "Uri", Hub, "Base.cs", 6, [looped]),
        };
        var hazards = new[]
        {
            new FactHotspotReport.FindingSite(Hub, "dual_write", "Base.cs", 5),
            new FactHotspotReport.FindingSite(Hub, "event_cycle", "Base.cs", 7),
        };

        var row = FactHotspotReport
            .Build(graph, methods, effects, hazards, [new FactAmplificationRule(["http"], [])])
            .Single(r => r.Id == Hub);

        row.Lines.ShouldBe(10);
        row.CallerMethods.ShouldBe(2);
        row.IncomingCallSites.ShouldBe(3); // duplicate edge at the same caller/file/line is one site
        row.CalleeMethods.ShouldBe(2);
        row.OutgoingCallSites.ShouldBe(2);
        row.EffectSites.ShouldBe(3);
        row.EffectKinds.ShouldBe(2);
        row.EffectSitesPer100Lines.ShouldBe(30d);
        row.HazardSites.ShouldBe(2); // one effect-attached + one graph-tier
        row.HazardKinds.ShouldBe(2);
        row.AmplificationSites.ShouldBe(1);
        row.ResidualDispatchFan.ShouldBe(2);
        row.DispatchIncomingEdges.ShouldBe(4); // raw un-narrowed graph edges, as DispatchFanReport defines it
        row.DispatchRank.ShouldBe(8);
    }

    [Test]
    public void Retains_generated_rows_for_presentation_time_filtering()
    {
        var generated = FactHotspotReport
            .Build(
                Graph(),
                [new FactHotspotReport.Method("M:N.Generated.G", "G", "Generated/G.cs", 20, 20, true, false)],
                [],
                [],
                []
            )
            .Single();

        generated.IsGenerated.ShouldBeTrue();
        generated.Lines.ShouldBe(1);
    }

    private static FactGraphData Graph()
    {
        var edges = new[]
        {
            new CallEdge("M:N.A.Go", Hub, "invocation", "A.cs", 1),
            new CallEdge("M:N.A.Go", Hub, "invocation", "A.cs", 1),
            new CallEdge("M:N.A.Go", Hub, "invocation", "A.cs", 2),
            new CallEdge("M:N.B.Go", Hub, "invocation", "B.cs", 3),
            new CallEdge(Hub, "M:N.X.Do", "invocation", "Base.cs", 8),
            new CallEdge(Hub, "M:N.X.Do", "invocation", "Base.cs", 8),
            new CallEdge(Hub, "M:N.Y.Do", "invocation", "Base.cs", 9),
        };
        var methods = new[]
        {
            new MethodRef("M:N.A.Go", "Go", "T:N.A"),
            new MethodRef("M:N.B.Go", "Go", "T:N.B"),
            new MethodRef(Hub, "Run", "T:N.Base"),
            new MethodRef("M:N.One.Run", "Run", "T:N.One", IsOverride: true),
            new MethodRef("M:N.Two.Run", "Run", "T:N.Two", IsOverride: true),
            new MethodRef("M:N.X.Do", "Do", "T:N.X"),
            new MethodRef("M:N.Y.Do", "Do", "T:N.Y"),
        };
        return new FactGraphData(
            edges,
            [],
            methods,
            [new BaseEdge("T:N.One", "T:N.Base"), new BaseEdge("T:N.Two", "T:N.Base")],
            [new DispatchFact(Hub, "M:N.One.Run", "override"), new DispatchFact(Hub, "M:N.Two.Run", "override")]
        );
    }
}
