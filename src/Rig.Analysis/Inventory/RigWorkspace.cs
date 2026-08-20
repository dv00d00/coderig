using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;

namespace Rig.Analysis.Inventory;

// The workspace substrate for the resident/incremental indexing design (live-background-index).
// AdhocWorkspace is sealed, so rig needs its own Workspace subclass to reach the protected On* state
// mutators — specifically OnDocumentTextChanged for the single-document edit path (and, later,
// OnProjectReloaded for the .csproj-changed path; deliberately not built yet, but nothing here designs
// it out — it is just another protected mutator a future public wrapper will expose).
//
// This is a substrate swap, not a redesign: construction mirrors AdhocWorkspace exactly (same default
// MEF host services, same "Custom" workspace kind, same AddSolution = OnSolutionAdded +
// UpdateReferencesAfterAdd, CanApplyChange all-true so the analyzer-reference wiring's TryApplyChanges
// keeps working). The one deliberate divergence is HOW an edit is applied:
//
//   - ChangeDocumentText goes through OnDocumentTextChanged, NOT TryApplyChanges. TryApplyChanges
//     diffs a forked Solution against CurrentSolution across the WHOLE solution and rejects/merges —
//     it would discard the caller's fork; OnDocumentTextChanged mutates exactly one document's text.
//   - It takes a SourceText, never a TextLoader: handing Roslyn the new SourceText is what selects the
//     INCREMENTAL reparse path (DocumentState can diff old vs new text); a TextLoader forces a full
//     reparse of the document.
internal sealed class RigWorkspace : Workspace
{
    public RigWorkspace()
        : base(MefHostServices.DefaultHost, workspaceKind: "Custom") { } // same kind AdhocWorkspace defaults to (WorkspaceKind.Custom is internal)

    // Mirror AdhocWorkspace: every change kind is applicable, so TryApplyChanges (used once at build
    // time by WireGeneratorAnalyzersAsync to add analyzer references) routes through the default
    // Apply* -> On* implementations. The incremental EDIT path never goes through here.
    public override bool CanApplyChange(ApplyChangesKind feature) => true;

    // Mirror of AdhocWorkspace.AddSolution: installs the assembled SolutionInfo as CurrentSolution.
    // UpdateReferencesAfterAdd is the same post-step AdhocWorkspace runs (converts metadata references
    // that point at an in-workspace project's output path into project references — a no-op in rig's
    // flow, where ProjectInfos carry no OutputFilePath and the loader resolves project refs itself).
    public Solution AddSolution(SolutionInfo solutionInfo)
    {
        ArgumentNullException.ThrowIfNull(solutionInfo);
        OnSolutionAdded(solutionInfo);
        UpdateReferencesAfterAdd();
        return CurrentSolution;
    }

    // The live single-document edit: replace one document's text in CurrentSolution, in place.
    // PreserveValue matches what Workspace.ApplyDocumentTextChanged itself uses for applied edits.
    public void ChangeDocumentText(DocumentId documentId, SourceText newText)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(newText);
        OnDocumentTextChanged(documentId, newText, PreservationMode.PreserveValue);
    }
}
