using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Rig.Analysis.Inventory;
using Rig.Domain;
using Rig.Domain.Data;
using Shouldly;
using RuleSet = Rig.Domain.Data.RuleSet;

namespace Rig.Tests.Analysis;

[NotInParallel]
public sealed class FactSnapshotPublicationTests
{
    [Test]
    public async Task Captured_revision_stays_self_consistent_while_the_next_revision_publishes()
    {
        using var fixture = SnapshotFixture.Create();
        var index = fixture.Index;
        var capturedSignal = new TaskCompletionSource<FactSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var publicationSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var query = Task.Run(async () =>
        {
            var captured = index.CaptureSnapshot();
            capturedSignal.SetResult(captured);
            await publicationSignal.Task;
            return (
                Snapshot: captured,
                Text: (await captured.Solution.GetDocument(fixture.FirstDocumentId)!.GetTextAsync()).ToString(),
                FactEvidence: captured.FlattenedFacts.SourceFiles.Single(f => f.FilePath == fixture.FirstFilePath).Evidence
            );
        });

        var revisionN = await capturedSignal.Task;
        await index.ApplyEditAsync(fixture.FirstFilePath, SourceText.From("revision-one"));
        var revisionNPlusOne = index.CaptureSnapshot();
        publicationSignal.SetResult();
        var answer = await query;

        answer.Snapshot.ShouldBeSameAs(revisionN);
        answer.Snapshot.Revision.Value.ShouldBe(0);
        answer.Text.ShouldBe("revision-zero");
        answer.FactEvidence.ShouldBe("revision-zero");

        revisionNPlusOne.ShouldNotBeSameAs(revisionN);
        revisionNPlusOne.Revision.Value.ShouldBe(1);
        (await revisionNPlusOne.Solution.GetDocument(fixture.FirstDocumentId)!.GetTextAsync()).ToString().ShouldBe("revision-one");
        revisionNPlusOne.FlattenedFacts.SourceFiles.Single(f => f.FilePath == fixture.FirstFilePath).Evidence.ShouldBe("revision-one");
    }

    [Test]
    public async Task Cancellation_after_an_uncooperative_extractor_returns_publishes_nothing()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = SnapshotFixture.Create(
            async (solution, documents, _, _, _, _) =>
            {
                entered.SetResult();
                await release.Task; // deliberately ignores the cancellation token
                return await SnapshotFixture.ExtractAsync(solution, documents);
            }
        );
        var index = fixture.Index;
        var before = index.CaptureSnapshot();
        var beforeFacts = before.FlattenedFacts;
        using var cancellation = new CancellationTokenSource();

        var apply = index.ApplyEditAsync(fixture.FirstFilePath, SourceText.From("cancelled-revision"), cancellation.Token);
        await entered.Task;
        cancellation.Cancel();
        release.SetResult();

        await Should.ThrowAsync<OperationCanceledException>(async () => await apply);
        var after = index.CaptureSnapshot();
        after.ShouldBeSameAs(before);
        after.Solution.ShouldBeSameAs(before.Solution);
        after.Overlay.ShouldBeSameAs(before.Overlay);
        after.FlattenedFacts.ShouldBeSameAs(beforeFacts);
        after.Dirty.ShouldBeSameAs(before.Dirty);
        after.Revision.ShouldBe(before.Revision);
    }

    [Test]
    public async Task Reconciliation_based_on_an_old_snapshot_cannot_overwrite_a_newer_edit()
    {
        var reconcileEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReconcile = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondDocument = new StrongBox<DocumentId?>();
        using var fixture = SnapshotFixture.Create(
            async (solution, documents, _, _, cancellationToken, _) =>
            {
                if (documents.All(id => id == secondDocument.Value))
                {
                    reconcileEntered.SetResult();
                    await releaseReconcile.Task.WaitAsync(cancellationToken);
                }

                return await SnapshotFixture.ExtractAsync(solution, documents);
            }
        );
        secondDocument.Value = fixture.SecondDocumentId;
        var index = fixture.Index;

        await index.ApplyEditAsync(fixture.FirstFilePath, SourceText.From("revision-one"));
        index.CaptureSnapshot().Dirty.PendingDocuments.ShouldContain(fixture.SecondDocumentId);

        var reconcile = index.ReconcileAsync();
        await reconcileEntered.Task;
        await index.ApplyEditAsync(fixture.FirstFilePath, SourceText.From("revision-two"));
        var newer = index.CaptureSnapshot();
        releaseReconcile.SetResult();

        (await reconcile).ShouldBeFalse("a reconcile built from an older reference must report that publication was superseded");
        index.CaptureSnapshot().ShouldBeSameAs(newer);
        (await newer.Solution.GetDocument(fixture.FirstDocumentId)!.GetTextAsync()).ToString().ShouldBe("revision-two");
        newer.FlattenedFacts.SourceFiles.Single(f => f.FilePath == fixture.FirstFilePath).Evidence.ShouldBe("revision-two");
        newer.Dirty.PendingDocuments.ShouldContain(fixture.SecondDocumentId);
    }

    [Test]
    public async Task Inactive_snapshot_is_collectible_after_the_last_reader_releases_it()
    {
        var (fixture, retired) = PublishAndReleaseRetiredSnapshot();
        using (fixture)
        {
            ForceCompactingCollection();
            retired.Snapshot.TryGetTarget(out _).ShouldBeFalse("the resident index must not retain a predecessor chain");
            retired.Overlay.TryGetTarget(out _).ShouldBeFalse("a replacement must not retain the retired overlay wrapper");
            retired.FileFacts.TryGetTarget(out _).ShouldBeFalse("replacing a file must release its retired fact slice");
            retired.Solution.TryGetTarget(out _).ShouldBeFalse("the current revision must not retain its predecessor Solution");

            // Keep the index alive throughout the collection and prove the published replacement is still
            // usable; otherwise collecting the whole fixture would make the weak-reference assertions vacuous.
            var current = fixture.Index.CaptureSnapshot();
            current.Revision.Value.ShouldBe(2);
            (await current.Solution.GetDocument(fixture.FirstDocumentId)!.GetTextAsync()).ToString().ShouldBe("revision-two");
            current.FlattenedFacts.SourceFiles.Single(f => f.FilePath == fixture.FirstFilePath).Evidence.ShouldBe("revision-two");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (SnapshotFixture Fixture, RetiredSnapshotReferences Retired) PublishAndReleaseRetiredSnapshot()
    {
        var fixture = SnapshotFixture.Create();
        fixture.Index.ApplyEditAsync(fixture.FirstFilePath, SourceText.From("revision-one")).GetAwaiter().GetResult();

        var retiredSnapshot = fixture.Index.CaptureSnapshot();
        retiredSnapshot.Revision.Value.ShouldBe(1);
        var retiredOverlay = retiredSnapshot.Overlay;
        var retiredFileFacts = retiredOverlay[fixture.FirstFilePath];
        var retiredSolution = retiredSnapshot.Solution;
        var retired = new RetiredSnapshotReferences(
            new WeakReference<FactSnapshot>(retiredSnapshot),
            new WeakReference<ImmutableDictionary<string, FileFacts>>(retiredOverlay),
            new WeakReference<FileFacts>(retiredFileFacts),
            new WeakReference<Solution>(retiredSolution)
        );

        fixture.Index.ApplyEditAsync(fixture.FirstFilePath, SourceText.From("revision-two")).GetAwaiter().GetResult();
        fixture.Index.CaptureSnapshot().Revision.Value.ShouldBe(2);

        // Explicitly clear the helper's payload locals before returning. No async state machine or closure
        // can retain them, and the NoInlining boundary keeps the caller's forced GC outside this stack frame.
        retiredSnapshot = null!;
        retiredOverlay = null!;
        retiredFileFacts = null!;
        retiredSolution = null!;
        return (fixture, retired);
    }

    private sealed record RetiredSnapshotReferences(
        WeakReference<FactSnapshot> Snapshot,
        WeakReference<ImmutableDictionary<string, FileFacts>> Overlay,
        WeakReference<FileFacts> FileFacts,
        WeakReference<Solution> Solution
    );

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ForceCompactingCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private sealed class SnapshotFixture : IDisposable
    {
        private SnapshotFixture(
            ResidentIndex index,
            DocumentId firstDocumentId,
            DocumentId secondDocumentId,
            string firstFilePath,
            string secondFilePath
        )
        {
            Index = index;
            FirstDocumentId = firstDocumentId;
            SecondDocumentId = secondDocumentId;
            FirstFilePath = firstFilePath;
            SecondFilePath = secondFilePath;
        }

        internal ResidentIndex Index { get; }
        internal DocumentId FirstDocumentId { get; }
        internal DocumentId SecondDocumentId { get; }
        internal string FirstFilePath { get; }
        internal string SecondFilePath { get; }

        internal static SnapshotFixture Create(ResidentFileExtractor? extractor = null)
        {
            var workspace = new RigWorkspace();
            var projectId = ProjectId.CreateNewId("SnapshotProject");
            var firstDocumentId = DocumentId.CreateNewId(projectId, "First");
            var secondDocumentId = DocumentId.CreateNewId(projectId, "Second");
            var root = Path.Combine(Path.GetTempPath(), $"rig-fact-snapshot-{Guid.NewGuid():N}");
            var firstPath = Path.Combine(root, "First.cs");
            var secondPath = Path.Combine(root, "Second.cs");
            var project = ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "SnapshotProject",
                "SnapshotProject",
                LanguageNames.CSharp,
                documents: [Document(firstDocumentId, firstPath, "revision-zero"), Document(secondDocumentId, secondPath, "dependent-zero")]
            );
            workspace.AddSolution(SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Create(), projects: [project]));

            var meta = Shard("", "meta");
            var shards = new[] { Shard(firstPath, "revision-zero"), Shard(secondPath, "dependent-zero"), meta };
            var baseFacts = new AnalysisResult(
                Path.Combine(root, "Snapshot.sln"),
                [Source(firstPath, "revision-zero"), Source(secondPath, "dependent-zero")],
                [],
                ProjectSurfaces:
                [
                    new ProjectSurfaceSnapshot("SnapshotProject", "", "SnapshotProject", shards, ProjectSurfaceBuilder.Aggregate(shards)),
                ]
            );
            var index = new ResidentIndex(
                workspace,
                baseFacts,
                baseFacts.SolutionPath,
                new RuleSet(),
                extractFiles: extractor ?? ExtractAsync,
                refreshSurface: (_, _, _, _, _) => Task.FromResult(new ProjectSurfaceRefresh([], meta, IsClassifiable: true))
            );
            return new SnapshotFixture(index, firstDocumentId, secondDocumentId, firstPath, secondPath);
        }

        internal static async Task<Dictionary<string, FileFacts>> ExtractAsync(
            Solution solution,
            IReadOnlyCollection<DocumentId> documents,
            string _ = "",
            RuleSet? __ = null,
            CancellationToken cancellationToken = default,
            Rig.Analysis.Extraction.StringInterner? ___ = null
        )
        {
            var byFile = new Dictionary<string, FileFacts>(StringComparer.OrdinalIgnoreCase);
            foreach (var documentId in documents)
            {
                var document = solution.GetDocument(documentId)!;
                var filePath = document.FilePath!;
                var evidence = (await document.GetTextAsync(cancellationToken)).ToString();
                byFile[filePath] = new FileFacts(
                    [Source(filePath, evidence)],
                    [],
                    [],
                    [],
                    [],
                    [],
                    [],
                    [],
                    [
                        new ProjectSurfaceContribution(
                            "SnapshotProject",
                            "",
                            "SnapshotProject",
                            Shard(filePath, evidence),
                            IsClassifiable: true
                        ),
                    ]
                );
            }

            return byFile;
        }

        public void Dispose() => Index.Dispose();

        private static DocumentInfo Document(DocumentId id, string path, string text) =>
            DocumentInfo.Create(
                id,
                Path.GetFileName(path),
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(text), VersionStamp.Create(), path)),
                filePath: path
            );

        private static SourceFileInfo Source(string path, string evidence) =>
            new("SnapshotProject", path, "indexed", "high", "test", "test", evidence);

        private static ProjectSurfaceShard Shard(string path, string value) => new(path, false, ProjectContentHash.Compute([value]));
    }
}
