using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

public sealed class FileEffectReadModelIndexTests
{
    private const string File = "/repo/File.cs";
    private const string OtherFile = "/repo/Other.cs";

    [Test]
    public void Projects_one_global_reverse_effect_closure_into_ready_models_by_file()
    {
        var graph = Graph(
            [
                new CallEdge("M:File.Start", "M:Bridge", "invocation", File, 10),
                new CallEdge("M:Bridge", "M:Effect", "invocation", OtherFile, 20),
                new CallEdge("M:Other.Caller", "M:Effect", "invocation", OtherFile, 30),
            ],
            "M:File.Clean"
        );
        var symbols = new[]
        {
            Method("M:File.Start", "Start", File, 10, 15),
            Method("M:File.Start", "Start duplicate", File, 90, 95),
            Method("M:File.Clean", "Clean", File, 20, 22),
            Method("M:Bridge", "Bridge", OtherFile, 20, 24),
            Method("M:Effect", "Query", OtherFile, 40, 42),
            Method("M:Other.Caller", "Caller", OtherFile, 50, 55),
            Lambda("M:File.Start~λ0", File, 12),
        };
        var effects = new[] { new DerivedEffect("ado", "read", "db", "M:Effect", OtherFile, 41) };

        var index = FileEffectReadModelIndex.Build(graph, symbols, effects, effectSelector: "db");

        var file = index.Find(File);
        file.ShouldNotBeNull();
        file!.EffectSelector.ShouldBe("db");
        file.Methods.ShouldBe([new FileEffectMethod("M:File.Start", "Start", Line: 10, EndLine: 15, NearestDepth: 2)]);
        index.Find(File).ShouldBeSameAs(file); // ready read model; lookup does not traverse again

        var other = index.Find(OtherFile);
        other.ShouldNotBeNull();
        other!
            .Methods.Select(method => (method.SymbolId, method.NearestDepth))
            .ShouldBe(new[] { ("M:Bridge", 1), ("M:Effect", 0), ("M:Other.Caller", 1) });

        index.Find("/repo/Missing.cs").ShouldBeNull();
    }

    private static SymbolFact Method(string id, string name, string file, int line, int endLine) =>
        new(
            id,
            SymbolKinds.Method,
            name,
            "Fixture",
            "T:Fixture",
            "",
            "",
            $"void {name}()",
            file,
            line,
            endLine,
            "Fixture",
            false,
            BodyHash: id
        );

    private static SymbolFact Lambda(string id, string file, int line) =>
        new(id, "lambda", "λ0", "Fixture", "M:File.Start", "", "", "lambda", file, line, line, "Fixture", false, BodyHash: id);

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
