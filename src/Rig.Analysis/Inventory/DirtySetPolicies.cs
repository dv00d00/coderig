using Microsoft.CodeAnalysis;

namespace Rig.Analysis.Inventory;

// The pluggable invalidation policy for the resident index (live-background-index slice 3, per the
// DECISION 2026-08-20 in docs/backlog/progress/live-background-index.md): given the file paths that
// changed, which documents must be re-extracted for the fact set to be sound again? The coarse-vs-gated
// choice is deliberately a POLICY SWAP decided by measurement, not an architectural fork — the
// surface-hash gate (slice 4) becomes a third implementation of this same interface.
internal interface IDirtySetPolicy
{
    IReadOnlyCollection<DocumentId> DocumentsToReextract(Solution solution, IReadOnlyCollection<string> changedFilePaths);
}

// The EAGER arm of the converging design: just the documents whose paths changed. Fast, and NOT sound
// on its own — a dependent's binding can change without its text changing (add an overload in file A
// and file B's call may re-bind). ResidentIndex uses it for the immediate re-extract on an edit, then
// discloses the outstanding cascade until ReconcileAsync has run it.
internal sealed class ChangedFilesOnlyPolicy : IDirtySetPolicy
{
    public IReadOnlyCollection<DocumentId> DocumentsToReextract(Solution solution, IReadOnlyCollection<string> changedFilePaths)
    {
        var documents = new List<DocumentId>();
        var seen = new HashSet<DocumentId>();
        foreach (var path in changedFilePaths)
        {
            // A file linked into several projects yields one DocumentId per project — all of them are
            // dirty (each project context re-binds the file independently).
            foreach (var documentId in solution.GetDocumentIdsWithFilePath(Path.GetFullPath(path)))
            {
                if (seen.Add(documentId))
                {
                    documents.Add(documentId);
                }
            }
        }

        return documents;
    }
}

// The COARSE sound policy: every document of the changed files' projects plus all TRANSITIVE
// DEPENDENTS. The dependency graph comes from Roslyn's Project.ProjectReferences — which the loader
// populates from the MSBuild ProjectReference closure — and deliberately NOT from reference_facts: a
// facts-derived graph is a measured LOWER BOUND (a csproj reference with no observed call produces no
// edge), so cascading over it silently under-invalidates.
internal sealed class ProjectCascadePolicy : IDirtySetPolicy
{
    public IReadOnlyCollection<DocumentId> DocumentsToReextract(Solution solution, IReadOnlyCollection<string> changedFilePaths)
    {
        var seeds = new HashSet<ProjectId>();
        foreach (var path in changedFilePaths)
        {
            foreach (var documentId in solution.GetDocumentIdsWithFilePath(Path.GetFullPath(path)))
            {
                seeds.Add(documentId.ProjectId);
            }
        }

        // Reverse edges: referenced project -> its direct dependents.
        var dependents = new Dictionary<ProjectId, List<ProjectId>>();
        foreach (var project in solution.Projects)
        {
            foreach (var reference in project.ProjectReferences)
            {
                if (!dependents.TryGetValue(reference.ProjectId, out var list))
                {
                    dependents[reference.ProjectId] = list = [];
                }

                list.Add(project.Id);
            }
        }

        // Transitive closure over the reverse edges, seeds included.
        var affected = new HashSet<ProjectId>(seeds);
        var queue = new Queue<ProjectId>(seeds);
        while (queue.Count > 0)
        {
            if (!dependents.TryGetValue(queue.Dequeue(), out var directDependents))
            {
                continue;
            }

            foreach (var dependent in directDependents)
            {
                if (affected.Add(dependent))
                {
                    queue.Enqueue(dependent);
                }
            }
        }

        var documents = new List<DocumentId>();
        foreach (var project in solution.Projects)
        {
            if (!affected.Contains(project.Id) || project.Language != LanguageNames.CSharp)
            {
                continue;
            }

            foreach (var document in project.Documents)
            {
                if (document.FilePath?.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) == true)
                {
                    documents.Add(document.Id);
                }
            }
        }

        return documents;
    }
}
