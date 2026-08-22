using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rig.Analysis;
using Rig.Analysis.Extraction;
using Rig.Domain.Data;
using Shouldly;

namespace Rig.Tests.Analysis;

// The extraction-side string interner (live-background-index memory slice). Two properties matter:
//   1. IDENTITY ONLY — facts extracted with the interner are VALUE-identical to facts extracted
//      without it (records compare by value, so SequenceEqual is the whole gate).
//   2. SHARING — equal retained strings are ONE instance across files and across extraction batches
//      (per-batch SymbolStringCaches sharing one interner is exactly the per-project / per-generation
//      shape SolutionAnalyzer and ResidentIndex wire up).
public sealed class StringInternerTests
{
    private const string SharedSource = """
        namespace App
        {
            public static class Shared
            {
                public static void Do(string item) { }
            }
        }
        """;

    // Both files carry the SAME repetitive retained strings: the target DocID, the loop detail
    // ("item in items"), and a guard predicate — the columns measured as the duplication hogs.
    private const string CallerSource = """
        namespace App
        {
            public sealed class CALLER_NAME
            {
                public void Go(string[] items, bool flag)
                {
                    foreach (var item in items)
                    {
                        if (flag)
                        {
                            Shared.Do(item);
                        }
                    }
                }
            }
        }
        """;

    private static (FactExtractionResult A, FactExtractionResult B) ExtractTwoFiles(StringInterner? interner)
    {
        var sharedTree = CSharpSyntaxTree.ParseText(SharedSource, path: "Shared.cs");
        var treeA = CSharpSyntaxTree.ParseText(CallerSource.Replace("CALLER_NAME", "CallerA"), path: "CallerA.cs");
        var treeB = CSharpSyntaxTree.ParseText(CallerSource.Replace("CALLER_NAME", "CallerB"), path: "CallerB.cs");
        var compilation = CSharpCompilation.Create(
            "Snippet",
            [sharedTree, treeA, treeB],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        // One SymbolStringCache per FILE here — deliberately harsher than production (one per project
        // batch) — so any observed sharing can only come from the interner, never from the symbol cache.
        var a = FactExtractor.Extract(
            new SourceModel("Snippet", "CallerA.cs", treeA, treeA.GetRoot(), compilation.GetSemanticModel(treeA)),
            new SymbolStringCache(interner)
        );
        var b = FactExtractor.Extract(
            new SourceModel("Snippet", "CallerB.cs", treeB, treeB.GetRoot(), compilation.GetSemanticModel(treeB)),
            new SymbolStringCache(interner)
        );
        return (a, b);
    }

    [Test]
    public void Intern_returns_an_equal_string_and_canonicalizes_duplicates()
    {
        var interner = new StringInterner();
        var first = new string(['a', 'b', 'c']);
        var second = new string(['a', 'b', 'c']);
        ReferenceEquals(first, second).ShouldBeFalse("the fixture needs two distinct instances of one value");

        var internedFirst = interner.Intern(first);
        var internedSecond = interner.Intern(second);

        internedFirst.ShouldBe("abc");
        ReferenceEquals(internedFirst, internedSecond).ShouldBeTrue();
        ReferenceEquals(internedFirst, first).ShouldBeTrue("first writer's instance wins");
        interner.Intern("").ShouldBeSameAs(string.Empty);
        interner.Count.ShouldBe(1);
    }

    [Test]
    public void Interned_extraction_is_value_identical_to_uninterned_extraction()
    {
        var (plainA, plainB) = ExtractTwoFiles(interner: null);
        var (internedA, internedB) = ExtractTwoFiles(interner: new StringInterner());

        // Records compare by value, so sequence equality over every fact kind IS the identity gate:
        // interning may change which INSTANCE a fact holds, never what the fact SAYS.
        internedA.Symbols.SequenceEqual(plainA.Symbols).ShouldBeTrue();
        internedA.References.SequenceEqual(plainA.References).ShouldBeTrue();
        internedA.TypeRelations.SequenceEqual(plainA.TypeRelations).ShouldBeTrue();
        internedA.Dispatch.SequenceEqual(plainA.Dispatch).ShouldBeTrue();
        internedA.Allocations.SequenceEqual(plainA.Allocations).ShouldBeTrue();
        internedB.References.SequenceEqual(plainB.References).ShouldBeTrue();
    }

    [Test]
    public void Equal_retained_strings_share_one_instance_across_files_and_caches()
    {
        static ReferenceFact InvocationOf(FactExtractionResult result) =>
            result.References.Single(r => r.RefKind == "invocation" && r.TargetSymbolId.Contains("Shared.Do"));

        // WITH one shared interner: the two files' equal retained strings are the SAME instance, even
        // though each file went through its own SymbolStringCache (the per-batch production shape).
        var (a, b) = ExtractTwoFiles(interner: new StringInterner());
        var factA = InvocationOf(a);
        var factB = InvocationOf(b);
        ReferenceEquals(factA.TargetSymbolId, factB.TargetSymbolId).ShouldBeTrue();
        factA.EnclosingLoopDetail.ShouldBe("item in items");
        ReferenceEquals(factA.EnclosingLoopDetail, factB.EnclosingLoopDetail).ShouldBeTrue();
        factA.EnclosingGuards.ShouldNotBeNull("the guarded call must carry its predicate for this fixture to bite");
        ReferenceEquals(factA.EnclosingGuards, factB.EnclosingGuards).ShouldBeTrue();

        // ANTI-VACUITY — without the interner the same values are DISTINCT instances (fresh
        // StringBuilder/interpolation products), which is exactly the duplication being removed. If this
        // arm ever starts sharing on its own, the interner is dead weight and should be re-examined.
        var (plainA2, plainB2) = ExtractTwoFiles(interner: null);
        var plainFactA = InvocationOf(plainA2);
        var plainFactB = InvocationOf(plainB2);
        plainFactA.TargetSymbolId.ShouldBe(factA.TargetSymbolId);
        ReferenceEquals(plainFactA.TargetSymbolId, plainFactB.TargetSymbolId).ShouldBeFalse();
        ReferenceEquals(plainFactA.EnclosingLoopDetail, plainFactB.EnclosingLoopDetail).ShouldBeFalse();
    }
}
