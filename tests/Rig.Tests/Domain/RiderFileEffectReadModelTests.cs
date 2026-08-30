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
        index.Find(CleanFile).ShouldBeSameAs(clean);

        index.Find("/repo/NoMethods.cs").ShouldNotBeNull().Methods.ShouldBeEmpty();

        index.Find("/repo/Unknown.cs").ShouldBeNull();

        var contractProperties = typeof(FileEffectMethod).GetProperties().Select(property => property.Name).ToArray();
        contractProperties.ShouldBe([nameof(FileEffectMethod.SymbolId), nameof(FileEffectMethod.Effects)]);
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
