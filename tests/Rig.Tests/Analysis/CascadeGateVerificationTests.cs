using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Rig.Analysis;
using Rig.Analysis.Inventory;
using Rig.Cli;
using Rig.Cli.Commands;
using Rig.Domain;
using Rig.Domain.Data;
using Shouldly;
using RuleSet = Rig.Domain.Data.RuleSet;

namespace Rig.Tests.Analysis;

[NotInParallel]
public sealed class CascadeGateVerificationTests
{
    [Test]
    public async Task Matching_body_only_verification_is_batched_discarded_and_clears_only_its_origin_atomically()
    {
        using var fixture = VerificationFixture.Create();

        await fixture.Index.ApplyEditsAsync(
            new Dictionary<string, SourceText>
            {
                [fixture.APath] = SourceText.From("A-BODY-1"),
                [fixture.CMainPath] = SourceText.From("C-BODY-1"),
            }
        );
        var basis = fixture.Index.CaptureSnapshot();
        basis.Dirty.PendingByOrigin.Keys.ShouldBe([fixture.A, fixture.C], ignoreOrder: true);
        fixture.ExtractCalls.ShouldBe(1);

        (await fixture.Index.RefineUnknownSurfacesAsync(ImmutableHashSet.Create(fixture.A))).ShouldBeTrue();

        var verified = fixture.Index.CaptureSnapshot();
        verified.Revision.Value.ShouldBe(basis.Revision.Value + 1);
        verified.Delta.SurfaceStates[fixture.A].ShouldBe(SurfaceState.BodyOnly);
        verified.Delta.SurfaceStates[fixture.C].ShouldBe(SurfaceState.Unknown);
        verified.Dirty.PendingByOrigin.Keys.ShouldBe([fixture.C]);
        verified.Surfaces.Projects[fixture.A].GateDisabled.ShouldBeFalse();
        fixture.Index.CascadeGateDisabledProjects.ShouldBeEmpty();
        fixture.ExtractCalls.ShouldBe(2, "the skipped cascade must be verified in one additional batch");
        fixture.VerifierCalls.ShouldBe(1);
        fixture.LastVerifierDocuments.ShouldBe([fixture.BDocument], ignoreOrder: true);
        verified.Overlay.ShouldNotContainKey(fixture.BPath, "matching verifier slices are private evidence, not publications");
        verified.EnumerateReferences().ShouldContain(reference => reference.TargetSymbolId == VerificationFixture.OldTarget);
        verified.FullMaterializationCount.ShouldBe(0);
        await fixture.AssertCurrentSourceOracleAsync();
    }

    [Test]
    public async Task Reference_mismatch_publishes_coarse_fallback_disables_origin_and_forces_later_coarse_reconciliation()
    {
        using var fixture = VerificationFixture.Create(mismatch: true);

        await fixture.Index.ApplyEditAsync(fixture.APath, SourceText.From("A-BODY-1"));
        var basis = fixture.Index.CaptureSnapshot();
        (await fixture.Index.RefineUnknownSurfacesAsync()).ShouldBeTrue();

        var fallback = fixture.Index.CaptureSnapshot();
        fallback.Revision.Value.ShouldBe(basis.Revision.Value + 1, "classification and fallback facts publish in one CAS");
        fallback.Delta.SurfaceStates[fixture.A].ShouldBe(SurfaceState.Changed);
        fallback.Dirty.PendingByOrigin.ShouldBeEmpty();
        fallback.Surfaces.Projects[fixture.A].GateDisabled.ShouldBeTrue();
        fallback.Surfaces.GateDisabledProjectNames.ShouldBe(["A"]);
        fixture.Index.CascadeGateDisabledProjects.ShouldBe(["A"]);
        WatchHost
            .CascadeGateStatusSegment(fallback)
            .ShouldBe("cascade gate disabled for 1 project(s) after verification mismatch — coarse fallback active");
        fallback.Overlay.ShouldContainKey(fixture.BPath);
        fallback.EnumerateReferences().ShouldContain(reference => reference.TargetSymbolId == VerificationFixture.NewTarget);
        fallback.EnumerateReferences().ShouldNotContain(reference => reference.TargetSymbolId == VerificationFixture.OldTarget);
        fallback.FullMaterializationCount.ShouldBe(0);

        await fixture.Index.ApplyEditAsync(fixture.APath, SourceText.From("A-BODY-2"));
        var secondEdit = fixture.Index.CaptureSnapshot();
        (await fixture.Index.RefineUnknownSurfacesAsync()).ShouldBeTrue();
        var forced = fixture.Index.CaptureSnapshot();
        forced.Delta.SurfaceStates[fixture.A].ShouldBe(SurfaceState.Changed);
        forced.Dirty.PendingByOrigin.Keys.ShouldBe([fixture.A]);
        forced.Surfaces.Projects[fixture.A].RequiresCoarseReconciliation.ShouldBeTrue();
        fixture.VerifierCalls.ShouldBe(1, "a disabled origin must never trust the body-only gate again");

        var extractsBeforeCoarse = fixture.ExtractCalls;
        (await fixture.Index.ReconcileAsync()).ShouldBeTrue();
        fixture.ExtractCalls.ShouldBe(extractsBeforeCoarse + 1);
        fixture.Index.CaptureSnapshot().Dirty.PendingByOrigin.ShouldBeEmpty();
        fixture.Index.CascadeGateDisabledProjects.ShouldBe(["A"]);
        fixture.Index.CaptureSnapshot().Revision.Value.ShouldBe(forced.Revision.Value + 1);
        secondEdit.Revision.Value.ShouldBeLessThan(forced.Revision.Value);

        var output = new StringWriter();
        var error = new StringWriter();
        (await CliApplication.RunAsync(["watch", "--help"], output, error)).ShouldBe(0, error.ToString());
        var help = string.Join(' ', output.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        help.ShouldContain("--verify-cascade-gate");
        help.ShouldContain("A mismatch publishes the fresh coarse facts and permanently disables the gate for that project");
    }

    [Test]
    public async Task Cancelled_and_superseded_uncooperative_verifiers_publish_no_candidate()
    {
        using (var cancelled = VerificationFixture.Create(blockVerifier: true))
        {
            await cancelled.Index.ApplyEditAsync(cancelled.APath, SourceText.From("A-BODY-1"));
            var basis = cancelled.Index.CaptureSnapshot();
            using var cancellation = new CancellationTokenSource();
            var refinement = cancelled.Index.RefineUnknownSurfacesAsync(cancellationToken: cancellation.Token);
            await cancelled.VerifierStarted.Task;
            cancellation.Cancel();
            cancelled.ReleaseVerifier.SetResult();
            await Should.ThrowAsync<OperationCanceledException>(async () => await refinement);
            ReferenceEquals(cancelled.Index.CaptureSnapshot(), basis).ShouldBeTrue();
            cancelled.Index.CascadeGateDisabledProjects.ShouldBeEmpty();
        }

        using (var superseded = VerificationFixture.Create(blockVerifier: true))
        {
            await superseded.Index.ApplyEditAsync(superseded.APath, SourceText.From("A-BODY-1"));
            var basis = superseded.Index.CaptureSnapshot();
            var refinement = superseded.Index.RefineUnknownSurfacesAsync();
            await superseded.VerifierStarted.Task;
            await superseded.Index.ApplyEditAsync(superseded.APath, SourceText.From("A-BODY-2"));
            var newer = superseded.Index.CaptureSnapshot();
            superseded.ReleaseVerifier.SetResult();
            (await refinement).ShouldBeFalse();
            ReferenceEquals(superseded.Index.CaptureSnapshot(), newer).ShouldBeTrue();
            ReferenceEquals(newer, basis).ShouldBeFalse();
            superseded.Index.CascadeGateDisabledProjects.ShouldBeEmpty();
        }
    }

    private sealed class VerificationFixture : IDisposable
    {
        internal const string OldTarget = "M:B.OldTarget()";
        internal const string NewTarget = "M:B.NewTarget()";

        private readonly bool _mismatch;
        private readonly bool _blockVerifier;
        private readonly Dictionary<ProjectId, ProjectSurfaceShard> _meta;

        private VerificationFixture(
            ResidentIndex index,
            ProjectId a,
            ProjectId c,
            DocumentId bDocument,
            string aPath,
            string bPath,
            string cMainPath,
            bool mismatch,
            bool blockVerifier,
            Dictionary<ProjectId, ProjectSurfaceShard> meta
        )
        {
            Index = index;
            A = a;
            C = c;
            BDocument = bDocument;
            APath = aPath;
            BPath = bPath;
            CMainPath = cMainPath;
            _mismatch = mismatch;
            _blockVerifier = blockVerifier;
            _meta = meta;
        }

        internal ResidentIndex Index { get; }
        internal ProjectId A { get; }
        internal ProjectId C { get; }
        internal DocumentId BDocument { get; }
        internal string APath { get; }
        internal string BPath { get; }
        internal string CMainPath { get; }
        internal int ExtractCalls { get; private set; }
        internal int VerifierCalls { get; private set; }
        internal IReadOnlyCollection<DocumentId> LastVerifierDocuments { get; private set; } = [];
        internal TaskCompletionSource VerifierStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseVerifier { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal static VerificationFixture Create(bool mismatch = false, bool blockVerifier = false)
        {
            var root = Path.Combine(Path.GetTempPath(), $"rig-cascade-verify-{Guid.NewGuid():N}");
            var aPath = Path.Combine(root, "A.cs");
            var bPath = Path.Combine(root, "B.cs");
            var cMainPath = Path.Combine(root, "C.cs");
            var cDebtPath = Path.Combine(root, "C.Debt.cs");
            var aProjectPath = Path.Combine(root, "A.csproj");
            var bProjectPath = Path.Combine(root, "B.csproj");
            var cProjectPath = Path.Combine(root, "C.csproj");
            var workspace = new RigWorkspace();
            var a = ProjectId.CreateNewId("A");
            var b = ProjectId.CreateNewId("B");
            var c = ProjectId.CreateNewId("C");
            var aDocument = DocumentId.CreateNewId(a, "A.cs");
            var bDocument = DocumentId.CreateNewId(b, "B.cs");
            var cMainDocument = DocumentId.CreateNewId(c, "C.cs");
            var cDebtDocument = DocumentId.CreateNewId(c, "C.Debt.cs");
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
                            documents: [Document(aDocument, aPath, "A-BASE")]
                        ),
                        ProjectInfo.Create(
                            b,
                            VersionStamp.Create(),
                            "B",
                            "B",
                            LanguageNames.CSharp,
                            filePath: bProjectPath,
                            documents: [Document(bDocument, bPath, "B-BASE")],
                            projectReferences: [new ProjectReference(a)]
                        ),
                        ProjectInfo.Create(
                            c,
                            VersionStamp.Create(),
                            "C",
                            "C",
                            LanguageNames.CSharp,
                            filePath: cProjectPath,
                            documents: [Document(cMainDocument, cMainPath, "C-BASE"), Document(cDebtDocument, cDebtPath, "C-DEBT-BASE")]
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
            var surfaces = new[]
            {
                Surface("A", aProjectPath, [Shard(aPath, "surface-a"), meta[a]]),
                Surface("B", bProjectPath, [Shard(bPath, "surface-b"), meta[b]]),
                Surface("C", cProjectPath, [Shard(cMainPath, "surface-c-main"), Shard(cDebtPath, "surface-c-debt"), meta[c]]),
            };
            var baseFacts = new AnalysisResult(
                Path.Combine(root, "Verify.sln"),
                [
                    Source("A", aPath, "A-BASE"),
                    Source("B", bPath, "B-BASE"),
                    Source("C", cMainPath, "C-BASE"),
                    Source("C", cDebtPath, "C-DEBT-BASE"),
                ],
                [],
                References: [Reference(OldTarget, bPath)],
                ProjectSurfaces: surfaces
            );

            VerificationFixture? fixture = null;
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
                var isVerifier = documents.Any(document => document == bDocument);
                if (isVerifier)
                {
                    fixture.VerifierCalls++;
                    fixture.LastVerifierDocuments = documents.ToArray();
                    if (fixture._blockVerifier)
                    {
                        fixture.VerifierStarted.TrySetResult();
                        await fixture.ReleaseVerifier.Task;
                    }
                }

                var result = new Dictionary<string, FileFacts>(StringComparer.OrdinalIgnoreCase);
                foreach (var documentId in documents)
                {
                    var document = solution.GetDocument(documentId)!;
                    var path = document.FilePath!;
                    var text = (await document.GetTextAsync(cancellationToken: default)).ToString();
                    var references =
                        documentId == bDocument
                            ? ImmutableArray.Create(Reference(fixture._mismatch ? NewTarget : OldTarget, path))
                            : ImmutableArray<ReferenceFact>.Empty;
                    result[path] = new FileFacts(
                        [Source(document.Project.Name, path, text)],
                        [],
                        [],
                        references,
                        [],
                        [],
                        [],
                        [],
                        [
                            new ProjectSurfaceContribution(
                                document.Project.Name,
                                document.Project.FilePath ?? "",
                                document.Project.AssemblyName ?? document.Project.Name,
                                Shard(path, SurfaceKey(path, aPath, bPath, cMainPath, cDebtPath)),
                                true
                            ),
                        ]
                    );
                }
                return result;
            }

            Task<ProjectSurfaceRefresh> Refresh(
                Solution _,
                ProjectId projectId,
                RuleSet __,
                CancellationToken ___,
                Rig.Analysis.Extraction.StringInterner? ____
            ) => Task.FromResult(new ProjectSurfaceRefresh([], fixture!._meta[projectId], true));

            var index = new ResidentIndex(
                workspace,
                baseFacts,
                baseFacts.SolutionPath,
                new RuleSet(),
                extractFiles: Extract,
                refreshSurface: Refresh,
                verifyCascadeGate: true
            );
            fixture = new VerificationFixture(index, a, c, bDocument, aPath, bPath, cMainPath, mismatch, blockVerifier, meta);
            return fixture;
        }

        internal async Task AssertCurrentSourceOracleAsync()
        {
            var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var document in Index.CurrentSolution.Projects.SelectMany(project => project.Documents))
            {
                expected[document.FilePath!] = (await document.GetTextAsync()).ToString();
            }

            var actual = Index
                .CaptureSnapshot()
                .EnumerateSourceFiles()
                .ToDictionary(source => source.FilePath, source => source.Evidence, StringComparer.OrdinalIgnoreCase);
            actual.Count.ShouldBe(expected.Count);
            foreach (var (path, evidence) in expected)
            {
                actual.ShouldContainKey(path);
                actual[path].ShouldBe(evidence);
            }
        }

        public void Dispose() => Index.Dispose();

        private static string SurfaceKey(string path, string aPath, string bPath, string cMainPath, string cDebtPath) =>
            path == aPath ? "surface-a"
            : path == bPath ? "surface-b"
            : path == cMainPath ? "surface-c-main"
            : path == cDebtPath ? "surface-c-debt"
            : "unknown";
    }

    private static ReferenceFact Reference(string target, string path) => new(target, "invocation", "M:B.Run()", "B", true, path, 1);

    private static ProjectSurfaceShard Shard(string path, string value) => new(path, false, ProjectContentHash.Compute([value]));

    private static ProjectSurfaceSnapshot Surface(string name, string projectPath, IReadOnlyList<ProjectSurfaceShard> shards) =>
        new(name, projectPath, name, shards, ProjectSurfaceBuilder.Aggregate(shards));

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
