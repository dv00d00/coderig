using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Rig.Analysis.Inventory;
using Rig.Cli.Live;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;
using Shouldly;
using RuleSet = Rig.Domain.Data.RuleSet;

namespace Rig.Tests.Analysis;

[NotInParallel]
public sealed class CompositeFactSnapshotViewTests
{
    [Test]
    public async Task Published_live_query_artifacts_do_not_flatten_the_snapshot()
    {
        using var workspace = new RigWorkspace();
        var projectId = ProjectId.CreateNewId("CompositeView");
        var documentId = DocumentId.CreateNewId(projectId, "Program");
        var root = Path.Combine(Path.GetTempPath(), $"rig-composite-view-{Guid.NewGuid():N}");
        var filePath = Path.Combine(root, "Program.cs");
        var solutionPath = Path.Combine(root, "CompositeView.sln");
        var document = DocumentInfo.Create(
            documentId,
            "Program.cs",
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From("revision-zero"), VersionStamp.Create(), filePath)),
            filePath: filePath
        );
        workspace.AddSolution(
            SolutionInfo.Create(
                SolutionId.CreateNewId(),
                VersionStamp.Create(),
                projects:
                [
                    ProjectInfo.Create(
                        projectId,
                        VersionStamp.Create(),
                        "CompositeView",
                        "CompositeView",
                        LanguageNames.CSharp,
                        documents: [document]
                    ),
                ]
            )
        );

        var baseSlice = QuerySlice(filePath, "revision-zero");
        var baseFacts = ResultFromSlice(solutionPath, baseSlice);
        using var index = new ResidentIndex(
            workspace,
            baseFacts,
            solutionPath,
            new RuleSet(),
            extractFiles: async (solution, documents, _, _, cancellationToken, _) =>
            {
                var text = await solution.GetDocument(documents.Single())!.GetTextAsync(cancellationToken);
                return new Dictionary<string, FileFacts>(StringComparer.OrdinalIgnoreCase)
                {
                    [filePath] = QuerySlice(filePath, text.ToString()),
                };
            }
        );

        await index.ApplyEditAsync(filePath, SourceText.From("revision-one"));
        var snapshot = index.CaptureSnapshot();
        snapshot.FullMaterializationCount.ShouldBe(0);

        var live = new LiveFactSource(snapshot, new RuleSet());
        live.TraversalGraph.CallEdges.Count.ShouldBe(1);
        live.ReachInputs.Invocations.Count.ShouldBe(1);
        live.EpData.Methods.Count.ShouldBe(2);
        var querySource = new LiveQueryFactSource(live);
        var queriedGraph = await querySource.LoadShapedTraversalGraphAsync("Caller", SqlReachability.Direction.Forward, new RuleSet());
        queriedGraph.CallEdges.Count.ShouldBe(1);

        snapshot.FullMaterializationCount.ShouldBe(0);
    }

    [Test]
    public void Segmented_multisets_and_health_equal_the_memoized_flattening_oracle()
    {
        const string Replaced = "/repo/Replaced.cs";
        const string Kept = "/repo/Kept.cs";
        using var workspace = new AdhocWorkspace();
        var partial = new ProjectCompileFailure("Partial", ProjectCompileFailure.NoCompilation);
        var baseFacts = new AnalysisResult(
            "/repo/App.sln",
            [Source(Replaced, "old"), Source(Kept, "keep")],
            [Di(Replaced, "old"), Di("", "static"), Di("/repo/map.xml", "xml")],
            Symbols: [Symbol("M:Old", Replaced), Symbol("M:Keep", Kept)],
            References: [Reference("M:Old", Replaced), Reference("M:Keep", Kept)],
            TypeRelations: [new("T:Old", "T:I", RelationKinds.Interface, Replaced), new("T:Keep", "T:B", RelationKinds.Base, Kept)],
            DispatchFacts: [new("M:I.Old", "M:Old", DispatchKinds.Impl, Replaced), new("M:B.Keep", "M:Keep", DispatchKinds.Override, Kept)],
            AllocationFacts: [Allocation("Old", Replaced), Allocation("Keep", Kept)],
            CompilationHealth: new CompilationHealth([Health(Replaced, "old error"), Health(Kept, "keep error")], [partial], 3)
        );
        var replacement = new FileFacts(
            [Source(Replaced, "new")],
            [],
            [Symbol("M:New", Replaced)],
            [Reference("M:New", Replaced)],
            [], // deletion tombstone for the replaced file's relation rows
            [new("M:I.New", "M:New", DispatchKinds.Impl, Replaced)],
            [], // deletion tombstone for allocation rows
            [Health(Replaced, "new error")]
        );
        var snapshot = Snapshot(workspace.CurrentSolution, baseFacts, (Replaced, replacement));

        snapshot.EnumerateSourceFiles().Count().ShouldBe(2);
        snapshot.EnumerateDiRegistrations().Count().ShouldBe(2);
        snapshot.EnumerateDiRegistrations().ShouldNotContain(r => r.FilePath == Replaced);
        snapshot.EnumerateSymbols().Count().ShouldBe(2);
        snapshot.EnumerateReferences().Count().ShouldBe(2);
        snapshot.EnumerateTypeRelations().Count().ShouldBe(1);
        snapshot.EnumerateDispatchFacts().Count().ShouldBe(2);
        snapshot.EnumerateAllocationFacts().Count().ShouldBe(1);
        snapshot.GetCompilationHealth()!.Files.Count.ShouldBe(2);
        snapshot.FullMaterializationCount.ShouldBe(0);

        snapshot.EnumerateSourceFiles().Single(f => f.FilePath == Replaced).Evidence.ShouldBe("new");
        snapshot.EnumerateDiRegistrations().ShouldNotContain(r => r.FilePath == Replaced);
        snapshot.EnumerateSymbols().ShouldContain(s => s.SymbolId == "M:New");
        snapshot.EnumerateSymbols().ShouldNotContain(s => s.SymbolId == "M:Old");
        snapshot.EnumerateReferences().ShouldContain(r => r.TargetSymbolId == "M:New");
        snapshot.EnumerateReferences().ShouldNotContain(r => r.TargetSymbolId == "M:Old");
        snapshot.EnumerateTypeRelations().ShouldNotContain(r => r.TypeSymbolId == "T:Old");
        snapshot.EnumerateDispatchFacts().ShouldContain(d => d.SourceMember == "M:I.New");
        snapshot.EnumerateDispatchFacts().ShouldNotContain(d => d.SourceMember == "M:I.Old");
        snapshot.EnumerateAllocationFacts().ShouldNotContain(a => a.ResourceType == "Old");
        snapshot.GetCompilationHealth()!.Files.ShouldContain(f => f.FirstMessage == "new error");
        snapshot.GetCompilationHealth()!.Files.ShouldNotContain(f => f.FirstMessage == "old error");

        var flattened = snapshot.FlattenedFacts;
        snapshot.FullMaterializationCount.ShouldBe(1);
        snapshot.FlattenedFacts.ShouldBeSameAs(flattened);
        snapshot.FullMaterializationCount.ShouldBe(1);
        flattened.SourceFiles.ShouldBe(snapshot.EnumerateSourceFiles());
        flattened.DiRegistrations.ShouldBe(snapshot.EnumerateDiRegistrations());
        flattened.Symbols!.ShouldBe(snapshot.EnumerateSymbols());
        flattened.References!.ShouldBe(snapshot.EnumerateReferences());
        flattened.TypeRelations!.ShouldBe(snapshot.EnumerateTypeRelations());
        flattened.DispatchFacts!.ShouldBe(snapshot.EnumerateDispatchFacts());
        flattened.AllocationFacts!.ShouldBe(snapshot.EnumerateAllocationFacts());
        AssertHealthEqual(snapshot.GetCompilationHealth(), flattened.CompilationHealth);

        var emptyOverlay = Snapshot(workspace.CurrentSolution, baseFacts);
        emptyOverlay.FlattenedFacts.ShouldBeSameAs(baseFacts);
        emptyOverlay.FullMaterializationCount.ShouldBe(1);
    }

    [Test]
    public void Duplicate_provenance_rows_survive_view_while_graph_edges_are_semantically_deduplicated()
    {
        const string EmitterA = "/repo/A.cs";
        const string EmitterB = "/repo/B.cs";
        const string Type = "T:Partials.Emitter";
        const string Interface = "T:Partials.IEmitter";
        const string Contract = "M:Partials.IEmitter.Send";
        const string Implementation = "M:Partials.Emitter.Send";
        using var workspace = new AdhocWorkspace();
        var baseFacts = new AnalysisResult(
            "/repo/App.sln",
            [Source(EmitterA, "a")],
            [],
            TypeRelations: [new(Type, Interface, RelationKinds.Interface, EmitterA)],
            DispatchFacts: [new(Contract, Implementation, DispatchKinds.Impl, EmitterA)]
        );
        var emitterB = new FileFacts(
            [Source(EmitterB, "b")],
            [],
            [],
            [],
            [new(Type, Interface, RelationKinds.Interface, EmitterB)],
            [new(Contract, Implementation, DispatchKinds.Impl, EmitterB)],
            [],
            []
        );
        var snapshot = Snapshot(workspace.CurrentSolution, baseFacts, (EmitterB, emitterB));

        snapshot.EnumerateTypeRelations().Select(r => r.FilePath).ShouldBe([EmitterA, EmitterB]);
        snapshot.EnumerateDispatchFacts().Select(d => d.FilePath).ShouldBe([EmitterA, EmitterB]);

        var graph = FactGraphProjection.FromView(snapshot);
        graph.ImplementsEdges.Count(e => e.ImplType == Type && e.InterfaceType == Interface).ShouldBe(1);
        graph.MinedDispatch!.Count(d => d.SourceMember == Contract && d.TargetMember == Implementation).ShouldBe(1);
        snapshot.FullMaterializationCount.ShouldBe(0);
    }

    private static FactSnapshot Snapshot(Solution solution, AnalysisResult baseFacts, params (string Path, FileFacts Slice)[] overlay)
    {
        var entries = ImmutableDictionary.CreateBuilder<string, FileFacts>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, slice) in overlay)
        {
            entries[path] = slice;
        }

        return new FactSnapshot(new FactRevision(0), solution, baseFacts, entries.ToImmutable(), DirtySet.Empty, SnapshotDelta.Empty);
    }

    private static AnalysisResult ResultFromSlice(string solutionPath, FileFacts slice) =>
        new(
            solutionPath,
            slice.SourceFiles,
            slice.DiRegistrations,
            Symbols: slice.Symbols,
            References: slice.References,
            TypeRelations: slice.TypeRelations,
            DispatchFacts: slice.Dispatch,
            AllocationFacts: slice.Allocations,
            CompilationHealth: new CompilationHealth(slice.CompileHealth, [], 0)
        );

    private static FileFacts QuerySlice(string path, string evidence) =>
        new(
            [Source(path, evidence)],
            [],
            [Symbol("M:Sample.Caller", path), Symbol("M:Sample.Callee", path)],
            [Reference("M:Sample.Callee", path)],
            [],
            [],
            [],
            []
        );

    private static SourceFileInfo Source(string path, string evidence) => new("Project", path, "indexed", "high", "test", "test", evidence);

    private static DiRegistrationInfo Di(string path, string implementation) =>
        new("IService", implementation, "singleton", "test", path, 1, "high", "test", "test", "test");

    private static SymbolFact Symbol(string id, string path)
    {
        var line = id.EndsWith("Caller", StringComparison.Ordinal) ? 1 : 2;
        return new(
            id,
            SymbolKinds.Method,
            id[(id.LastIndexOf('.') + 1)..],
            "Sample",
            null,
            "public",
            "",
            id,
            path,
            line,
            line,
            "Sample",
            false
        );
    }

    private static ReferenceFact Reference(string target, string path) =>
        new(target, RefKinds.Invocation, "M:Sample.Caller", "Sample", true, path, 2);

    private static AllocationFact Allocation(string type, string path) => new("object", type, "M:Sample.Caller", path, 3);

    private static FileCompileHealth Health(string path, string message) => new(path, 1, "CS0001", message);

    private static void AssertHealthEqual(CompilationHealth? actual, CompilationHealth? expected)
    {
        actual.ShouldNotBeNull();
        expected.ShouldNotBeNull();
        actual.Files.ShouldBe(expected.Files);
        actual.PartialProjects.ShouldBe(expected.PartialProjects);
        actual.UnlocatedErrorCount.ShouldBe(expected.UnlocatedErrorCount);
    }
}
