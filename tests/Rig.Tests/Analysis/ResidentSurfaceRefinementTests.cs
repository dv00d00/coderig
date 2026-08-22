using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Rig.Analysis;
using Rig.Analysis.Inventory;
using Rig.Domain;
using Rig.Domain.Data;
using Shouldly;
using RuleSet = Rig.Domain.Data.RuleSet;

namespace Rig.Tests.Analysis;

[NotInParallel]
public sealed class ResidentSurfaceRefinementTests
{
    [Test]
    public async Task Body_only_edit_stays_unknown_until_refined_then_clears_only_its_origin_and_matches_current_oracle()
    {
        using var fixture = MockFixture.Create();

        await fixture.Index.ApplyEditAsync(fixture.AMainPath, SourceText.From("A-BODY-1"));
        var published = fixture.Index.CaptureSnapshot();
        published.Delta.SurfaceStates[fixture.A].ShouldBe(SurfaceState.Unknown);
        published.Dirty.PendingByOrigin.Keys.ShouldBe([fixture.A], ignoreOrder: true);
        published.FullMaterializationCount.ShouldBe(0);
        fixture.RefreshCalls.ShouldBe(0);

        (await fixture.Index.RefineUnknownSurfacesAsync()).ShouldBeTrue();
        var refined = fixture.Index.CaptureSnapshot();
        refined.Delta.SurfaceStates[fixture.A].ShouldBe(SurfaceState.BodyOnly);
        refined.Dirty.PendingByOrigin.ShouldBeEmpty();
        refined.Dirty.PendingDocuments.ShouldBeEmpty();
        refined.FullMaterializationCount.ShouldBe(0);
        await fixture.AssertSourceOracleAsync();
    }

    [Test]
    public async Task Member_type_surface_change_stays_changed_until_coarse_reconcile_and_matches_current_oracle()
    {
        using var fixture = MockFixture.Create();

        await fixture.Index.ApplyEditAsync(fixture.AMainPath, SourceText.From("A-SURFACE-1"));
        (await fixture.Index.RefineUnknownSurfacesAsync()).ShouldBeTrue();
        var changed = fixture.Index.CaptureSnapshot();
        changed.Delta.SurfaceStates[fixture.A].ShouldBe(SurfaceState.Changed);
        changed.Dirty.PendingByOrigin[fixture.A].ShouldContain(fixture.APartDocument);
        changed.Dirty.PendingByOrigin[fixture.A].ShouldContain(fixture.BDocument);

        var callsBefore = fixture.ExtractCalls;
        (await fixture.Index.ReconcileAsync()).ShouldBeTrue();
        fixture.ExtractCalls.ShouldBe(callsBefore + 1);
        fixture.LastExtractedDocuments.ShouldBe(changed.Dirty.PendingDocuments, ignoreOrder: true);
        fixture.Index.CaptureSnapshot().Dirty.PendingDocuments.ShouldBeEmpty();
        await fixture.AssertSourceOracleAsync();
    }

    [Test]
    public async Task Mixed_batch_and_overlapping_debt_settle_one_origin_without_clearing_another()
    {
        using var fixture = MockFixture.Create();

        await fixture.Index.ApplyEditsAsync(
            new Dictionary<string, SourceText>
            {
                [fixture.AMainPath] = SourceText.From("A-BODY-1"),
                [fixture.CMainPath] = SourceText.From("C-SURFACE-1"),
            }
        );
        var initial = fixture.Index.CaptureSnapshot();
        initial.Dirty.PendingByOrigin.Keys.ShouldBe([fixture.A, fixture.C], ignoreOrder: true);
        initial.Dirty.PendingByOrigin[fixture.A].ShouldContain(fixture.BDocument);

        (await fixture.Index.RefineUnknownSurfacesAsync(ImmutableHashSet.Create(fixture.A))).ShouldBeTrue();
        var afterBody = fixture.Index.CaptureSnapshot();
        afterBody.Delta.SurfaceStates[fixture.A].ShouldBe(SurfaceState.BodyOnly);
        afterBody.Delta.SurfaceStates[fixture.C].ShouldBe(SurfaceState.Unknown);
        afterBody.Dirty.PendingByOrigin.Keys.ShouldBe([fixture.C], ignoreOrder: true);
        afterBody.Dirty.PendingByOrigin[fixture.C].ShouldContain(fixture.BDocument);

        (await fixture.Index.RefineUnknownSurfacesAsync(ImmutableHashSet.Create(fixture.C))).ShouldBeTrue();
        var afterChanged = fixture.Index.CaptureSnapshot();
        afterChanged.Delta.SurfaceStates[fixture.C].ShouldBe(SurfaceState.Changed);
        afterChanged.Dirty.PendingByOrigin.Keys.ShouldBe([fixture.C], ignoreOrder: true);
    }

    [Test]
    public async Task Repeated_origin_uses_last_accepted_fingerprint_and_partial_second_file_participates()
    {
        using var fixture = MockFixture.Create();

        await fixture.Index.ApplyEditAsync(fixture.AMainPath, SourceText.From("A-SURFACE-1"));
        (await fixture.Index.RefineUnknownSurfacesAsync()).ShouldBeTrue();
        fixture.Index.CaptureSnapshot().Delta.SurfaceStates[fixture.A].ShouldBe(SurfaceState.Changed);

        await fixture.Index.ApplyEditAsync(fixture.AMainPath, SourceText.From("A-SURFACE-1-BODY"));
        (await fixture.Index.RefineUnknownSurfacesAsync()).ShouldBeTrue();
        fixture.Index.CaptureSnapshot().Delta.SurfaceStates[fixture.A].ShouldBe(SurfaceState.BodyOnly);
        fixture.Index.CaptureSnapshot().Dirty.PendingByOrigin.Keys.ShouldBe([fixture.A]);

        await fixture.Index.ReconcileAsync();

        await fixture.Index.ApplyEditAsync(fixture.AMainPath, SourceText.From("A-SURFACE-1-BODY-2"));
        (await fixture.Index.RefineUnknownSurfacesAsync()).ShouldBeTrue();
        fixture.Index.CaptureSnapshot().Delta.SurfaceStates[fixture.A].ShouldBe(SurfaceState.BodyOnly);
        fixture.Index.CaptureSnapshot().Dirty.PendingByOrigin.ShouldBeEmpty();

        await fixture.Index.ApplyEditAsync(fixture.APartPath, SourceText.From("A-PART-SURFACE-1"));
        (await fixture.Index.RefineUnknownSurfacesAsync()).ShouldBeTrue();
        var partial = fixture.Index.CaptureSnapshot();
        partial.Delta.SurfaceStates[fixture.A].ShouldBe(SurfaceState.Changed);
        partial.Dirty.PendingByOrigin[fixture.A].ShouldContain(fixture.BDocument);
    }

    [Test]
    public async Task Cancelled_and_superseded_uncooperative_refinements_publish_nothing()
    {
        await using (var cancellation = BlockingFixture.Create())
        {
            await cancellation.Index.ApplyEditAsync(cancellation.Path, SourceText.From("BODY-1"));
            var basis = cancellation.Index.CaptureSnapshot();
            using var source = new CancellationTokenSource();
            var refine = cancellation.Index.RefineUnknownSurfacesAsync(cancellationToken: source.Token);
            await cancellation.Started.Task;
            source.Cancel();
            cancellation.Release.SetResult();
            await Should.ThrowAsync<OperationCanceledException>(async () => await refine);
            ReferenceEquals(cancellation.Index.CaptureSnapshot(), basis).ShouldBeTrue();
        }

        await using (var stale = BlockingFixture.Create())
        {
            await stale.Index.ApplyEditAsync(stale.Path, SourceText.From("BODY-1"));
            var refineBasis = stale.Index.CaptureSnapshot();
            var refine = stale.Index.RefineUnknownSurfacesAsync();
            await stale.Started.Task;
            await stale.Index.ApplyEditAsync(stale.Path, SourceText.From("BODY-2"));
            var newer = stale.Index.CaptureSnapshot();
            stale.Release.SetResult();
            (await refine).ShouldBeFalse();
            ReferenceEquals(stale.Index.CaptureSnapshot(), newer).ShouldBeTrue();
            ReferenceEquals(newer, refineBasis).ShouldBeFalse();
        }
    }

    [Test]
    public async Task Explicit_reconcile_uses_real_emitter_and_fails_closed_for_refresh_errors_and_generated_collisions()
    {
        await using var fixture = await RealSurfaceFixture.CreateAsync();

        await fixture.Index.ApplyEditAsync(fixture.EditPath, SourceText.From("public class A { public int M() => 2; }"));
        var published = fixture.Index.CaptureSnapshot();
        published.Delta.SurfaceStates[fixture.ProjectId].ShouldBe(SurfaceState.Unknown);
        fixture.RefreshCalls.ShouldBe(0);
        fixture.ExtractCalls.ShouldBe(1);
        published.FullMaterializationCount.ShouldBe(0);

        (await fixture.Index.ReconcileAsync()).ShouldBeTrue();
        var reconciled = fixture.Index.CaptureSnapshot();
        reconciled.Delta.SurfaceStates[fixture.ProjectId].ShouldBe(SurfaceState.BodyOnly);
        reconciled.Dirty.PendingDocuments.ShouldBeEmpty();
        fixture.RefreshCalls.ShouldBe(1);
        fixture.ExtractCalls.ShouldBe(1, "a BodyOnly result must avoid the coarse extractor");
        reconciled.FullMaterializationCount.ShouldBe(0);

        using var failedRefresh = MockFixture.Create();
        failedRefresh.FailRefreshFor = failedRefresh.A;
        await failedRefresh.Index.ApplyEditAsync(failedRefresh.AMainPath, SourceText.From("A-BODY-1"));
        (await failedRefresh.Index.ReconcileAsync()).ShouldBeFalse();
        failedRefresh.Index.CaptureSnapshot().Delta.SurfaceStates[failedRefresh.A].ShouldBe(SurfaceState.Unknown);
        failedRefresh.Index.CaptureSnapshot().Dirty.PendingByOrigin.Keys.ShouldBe([failedRefresh.A]);
        failedRefresh.ExtractCalls.ShouldBe(1, "a failed generator refresh must not run a source-only coarse clear");

        using var generatedCollision = MockFixture.Create(generatedCollision: true);
        generatedCollision.Index.CaptureSnapshot().Surfaces.Projects[generatedCollision.A].IsClassifiable.ShouldBeFalse();
        generatedCollision.Index.CaptureSnapshot().Surfaces.Projects[generatedCollision.C].IsClassifiable.ShouldBeFalse();
        await generatedCollision.Index.ApplyEditAsync(generatedCollision.AMainPath, SourceText.From("A-BODY-1"));
        (await generatedCollision.Index.ReconcileAsync()).ShouldBeFalse();
        generatedCollision.RefreshCalls.ShouldBe(0);
        generatedCollision.Index.CaptureSnapshot().Dirty.PendingByOrigin.Keys.ShouldBe([generatedCollision.A]);

        using var linkedDebt = MockFixture.Create(linkedDebt: true);
        await linkedDebt.Index.ApplyEditAsync(linkedDebt.AMainPath, SourceText.From("A-SURFACE-1"));
        (await linkedDebt.Index.RefineUnknownSurfacesAsync()).ShouldBeTrue();
        (await linkedDebt.Index.ReconcileAsync()).ShouldBeTrue();
        linkedDebt.LastExtractedDocuments.ShouldContain(
            linkedDebt.LinkedPartDocument!,
            "path-global replacement must re-extract every linked project context"
        );
    }

    private sealed class MockFixture : IDisposable
    {
        private readonly Dictionary<ProjectId, ProjectSurfaceShard> _meta;

        private MockFixture(
            RigWorkspace workspace,
            ResidentIndex index,
            ProjectId a,
            ProjectId b,
            ProjectId c,
            DocumentId aPartDocument,
            DocumentId bDocument,
            DocumentId? linkedPartDocument,
            string aMainPath,
            string aPartPath,
            string cMainPath,
            Dictionary<ProjectId, ProjectSurfaceShard> meta
        )
        {
            Workspace = workspace;
            Index = index;
            A = a;
            B = b;
            C = c;
            APartDocument = aPartDocument;
            BDocument = bDocument;
            LinkedPartDocument = linkedPartDocument;
            AMainPath = aMainPath;
            APartPath = aPartPath;
            CMainPath = cMainPath;
            _meta = meta;
        }

        internal RigWorkspace Workspace { get; }
        internal ResidentIndex Index { get; }
        internal ProjectId A { get; }
        internal ProjectId B { get; }
        internal ProjectId C { get; }
        internal DocumentId APartDocument { get; }
        internal DocumentId BDocument { get; }
        internal DocumentId? LinkedPartDocument { get; }
        internal string AMainPath { get; }
        internal string APartPath { get; }
        internal string CMainPath { get; }
        internal int ExtractCalls { get; private set; }
        internal int RefreshCalls { get; private set; }
        internal ProjectId? FailRefreshFor { get; set; }
        internal IReadOnlyCollection<DocumentId> LastExtractedDocuments { get; private set; } = [];

        internal static MockFixture Create(bool generatedCollision = false, bool linkedDebt = false)
        {
            var root = Path.Combine(Path.GetTempPath(), $"rig-surface-refine-{Guid.NewGuid():N}");
            var workspace = new RigWorkspace();
            var a = ProjectId.CreateNewId("A");
            var b = ProjectId.CreateNewId("B");
            var c = ProjectId.CreateNewId("C");
            var aMain = DocumentId.CreateNewId(a, "A.cs");
            var aPart = DocumentId.CreateNewId(a, "A.Part.cs");
            var bMain = DocumentId.CreateNewId(b, "B.cs");
            var cMain = DocumentId.CreateNewId(c, "C.cs");
            var linkedPart = DocumentId.CreateNewId(c, "LinkedA.Part.cs");
            var aMainPath = Path.Combine(root, "A.cs");
            var aPartPath = Path.Combine(root, "A.Part.cs");
            var bPath = Path.Combine(root, "B.cs");
            var cPath = Path.Combine(root, "C.cs");
            var aProjectPath = Path.Combine(root, "A.csproj");
            var bProjectPath = Path.Combine(root, "B.csproj");
            var cProjectPath = Path.Combine(root, "C.csproj");
            var sharedGeneratedPath = Path.Combine(root, "Shared.Generated.g.cs");
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
                            documents: [Document(aMain, aMainPath, "A-BASE"), Document(aPart, aPartPath, "A-PART-BASE")]
                        ),
                        ProjectInfo.Create(
                            b,
                            VersionStamp.Create(),
                            "B",
                            "B",
                            LanguageNames.CSharp,
                            filePath: bProjectPath,
                            documents: [Document(bMain, bPath, "B-BASE")],
                            projectReferences: [new ProjectReference(a), new ProjectReference(c)]
                        ),
                        ProjectInfo.Create(
                            c,
                            VersionStamp.Create(),
                            "C",
                            "C",
                            LanguageNames.CSharp,
                            filePath: cProjectPath,
                            documents: linkedDebt
                                ? [Document(cMain, cPath, "C-BASE"), Document(linkedPart, aPartPath, "A-PART-BASE")]
                                : [Document(cMain, cPath, "C-BASE")]
                        ),
                    ]
                )
            );

            var meta = new Dictionary<ProjectId, ProjectSurfaceShard>
            {
                [a] = Shard("", "meta-a"),
                [b] = Shard("", "meta-b"),
                [c] = Shard("", "meta-c"),
            };
            var aShards = new List<ProjectSurfaceShard> { Shard(aMainPath, "a-main"), Shard(aPartPath, "a-part"), meta[a] };
            var cShards = new List<ProjectSurfaceShard> { Shard(cPath, "c-main"), meta[c] };
            if (generatedCollision)
            {
                aShards.Add(new ProjectSurfaceShard(sharedGeneratedPath, true, ProjectContentHash.Compute(["gen-a"])));
                cShards.Add(new ProjectSurfaceShard(sharedGeneratedPath, true, ProjectContentHash.Compute(["gen-c"])));
            }
            var surfaces = new[]
            {
                Surface("A", aProjectPath, "A", aShards),
                Surface("B", bProjectPath, "B", [Shard(bPath, "b-main"), meta[b]]),
                Surface("C", cProjectPath, "C", cShards),
            };
            var sourceFiles = new List<SourceFileInfo>
            {
                Source("A", aMainPath, "A-BASE"),
                Source("A", aPartPath, "A-PART-BASE"),
                Source("B", bPath, "B-BASE"),
                Source("C", cPath, "C-BASE"),
            };
            if (linkedDebt)
            {
                sourceFiles.Add(Source("C", aPartPath, "A-PART-BASE"));
            }
            var baseFacts = new AnalysisResult(Path.Combine(root, "Surface.sln"), sourceFiles, [], ProjectSurfaces: surfaces);

            MockFixture? fixture = null;
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
                var result = new Dictionary<string, FileFacts>(StringComparer.OrdinalIgnoreCase);
                foreach (var documentId in documents)
                {
                    var document = solution.GetDocument(documentId)!;
                    var text = (await document.GetTextAsync(cancellationToken)).ToString();
                    var path = document.FilePath!;
                    var surfaceKey = SurfaceKey(path, text, aMainPath, aPartPath, cPath);
                    var contribution = new ProjectSurfaceContribution(
                        document.Project.Name,
                        document.Project.FilePath ?? "",
                        document.Project.AssemblyName ?? document.Project.Name,
                        Shard(path, surfaceKey),
                        true
                    );
                    result[path] = new FileFacts([Source(document.Project.Name, path, text)], [], [], [], [], [], [], [], [contribution]);
                }
                return result;
            }

            Task<ProjectSurfaceRefresh> Refresh(
                Solution _,
                ProjectId projectId,
                RuleSet __,
                CancellationToken ___,
                Rig.Analysis.Extraction.StringInterner? ____
            )
            {
                fixture!.RefreshCalls++;
                if (fixture.FailRefreshFor == projectId)
                {
                    return Task.FromResult(new ProjectSurfaceRefresh([], new ProjectSurfaceShard("", false, ""), false));
                }
                return Task.FromResult(new ProjectSurfaceRefresh([], meta[projectId], true));
            }

            var index = new ResidentIndex(
                workspace,
                baseFacts,
                baseFacts.SolutionPath,
                new RuleSet(),
                extractFiles: Extract,
                refreshSurface: Refresh
            );
            fixture = new MockFixture(
                workspace,
                index,
                a,
                b,
                c,
                aPart,
                bMain,
                linkedDebt ? linkedPart : null,
                aMainPath,
                aPartPath,
                cPath,
                meta
            );
            return fixture;
        }

        internal async Task AssertSourceOracleAsync()
        {
            var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var document in Index.CurrentSolution.Projects.SelectMany(p => p.Documents))
            {
                expected[document.FilePath!] = (await document.GetTextAsync()).ToString();
            }
            var actual = Index
                .CaptureSnapshot()
                .EnumerateSourceFiles()
                .ToDictionary(s => s.FilePath, s => s.Evidence, StringComparer.OrdinalIgnoreCase);
            actual.Count.ShouldBe(expected.Count);
            foreach (var (path, evidence) in expected)
            {
                actual.ShouldContainKey(path);
                actual[path].ShouldBe(evidence);
            }
        }

        public void Dispose() => Index.Dispose();

        private static string SurfaceKey(string path, string text, string aMain, string aPart, string c) =>
            path == aMain
                ? text.StartsWith("A-SURFACE-1", StringComparison.Ordinal)
                    ? "a-main-v1"
                    : "a-main"
                : path == aPart
                    ? text.Contains("SURFACE", StringComparison.Ordinal)
                        ? "a-part-v1"
                        : "a-part"
                    : path == c
                        ? text.Contains("SURFACE", StringComparison.Ordinal)
                            ? "c-main-v1"
                            : "c-main"
                        : "b-main";
    }

    private sealed class BlockingFixture : IAsyncDisposable
    {
        private BlockingFixture(ResidentIndex index, string path)
        {
            Index = index;
            Path = path;
        }

        internal ResidentIndex Index { get; }
        internal string Path { get; }
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal static BlockingFixture Create()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rig-surface-block-{Guid.NewGuid():N}");
            var path = System.IO.Path.Combine(root, "A.cs");
            var projectPath = System.IO.Path.Combine(root, "A.csproj");
            var workspace = new RigWorkspace();
            var project = ProjectId.CreateNewId("A");
            var document = DocumentId.CreateNewId(project, "A.cs");
            workspace.AddSolution(
                SolutionInfo.Create(
                    SolutionId.CreateNewId(),
                    VersionStamp.Create(),
                    projects:
                    [
                        ProjectInfo.Create(
                            project,
                            VersionStamp.Create(),
                            "A",
                            "A",
                            LanguageNames.CSharp,
                            filePath: projectPath,
                            documents: [Document(document, path, "BASE")]
                        ),
                    ]
                )
            );
            var meta = Shard("", "meta");
            var surface = Surface("A", projectPath, "A", [Shard(path, "source"), meta]);
            var facts = new AnalysisResult("Surface.sln", [Source("A", path, "BASE")], [], ProjectSurfaces: [surface]);
            BlockingFixture? fixture = null;
            Task<Dictionary<string, FileFacts>> Extract(
                Solution solution,
                IReadOnlyCollection<DocumentId> documents,
                string _,
                RuleSet __,
                CancellationToken ___,
                Rig.Analysis.Extraction.StringInterner? ____
            )
            {
                var text = solution.GetDocument(documents.Single())!.GetTextAsync().Result.ToString();
                return Task.FromResult(
                    new Dictionary<string, FileFacts>
                    {
                        [path] = new(
                            [Source("A", path, text)],
                            [],
                            [],
                            [],
                            [],
                            [],
                            [],
                            [],
                            [new ProjectSurfaceContribution("A", projectPath, "A", Shard(path, "source"), true)]
                        ),
                    }
                );
            }
            async Task<ProjectSurfaceRefresh> Refresh(
                Solution _,
                ProjectId __,
                RuleSet ___,
                CancellationToken ____,
                Rig.Analysis.Extraction.StringInterner? _____
            )
            {
                fixture!.Started.TrySetResult();
                await fixture.Release.Task;
                return new ProjectSurfaceRefresh([], meta, true);
            }
            var index = new ResidentIndex(
                workspace,
                facts,
                facts.SolutionPath,
                new RuleSet(),
                extractFiles: Extract,
                refreshSurface: Refresh
            );
            fixture = new BlockingFixture(index, path);
            return fixture;
        }

        public ValueTask DisposeAsync()
        {
            Index.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RealSurfaceFixture : IAsyncDisposable
    {
        private RealSurfaceFixture(ResidentIndex index, ProjectId projectId, string editPath)
        {
            Index = index;
            ProjectId = projectId;
            EditPath = editPath;
        }

        internal ResidentIndex Index { get; }
        internal ProjectId ProjectId { get; }
        internal string EditPath { get; }
        internal int ExtractCalls { get; private set; }
        internal int RefreshCalls { get; private set; }

        internal static async Task<RealSurfaceFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"rig-real-surface-{Guid.NewGuid():N}");
            var editPath = Path.Combine(root, "A.cs");
            var debtPath = Path.Combine(root, "Debt.cs");
            var projectPath = Path.Combine(root, "Real.csproj");
            var workspace = new RigWorkspace();
            var projectId = ProjectId.CreateNewId("Real");
            var edit = DocumentId.CreateNewId(projectId, "A.cs");
            var debt = DocumentId.CreateNewId(projectId, "Debt.cs");
            workspace.AddSolution(
                SolutionInfo.Create(
                    SolutionId.CreateNewId(),
                    VersionStamp.Create(),
                    projects:
                    [
                        ProjectInfo.Create(
                            projectId,
                            VersionStamp.Create(),
                            "Real",
                            "Real",
                            LanguageNames.CSharp,
                            filePath: projectPath,
                            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
                            metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                            documents:
                            [
                                Document(edit, editPath, "public class A { public int M() => 1; }"),
                                Document(debt, debtPath, "public class Debt { public int N() => 1; }"),
                            ]
                        ),
                    ]
                )
            );
            var solution = workspace.CurrentSolution;
            var rules = new RuleSet();
            var baseSlices = await SolutionAnalyzer.ExtractFromDocumentsByFileAsync(
                solution,
                [edit, debt],
                Path.Combine(root, "Real.sln"),
                rules
            );
            var empty = new AnalysisResult(Path.Combine(root, "Real.sln"), [], []);
            var merged = ResidentIndex.MergeFacts(empty, baseSlices.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase));
            var compilation = (await solution.GetProject(projectId)!.GetCompilationAsync())!;
            var sourceShards = baseSlices.Values.SelectMany(s => s.ProjectSurfaces).Select(c => c.Shard).ToArray();
            var meta = ProjectSurfaceBuilder.BuildMeta((CSharpParseOptions?)solution.GetProject(projectId)!.ParseOptions, compilation);
            var shards = sourceShards.Append(meta).ToArray();
            var surface = new ProjectSurfaceSnapshot("Real", projectPath, "Real", shards, ProjectSurfaceBuilder.Aggregate(shards));
            var baseFacts = merged with { ProjectSurfaces = [surface] };

            RealSurfaceFixture? fixture = null;
            async Task<Dictionary<string, FileFacts>> Extract(
                Solution current,
                IReadOnlyCollection<DocumentId> documents,
                string solutionPath,
                RuleSet currentRules,
                CancellationToken cancellationToken,
                Rig.Analysis.Extraction.StringInterner? interner
            )
            {
                fixture!.ExtractCalls++;
                return await SolutionAnalyzer.ExtractFromDocumentsByFileAsync(
                    current,
                    documents,
                    solutionPath,
                    currentRules,
                    cancellationToken,
                    interner
                );
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
                rules,
                extractFiles: Extract,
                refreshSurface: Refresh
            );
            fixture = new RealSurfaceFixture(index, projectId, editPath);
            return fixture;
        }

        public ValueTask DisposeAsync()
        {
            Index.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private static ProjectSurfaceShard Shard(string path, string value) => new(path, false, ProjectContentHash.Compute([value]));

    private static ProjectSurfaceSnapshot Surface(
        string name,
        string projectPath,
        string assembly,
        IReadOnlyList<ProjectSurfaceShard> shards
    ) => new(name, projectPath, assembly, shards, ProjectSurfaceBuilder.Aggregate(shards));

    private static DocumentInfo Document(DocumentId id, string path, string text) =>
        DocumentInfo.Create(
            id,
            Path.GetFileName(path),
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(text), VersionStamp.Create(), path)),
            filePath: path
        );

    private static SourceFileInfo Source(string project, string path, string evidence) =>
        new(project, path, "indexed", "high", "test", "test", evidence);
}
