using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rig.Analysis;
using Rig.Analysis.Extraction;
using Rig.Analysis.Inventory;
using Rig.Domain.Data;
using Shouldly;

namespace Rig.Tests.Analysis;

public sealed class SurfaceFactSubstrateTests
{
    [Test]
    public void Declaration_surface_ignores_bodies_formatting_and_comments_but_tracks_signature_types()
    {
        var before = Method("public string M(int value = 1) { return value.ToString(); }");
        var bodyAndTrivia = Method("public string M( /* trivia */ int value\n = 1 ) { return (value + 1).ToString(); }");
        var returnType = Method("public int M(int value = 1) { return value; }");
        var parameterSurface = Method("public string M(params int[] value) { return value.Length.ToString(); }");

        bodyAndTrivia.SurfaceHash.ShouldBe(before.SurfaceHash);
        bodyAndTrivia.BodyHash.ShouldNotBe(before.BodyHash);
        returnType.SurfaceHash.ShouldNotBe(before.SurfaceHash);
        parameterSurface.SurfaceHash.ShouldNotBe(before.SurfaceHash);
    }

    [Test]
    public void Field_property_const_enum_and_accessor_surfaces_follow_initializer_rules()
    {
        var baseline = Extract(
            """
            public class C
            {
                private int field = 1;
                private const int Constant = 1;
                public event System.Action? Changed;
                public int P { get { return field; } private set { field = value; } }
            }
            public enum E { A = 1 }
            """
        );
        var bodyOnly = Extract(
            """
            public class C
            {
                private int field = 2;
                private const int Constant = 1;
                public event System.Action? Changed;
                public int P { get { return field + 1; } private set { field = value + 1; } }
            }
            public enum E { A = 1 }
            """
        );
        Fact(baseline, "field").SurfaceHash.ShouldBe(Fact(bodyOnly, "field").SurfaceHash);
        Fact(baseline, "P").SurfaceHash.ShouldBe(Fact(bodyOnly, "P").SurfaceHash);

        var changedEventType = Extract(
            "public class C { private int field = 1; private const int Constant = 1; public event System.EventHandler? Changed; public int P { get { return field; } private set { field = value; } } } public enum E { A = 1 }"
        );
        Fact(changedEventType, "Changed").SurfaceHash.ShouldNotBe(Fact(baseline, "Changed").SurfaceHash);

        var fieldType = Extract(
            "public class C { private long field = 1; private const int Constant = 1; public int P { get; private set; } } public enum E { A = 1 }"
        );
        Fact(fieldType, "field").SurfaceHash.ShouldNotBe(Fact(baseline, "field").SurfaceHash);

        var changedConst = Extract(
            "public class C { private int field = 1; private const int Constant = 2; public int P { get; private set; } } public enum E { A = 1 }"
        );
        Fact(changedConst, "Constant").SurfaceHash.ShouldNotBe(Fact(baseline, "Constant").SurfaceHash);

        var changedEnum = Extract(
            "public class C { private int field = 1; private const int Constant = 1; public int P { get; private set; } } public enum E { A = 2 }"
        );
        Fact(changedEnum, "A").SurfaceHash.ShouldNotBe(Fact(baseline, "A").SurfaceHash);

        var changedAccessor = Extract(
            "public class C { private int field = 1; private const int Constant = 1; public event System.Action? Changed; public int P { get { return field; } public set { field = value; } } } public enum E { A = 1 }"
        );
        Fact(changedAccessor, "P").SurfaceHash.ShouldNotBe(Fact(baseline, "P").SurfaceHash);
    }

    [Test]
    public void Iterator_widening_ignores_nested_functions_but_tracks_yield_in_the_declared_method()
    {
        var ordinary = Method("public System.Collections.Generic.IEnumerable<int> M() { return System.Array.Empty<int>(); }");
        var nested = Method(
            "public System.Collections.Generic.IEnumerable<int> M() { System.Collections.Generic.IEnumerable<int> Local() { yield return 1; } return Local(); }"
        );
        var iterator = Method("public System.Collections.Generic.IEnumerable<int> M() { yield return 1; }");

        ordinary.IsIterator.ShouldBeFalse();
        nested.IsIterator.ShouldBeFalse();
        iterator.IsIterator.ShouldBeTrue();
        iterator.SurfaceHash.ShouldBe(ordinary.SurfaceHash);
    }

    [Test]
    public void Partial_type_project_aggregate_tracks_either_part_surface_but_not_body_edits()
    {
        var baseline = Project([
            "public partial class P { public int A() { return 1; } }",
            "public partial class P { public int B() { return 2; } }",
        ]);
        var body = Project([
            "public partial class P { public int A() { return 9; } }",
            "public partial class P { public int B() { return 2; } }",
        ]);
        var secondPartSurface = Project([
            "public partial class P { public int A() { return 1; } }",
            "public partial class P { public int B() { return 2; } public string C() => \"x\"; }",
        ]);

        body.SurfaceHash.ShouldBe(baseline.SurfaceHash);
        secondPartSurface.SurfaceHash.ShouldNotBe(baseline.SurfaceHash);
        baseline.Shards.Count(s => s.EmitterFilePath.EndsWith(".cs", StringComparison.Ordinal)).ShouldBe(2);
    }

    [Test]
    public void Project_only_attributes_global_usings_generated_tokens_and_options_move_the_aggregate()
    {
        var baseline = Project(["public class C { public int M() => 1; }"]);
        Project(["[assembly: System.CLSCompliant(true)] public class C { public int M() => 1; }"])
            .SurfaceHash.ShouldNotBe(baseline.SurfaceHash);
        Project(["[module: System.Runtime.CompilerServices.SkipLocalsInit] public class C { public int M() => 1; }"])
            .SurfaceHash.ShouldNotBe(baseline.SurfaceHash);
        Project(["global using Alias = System.String; public class C { public int M() => 1; }"])
            .SurfaceHash.ShouldNotBe(baseline.SurfaceHash);
        Project(["using Alias = System.String; public class C { public Alias M() => \"x\"; }"])
            .SurfaceHash.ShouldNotBe(Project(["using Alias = System.Int32; public class C { public Alias M() => 1; }"]).SurfaceHash);
        Project(["public class C { public int M() => 2; }"], generated: [true])
            .SurfaceHash.ShouldNotBe(Project(["public class C { public int M() => 1; }"], generated: [true]).SurfaceHash);
        Project(["public class C { public int M() => 1; }"], allowUnsafe: true).SurfaceHash.ShouldNotBe(baseline.SurfaceHash);
        Project(["public class C { public int M() => 1; }"], symbols: ["FEATURE"]).SurfaceHash.ShouldNotBe(baseline.SurfaceHash);
        Project(["public class C { public int M() => 1; }"], languageVersion: LanguageVersion.CSharp13)
            .SurfaceHash.ShouldNotBe(baseline.SurfaceHash);
        Project(["public class C { public int M() => 1; }"], nullable: NullableContextOptions.Enable)
            .SurfaceHash.ShouldNotBe(baseline.SurfaceHash);
        Project(["extern alias A; public class C { }"])
            .SurfaceHash.ShouldNotBe(Project(["extern alias B; public class C { }"]).SurfaceHash);
    }

    private static SymbolFact Method(string declaration) => Fact(Extract($"public class C {{ {declaration} }}"), "M");

    private static SymbolFact Fact(FactExtractionResult result, string name) => result.Symbols.Single(s => s.Name == name);

    private static FactExtractionResult Extract(string source)
    {
        var (sources, _) = Compile([source]);
        return FactExtractor.Extract(sources[0], new SymbolStringCache());
    }

    private static ProjectSurfaceSnapshot Project(
        IReadOnlyList<string> texts,
        IReadOnlyList<bool>? generated = null,
        bool allowUnsafe = false,
        IReadOnlyList<string>? symbols = null,
        LanguageVersion languageVersion = LanguageVersion.Preview,
        NullableContextOptions nullable = NullableContextOptions.Disable
    )
    {
        var parse = new CSharpParseOptions(languageVersion, preprocessorSymbols: symbols ?? []);
        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            allowUnsafe: allowUnsafe,
            nullableContextOptions: nullable
        );
        var (sources, compilation) = Compile(texts, parse, options, generated);
        var extractions = sources
            .Select(source =>
            {
                var facts = FactExtractor.Extract(source, new SymbolStringCache());
                return new SourceExtractionResult(
                    [],
                    facts.Symbols,
                    facts.References,
                    facts.TypeRelations,
                    facts.Dispatch,
                    facts.Allocations
                );
            })
            .ToArray();
        return ProjectSurfaceBuilder.Build("SurfaceProject", "/src/SurfaceProject.csproj", parse, compilation, sources, extractions);
    }

    private static (IReadOnlyList<SourceModel> Sources, CSharpCompilation Compilation) Compile(
        IReadOnlyList<string> texts,
        CSharpParseOptions? parse = null,
        CSharpCompilationOptions? options = null,
        IReadOnlyList<bool>? generated = null
    )
    {
        parse ??= new CSharpParseOptions(LanguageVersion.Preview);
        options ??= new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        var trees = texts.Select((text, i) => CSharpSyntaxTree.ParseText(text, parse, path: Path.GetFullPath($"Surface{i}.cs"))).ToArray();
        var references = new[]
        {
            typeof(object).Assembly.Location,
            typeof(Enumerable).Assembly.Location,
            typeof(System.Runtime.CompilerServices.SkipLocalsInitAttribute).Assembly.Location,
        }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("SurfaceAssembly", trees, references, options);
        var sources = trees
            .Select(
                (tree, i) =>
                    new SourceModel(
                        "SurfaceProject",
                        tree.FilePath,
                        tree,
                        tree.GetRoot(),
                        compilation.GetSemanticModel(tree),
                        generated is not null && generated[i]
                    )
            )
            .ToArray();
        return (sources, compilation);
    }
}
