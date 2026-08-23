using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Rig.Analysis;
using Rig.Analysis.Inventory;
using Rig.Domain;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;
using RuleSet = Rig.Domain.Data.RuleSet;

namespace Rig.Tests.Analysis;

public sealed class ExactPathRefinementTests
{
    [Test]
    public void Disjoint_unknown_origin_requires_no_work_and_does_not_materialize_facts()
    {
        using var fixture = Fixture.Create(references: [Reference(Fixture.Start, Fixture.End, Fixture.APath)], dirty: DirtyOrigin.B);

        var plan = ExactForwardRefinement.Plan(fixture.Snapshot, Demand(Fixture.Start, Fixture.End));

        plan.UnavailableReason.ShouldBeNull();
        plan.SelectedOrigins.ShouldBeEmpty();
        fixture.Snapshot.FullMaterializationCount.ShouldBe(0);
    }

    [Test]
    public void Unrelated_dirty_generator_origin_is_selected_even_when_broad_endpoints_already_match()
    {
        using var fixture = Fixture.Create(
            references: [Reference(Fixture.Start, Fixture.End, Fixture.APath)],
            dirty: DirtyOrigin.B,
            generatorCapableB: true
        );

        var plan = ExactForwardRefinement.Plan(fixture.Snapshot, Demand("Flow.Start", "Flow.End"));

        plan.FromMatched.ShouldBeTrue();
        plan.ToMatched.ShouldBeTrue();
        plan.SelectedOrigins.ShouldBe([fixture.B], ignoreOrder: true);
        plan.UnknownOrigins.ShouldBe([fixture.B], ignoreOrder: true);
        fixture.Snapshot.FullMaterializationCount.ShouldBe(0);
    }

    [Test]
    public void Intersecting_origin_is_selected_by_its_actual_emitter_document()
    {
        using var fixture = Fixture.Create(references: [Reference(Fixture.Start, Fixture.End, Fixture.APath)], dirty: DirtyOrigin.A);

        var plan = ExactForwardRefinement.Plan(fixture.Snapshot, Demand(Fixture.Start, Fixture.End));

        plan.SelectedOrigins.ShouldBe([fixture.A], ignoreOrder: true);
        plan.UnknownOrigins.ShouldBe([fixture.A], ignoreOrder: true);
        fixture.Snapshot.FullMaterializationCount.ShouldBe(0);
    }

    [Test]
    public void Path_with_same_from_and_to_populates_reverse_endpoint_topology_independently()
    {
        using var fixture = Fixture.Create(references: [], dirty: DirtyOrigin.B, bReferencesA: true);

        var plan = ExactForwardRefinement.Plan(fixture.Snapshot, Demand(Fixture.Start, Fixture.Start));

        // B is a reverse dependent of the TO seed. This fails if FROM wins a mutually-exclusive endpoint map.
        plan.SelectedOrigins.ShouldBe([fixture.B], ignoreOrder: true);
    }

    [Test]
    public void Sideways_interface_dispatch_selects_implementation_project_without_project_reference()
    {
        using var fixture = Fixture.Create(
            references: [],
            dirty: DirtyOrigin.B,
            symbols:
            [
                Method(Fixture.InterfaceMethod, "T:Contracts.IWork", Fixture.APath, "Contracts"),
                Method(Fixture.ImplementationMethod, "T:Impl.Work", Fixture.BPath, "Impl"),
            ],
            dispatch: [new DispatchFact(Fixture.InterfaceMethod, Fixture.ImplementationMethod, DispatchKinds.Impl, Fixture.BPath)]
        );

        var plan = ExactForwardRefinement.Plan(fixture.Snapshot, Demand(Fixture.InterfaceMethod, Fixture.ImplementationMethod));

        plan.UnavailableReason.ShouldBeNull();
        plan.SelectedOrigins.ShouldBe([fixture.B], ignoreOrder: true);
        fixture.Snapshot.FullMaterializationCount.ShouldBe(0);
    }

    [Test]
    public void Linked_source_emitter_maps_to_every_owning_project()
    {
        using var fixture = Fixture.Create(references: [], dirty: DirtyOrigin.None, linkedSource: true);

        var ownership = fixture.Snapshot.Surfaces.ResolveEmitterOwnership(fixture.Snapshot.Solution, Fixture.APath, "A");

        ownership.IsExact.ShouldBeTrue();
        ownership.ProjectIds.ShouldBe([fixture.A, fixture.B], ignoreOrder: true);
    }

    [Test]
    public void Unowned_generated_endpoint_is_exact_unavailable_not_an_authoritative_no_path()
    {
        var generatedPath = Path.Combine(Path.GetTempPath(), "rig-exact-tests", "obj", "Generated.g.cs");
        using var fixture = Fixture.Create(
            references: [],
            dirty: DirtyOrigin.None,
            symbols: [Method(Fixture.Start, "T:Generated.Type", generatedPath, "Generated")]
        );

        var plan = ExactForwardRefinement.Plan(fixture.Snapshot, Demand(Fixture.Start, Fixture.Start));

        plan.UnavailableReason.ShouldNotBeNull();
        plan.UnavailableReason.ShouldContain("owned");
    }

    [Test]
    public async Task Body_only_intersection_publishes_once_without_coarse_extraction()
    {
        using var fixture = TransactionFixture.Create();
        await fixture.ApplyEditAsync(surfaceChanged: false);
        var basis = fixture.Index.CaptureSnapshot();

        var outcome = await fixture.Index.EnsureExactForwardAsync(basis, Demand(TransactionFixture.Start, TransactionFixture.End));

        outcome.Kind.ShouldBe(ExactForwardRefinementKind.ExactPublished);
        outcome.Snapshot.ShouldBeSameAs(fixture.Index.CaptureSnapshot());
        outcome.Snapshot.Revision.Value.ShouldBe(
            basis.Revision.Value + 1,
            "the private surface candidate must publish through one final CAS"
        );
        outcome.Snapshot.Dirty.PendingDocuments.ShouldBeEmpty();
        outcome.Snapshot.FullMaterializationCount.ShouldBe(0);
        fixture.RefreshCalls.ShouldBe(1);
        fixture.ExtractCalls.ShouldBe(1, "only the eager edited-file extraction should run for BodyOnly");
    }

    [Test]
    public async Task Changed_intersection_batches_the_selected_origin_and_still_publishes_once()
    {
        using var fixture = TransactionFixture.Create();
        await fixture.ApplyEditAsync(surfaceChanged: true);
        var basis = fixture.Index.CaptureSnapshot();

        var outcome = await fixture.Index.EnsureExactForwardAsync(basis, Demand(TransactionFixture.Start, TransactionFixture.End));

        outcome.Kind.ShouldBe(ExactForwardRefinementKind.ExactPublished);
        outcome.Snapshot.ShouldBeSameAs(fixture.Index.CaptureSnapshot());
        outcome.Snapshot.Revision.Value.ShouldBe(basis.Revision.Value + 1);
        outcome.Snapshot.Dirty.PendingDocuments.ShouldBeEmpty();
        outcome.Snapshot.FullMaterializationCount.ShouldBe(0);
        fixture.RefreshCalls.ShouldBe(1);
        fixture.ExtractCalls.ShouldBe(2, "eager extraction plus one deduplicated coarse batch");
        fixture.LastExtractedDocuments.ShouldBe([fixture.FlowDocument, fixture.DependentDocument], ignoreOrder: true);
        outcome
            .Snapshot.GraphView.ReferencesFrom(TransactionFixture.Start)
            .ShouldContain(reference => reference.TargetSymbolId == TransactionFixture.End);
    }

    [Test]
    public async Task Cancellation_before_the_final_CAS_publishes_no_private_candidate()
    {
        using var fixture = TransactionFixture.Create(blockRefresh: true);
        await fixture.ApplyEditAsync(surfaceChanged: false);
        var basis = fixture.Index.CaptureSnapshot();
        using var cancellation = new CancellationTokenSource();

        var refinement = fixture.Index.EnsureExactForwardAsync(
            basis,
            Demand(TransactionFixture.Start, TransactionFixture.End),
            cancellation.Token
        );
        await fixture.RefreshStarted.Task;
        cancellation.Cancel();
        fixture.ReleaseRefresh.SetResult();

        await Should.ThrowAsync<OperationCanceledException>(async () => await refinement);
        fixture.Index.CaptureSnapshot().ShouldBeSameAs(basis);
        fixture.Index.CaptureSnapshot().Revision.ShouldBe(basis.Revision);
    }

    private static ExactForwardDemand Demand(string from, string to) =>
        new(
            ExactForwardQueryKind.Path,
            from,
            to,
            new DemandForwardGraphRules(new ForwardCallProjectionRules(), [], []),
            int.MaxValue,
            FactPathFinder.TraversalMode.SyncCut
        );

    private static SymbolFact Method(string id, string containingType, string path, string assembly) =>
        new(id, SymbolKinds.Method, id, "", containingType, "public", "", id, path, 1, 1, assembly, false);

    private static ReferenceFact Reference(string caller, string callee, string path) =>
        new(callee, RefKinds.Invocation, caller, "A", true, path, 1);

    private enum DirtyOrigin
    {
        None,
        A,
        B,
    }

    private sealed class Fixture : IDisposable
    {
        internal const string Start = "M:A.Flow.Start()";
        internal const string End = "M:A.Flow.End()";
        internal const string InterfaceMethod = "M:Contracts.IWork.Run()";
        internal const string ImplementationMethod = "M:Impl.Work.Run()";
        internal static readonly string APath = Path.Combine(Path.GetTempPath(), "rig-exact-tests", "A", "A.cs");
        internal static readonly string BPath = Path.Combine(Path.GetTempPath(), "rig-exact-tests", "B", "B.cs");

        private Fixture(AdhocWorkspace workspace, FactSnapshot snapshot, ProjectId a, ProjectId b)
        {
            Workspace = workspace;
            Snapshot = snapshot;
            A = a;
            B = b;
        }

        internal AdhocWorkspace Workspace { get; }
        internal FactSnapshot Snapshot { get; }
        internal ProjectId A { get; }
        internal ProjectId B { get; }

        internal static Fixture Create(
            IReadOnlyList<ReferenceFact> references,
            DirtyOrigin dirty,
            IReadOnlyList<SymbolFact>? symbols = null,
            IReadOnlyList<DispatchFact>? dispatch = null,
            bool bReferencesA = false,
            bool linkedSource = false,
            bool generatorCapableB = false
        )
        {
            var workspace = new AdhocWorkspace();
            var a = ProjectId.CreateNewId("A");
            var b = ProjectId.CreateNewId("B");
            var aDocument = DocumentId.CreateNewId(a, "A.cs");
            var bDocument = DocumentId.CreateNewId(b, "B.cs");
            var solution = SolutionInfo.Create(
                SolutionId.CreateNewId(),
                VersionStamp.Create(),
                projects:
                [
                    ProjectInfo.Create(
                        a,
                        VersionStamp.Create(),
                        "A",
                        "A",
                        LanguageNames.CSharp,
                        filePath: Path.ChangeExtension(APath, ".csproj"),
                        documents: [Document(aDocument, APath)]
                    ),
                    ProjectInfo.Create(
                        b,
                        VersionStamp.Create(),
                        "B",
                        "B",
                        LanguageNames.CSharp,
                        filePath: Path.ChangeExtension(BPath, ".csproj"),
                        projectReferences: bReferencesA ? [new ProjectReference(a)] : [],
                        analyzerReferences: generatorCapableB
                            ?
                            [
                                new Microsoft.CodeAnalysis.Diagnostics.AnalyzerFileReference(
                                    typeof(ExactPathRefinementTests).Assembly.Location,
                                    TestAnalyzerLoader.Instance
                                ),
                            ]
                            : [],
                        documents: [Document(bDocument, linkedSource ? APath : BPath)]
                    ),
                ]
            );
            workspace.AddSolution(solution);

            var methodRows = symbols ?? [Method(Start, "T:A.Flow", APath, "A"), Method(End, "T:A.Flow", APath, "A")];
            var facts = new AnalysisResult("Exact.sln", [], [], Symbols: methodRows, References: references, DispatchFacts: dispatch ?? []);
            var contributions = ImmutableDictionary.CreateBuilder<ProjectId, ImmutableHashSet<DocumentId>>();
            if (dirty == DirtyOrigin.A)
            {
                contributions[a] = [aDocument];
            }
            else if (dirty == DirtyOrigin.B)
            {
                contributions[b] = [bDocument];
            }
            var states = contributions.Keys.ToImmutableDictionary(id => id, _ => SurfaceState.Unknown);
            var current = workspace.CurrentSolution;
            var snapshot = new FactSnapshot(
                new FactRevision(1),
                current,
                facts,
                ImmutableDictionary.Create<string, FileFacts>(StringComparer.OrdinalIgnoreCase),
                DirtySet.FromContributions(current, contributions.ToImmutable()),
                SnapshotDelta.Empty with
                {
                    SurfaceStates = states,
                }
            );
            return new Fixture(workspace, snapshot, a, b);
        }

        public void Dispose() => Workspace.Dispose();

        private static DocumentInfo Document(DocumentId id, string path) =>
            DocumentInfo.Create(
                id,
                Path.GetFileName(path),
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(""), VersionStamp.Create())),
                filePath: path
            );

        private sealed class TestAnalyzerLoader : Microsoft.CodeAnalysis.IAnalyzerAssemblyLoader
        {
            internal static readonly TestAnalyzerLoader Instance = new();

            public void AddDependencyLocation(string fullPath) { }

            public System.Reflection.Assembly LoadFromPath(string fullPath) => System.Reflection.Assembly.LoadFrom(fullPath);
        }
    }

    private sealed class TransactionFixture : IDisposable
    {
        internal const string Start = "M:A.Flow.Start()";
        internal const string End = "M:A.Flow.End()";

        private readonly bool _blockRefresh;

        private TransactionFixture(
            ResidentIndex index,
            string editPath,
            DocumentId flowDocument,
            DocumentId dependentDocument,
            bool blockRefresh
        )
        {
            Index = index;
            EditPath = editPath;
            FlowDocument = flowDocument;
            DependentDocument = dependentDocument;
            _blockRefresh = blockRefresh;
        }

        internal ResidentIndex Index { get; }
        internal string EditPath { get; }
        internal DocumentId FlowDocument { get; }
        internal DocumentId DependentDocument { get; }
        internal int ExtractCalls { get; private set; }
        internal int RefreshCalls { get; private set; }
        internal IReadOnlyCollection<DocumentId> LastExtractedDocuments { get; private set; } = [];
        internal TaskCompletionSource RefreshStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseRefresh { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal static TransactionFixture Create(bool blockRefresh = false)
        {
            var root = Path.Combine(Path.GetTempPath(), $"rig-exact-transaction-{Guid.NewGuid():N}");
            var editPath = Path.Combine(root, "A.Edit.cs");
            var flowPath = Path.Combine(root, "A.Flow.cs");
            var dependentPath = Path.Combine(root, "B.cs");
            var aProjectPath = Path.Combine(root, "A.csproj");
            var bProjectPath = Path.Combine(root, "B.csproj");
            var workspace = new RigWorkspace();
            var a = ProjectId.CreateNewId("A");
            var b = ProjectId.CreateNewId("B");
            var edit = DocumentId.CreateNewId(a, "A.Edit.cs");
            var flow = DocumentId.CreateNewId(a, "A.Flow.cs");
            var dependent = DocumentId.CreateNewId(b, "B.cs");
            workspace.AddSolution(
                SolutionInfo.Create(
                    SolutionId.CreateNewId(),
                    VersionStamp.Create(),
                    projects:
                    [
                        ProjectInfo.Create(
                            a,
                            VersionStamp.Create(),
                            "A",
                            "A",
                            LanguageNames.CSharp,
                            filePath: aProjectPath,
                            documents: [Document(edit, editPath, "BODY-0"), Document(flow, flowPath, "FLOW")]
                        ),
                        ProjectInfo.Create(
                            b,
                            VersionStamp.Create(),
                            "B",
                            "B",
                            LanguageNames.CSharp,
                            filePath: bProjectPath,
                            projectReferences: [new ProjectReference(a)],
                            documents: [Document(dependent, dependentPath, "DEPENDENT")]
                        ),
                    ]
                )
            );

            var metaA = SurfaceShard("", "meta-a");
            var metaB = SurfaceShard("", "meta-b");
            var surfaces = new[]
            {
                Surface("A", aProjectPath, "A", [SurfaceShard(editPath, "edit"), SurfaceShard(flowPath, "flow"), metaA]),
                Surface("B", bProjectPath, "B", [SurfaceShard(dependentPath, "dependent"), metaB]),
            };
            var baseFacts = new AnalysisResult(
                Path.Combine(root, "Exact.sln"),
                [Source("A", editPath, "BODY-0"), Source("A", flowPath, "FLOW"), Source("B", dependentPath, "DEPENDENT")],
                [],
                Symbols: [Method(Start, "T:A.Flow", flowPath, "A"), Method(End, "T:A.Flow", flowPath, "A")],
                References: [Reference(Start, End, flowPath)],
                ProjectSurfaces: surfaces
            );

            TransactionFixture? fixture = null;
            async Task<Dictionary<string, FileFacts>> Extract(
                Solution solution,
                IReadOnlyCollection<DocumentId> documents,
                string _,
                RuleSet __,
                CancellationToken cancellationToken,
                Rig.Analysis.Extraction.StringInterner? ___
            )
            {
                fixture!.ExtractCalls++;
                fixture.LastExtractedDocuments = documents.ToArray();
                var slices = new Dictionary<string, FileFacts>(StringComparer.OrdinalIgnoreCase);
                foreach (var documentId in documents)
                {
                    var document = solution.GetDocument(documentId)!;
                    var path = document.FilePath!;
                    var text = (await document.GetTextAsync(cancellationToken)).ToString();
                    var isFlow = string.Equals(path, flowPath, StringComparison.OrdinalIgnoreCase);
                    var surfaceKey = string.Equals(path, editPath, StringComparison.OrdinalIgnoreCase)
                        ? text.Contains("SURFACE", StringComparison.Ordinal)
                            ? "edit-v2"
                            : "edit"
                        : isFlow
                            ? "flow"
                            : "dependent";
                    var contribution = new ProjectSurfaceContribution(
                        document.Project.Name,
                        document.Project.FilePath ?? "",
                        document.Project.AssemblyName ?? document.Project.Name,
                        SurfaceShard(path, surfaceKey),
                        true
                    );
                    slices[path] = new FileFacts(
                        [Source(document.Project.Name, path, text)],
                        [],
                        isFlow ? [Method(Start, "T:A.Flow", flowPath, "A"), Method(End, "T:A.Flow", flowPath, "A")] : [],
                        isFlow ? [Reference(Start, End, flowPath)] : [],
                        [],
                        [],
                        [],
                        [],
                        [contribution]
                    );
                }
                return slices;
            }

            async Task<ProjectSurfaceRefresh> Refresh(
                Solution _,
                ProjectId projectId,
                RuleSet __,
                CancellationToken ___,
                Rig.Analysis.Extraction.StringInterner? ____
            )
            {
                fixture!.RefreshCalls++;
                if (fixture._blockRefresh)
                {
                    fixture.RefreshStarted.TrySetResult();
                    await fixture.ReleaseRefresh.Task;
                }
                return new ProjectSurfaceRefresh([], projectId == a ? metaA : metaB, true);
            }

            var index = new ResidentIndex(
                workspace,
                baseFacts,
                baseFacts.SolutionPath,
                new RuleSet(),
                extractFiles: Extract,
                refreshSurface: Refresh
            );
            fixture = new TransactionFixture(index, editPath, flow, dependent, blockRefresh);
            return fixture;
        }

        internal Task ApplyEditAsync(bool surfaceChanged) =>
            Index.ApplyEditAsync(EditPath, SourceText.From(surfaceChanged ? "SURFACE-1" : "BODY-1"));

        public void Dispose() => Index.Dispose();

        private static ProjectSurfaceShard SurfaceShard(string path, string value) => new(path, false, ProjectContentHash.Compute([value]));

        private static ProjectSurfaceSnapshot Surface(
            string name,
            string projectPath,
            string assembly,
            IReadOnlyList<ProjectSurfaceShard> shards
        ) => new(name, projectPath, assembly, shards, ProjectSurfaceBuilder.Aggregate(shards));

        private static SourceFileInfo Source(string project, string path, string evidence) =>
            new(project, path, "indexed", "high", "test", "test", evidence);

        private static DocumentInfo Document(DocumentId id, string path, string text) =>
            DocumentInfo.Create(
                id,
                Path.GetFileName(path),
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(text), VersionStamp.Create(), path)),
                filePath: path
            );
    }
}
