using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

public sealed class FileEffectReadModelCorrectnessTests
{
    private const string File = "/repo/File.cs";
    private const string OwnersFile = "/repo/Owners.cs";

    [Test]
    public void Nested_lambda_effects_fold_to_the_outer_declared_method_and_keep_the_physical_line()
    {
        const string method = "M:Fixture.Run()";
        const string outerLambda = "M:Fixture.Run()~λ0";
        const string nestedLambda = "M:Fixture.Run()~λ1";
        const string target = "M:Database.Execute()";
        var graph = Graph([
            new CallEdge(method, outerLambda, EdgeKinds.MethodGroup, File, 10),
            new CallEdge(outerLambda, nestedLambda, EdgeKinds.MethodGroup, File, 18),
            new CallEdge(nestedLambda, target, EdgeKinds.Invocation, File, 23),
        ]);
        var symbols = new[]
        {
            Method(method, File, 5),
            Lambda(outerLambda, "M:Fixture.Run()", File, 10),
            Lambda(nestedLambda, "M:Fixture.Run()", File, 18),
        };

        var model = Build(graph, symbols, [Effect("ado", nestedLambda, File, 23)], SqlSelector).Find(File).ShouldNotBeNull();

        model.Methods.Select(Row).ShouldBe([(method, "sql", 0)]);
        model.CallSites.Select(Site).ShouldBe([(method, target, 23, "sql", 0)]);
    }

    [Test]
    public void Store_backed_projection_folds_lambda_ownership_without_loading_lambda_symbol_rows()
    {
        const string method = "M:Fixture.Run()";
        const string lambda = "M:Fixture.Run()~λ0";
        const string target = "M:Database.Execute()";
        var graph = Graph([
            new CallEdge(method, lambda, EdgeKinds.MethodGroup, File, 10),
            new CallEdge(lambda, target, EdgeKinds.Invocation, File, 18),
        ]);

        // FileEffectsQueryService deliberately supplies only canonical declared methods. The whole graph is
        // already resident, so lambda ownership must not depend on loading solution-wide lambda SymbolFacts.
        var model = Build(graph, [Method(method, File, 5)], [Effect("ado", lambda, File, 18)], SqlSelector).Find(File).ShouldNotBeNull();

        model.Methods.Select(Row).ShouldBe([(method, "sql", 0)]);
        model.CallSites.Select(Site).ShouldBe([(method, target, 18, "sql", 0)]);
    }

    [Test]
    public void Editor_depth_counts_visible_calls_but_not_the_method_group_hop_into_a_lambda()
    {
        const string entry = "M:Fixture.Entry()";
        const string owner = "M:Fixture.Owner()";
        const string lambda = "M:Fixture.Owner()~λ0";
        var graph = Graph([
            new CallEdge(entry, owner, EdgeKinds.Invocation, File, 15),
            new CallEdge(owner, lambda, EdgeKinds.MethodGroup, OwnersFile, 40),
        ]);
        var symbols = new[] { Method(entry, File, 5), Method(owner, OwnersFile, 30), Lambda(lambda, owner, OwnersFile, 40) };

        var model = Build(graph, symbols, [Effect("ado", lambda, OwnersFile, 41)], SqlSelector).Find(File).ShouldNotBeNull();

        // A forward graph walk reaches the physical lambda seed in two edges (invocation + methodGroup).
        // The file lens folds that seed to Owner, so its editor-facing answer is one visible source call.
        model.Methods.Select(Row).ShouldBe([(entry, "sql", 1)]);
        model.CallSites.Select(Site).ShouldBe([(entry, owner, 15, "sql", 0)]);
    }

    [Test]
    public void Property_lambda_folds_through_its_method_group_edge_to_the_getter_not_the_property_id()
    {
        const string getter = "M:Fixture.get_Value";
        const string property = "P:Fixture.Value";
        const string lambda = "P:Fixture.Value~λ0";
        var graph = Graph([new CallEdge(getter, lambda, EdgeKinds.MethodGroup, File, 12)]);
        var symbols = new[] { Method(getter, File, 8), Lambda(lambda, property, File, 12) };

        var model = Build(graph, symbols, [Effect("ado", lambda, File, 13)], SqlSelector).Find(File).ShouldNotBeNull();

        model.Methods.Select(Row).ShouldBe([(getter, "sql", 0)]);
        model.Methods.ShouldNotContain(method => method.SymbolId == property);
        model.CallSites.Select(Site).ShouldBe([(getter, "", 13, "sql", 0)]);
    }

    [Test]
    public void Cross_family_direct_effect_survives_a_targeted_reachable_call_on_the_same_line()
    {
        const string caller = "M:Fixture.Caller()";
        const string bridge = "M:Fixture.Bridge()";
        const string rpcOwner = "M:Fixture.RpcOwner()";
        var graph = Graph([
            new CallEdge(caller, bridge, EdgeKinds.Invocation, File, 20),
            new CallEdge(bridge, rpcOwner, EdgeKinds.Invocation, OwnersFile, 30),
        ]);
        var symbols = new[] { Method(caller, File, 5), Method(bridge, OwnersFile, 25), Method(rpcOwner, OwnersFile, 35) };
        var effects = new[] { Effect("file", caller, File, 20), Effect("http", rpcOwner, OwnersFile, 36) };
        var selectors = new[]
        {
            new FileEffectSelector("io", [new EffectPredicate("file")]),
            new FileEffectSelector("rpc", [new EffectPredicate("http")]),
        };

        var model = Build(graph, symbols, effects, selectors).Find(File).ShouldNotBeNull();

        model
            .Methods.SelectMany(method => method.Effects.Select(effect => (method.SymbolId, effect.Family, effect.NearestDepth)))
            .ShouldBe([(caller, "io", 0), (caller, "rpc", 2)]);
        model.CallSites.Select(Site).ShouldBe([(caller, "", 20, "io", 0), (caller, bridge, 20, "rpc", 1)]);
    }

    [Test]
    public void Multi_callee_line_keeps_same_family_direct_zero_beside_the_distant_target()
    {
        const string caller = "M:Fixture.Caller()";
        const string bridge = "M:Fixture.Bridge()";
        const string pure = "M:Fixture.Pure()";
        const string owner = "M:Fixture.Owner()";
        var graph = Graph([
            new CallEdge(caller, bridge, EdgeKinds.Invocation, File, 40),
            new CallEdge(caller, pure, EdgeKinds.Invocation, File, 40),
            new CallEdge(bridge, owner, EdgeKinds.Invocation, OwnersFile, 50),
        ]);
        var symbols = new[]
        {
            Method(caller, File, 5),
            Method(bridge, OwnersFile, 45),
            Method(pure, OwnersFile, 46),
            Method(owner, OwnersFile, 55),
        };
        var effects = new[] { Effect("ado", caller, File, 40), Effect("ado", owner, OwnersFile, 56) };

        var model = Build(graph, symbols, effects, SqlSelector).Find(File).ShouldNotBeNull();

        model.Methods.Select(Row).ShouldBe([(caller, "sql", 0)]);
        model.CallSites.Select(Site).ShouldBe([(caller, "", 40, "sql", 0), (caller, bridge, 40, "sql", 1)]);
    }

    [Test]
    public void Every_marked_family_has_an_owning_method_row_including_an_isolated_direct_owner()
    {
        const string isolated = "M:Fixture.Isolated()";
        const string caller = "M:Fixture.Caller()";
        const string owner = "M:Fixture.Owner()";
        var graph = Graph([new CallEdge(caller, owner, EdgeKinds.Invocation, File, 30)], isolated);
        var symbols = new[] { Method(isolated, File, 5), Method(caller, File, 25), Method(owner, OwnersFile, 40) };
        var effects = new[] { Effect("file", isolated, File, 8), Effect("ado", owner, OwnersFile, 41) };
        var selectors = new[] { new FileEffectSelector("io", [new EffectPredicate("file")]), SqlSelector };

        var model = Build(graph, symbols, effects, selectors).Find(File).ShouldNotBeNull();

        model
            .Methods.SelectMany(method => method.Effects.Select(effect => (method.SymbolId, effect.Family, effect.NearestDepth)))
            .ShouldBe([(caller, "sql", 1), (isolated, "io", 0)]);
        model.CallSites.Select(Site).ShouldBe([(caller, owner, 30, "sql", 0), (isolated, "", 8, "io", 0)]);

        var methodFamilies = model.Methods.ToDictionary(
            method => method.SymbolId,
            method => method.Effects.Select(effect => effect.Family).ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal
        );
        foreach (var site in model.CallSites)
        {
            methodFamilies.ShouldContainKey(site.EnclosingSymbolId);
            site.Effects.ShouldAllBe(effect => methodFamilies[site.EnclosingSymbolId].Contains(effect.Family));
        }
    }

    private static readonly FileEffectSelector SqlSelector = new("sql", [new EffectPredicate("ado")]);

    private static FileEffectReadModelIndex Build(
        FactGraphData graph,
        IEnumerable<SymbolFact> symbols,
        IEnumerable<DerivedEffect> effects,
        params FileEffectSelector[] selectors
    ) => FileEffectReadModelIndex.Build(graph, symbols, effects, selectors, [File, OwnersFile]);

    private static (string SymbolId, string Family, int Depth) Row(FileEffectMethod method) =>
        (method.SymbolId, method.Effects.Single().Family, method.Effects.Single().NearestDepth);

    private static (string Enclosing, string Target, int Line, string Family, int Depth) Site(FileEffectCallSite site) =>
        (site.EnclosingSymbolId, site.TargetSymbolId, site.Line, site.Effects.Single().Family, site.Effects.Single().NearestDepth);

    private static DerivedEffect Effect(string provider, string owner, string file, int line) =>
        new(provider, "read", provider, owner, file, line);

    private static SymbolFact Method(string id, string file, int line) =>
        new(
            id,
            SymbolKinds.Method,
            id,
            "Fixture",
            "T:Fixture",
            "",
            "",
            $"void {id}()",
            file,
            line,
            line + 20,
            "Fixture",
            false,
            BodyHash: id
        );

    private static SymbolFact Lambda(string id, string containing, string file, int line) =>
        new(id, "lambda", "lambda", "Fixture", containing, "", "", "lambda", file, line, line + 5, "Fixture", false, BodyHash: id);

    private static FactGraphData Graph(IReadOnlyList<CallEdge> edges, params string[] extraMethods)
    {
        var methods = edges
            .SelectMany(edge => new[] { edge.Caller, edge.Callee })
            .Concat(extraMethods)
            .Distinct(StringComparer.Ordinal)
            .Select(id => new MethodRef(id, id, "T:Fixture"))
            .ToArray();
        return new FactGraphData(edges, [], methods, []);
    }
}
