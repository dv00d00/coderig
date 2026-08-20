using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Rig.Analysis.Inventory;
using Shouldly;

namespace Rig.Tests.Analysis;

// The text-change path of RigWorkspace (the resident-workspace substrate, live-background-index
// slice 1): ChangeDocumentText must (a) actually mutate the changed document's text and syntax tree
// in CurrentSolution, and (b) leave an UNCHANGED project's Compilation instance reference-equal
// across the edit — the compilation-reuse property the whole resident design rests on. If (b) ever
// fails, that is a load-bearing FINDING about Roslyn's compilation tracker, not a cosmetic breakage.
public sealed class RigWorkspaceTests
{
    private const string TextA = "public class A { public int Answer() { return 41; } }";
    private const string TextAEdited = "public class A { public int Answer() { return 42; } }";
    private const string TextB = "public class B { public string Name() { return \"b\"; } }";

    [Test]
    public async Task ChangeDocumentText_updates_the_documents_text_and_syntax_tree()
    {
        using var workspace = new RigWorkspace();
        var (editedDocId, _) = AddTwoIndependentProjects(workspace);

        workspace.ChangeDocumentText(editedDocId, SourceText.From(TextAEdited));

        var document = workspace.CurrentSolution.GetDocument(editedDocId).ShouldNotBeNull();
        (await document.GetTextAsync()).ToString().ShouldBe(TextAEdited);

        var root = (await document.GetSyntaxRootAsync()).ShouldNotBeNull();
        root.ToFullString().ShouldContain("return 42");
        root.ToFullString().ShouldNotContain("return 41");
    }

    [Test]
    public async Task ChangeDocumentText_leaves_an_unchanged_projects_compilation_reference_equal()
    {
        using var workspace = new RigWorkspace();
        var (editedDocId, untouchedProjectId) = AddTwoIndependentProjects(workspace);
        var editedProjectId = editedDocId.ProjectId;

        // Materialize both compilations BEFORE the edit so the reuse comparison is instance-level.
        var untouchedBefore = (
            await workspace.CurrentSolution.GetProject(untouchedProjectId).ShouldNotBeNull().GetCompilationAsync()
        ).ShouldNotBeNull();
        var editedBefore = (
            await workspace.CurrentSolution.GetProject(editedProjectId).ShouldNotBeNull().GetCompilationAsync()
        ).ShouldNotBeNull();

        workspace.ChangeDocumentText(editedDocId, SourceText.From(TextAEdited));

        // The untouched project's compilation must be the SAME instance — this is the reuse property
        // the resident design rests on. (The edited project's must NOT be, or the edit didn't land:
        // the anti-vacuity arm of the same claim.)
        var untouchedAfter = (
            await workspace.CurrentSolution.GetProject(untouchedProjectId).ShouldNotBeNull().GetCompilationAsync()
        ).ShouldNotBeNull();
        var editedAfter = (
            await workspace.CurrentSolution.GetProject(editedProjectId).ShouldNotBeNull().GetCompilationAsync()
        ).ShouldNotBeNull();

        ReferenceEquals(untouchedBefore, untouchedAfter)
            .ShouldBeTrue(
                "the unchanged project's Compilation was rebuilt across an unrelated document edit — compilation reuse does NOT hold"
            );
        ReferenceEquals(editedBefore, editedAfter)
            .ShouldBeFalse("the edited project's Compilation is the same instance after the edit — the text change did not take effect");
    }

    // Two SINGLE-DOCUMENT C# projects with no reference between them, installed via the same
    // AddSolution path SolutionSourceLoader.BuildWorkspaceFromResults uses. Project A holds the
    // document the tests edit; project B is the untouched control.
    private static (DocumentId EditedDocId, ProjectId UntouchedProjectId) AddTwoIndependentProjects(RigWorkspace workspace)
    {
        var projectAId = ProjectId.CreateNewId("A");
        var projectBId = ProjectId.CreateNewId("B");
        var documentAId = DocumentId.CreateNewId(projectAId);

        var projectA = ProjectInfo.Create(
            projectAId,
            VersionStamp.Create(),
            name: "A",
            assemblyName: "A",
            language: LanguageNames.CSharp,
            documents: [Document(documentAId, "ClassA.cs", TextA)]
        );
        var projectB = ProjectInfo.Create(
            projectBId,
            VersionStamp.Create(),
            name: "B",
            assemblyName: "B",
            language: LanguageNames.CSharp,
            documents: [Document(DocumentId.CreateNewId(projectBId), "ClassB.cs", TextB)]
        );

        workspace.AddSolution(SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Create(), projects: [projectA, projectB]));
        return (documentAId, projectBId);
    }

    private static DocumentInfo Document(DocumentId documentId, string name, string text) =>
        DocumentInfo.Create(
            documentId,
            name,
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(text), VersionStamp.Create(), name)),
            filePath: name
        );
}
