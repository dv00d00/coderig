using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Rig.Analysis;
using Rig.Analysis.Inventory;
using Rig.Domain;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;
using AnalyzerReference = Microsoft.CodeAnalysis.Diagnostics.AnalyzerReference;
using RuleSet = Rig.Domain.Data.RuleSet;

namespace Rig.Tests.Analysis;

public sealed class ExactCallersRefinementTests
{
    [Test]
    public void Reverse_boundary_includes_every_structural_owner_and_its_project_dependents_but_not_disjoint_debt()
    {
        using var fixture = Fixture.Create(allChanged: true);

        var plan = ExactCallersRefinement.Plan(fixture.Snapshot, Demand(Fixture.Target));

        plan.UnavailableReason.ShouldBeNull();
        plan.ToMatched.ShouldBeTrue();
        plan.SelectedOrigins.ShouldBe([fixture.Contract, fixture.Impl, fixture.Caller, fixture.Dependent], ignoreOrder: true);
        plan.SelectedOrigins.ShouldNotContain(fixture.Noise);
        plan.UnknownOrigins.ShouldBeEmpty();
        fixture.Snapshot.FullMaterializationCount.ShouldBe(0);
    }

    [Test]
    public void Dispatch_and_type_relation_emitter_ownership_is_load_bearing_for_contract_dependents()
    {
        using var fixture = Fixture.Create(allChanged: true, includeCallerReference: false);

        var plan = ExactCallersRefinement.Plan(fixture.Snapshot, Demand(Fixture.Target));

        plan.UnavailableReason.ShouldBeNull();
        plan.SelectedOrigins.ShouldContain(fixture.Contract);
        plan.SelectedOrigins.ShouldContain(fixture.Dependent, "Dependent references Contract, not the Impl project that owns TO");
        fixture.Snapshot.Solution.GetProject(fixture.Dependent)!.ProjectReferences.ShouldContain(r => r.ProjectId == fixture.Contract);
        fixture.Snapshot.Solution.GetProject(fixture.Dependent)!.ProjectReferences.ShouldNotContain(r => r.ProjectId == fixture.Impl);
        fixture.Snapshot.FullMaterializationCount.ShouldBe(0);
    }

    [Test]
    public void Whole_resident_scope_selects_all_debt()
    {
        using var fixture = Fixture.Create(allChanged: true);

        var plan = ExactCallersRefinement.Plan(
            fixture.Snapshot,
            Demand(Fixture.Target) with
            {
                DebtScope = ExactForwardDebtScope.WholeResident,
            }
        );

        plan.SelectedOrigins.ShouldBe(
            [fixture.Contract, fixture.Impl, fixture.Caller, fixture.Dependent, fixture.Noise],
            ignoreOrder: true
        );
        fixture.Snapshot.FullMaterializationCount.ShouldBe(0);
    }

    [Test]
    public void Missing_target_refreshes_every_unknown_origin()
    {
        using var fixture = Fixture.Create(allUnknown: true);

        var plan = ExactCallersRefinement.Plan(fixture.Snapshot, Demand("M:Missing.Target.Run()"));

        plan.ToMatched.ShouldBeFalse();
        plan.SelectedOrigins.ShouldBe(
            [fixture.Contract, fixture.Impl, fixture.Caller, fixture.Dependent, fixture.Noise],
            ignoreOrder: true
        );
        plan.UnknownOrigins.ShouldBe(plan.SelectedOrigins);
        fixture.Snapshot.FullMaterializationCount.ShouldBe(0);
    }

    [Test]
    public void Existing_target_still_selects_an_unrelated_generator_capable_unknown()
    {
        using var fixture = Fixture.Create(noiseUnknownAndGeneratorCapable: true);

        var plan = ExactCallersRefinement.Plan(fixture.Snapshot, Demand(Fixture.Target));

        plan.ToMatched.ShouldBeTrue();
        plan.SelectedOrigins.ShouldContain(fixture.Noise);
        plan.UnknownOrigins.ShouldContain(fixture.Noise);
        fixture.Snapshot.FullMaterializationCount.ShouldBe(0);
    }

    [Test]
    public void Unowned_generated_emitter_and_graph_cap_fail_closed()
    {
        using var generated = Fixture.Create(targetPath: Path.Combine(Fixture.Root, "obj", "Generated.g.cs"));

        var unowned = ExactCallersRefinement.Plan(generated.Snapshot, Demand(Fixture.Target));

        unowned.UnavailableReason.ShouldNotBeNull();
        unowned.UnavailableReason.ShouldContain("owned");
        unowned.SelectedOrigins.ShouldBeEmpty();
        generated.Snapshot.FullMaterializationCount.ShouldBe(0);

        using var capped = Fixture.Create();
        var overCap = ExactCallersRefinement.Plan(capped.Snapshot, Demand(Fixture.Target) with { MaxNodes = 1 });

        overCap.UnavailableReason.ShouldNotBeNull();
        overCap.UnavailableReason.ShouldContain("cap");
        overCap.SelectedOrigins.ShouldBeEmpty();
        capped.Snapshot.FullMaterializationCount.ShouldBe(0);
    }

    [Test]
    public async Task Exact_callers_transaction_repays_dirty_topology_and_publishes_once()
    {
        using var fixture = TransactionFixture.Create();
        await fixture.Index.ApplyEditAsync(fixture.EditPath, SourceText.From("BODY-1"));
        var basis = fixture.Index.CaptureSnapshot();

        var outcome = await fixture.Index.EnsureExactCallersAsync(basis, Demand(TransactionFixture.Target));

        outcome.Kind.ShouldBe(ExactForwardRefinementKind.ExactPublished);
        outcome.Snapshot.ShouldBeSameAs(fixture.Index.CaptureSnapshot());
        outcome.Snapshot.Revision.Value.ShouldBe(basis.Revision.Value + 1, "private replans publish through one final CAS");
        outcome.Snapshot.Dirty.PendingDocuments.ShouldBeEmpty();
        outcome.Snapshot.GraphView.ReferencesTo(TransactionFixture.Target).ShouldNotBeEmpty();
        outcome.Snapshot.FullMaterializationCount.ShouldBe(0);
        fixture.RefreshCalls.ShouldBe(1);
        fixture.ExtractCalls.ShouldBe(1, "BodyOnly debt is settled without a coarse extraction");
    }

    private static ExactCallersDemand Demand(string target) =>
        new(
            target,
            new DemandForwardGraphRules(new ForwardCallProjectionRules(), [], []),
            int.MaxValue,
            FactPathFinder.TraversalMode.SyncCut,
            FactPathFinder.TraversalMode.SyncCut
        );

    private sealed class Fixture : IDisposable
    {
        internal const string Target = "M:Impl.Work.Run()";
        private const string Hub = "M:Contracts.IWork.Run()";
        private const string RootMethod = "M:Caller.Root.Go()";
        internal static readonly string Root = Path.Combine(Path.GetTempPath(), "rig-exact-callers-tests");

        private Fixture(
            AdhocWorkspace workspace,
            FactSnapshot snapshot,
            ProjectId contract,
            ProjectId impl,
            ProjectId caller,
            ProjectId dependent,
            ProjectId noise
        )
        {
            Workspace = workspace;
            Snapshot = snapshot;
            Contract = contract;
            Impl = impl;
            Caller = caller;
            Dependent = dependent;
            Noise = noise;
        }

        internal AdhocWorkspace Workspace { get; }
        internal FactSnapshot Snapshot { get; }
        internal ProjectId Contract { get; }
        internal ProjectId Impl { get; }
        internal ProjectId Caller { get; }
        internal ProjectId Dependent { get; }
        internal ProjectId Noise { get; }

        internal static Fixture Create(
            bool allChanged = false,
            bool allUnknown = false,
            bool noiseUnknownAndGeneratorCapable = false,
            bool includeCallerReference = true,
            string? targetPath = null
        )
        {
            var workspace = new AdhocWorkspace();
            var contract = ProjectId.CreateNewId("Contract");
            var impl = ProjectId.CreateNewId("Impl");
            var caller = ProjectId.CreateNewId("Caller");
            var dependent = ProjectId.CreateNewId("Dependent");
            var noise = ProjectId.CreateNewId("Noise");
            var contractDocument = DocumentId.CreateNewId(contract, "Contract.cs");
            var implDocument = DocumentId.CreateNewId(impl, "Impl.cs");
            var callerDocument = DocumentId.CreateNewId(caller, "Caller.cs");
            var dependentDocument = DocumentId.CreateNewId(dependent, "Dependent.cs");
            var noiseDocument = DocumentId.CreateNewId(noise, "Noise.cs");
            var contractPath = Path.Combine(Root, "Contract", "Contract.cs");
            var implPath = Path.Combine(Root, "Impl", "Impl.cs");
            var callerPath = Path.Combine(Root, "Caller", "Caller.cs");
            var dependentPath = Path.Combine(Root, "Dependent", "Dependent.cs");
            var noisePath = Path.Combine(Root, "Noise", "Noise.cs");
            targetPath ??= implPath;

            workspace.AddSolution(
                SolutionInfo.Create(
                    SolutionId.CreateNewId(),
                    VersionStamp.Create(),
                    projects:
                    [
                        Project(contract, "Contract", contractDocument, contractPath),
                        Project(impl, "Impl", implDocument, implPath, [new ProjectReference(contract)]),
                        Project(caller, "Caller", callerDocument, callerPath, [new ProjectReference(contract)]),
                        Project(dependent, "Dependent", dependentDocument, dependentPath, [new ProjectReference(contract)]),
                        Project(
                            noise,
                            "Noise",
                            noiseDocument,
                            noisePath,
                            analyzers: noiseUnknownAndGeneratorCapable
                                ?
                                [
                                    new Microsoft.CodeAnalysis.Diagnostics.AnalyzerFileReference(
                                        typeof(ExactCallersRefinementTests).Assembly.Location,
                                        AnalyzerLoader.Instance
                                    ),
                                ]
                                : []
                        ),
                    ]
                )
            );

            var symbols = new List<SymbolFact>
            {
                Method(Hub, "T:Contracts.IWork", contractPath, "Contract"),
                Method(Target, "T:Impl.Work", targetPath, "Impl"),
                Method(RootMethod, "T:Caller.Root", callerPath, "Caller"),
            };
            var references = includeCallerReference
                ? new[] { new ReferenceFact(Hub, RefKinds.Invocation, RootMethod, "Contract", true, callerPath, 1, "Impl.Work") }
                : [];
            var facts = new AnalysisResult(
                "ExactCallers.sln",
                [],
                [],
                Symbols: symbols,
                References: references,
                TypeRelations: [new TypeRelationFact("T:Impl.Work", "T:Contracts.IWork", RelationKinds.Interface, contractPath)],
                DispatchFacts: [new DispatchFact(Hub, Target, DispatchKinds.Impl, contractPath)]
            );

            var documents = new Dictionary<ProjectId, DocumentId>
            {
                [contract] = contractDocument,
                [impl] = implDocument,
                [caller] = callerDocument,
                [dependent] = dependentDocument,
                [noise] = noiseDocument,
            };
            var contributions = ImmutableDictionary.CreateBuilder<ProjectId, ImmutableHashSet<DocumentId>>();
            var states = ImmutableDictionary.CreateBuilder<ProjectId, SurfaceState>();
            foreach (var pair in documents)
            {
                if (allChanged || allUnknown || noiseUnknownAndGeneratorCapable && pair.Key == noise)
                {
                    contributions[pair.Key] = [pair.Value];
                    states[pair.Key] =
                        allUnknown || noiseUnknownAndGeneratorCapable && pair.Key == noise ? SurfaceState.Unknown : SurfaceState.Changed;
                }
            }

            var solution = workspace.CurrentSolution;
            var snapshot = new FactSnapshot(
                new FactRevision(1),
                solution,
                facts,
                ImmutableDictionary.Create<string, FileFacts>(StringComparer.OrdinalIgnoreCase),
                DirtySet.FromContributions(solution, contributions.ToImmutable()),
                SnapshotDelta.Empty with
                {
                    SurfaceStates = states.ToImmutable(),
                }
            );
            return new Fixture(workspace, snapshot, contract, impl, caller, dependent, noise);
        }

        public void Dispose() => Workspace.Dispose();

        private static ProjectInfo Project(
            ProjectId id,
            string name,
            DocumentId document,
            string path,
            IReadOnlyList<ProjectReference>? references = null,
            IReadOnlyList<AnalyzerReference>? analyzers = null
        ) =>
            ProjectInfo.Create(
                id,
                VersionStamp.Create(),
                name,
                name,
                LanguageNames.CSharp,
                filePath: Path.ChangeExtension(path, ".csproj"),
                projectReferences: references ?? [],
                analyzerReferences: analyzers ?? [],
                documents: [Document(document, path, "")]
            );
    }

    private sealed class TransactionFixture : IDisposable
    {
        internal const string Target = "M:Flow.Target.Run()";
        private const string Caller = "M:Flow.Caller.Go()";

        private TransactionFixture(ResidentIndex index, string editPath)
        {
            Index = index;
            EditPath = editPath;
        }

        internal ResidentIndex Index { get; }
        internal string EditPath { get; }
        internal int ExtractCalls { get; private set; }
        internal int RefreshCalls { get; private set; }

        internal static TransactionFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"rig-exact-callers-transaction-{Guid.NewGuid():N}");
            var editPath = Path.Combine(root, "Edit.cs");
            var flowPath = Path.Combine(root, "Flow.cs");
            var projectPath = Path.Combine(root, "Flow.csproj");
            var workspace = new RigWorkspace();
            var project = ProjectId.CreateNewId("Flow");
            var edit = DocumentId.CreateNewId(project, "Edit.cs");
            var flow = DocumentId.CreateNewId(project, "Flow.cs");
            workspace.AddSolution(
                SolutionInfo.Create(
                    SolutionId.CreateNewId(),
                    VersionStamp.Create(),
                    projects:
                    [
                        ProjectInfo.Create(
                            project,
                            VersionStamp.Create(),
                            "Flow",
                            "Flow",
                            LanguageNames.CSharp,
                            filePath: projectPath,
                            documents: [Document(edit, editPath, "BODY-0"), Document(flow, flowPath, "FLOW")]
                        ),
                    ]
                )
            );

            var editShard = Shard(editPath, "edit");
            var flowShard = Shard(flowPath, "flow");
            var meta = Shard("", "meta");
            var baseFacts = new AnalysisResult(
                Path.Combine(root, "Exact.sln"),
                [],
                [],
                Symbols: [Method(Target, "T:Flow.Target", flowPath, "Flow"), Method(Caller, "T:Flow.Caller", flowPath, "Flow")],
                References: [new ReferenceFact(Target, RefKinds.Invocation, Caller, "Flow", true, flowPath, 1)],
                ProjectSurfaces:
                [
                    new ProjectSurfaceSnapshot(
                        "Flow",
                        projectPath,
                        "Flow",
                        [editShard, flowShard, meta],
                        ProjectSurfaceBuilder.Aggregate([editShard, flowShard, meta])
                    ),
                ]
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
                var slices = new Dictionary<string, FileFacts>(StringComparer.OrdinalIgnoreCase);
                foreach (var documentId in documents)
                {
                    var document = solution.GetDocument(documentId)!;
                    var path = document.FilePath!;
                    var text = (await document.GetTextAsync(cancellationToken)).ToString();
                    var shard = Shard(path, string.Equals(path, editPath, StringComparison.OrdinalIgnoreCase) ? "edit" : "flow");
                    slices[path] = new FileFacts(
                        [],
                        [],
                        string.Equals(path, flowPath, StringComparison.OrdinalIgnoreCase)
                            ? [Method(Target, "T:Flow.Target", flowPath, "Flow"), Method(Caller, "T:Flow.Caller", flowPath, "Flow")]
                            : [],
                        string.Equals(path, flowPath, StringComparison.OrdinalIgnoreCase)
                            ? [new ReferenceFact(Target, RefKinds.Invocation, Caller, "Flow", true, flowPath, 1)]
                            : [],
                        [],
                        [],
                        [],
                        [],
                        [new ProjectSurfaceContribution("Flow", projectPath, "Flow", shard, true)]
                    );
                    _ = text;
                }
                return slices;
            }

            Task<ProjectSurfaceRefresh> Refresh(
                Solution _,
                ProjectId __,
                RuleSet ___,
                CancellationToken ____,
                Rig.Analysis.Extraction.StringInterner? _____
            )
            {
                fixture!.RefreshCalls++;
                return Task.FromResult(new ProjectSurfaceRefresh([], meta, true));
            }

            var index = new ResidentIndex(
                workspace,
                baseFacts,
                baseFacts.SolutionPath,
                new RuleSet(),
                cascadePolicy: new SelectedDocumentsPolicy([flow]),
                extractFiles: Extract,
                refreshSurface: Refresh
            );
            fixture = new TransactionFixture(index, editPath);
            return fixture;
        }

        public void Dispose() => Index.Dispose();

        private static ProjectSurfaceShard Shard(string path, string value) => new(path, false, ProjectContentHash.Compute([value]));

        private sealed class SelectedDocumentsPolicy(IReadOnlyCollection<DocumentId> documents) : IDirtySetPolicy
        {
            public IReadOnlyCollection<DocumentId> DocumentsToReextract(Solution solution, IReadOnlyCollection<string> changedFilePaths) =>
                documents;
        }
    }

    private static SymbolFact Method(string id, string containingType, string path, string assembly) =>
        new(id, SymbolKinds.Method, id, "", containingType, "public", "", id, path, 1, 1, assembly, false);

    private static DocumentInfo Document(DocumentId id, string path, string text) =>
        DocumentInfo.Create(
            id,
            Path.GetFileName(path),
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(text), VersionStamp.Create(), path)),
            filePath: path
        );

    private sealed class AnalyzerLoader : IAnalyzerAssemblyLoader
    {
        internal static readonly AnalyzerLoader Instance = new();

        public void AddDependencyLocation(string fullPath) { }

        public System.Reflection.Assembly LoadFromPath(string fullPath) => System.Reflection.Assembly.LoadFrom(fullPath);
    }
}
