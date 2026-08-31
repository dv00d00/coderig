using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

public sealed class RiderFileEffectReadModelTests
{
    private const string File = "/repo/Effectful.cs";
    private const string CleanFile = "/repo/Clean.cs";
    private const string OwnersFile = "/repo/Owners.cs";

    [Test]
    public void Projects_one_union_closure_into_semantic_models_for_every_indexed_file()
    {
        var graph = Graph(
            [
                new CallEdge("M:File.Zed", "M:ReadOwner", "invocation", File, 10),
                new CallEdge("M:File.Alpha", "M:Bridge", "invocation", File, 20),
                new CallEdge("M:Bridge", "M:WriteOwner", "invocation", OwnersFile, 30),
                new CallEdge("M:File.Ignored", "M:HttpOwner", "invocation", File, 40),
            ],
            "M:File.Clean"
        );
        var symbols = new[]
        {
            Method("M:File.Zed", File, 10),
            Method("M:File.Alpha", File, 20),
            Method("M:File.Alpha", File, 90), // canonical duplicate must not produce a second row
            Method("M:File.Ignored", File, 40),
            Method("M:File.Clean", CleanFile, 5),
            Method("M:Bridge", OwnersFile, 30),
            Method("M:ReadOwner", OwnersFile, 50),
            Method("M:WriteOwner", OwnersFile, 60),
            Method("M:HttpOwner", OwnersFile, 70),
            Lambda("M:File.Alpha~lambda0", File, 21),
        };
        var effects = new[]
        {
            Effect("ado", "read", "M:ReadOwner", 50),
            Effect("ef", "write", "M:WriteOwner", 60),
            Effect("http", "send", "M:HttpOwner", 70),
            Effect("ADO", "read", "M:HttpOwner", 71), // matching is ordinal
        };
        var selector = new FileEffectSelector("sql", [new EffectPredicate("ado", "read"), new EffectPredicate("ef")]);

        var index = FileEffectReadModelIndex.Build(
            graph,
            symbols,
            effects,
            selector,
            indexedFilePaths: [File, CleanFile, OwnersFile, "/repo/NoMethods.cs"]
        );

        var file = index.Find(File);
        file.ShouldNotBeNull();
        file!.EffectSelector.ShouldBe("sql");
        file.Methods.Select(method =>
                (method.SymbolId, Family: method.Effects.Single().Family, NearestDepth: method.Effects.Single().NearestDepth)
            )
            .ShouldBe([("M:File.Alpha", "sql", 2), ("M:File.Zed", "sql", 1)]);
        file.CallSites.Select(callSite =>
                (
                    callSite.EnclosingSymbolId,
                    callSite.TargetSymbolId,
                    Family: callSite.Effects.Single().Family,
                    NearestDepth: callSite.Effects.Single().NearestDepth
                )
            )
            .ShouldBe([("M:File.Alpha", "M:Bridge", "sql", 1), ("M:File.Zed", "M:ReadOwner", "sql", 0)]);
        index.Find(File).ShouldBeSameAs(file);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            index.Find("/REPO/EFFECTFUL.CS").ShouldBeSameAs(file);
        }
        else
        {
            index.Find("/REPO/EFFECTFUL.CS").ShouldBeNull();
        }

        var clean = index.Find(CleanFile);
        clean.ShouldNotBeNull();
        clean!.Methods.ShouldBeEmpty();
        clean.CallSites.ShouldBeEmpty();
        index.Find(CleanFile).ShouldBeSameAs(clean);

        index.Find("/repo/NoMethods.cs").ShouldNotBeNull().Methods.ShouldBeEmpty();

        index.Find("/repo/Unknown.cs").ShouldBeNull();

        var contractProperties = typeof(FileEffectMethod).GetProperties().Select(property => property.Name).ToArray();
        contractProperties.ShouldBe([nameof(FileEffectMethod.SymbolId), nameof(FileEffectMethod.Effects)]);
        typeof(FileEffectCallSite)
            .GetProperties()
            .Select(property => property.Name)
            .ShouldBe([
                nameof(FileEffectCallSite.EnclosingSymbolId),
                nameof(FileEffectCallSite.TargetSymbolId),
                nameof(FileEffectCallSite.Effects),
            ]);
    }

    [Test]
    public void Direct_effect_call_site_is_projected_only_when_its_target_is_unambiguous()
    {
        var graph = Graph([
            new CallEdge("M:File.Direct", "M:Db.Execute", "invocation", File, 10),
            new CallEdge("M:File.Ambiguous", "M:Db.Execute", "invocation", File, 20),
            new CallEdge("M:File.Ambiguous", "M:Log.Write", "invocation", File, 20),
        ]);
        var symbols = new[] { Method("M:File.Direct", File, 9), Method("M:File.Ambiguous", File, 19) };
        var effects = new[]
        {
            new DerivedEffect("ado", "read", "db", "M:File.Direct", File, 10),
            new DerivedEffect("ado", "read", "db", "M:File.Ambiguous", File, 20),
        };

        var model = FileEffectReadModelIndex
            .Build(graph, symbols, effects, new FileEffectSelector("sql", [new EffectPredicate("ado")]))
            .Find(File)
            .ShouldNotBeNull();

        model.CallSites.Select(site => (site.EnclosingSymbolId, site.TargetSymbolId)).ShouldBe([("M:File.Direct", "M:Db.Execute")]);
    }

    // The tie case that a strict `calleeDepth < callerDepth` test dropped: one body calls two effectful
    // methods, the first hands the caller its nearest distance, and the sibling call ties it. Both are real
    // ways into the family, so both must be projected.
    [Test]
    public void A_second_effectful_call_from_one_body_is_projected_even_when_it_does_not_shorten_the_distance()
    {
        var graph = Graph([
            new CallEdge("M:File.Caller", "M:NearOwner", "invocation", File, 10),
            new CallEdge("M:File.Caller", "M:Sibling", "invocation", File, 11),
            new CallEdge("M:Sibling", "M:FarOwner", "invocation", OwnersFile, 30),
        ]);
        var symbols = new[]
        {
            Method("M:File.Caller", File, 9),
            Method("M:Sibling", OwnersFile, 20),
            Method("M:NearOwner", OwnersFile, 30),
            Method("M:FarOwner", OwnersFile, 40),
        };
        var effects = new[] { Effect("ado", "read", "M:NearOwner", 30), Effect("ado", "read", "M:FarOwner", 40) };

        var model = FileEffectReadModelIndex
            .Build(graph, symbols, effects, new FileEffectSelector("sql", [new EffectPredicate("ado")]))
            .Find(File)
            .ShouldNotBeNull();

        model
            .CallSites.Select(site => (site.EnclosingSymbolId, site.TargetSymbolId, site.Effects.Single().NearestDepth))
            .ShouldBe([("M:File.Caller", "M:NearOwner", 0), ("M:File.Caller", "M:Sibling", 1)]);
    }

    [Test]
    public void Selector_requires_a_named_family_and_at_least_one_valid_predicate()
    {
        Should.Throw<ArgumentException>(() => new FileEffectSelector(" ", [new EffectPredicate("ado")])).ParamName.ShouldBe("family");
        Should.Throw<ArgumentException>(() => new FileEffectSelector("sql", [])).ParamName.ShouldBe("predicates");
        Should.Throw<ArgumentException>(() => new FileEffectSelector("sql", [new EffectPredicate(" ")])).ParamName.ShouldBe("predicates");
    }

    private static DerivedEffect Effect(string provider, string operation, string owner, int line) =>
        new(provider, operation, "db", owner, OwnersFile, line);

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
            line + 1,
            "Fixture",
            false,
            BodyHash: id
        );

    private static SymbolFact Lambda(string id, string file, int line) =>
        new(id, "lambda", "lambda", "Fixture", "M:File.Alpha", "", "", "lambda", file, line, line, "Fixture", false, BodyHash: id);

    private static FactGraphData Graph(IReadOnlyList<CallEdge> edges, params string[] extraMethods)
    {
        var methods = edges
            .SelectMany(edge => new[] { edge.Caller, edge.Callee })
            .Concat(extraMethods)
            .Distinct(StringComparer.Ordinal)
            .Select(id => new MethodRef(id, id, "T:Fixture"))
            .ToArray();
        return new FactGraphData(edges, Array.Empty<ImplementsEdge>(), methods, Array.Empty<BaseEdge>());
    }
}
