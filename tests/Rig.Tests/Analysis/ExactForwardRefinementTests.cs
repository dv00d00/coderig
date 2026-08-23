using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Rig.Analysis;
using Rig.Analysis.Inventory;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Analysis;

public sealed class ExactForwardRefinementTests
{
    [Test]
    public void Reaches_and_tree_select_the_forward_boundary_but_not_a_reverse_only_dependent()
    {
        using var fixture = Fixture.Create(
            states: [(ProjectSlot.A, SurfaceState.Changed), (ProjectSlot.B, SurfaceState.Changed)],
            bReferencesA: true
        );

        var reaches = ExactForwardRefinement.Plan(fixture.Snapshot, Demand(ExactForwardQueryKind.Reaches, Fixture.Start));
        var tree = ExactForwardRefinement.Plan(fixture.Snapshot, Demand(ExactForwardQueryKind.Tree, Fixture.Start));

        reaches.SelectedOrigins.ShouldBe([fixture.A], ignoreOrder: true);
        tree.SelectedOrigins.ShouldBe([fixture.A], ignoreOrder: true);
        fixture.Snapshot.FullMaterializationCount.ShouldBe(0);
    }

    [Test]
    public void Missing_forward_seed_refreshes_every_unknown_origin()
    {
        using var fixture = Fixture.Create(states: [(ProjectSlot.A, SurfaceState.Unknown), (ProjectSlot.B, SurfaceState.Unknown)]);

        var plan = ExactForwardRefinement.Plan(fixture.Snapshot, Demand(ExactForwardQueryKind.Reaches, "Missing.Seed"));

        plan.FromMatched.ShouldBeFalse();
        plan.ToMatched.ShouldBeTrue();
        plan.SelectedOrigins.ShouldBe([fixture.A, fixture.B], ignoreOrder: true);
        plan.UnknownOrigins.ShouldBe([fixture.A, fixture.B], ignoreOrder: true);
    }

    [Test]
    public void Broad_existing_seed_still_refreshes_an_unrelated_generator_capable_unknown()
    {
        using var fixture = Fixture.Create(states: [(ProjectSlot.B, SurfaceState.Unknown)], generatorCapableB: true);

        var plan = ExactForwardRefinement.Plan(fixture.Snapshot, Demand(ExactForwardQueryKind.Tree, "Flow.Start"));

        plan.FromMatched.ShouldBeTrue();
        plan.SelectedOrigins.ShouldBe([fixture.B], ignoreOrder: true);
        plan.UnknownOrigins.ShouldBe([fixture.B], ignoreOrder: true);
        fixture.Snapshot.FullMaterializationCount.ShouldBe(0);
    }

    [Test]
    public void Whole_resident_scope_selects_all_debt_instead_of_only_the_forward_boundary()
    {
        using var fixture = Fixture.Create(states: [(ProjectSlot.A, SurfaceState.Changed), (ProjectSlot.B, SurfaceState.Changed)]);

        var plan = ExactForwardRefinement.Plan(
            fixture.Snapshot,
            Demand(ExactForwardQueryKind.Tree, Fixture.Start, ExactForwardDebtScope.WholeResident)
        );

        plan.SelectedOrigins.ShouldBe([fixture.A, fixture.B], ignoreOrder: true);
    }

    private static ExactForwardDemand Demand(
        ExactForwardQueryKind kind,
        string from,
        ExactForwardDebtScope scope = ExactForwardDebtScope.DemandBoundary
    ) =>
        new(
            kind,
            from,
            kind == ExactForwardQueryKind.Path ? from : null,
            new DemandForwardGraphRules(new ForwardCallProjectionRules(), [], []),
            int.MaxValue,
            FactPathFinder.TraversalMode.SyncCut,
            scope
        );

    private enum ProjectSlot
    {
        A,
        B,
    }

    private sealed class Fixture : IDisposable
    {
        internal const string Start = "M:A.Flow.Start()";
        private const string End = "M:A.Flow.End()";
        private static readonly string Root = Path.Combine(Path.GetTempPath(), "rig-exact-forward-tests");
        private static readonly string APath = Path.Combine(Root, "A", "Flow.cs");
        private static readonly string BPath = Path.Combine(Root, "B", "Other.cs");

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
            IReadOnlyList<(ProjectSlot Project, SurfaceState State)> states,
            bool bReferencesA = false,
            bool generatorCapableB = false
        )
        {
            var workspace = new AdhocWorkspace();
            var a = ProjectId.CreateNewId("A");
            var b = ProjectId.CreateNewId("B");
            var aDocument = DocumentId.CreateNewId(a, "Flow.cs");
            var bDocument = DocumentId.CreateNewId(b, "Other.cs");
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
                            filePath: Path.Combine(Root, "A", "A.csproj"),
                            documents: [Document(aDocument, APath)]
                        ),
                        ProjectInfo.Create(
                            b,
                            VersionStamp.Create(),
                            "B",
                            "B",
                            LanguageNames.CSharp,
                            filePath: Path.Combine(Root, "B", "B.csproj"),
                            projectReferences: bReferencesA ? [new ProjectReference(a)] : [],
                            analyzerReferences: generatorCapableB
                                ?
                                [
                                    new Microsoft.CodeAnalysis.Diagnostics.AnalyzerFileReference(
                                        typeof(ExactForwardRefinementTests).Assembly.Location,
                                        TestAnalyzerLoader.Instance
                                    ),
                                ]
                                : [],
                            documents: [Document(bDocument, BPath)]
                        ),
                    ]
                )
            );

            var contributionBuilder = ImmutableDictionary.CreateBuilder<ProjectId, ImmutableHashSet<DocumentId>>();
            var stateBuilder = ImmutableDictionary.CreateBuilder<ProjectId, SurfaceState>();
            foreach (var (slot, state) in states)
            {
                var project = slot == ProjectSlot.A ? a : b;
                var document = slot == ProjectSlot.A ? aDocument : bDocument;
                contributionBuilder[project] = [document];
                stateBuilder[project] = state;
            }

            var solution = workspace.CurrentSolution;
            var facts = new AnalysisResult(
                "ExactForward.sln",
                [],
                [],
                Symbols:
                [
                    Method(Start, "T:A.Flow", APath, "A"),
                    Method(End, "T:A.Flow", APath, "A"),
                    Method("M:B.Other.Run()", "T:B.Other", BPath, "B"),
                ],
                References: [new ReferenceFact(End, RefKinds.Invocation, Start, "A", true, APath, 1)]
            );
            var snapshot = new FactSnapshot(
                new FactRevision(1),
                solution,
                facts,
                ImmutableDictionary.Create<string, FileFacts>(StringComparer.OrdinalIgnoreCase),
                DirtySet.FromContributions(solution, contributionBuilder.ToImmutable()),
                SnapshotDelta.Empty with
                {
                    SurfaceStates = stateBuilder.ToImmutable(),
                }
            );
            return new Fixture(workspace, snapshot, a, b);
        }

        public void Dispose() => Workspace.Dispose();

        private static SymbolFact Method(string id, string containingType, string path, string assembly) =>
            new(id, SymbolKinds.Method, id, "", containingType, "public", "", id, path, 1, 1, assembly, false);

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
}
