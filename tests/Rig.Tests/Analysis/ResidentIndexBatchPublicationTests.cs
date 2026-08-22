using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Rig.Analysis.Inventory;
using Rig.Domain.Data;
using Shouldly;
using RuleSet = Rig.Domain.Data.RuleSet;

namespace Rig.Tests.Analysis;

[NotInParallel]
public sealed class ResidentIndexBatchPublicationTests
{
    [Test]
    public async Task Three_file_batch_publishes_one_complete_revision_and_one_extractor_batch()
    {
        var extractionCalls = 0;
        DocumentId[]? extracted = null;
        using var fixture = BatchFixture.Create(
            async (solution, documents, _, _, cancellationToken, _) =>
            {
                extractionCalls++;
                extracted = documents.ToArray();
                return await BatchFixture.ExtractAsync(solution, documents, cancellationToken);
            }
        );
        var before = fixture.Index.CaptureSnapshot();

        await fixture.Index.ApplyEditsAsync(
            new Dictionary<string, SourceText>(StringComparer.OrdinalIgnoreCase)
            {
                [fixture.FirstPath.ToUpperInvariant()] = SourceText.From("first-one"),
                [fixture.SecondPath] = SourceText.From("second-one"),
                [fixture.ThirdPath] = SourceText.From("third-one"),
            }
        );

        var after = fixture.Index.CaptureSnapshot();
        after.ShouldNotBeSameAs(before);
        after.Revision.Value.ShouldBe(1);
        extractionCalls.ShouldBe(1);
        extracted.ShouldNotBeNull();
        extracted!.Length.ShouldBe(4, "the linked first file contributes one eager document per project");
        extracted.Distinct().Count().ShouldBe(extracted.Length);
        extracted.ShouldBe(
            [fixture.FirstDocumentId, fixture.LinkedFirstDocumentId, fixture.SecondDocumentId, fixture.ThirdDocumentId],
            ignoreOrder: true
        );
        after.Overlay.Keys.ShouldBe([fixture.FirstPath, fixture.SecondPath, fixture.ThirdPath], ignoreOrder: true);
        after.Delta.ReplacedFiles.ShouldBe([fixture.FirstPath, fixture.SecondPath, fixture.ThirdPath], ignoreOrder: true);
        after.Dirty.PendingDocuments.ShouldBe([fixture.FirstDebtDocumentId, fixture.ThirdDebtDocumentId], ignoreOrder: true);
        after.Dirty.PendingProjects.ShouldBe(
            [fixture.FirstDebtDocumentId.ProjectId, fixture.ThirdDebtDocumentId.ProjectId],
            ignoreOrder: true
        );
        after.EnumerateSourceFiles().Single(f => f.FilePath == fixture.FirstPath).Evidence.ShouldBe("first-one");
        after.EnumerateSourceFiles().Single(f => f.FilePath == fixture.SecondPath).Evidence.ShouldBe("second-one");
        after.EnumerateSourceFiles().Single(f => f.FilePath == fixture.ThirdPath).Evidence.ShouldBe("third-one");
        (await after.Solution.GetDocument(fixture.LinkedFirstDocumentId)!.GetTextAsync()).ToString().ShouldBe("first-one");
        after.FullMaterializationCount.ShouldBe(0);
    }

    [Test]
    public async Task Cancelled_uncooperative_multi_file_extraction_publishes_nothing()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = BatchFixture.Create(
            async (solution, documents, _, _, _, _) =>
            {
                entered.SetResult();
                await release.Task;
                return await BatchFixture.ExtractAsync(solution, documents, CancellationToken.None);
            }
        );
        var before = fixture.Index.CaptureSnapshot();
        using var cancellation = new CancellationTokenSource();

        var apply = fixture.Index.ApplyEditsAsync(
            new Dictionary<string, SourceText>
            {
                [fixture.FirstPath] = SourceText.From("cancelled-first"),
                [fixture.SecondPath] = SourceText.From("cancelled-second"),
                [fixture.ThirdPath] = SourceText.From("cancelled-third"),
            },
            cancellation.Token
        );
        await entered.Task;
        cancellation.Cancel();
        release.SetResult();

        await Should.ThrowAsync<OperationCanceledException>(async () => await apply);
        var after = fixture.Index.CaptureSnapshot();
        after.ShouldBeSameAs(before);
        after.Solution.ShouldBeSameAs(before.Solution);
        after.Overlay.ShouldBeSameAs(before.Overlay);
        after.Dirty.ShouldBeSameAs(before.Dirty);
        after.Revision.ShouldBe(before.Revision);
        after.FullMaterializationCount.ShouldBe(0);
    }

    [Test]
    public async Task Second_batch_preserves_prior_debt_and_removes_documents_settled_eagerly()
    {
        var eagerCalls = 0;
        var cascadeCalls = 0;
        var eagerPolicy = new MutablePolicy();
        var cascadePolicy = new MutablePolicy();
        using var fixture = BatchFixture.Create(eagerPolicy: eagerPolicy, cascadePolicy: cascadePolicy);
        eagerPolicy.Select = (solution, changed) =>
        {
            eagerCalls++;
            return changed.SelectMany(path => solution.GetDocumentIdsWithFilePath(path)).Distinct().ToArray();
        };
        cascadePolicy.Select = (solution, changed) =>
        {
            cascadeCalls++;
            var changedIds = changed.SelectMany(path => solution.GetDocumentIdsWithFilePath(path)).Distinct().ToList();
            if (changed.Contains(fixture.FirstPath, StringComparer.OrdinalIgnoreCase))
            {
                changedIds.Add(fixture.FirstDebtDocumentId);
                changedIds.Add(fixture.ThirdDebtDocumentId);
            }
            else
            {
                changedIds.Add(fixture.SecondDocumentId);
            }

            return changedIds;
        };

        await fixture.Index.ApplyEditAsync(fixture.FirstPath, SourceText.From("first-one"));
        var first = fixture.Index.CaptureSnapshot();
        first.Dirty.PendingDocuments.ShouldBe([fixture.FirstDebtDocumentId, fixture.ThirdDebtDocumentId], ignoreOrder: true);

        await fixture.Index.ApplyEditAsync(fixture.FirstDebtPath, SourceText.From("first-debt-one"));
        var second = fixture.Index.CaptureSnapshot();
        second.Revision.Value.ShouldBe(2);
        second.Dirty.PendingDocuments.ShouldBe([fixture.ThirdDebtDocumentId, fixture.SecondDocumentId], ignoreOrder: true);
        second.Dirty.PendingDocuments.ShouldNotContain(fixture.FirstDebtDocumentId);
        second.Overlay[fixture.FirstPath].SourceFiles.Single().Evidence.ShouldBe("first-one");
        second.Overlay[fixture.FirstDebtPath].SourceFiles.Single().Evidence.ShouldBe("first-debt-one");
        eagerCalls.ShouldBe(2);
        cascadeCalls.ShouldBe(2);
        second.FullMaterializationCount.ShouldBe(0);
    }

    private sealed class MutablePolicy : IDirtySetPolicy
    {
        internal Func<Solution, IReadOnlyCollection<string>, IReadOnlyCollection<DocumentId>> Select { get; set; } =
            (_, _) => throw new InvalidOperationException("The test policy was invoked before configuration.");

        public IReadOnlyCollection<DocumentId> DocumentsToReextract(Solution solution, IReadOnlyCollection<string> changedFilePaths) =>
            Select(solution, changedFilePaths);
    }

    private sealed class BatchFixture : IDisposable
    {
        private BatchFixture(
            ResidentIndex index,
            string firstPath,
            string firstDebtPath,
            string secondPath,
            string thirdPath,
            DocumentId firstDocumentId,
            DocumentId linkedFirstDocumentId,
            DocumentId firstDebtDocumentId,
            DocumentId secondDocumentId,
            DocumentId thirdDocumentId,
            DocumentId thirdDebtDocumentId
        )
        {
            Index = index;
            FirstPath = firstPath;
            FirstDebtPath = firstDebtPath;
            SecondPath = secondPath;
            ThirdPath = thirdPath;
            FirstDocumentId = firstDocumentId;
            LinkedFirstDocumentId = linkedFirstDocumentId;
            FirstDebtDocumentId = firstDebtDocumentId;
            SecondDocumentId = secondDocumentId;
            ThirdDocumentId = thirdDocumentId;
            ThirdDebtDocumentId = thirdDebtDocumentId;
        }

        internal ResidentIndex Index { get; private set; }
        internal string FirstPath { get; }
        internal string FirstDebtPath { get; }
        internal string SecondPath { get; }
        internal string ThirdPath { get; }
        internal DocumentId FirstDocumentId { get; }
        internal DocumentId LinkedFirstDocumentId { get; }
        internal DocumentId FirstDebtDocumentId { get; }
        internal DocumentId SecondDocumentId { get; }
        internal DocumentId ThirdDocumentId { get; }
        internal DocumentId ThirdDebtDocumentId { get; }

        internal static BatchFixture Create(
            ResidentFileExtractor? extractor = null,
            IDirtySetPolicy? eagerPolicy = null,
            IDirtySetPolicy? cascadePolicy = null
        )
        {
            var workspace = new RigWorkspace();
            var root = Path.Combine(Path.GetTempPath(), $"rig-resident-batch-{Guid.NewGuid():N}");
            var firstPath = Path.Combine(root, "First.cs");
            var firstDebtPath = Path.Combine(root, "FirstDebt.cs");
            var secondPath = Path.Combine(root, "Second.cs");
            var thirdPath = Path.Combine(root, "Third.cs");
            var thirdDebtPath = Path.Combine(root, "ThirdDebt.cs");
            var firstProjectId = ProjectId.CreateNewId("FirstProject");
            var secondProjectId = ProjectId.CreateNewId("SecondProject");
            var thirdProjectId = ProjectId.CreateNewId("ThirdProject");
            var firstDocumentId = DocumentId.CreateNewId(firstProjectId, "First");
            var linkedFirstDocumentId = DocumentId.CreateNewId(secondProjectId, "LinkedFirst");
            var firstDebtDocumentId = DocumentId.CreateNewId(firstProjectId, "FirstDebt");
            var secondDocumentId = DocumentId.CreateNewId(secondProjectId, "Second");
            var thirdDocumentId = DocumentId.CreateNewId(thirdProjectId, "Third");
            var thirdDebtDocumentId = DocumentId.CreateNewId(thirdProjectId, "ThirdDebt");
            var firstProject = ProjectInfo.Create(
                firstProjectId,
                VersionStamp.Create(),
                "FirstProject",
                "FirstProject",
                LanguageNames.CSharp,
                documents:
                [
                    Document(firstDocumentId, firstPath, "first-zero"),
                    Document(firstDebtDocumentId, firstDebtPath, "first-debt-zero"),
                ]
            );
            var secondProject = ProjectInfo.Create(
                secondProjectId,
                VersionStamp.Create(),
                "SecondProject",
                "SecondProject",
                LanguageNames.CSharp,
                documents:
                [
                    Document(linkedFirstDocumentId, firstPath, "first-zero"),
                    Document(secondDocumentId, secondPath, "second-zero"),
                ],
                projectReferences: [new ProjectReference(firstProjectId)]
            );
            var thirdProject = ProjectInfo.Create(
                thirdProjectId,
                VersionStamp.Create(),
                "ThirdProject",
                "ThirdProject",
                LanguageNames.CSharp,
                documents:
                [
                    Document(thirdDocumentId, thirdPath, "third-zero"),
                    Document(thirdDebtDocumentId, thirdDebtPath, "third-debt-zero"),
                ],
                projectReferences: [new ProjectReference(secondProjectId)]
            );
            workspace.AddSolution(
                SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Create(), projects: [firstProject, secondProject, thirdProject])
            );
            var solutionPath = Path.Combine(root, "Batch.sln");
            var baseFacts = new AnalysisResult(
                solutionPath,
                [
                    Source("FirstProject", firstPath, "first-zero"),
                    Source("FirstProject", firstDebtPath, "first-debt-zero"),
                    Source("SecondProject", secondPath, "second-zero"),
                    Source("ThirdProject", thirdPath, "third-zero"),
                    Source("ThirdProject", thirdDebtPath, "third-debt-zero"),
                ],
                []
            );
            var index = new ResidentIndex(
                workspace,
                baseFacts,
                solutionPath,
                new RuleSet(),
                eagerPolicy,
                cascadePolicy,
                extractFiles: extractor ?? ExtractDelegate
            );
            return new BatchFixture(
                index,
                firstPath,
                firstDebtPath,
                secondPath,
                thirdPath,
                firstDocumentId,
                linkedFirstDocumentId,
                firstDebtDocumentId,
                secondDocumentId,
                thirdDocumentId,
                thirdDebtDocumentId
            );
        }

        internal static async Task<Dictionary<string, FileFacts>> ExtractAsync(
            Solution solution,
            IReadOnlyCollection<DocumentId> documents,
            CancellationToken cancellationToken
        )
        {
            var slices = new Dictionary<string, FileFacts>(StringComparer.OrdinalIgnoreCase);
            foreach (var documentId in documents)
            {
                var document = solution.GetDocument(documentId)!;
                var path = document.FilePath!;
                var text = (await document.GetTextAsync(cancellationToken)).ToString();
                slices[path] = new FileFacts([Source(document.Project.Name, path, text)], [], [], [], [], [], [], []);
            }

            return slices;
        }

        public void Dispose() => Index.Dispose();

        private static Task<Dictionary<string, FileFacts>> ExtractDelegate(
            Solution solution,
            IReadOnlyCollection<DocumentId> documents,
            string _,
            RuleSet __,
            CancellationToken cancellationToken,
            Rig.Analysis.Extraction.StringInterner? ___
        ) => ExtractAsync(solution, documents, cancellationToken);

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
}
