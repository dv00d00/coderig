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
        row.DispatchIncomingEdges.ShouldBe(3); // distinct physical source sites after source-method aggregation
        row.DispatchRank.ShouldBe(6);
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

    [Test]
    public void Collapses_monomorphized_executions_into_source_method_metrics_without_phantom_rows()
    {
        const string caller = "M:N.Entry.Go";
        const string open = "M:N.Base.Run``1";
        const string work = "M:N.Work.Save";
        const string one = "M:N.One.Run``1";
        const string two = "M:N.Two.Run``1";
        var intRun = MonomorphizedNodeId.For(open, [], ["System.Int32"]);
        var stringRun = MonomorphizedNodeId.For(open, [], ["System.String"]);
        var graph = new FactGraphData(
            CallEdges:
            [
                new CallEdge(caller, intRun, "invocation", "Entry.cs", 4),
                new CallEdge(caller, stringRun, "invocation", "Entry.cs", 4),
                new CallEdge(intRun, work, "invocation", "Base.cs", 8),
                new CallEdge(stringRun, work, "invocation", "Base.cs", 8),
            ],
            ImplementsEdges: [],
            Methods:
            [
                new MethodRef(caller, "Go", "T:N.Entry"),
                new MethodRef(open, "Run", "T:N.Base"),
                new MethodRef(work, "Save", "T:N.Work"),
                new MethodRef(one, "Run", "T:N.One", IsOverride: true),
                new MethodRef(two, "Run", "T:N.Two", IsOverride: true),
            ],
            BaseEdges: [new BaseEdge("T:N.One", "T:N.Base"), new BaseEdge("T:N.Two", "T:N.Base")],
            MinedDispatch: [new DispatchFact(open, one, "override"), new DispatchFact(open, two, "override")]
        );
        var methods = new[]
        {
            new FactHotspotReport.Method(caller, "Go", "Entry.cs", 1, 5, false, false),
            new FactHotspotReport.Method(open, "Run", "Base.cs", 6, 10, false, false),
            new FactHotspotReport.Method(work, "Save", "Work.cs", 1, 3, false, false),
        };
        var effects = new[]
        {
            new DerivedEffect("db", "write", "Row", intRun, "Base.cs", 9),
            new DerivedEffect("db", "write", "Row", stringRun, "Base.cs", 9),
        };
        var hazards = new[]
        {
            new FactHotspotReport.FindingSite(intRun, "dual_write", "Base.cs", 9),
            new FactHotspotReport.FindingSite(stringRun, "dual_write", "Base.cs", 9),
        };

        var rows = FactHotspotReport.Build(graph, methods, effects, hazards, []);

        rows.Count.ShouldBe(3);
        rows.ShouldAllBe(r => !r.Id.Contains(MonomorphizedNodeId.Marker, StringComparison.Ordinal));
        var callerRow = rows.Single(r => r.Id == caller);
        callerRow.CalleeMethods.ShouldBe(1);
        callerRow.OutgoingCallSites.ShouldBe(1);
        var openRow = rows.Single(r => r.Id == open);
        openRow.CallerMethods.ShouldBe(1);
        openRow.IncomingCallSites.ShouldBe(1);
        openRow.CalleeMethods.ShouldBe(1);
        openRow.OutgoingCallSites.ShouldBe(1);
        openRow.EffectSites.ShouldBe(1);
        openRow.EffectKinds.ShouldBe(1);
        openRow.HazardSites.ShouldBe(1);
        openRow.HazardKinds.ShouldBe(1);
        openRow.ResidualDispatchFan.ShouldBe(2);
        openRow.DispatchIncomingEdges.ShouldBe(1);
        openRow.DispatchRank.ShouldBe(2);
        var workRow = rows.Single(r => r.Id == work);
        workRow.CallerMethods.ShouldBe(1);
        workRow.IncomingCallSites.ShouldBe(1);
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
