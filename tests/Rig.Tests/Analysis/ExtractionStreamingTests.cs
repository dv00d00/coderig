using System.Reflection;
using Rig.Analysis;
using Rig.Analysis.Rules;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Analysis;

// Pins live-background-index SLICE 2: after a load completes, the source set the analyzer consumes is
// Roslyn-FREE — no SemanticModel, no red syntax root, nothing that pins a Compilation per file. The
// assertion is STRUCTURAL (the types can no longer carry Roslyn state), not a memory measurement, so it
// cannot flake and cannot be satisfied by an "empty at the moment we looked" coincidence.
public sealed class ExtractionStreamingTests
{
    // The old shape was `SolutionSourceSet.IndexedSources: IReadOnlyList<SourceModel>` — a SemanticModel
    // + red root per file per project, retained for the whole run (~9 GB of bound-node caches on
    // MedDBase, and a ~6.7 GB per-generation leak once the process goes resident). This walks the entire
    // reachable property graph of the post-load types and fails if ANY reachable property type (or
    // generic argument) comes from a Microsoft.CodeAnalysis assembly — so the retention cannot creep
    // back through a new field either.
    [Test]
    public void Post_load_source_set_types_reach_no_roslyn_type()
    {
        // The one legitimately Roslyn-bearing type, SourceModel, must NOT be reachable from the set:
        // it is a short-lived per-file value that dies inside the loader's per-project pass.
        foreach (var root in new[] { typeof(SolutionSourceSet), typeof(ExtractedSource), typeof(SourceExtractionResult) })
        {
            var offenders = RoslynReachableFrom(root);
            offenders.ShouldBeEmpty($"{root.Name} must be Roslyn-free after the load, but reaches: {string.Join(", ", offenders)}");
        }

        // The retention field itself is gone, by name and by payload type.
        typeof(SolutionSourceSet).GetProperty("IndexedSources").ShouldBeNull();
        typeof(SolutionSourceSet)
            .GetProperties()
            .ShouldNotContain(p => ReferencesType(p.PropertyType, typeof(SourceModel)), "no property may carry SourceModel");
    }

    // Property-graph walk: every type reachable from `root` through public/non-public instance
    // properties and generic arguments. Recurses only into Rig-owned types and generic containers —
    // primitives/strings/BCL leaves carry no object graph worth walking.
    private static IReadOnlyList<string> RoslynReachableFrom(Type root)
    {
        var offenders = new List<string>();
        var visited = new HashSet<Type>();
        var queue = new Queue<(Type Type, string Path)>();
        queue.Enqueue((root, root.Name));

        while (queue.Count > 0)
        {
            var (type, path) = queue.Dequeue();
            if (!visited.Add(type))
            {
                continue;
            }

            if (type.Assembly.GetName().Name?.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal) == true)
            {
                offenders.Add(path);
                continue;
            }

            foreach (var argument in type.IsGenericType ? type.GetGenericArguments() : Type.EmptyTypes)
            {
                queue.Enqueue((argument, $"{path}<{argument.Name}>"));
            }

            if (type.Assembly.GetName().Name?.StartsWith("Rig.", StringComparison.Ordinal) != true)
            {
                continue;
            }

            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                queue.Enqueue((property.PropertyType, $"{path}.{property.Name}:{property.PropertyType.Name}"));
            }
        }

        return offenders;
    }

    private static bool ReferencesType(Type candidate, Type forbidden)
    {
        if (candidate == forbidden)
        {
            return true;
        }

        return candidate.IsGenericType && candidate.GetGenericArguments().Any(a => ReferencesType(a, forbidden));
    }

    // End-to-end over the real DeepChain playground (temp copy, never the checked-in one): the streamed
    // per-project extraction must reproduce the pre-slice oracle counts EXACTLY, and the facts must come
    // out in the global OrdinalIgnoreCase FilePath order — the invariant the *FactIndex surrogate keys
    // (and the order-sensitive GroupBy(SymbolId).First() method dedupe) depend on. That order was
    // previously produced by sorting retained SourceModels; slice 2 must reproduce it from the
    // per-project extraction results.
    [Test]
    public async Task Streamed_extraction_reproduces_the_cold_oracle_counts_and_global_path_order()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        var rules = RuleSetLoader.Load(playground.WorkingDirectory);

        var result = await SolutionAnalyzer.AnalyzeAsync(playground.SolutionPath, rules);

        // The DeepChain symbol oracle — verified against a real run of this build. Was 34 (the
        // docs/backlog/progress/live-background-index.md number); 51 since the playground gained the
        // cross-project dispatch scaffolding (INotifier/ChannelBase/EmailChannel/PushChannel) for
        // ResidentIndexScaleTests. The REFERENCE count is deliberately not pinned:
        // it depends on restore state (a restored copy adds obj/ AssemblyInfo attribute references the
        // unrestored 42-reference oracle arm lacks), so the stable identity checks here are the symbol
        // count, the cross-project bindings below, and the emit order.
        var symbols = result.Symbols ?? [];
        var references = result.References ?? [];
        symbols.Count.ShouldBe(51);

        // Anti-vacuity: the key cross-project bindings must resolve through the streamed per-project
        // extraction exactly as they did through the retained-model pass (mirrors the spike baseline).
        references.ShouldContain(r =>
            r.RefKind == "invocation"
            && r.TargetSymbolId == "M:Contracts.IPatientRepository.GetById(System.Int32)"
            && r.EnclosingSymbolId == "M:Business.BookingService.Book(System.Int32)"
        );
        references.ShouldContain(r =>
            r.RefKind == "invocation"
            && r.TargetSymbolId == "M:Foundation.Db.Query(System.String)"
            && r.EnclosingSymbolId == "M:DataAccess.PatientRepository.GetById(System.Int32)"
        );

        AssertNonDecreasingByPath(symbols.Select(s => s.FilePath), "symbols");
        AssertNonDecreasingByPath(references.Select(r => r.FilePath), "references");
    }

    private static void AssertNonDecreasingByPath(IEnumerable<string> paths, string factKind)
    {
        string? previous = null;
        var position = 0;
        foreach (var path in paths)
        {
            if (previous is not null)
            {
                StringComparer
                    .OrdinalIgnoreCase.Compare(previous, path)
                    .ShouldBeLessThanOrEqualTo(
                        0,
                        $"{factKind} emit order must be non-decreasing by FilePath (OrdinalIgnoreCase); "
                            + $"position {position}: '{previous}' > '{path}'"
                    );
            }

            previous = path;
            position++;
        }
    }
}
