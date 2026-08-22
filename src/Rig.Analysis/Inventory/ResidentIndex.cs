using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Rig.Analysis.Extraction;
using Rig.Domain.Data;
using RuleSet = Rig.Domain.Data.RuleSet;

namespace Rig.Analysis.Inventory;

// The converging resident index. It owns the retained RigWorkspace only as the Roslyn lifetime substrate;
// every published FactSnapshot owns an immutable Solution, BASE AnalysisResult, per-FILE overlay and dirty
// set. An edit forks the captured Solution and builds its eager replacement privately, then atomically
// publishes the complete candidate. The sound cascade is recorded as UNRECONCILED and re-extracted by
// ReconcileAsync. Between the two, CurrentFacts is servable and UnreconciledProjects is the disclosure a
// caller must surface — cascade latency becomes a disclosure problem, not an answer-blocking one.
//
// Deliberately NO background threads/timers in here: ReconcileAsync is a plain awaitable and the HOST
// owns scheduling (a later slice). Deliberately no Rig.Storage/Rig.Cli dependency: this is fact-level.
//
// Overlay grain and the replace-not-append rule: the overlay is keyed by FILE PATH, and every fact
// kind that carries a FilePath (symbols, references, type relations, dispatch, allocations, DI
// registrations, source-file rows, and compile-health rows) is REPLACED per file when merging — a
// stale row for a re-extracted file is a ghost fact, the worst bug this class can have. Re-extraction
// is BATCHED (one ExtractFromDocumentsByFileAsync call per ReextractAsync),
// and the batch result comes back already partitioned by file — grouped from the per-SourceModel
// extraction results before any flattening — so an overlay entry's relation/dispatch lists are exactly
// that file's emissions, across all its linked DocumentIds.
internal sealed class ResidentIndex : IDisposable
{
    private readonly RigWorkspace _workspace;
    private readonly AnalysisResult _baseResult;
    private readonly string _solutionPath;
    private readonly RuleSet _rules;
    private readonly IDirtySetPolicy _eagerPolicy;
    private readonly IDirtySetPolicy _cascadePolicy;
    private readonly ResidentFileExtractor _extractFiles;
    private readonly ResidentSurfaceRefresher _refreshSurface;
    private readonly bool _verifyCascadeGate;
    private readonly ImmutableDictionary<string, ImmutableArray<DocumentId>> _documentsByPath;
    private readonly SegmentedFactGraphBase _graphBase;

    // Host-lifetime string interner shared by every re-extraction: a re-extracted generation's retained
    // strings alias the previous generation's instead of duplicating the whole string set per edit (the
    // measured duplication is ~0.9 GB per full-cascade generation on MedDBase). Pass the SAME instance
    // the cold boot used (WatchCommand does) so overlay strings also alias the BASE facts'. The table
    // only grows — bounded by the union of values ever seen — which is the deliberate trade: dropping it
    // per generation would forfeit exactly the cross-generation sharing it exists for.
    private readonly StringInterner? _interner;

    private FactSnapshot _currentSnapshot;

    public ResidentIndex(
        RigWorkspace workspace,
        AnalysisResult baseResult,
        string solutionPath,
        RuleSet rules,
        IDirtySetPolicy? eagerPolicy = null,
        IDirtySetPolicy? cascadePolicy = null,
        StringInterner? interner = null,
        ResidentFileExtractor? extractFiles = null,
        ResidentSurfaceRefresher? refreshSurface = null,
        bool verifyCascadeGate = false
    )
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _baseResult = baseResult ?? throw new ArgumentNullException(nameof(baseResult));
        _solutionPath = solutionPath ?? throw new ArgumentNullException(nameof(solutionPath));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _eagerPolicy = eagerPolicy ?? new ChangedFilesOnlyPolicy();
        _cascadePolicy = cascadePolicy ?? new ProjectCascadePolicy();
        _interner = interner ?? StringInterner.CreateDefault();
        _extractFiles = extractFiles ?? SolutionAnalyzer.ExtractFromDocumentsByFileAsync;
        _refreshSurface = refreshSurface ?? SolutionAnalyzer.RefreshProjectSurfaceAsync;
        _verifyCascadeGate = verifyCascadeGate;
        _graphBase = SegmentedFactGraphBase.Build(_baseResult);
        var documentsByPath = ImmutableDictionary.CreateBuilder<string, ImmutableArray<DocumentId>>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in workspace.CurrentSolution.Projects.SelectMany(project => project.Documents))
        {
            if (document.FilePath is null)
            {
                continue;
            }

            var fullPath = Path.GetFullPath(document.FilePath);
            documentsByPath[fullPath] = documentsByPath.TryGetValue(fullPath, out var existing) ? existing.Add(document.Id) : [document.Id];
        }

        _documentsByPath = documentsByPath.ToImmutable();
        _currentSnapshot = new FactSnapshot(
            new FactRevision(0),
            workspace.CurrentSolution,
            _baseResult,
            ImmutableDictionary.Create<string, FileFacts>(StringComparer.OrdinalIgnoreCase),
            DirtySet.Empty,
            SnapshotDelta.Empty,
            ProjectSurfaceCatalog.Seed(workspace.CurrentSolution, baseResult.ProjectSurfaces),
            _graphBase,
            SegmentedFactGraphOverlay.Empty
        );
    }

    public Solution CurrentSolution => CaptureSnapshot().Solution;

    internal FactSnapshot CaptureSnapshot() => Volatile.Read(ref _currentSnapshot);

    // The disclosure: projects whose documents are owed to the cascade but not yet re-extracted.
    // Rendering this is NOT this class's job — a caller surfaces it alongside any answer served from
    // CurrentFacts while it is non-empty.
    public IReadOnlyCollection<string> UnreconciledProjects
    {
        get
        {
            var snapshot = CaptureSnapshot();
            return snapshot
                .Dirty.PendingProjects.Select(id => snapshot.Solution.GetProject(id)?.Name)
                .Where(name => name is not null)
                .Select(name => name!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public IReadOnlyCollection<string> CascadeGateDisabledProjects => CaptureSnapshot().Surfaces.GateDisabledProjectNames;

    // Base facts with every overlaid file's facts REPLACED (never appended). Recomputed lazily after
    // any edit/reconcile; callers get a consistent snapshot.
    public AnalysisResult CurrentFacts => CaptureSnapshot().FlattenedFacts;

    // Singleton compatibility wrapper. Publication semantics live in ApplyEditsAsync so a debounced
    // save burst cannot expose one intermediate generation per file.
    public Task ApplyEditAsync(string filePath, SourceText newText, CancellationToken cancellationToken = default) =>
        ApplyEditsAsync(new Dictionary<string, SourceText> { [filePath] = newText }, cancellationToken);

    // Apply a complete save burst as ONE immutable generation. All paths are normalized and validated
    // before extraction starts; every linked DocumentId receives its file's text in the same private
    // candidate Solution; policies and extraction each see the complete batch exactly once. Neither the
    // retained workspace nor any published snapshot member moves until the final reference-CAS.
    public async Task ApplyEditsAsync(IReadOnlyDictionary<string, SourceText> edits, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edits);
        if (edits.Count == 0)
        {
            return;
        }

        var basis = CaptureSnapshot();
        var normalized = new Dictionary<string, SourceText>(StringComparer.OrdinalIgnoreCase);
        foreach (var (filePath, newText) in edits)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(newText);
            normalized[Path.GetFullPath(filePath)] = newText;
        }

        var validated = new Dictionary<string, (SourceText Text, IReadOnlyCollection<DocumentId> Documents)>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var (fullPath, newText) in normalized)
        {
            var documentIds = DocumentsAtPath(fullPath);
            if (documentIds.Count == 0)
            {
                throw new ArgumentException($"No document in the retained workspace has the path '{fullPath}'.", nameof(edits));
            }

            var canonicalPath = basis.Solution.GetDocument(documentIds.First())!.FilePath!;
            validated[Path.GetFullPath(canonicalPath)] = (newText, documentIds);
        }

        var solution = basis.Solution;
        foreach (var (_, edit) in validated)
        {
            var (newText, documentIds) = edit;
            foreach (var documentId in documentIds)
            {
                solution = solution.WithDocumentText(documentId, newText, PreservationMode.PreserveValue);
            }
        }

        var changed = validated.Keys.ToArray();
        var eager = _eagerPolicy.DocumentsToReextract(solution, changed).Distinct().ToArray();
        var editedOrigins = validated.Values.SelectMany(v => v.Documents).Select(d => d.ProjectId).ToImmutableHashSet();
        IReadOnlyDictionary<ProjectId, IReadOnlyCollection<DocumentId>> cascadeByOrigin;
        if (_cascadePolicy is IOriginAwareDirtySetPolicy originAware)
        {
            cascadeByOrigin = originAware.DocumentsToReextractByOrigin(solution, changed);
        }
        else
        {
            // Custom/test policies retain their one-call contract. Attributing their conservative union to
            // every edited origin may retain extra debt, but can never clear required work.
            var union = _cascadePolicy.DocumentsToReextract(solution, changed).Distinct().ToArray();
            cascadeByOrigin = editedOrigins.ToDictionary(id => id, _ => (IReadOnlyCollection<DocumentId>)union);
        }
        var slices = await ExtractAsync(solution, eager, cancellationToken);
        var overlay = basis.Overlay.SetItems(slices);
        var graphOverlay = basis.GraphOverlay.Replace(slices);

        // The cascade the eager arm did NOT cover is owed; the eagerly covered documents are current
        // against the newest snapshot, so they also settle any PREVIOUS batch's cascade debt.
        var eagerSet = eager.ToImmutableHashSet();
        var pendingByOrigin = basis.Dirty.PendingByOrigin.ToBuilder();
        foreach (var origin in pendingByOrigin.Keys.ToArray())
        {
            var remaining = pendingByOrigin[origin].Except(eagerSet).ToImmutableHashSet();
            if (remaining.Count == 0)
            {
                pendingByOrigin.Remove(origin);
            }
            else
            {
                pendingByOrigin[origin] = remaining;
            }
        }
        foreach (var origin in editedOrigins)
        {
            var contribution = cascadeByOrigin.GetValueOrDefault(origin, []).ToImmutableHashSet().Except(eagerSet);
            if (contribution.Count == 0)
            {
                pendingByOrigin.Remove(origin);
            }
            else
            {
                pendingByOrigin[origin] = contribution;
            }
        }

        var surfaceStates = basis.Delta.SurfaceStates.ToBuilder();
        foreach (var origin in editedOrigins)
        {
            surfaceStates[origin] = SurfaceState.Unknown;
        }
        var surfaces = basis.Surfaces.ReplaceEmitters(
            solution,
            slices.Values.SelectMany(s => s.ProjectSurfaces.IsDefault ? [] : s.ProjectSurfaces)
        );
        var affectedProjects = cascadeByOrigin.Values.SelectMany(d => d).Concat(eager).Select(d => d.ProjectId).ToImmutableHashSet();
        var candidate = new FactSnapshot(
            basis.Revision.Next(),
            solution,
            _baseResult,
            overlay,
            DirtySet.FromContributions(solution, pendingByOrigin.ToImmutable()),
            new SnapshotDelta(
                slices.Keys.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase),
                affectedProjects,
                ImmutableHashSet<string>.Empty,
                surfaceStates.ToImmutable()
            ),
            surfaces,
            _graphBase,
            graphOverlay
        );

        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(Interlocked.CompareExchange(ref _currentSnapshot, candidate, basis), basis))
        {
            throw new InvalidOperationException("The resident edit batch was superseded by a newer fact snapshot.");
        }
    }

    // Lazily classify only the requested Unknown origin projects. All Roslyn/generator work happens
    // against one captured Solution and produces a private catalog candidate; cancellation or a stale
    // expected-reference CAS changes no published member.
    public async Task<bool> RefineUnknownSurfacesAsync(
        IReadOnlySet<ProjectId>? projects = null,
        CancellationToken cancellationToken = default
    )
    {
        var basis = CaptureSnapshot();
        var requested = basis
            .Delta.SurfaceStates.Where(pair =>
                pair.Value == SurfaceState.Unknown
                && basis.Surfaces.Projects.TryGetValue(pair.Key, out var partition)
                && partition.IsClassifiable
                && (projects is null || projects.Contains(pair.Key))
            )
            .Select(pair => pair.Key)
            .ToArray();
        if (requested.Length == 0)
        {
            return false;
        }

        var catalog = basis.Surfaces;
        var overlay = basis.Overlay;
        var graphOverlay = basis.GraphOverlay;
        var states = basis.Delta.SurfaceStates.ToBuilder();
        var debt = basis.Dirty.PendingByOrigin.ToBuilder();
        var wouldBeBodyOnly = new List<ProjectId>();
        var classified = false;
        foreach (var projectId in requested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var refresh = await _refreshSurface(basis.Solution, projectId, _rules, cancellationToken, _interner);
            cancellationToken.ThrowIfCancellationRequested();
            if (!catalog.TryApplyRefresh(projectId, refresh, out var refreshedCatalog, out var state))
            {
                continue;
            }

            if (refresh.GeneratedFacts is not null && catalog.Projects.TryGetValue(projectId, out var priorPartition))
            {
                var generated = refresh.GeneratedFacts;
                foreach (var retiredPath in priorPartition.GeneratedShards.Select(s => s.EmitterFilePath))
                {
                    if (!generated.ContainsKey(retiredPath))
                    {
                        overlay = overlay.SetItem(retiredPath, EmptyFileFacts());
                        graphOverlay = graphOverlay.Replace(
                            new Dictionary<string, FileFacts>(StringComparer.OrdinalIgnoreCase) { [retiredPath] = EmptyFileFacts() }
                        );
                    }
                }
                overlay = overlay.SetItems(generated);
                graphOverlay = graphOverlay.Replace(generated);
            }
            catalog = refreshedCatalog;
            states[projectId] = state;
            if (state == SurfaceState.Changed && !debt.ContainsKey(projectId))
            {
                // The eager edit already covered the entire affected closure, so there is no coarse
                // work to keep sticky even though the accepted surface fingerprint moved.
                catalog = catalog.MarkReconciled([projectId]);
            }
            if (state == SurfaceState.BodyOnly && !debt.ContainsKey(projectId))
            {
                // A later eager batch can pay every document owed by an earlier Changed surface.
                // Once no contribution remains, the sticky coarse requirement is satisfied too.
                catalog = catalog.MarkReconciled([projectId]);
            }
            if (
                state == SurfaceState.BodyOnly
                && !catalog.Projects[projectId].RequiresCoarseReconciliation
                && _verifyCascadeGate
                && debt.ContainsKey(projectId)
            )
            {
                wouldBeBodyOnly.Add(projectId);
            }
            else if (state == SurfaceState.BodyOnly && !catalog.Projects[projectId].RequiresCoarseReconciliation)
            {
                debt.Remove(projectId);
            }
            classified = true;
        }

        if (!classified)
        {
            return false;
        }

        if (wouldBeBodyOnly.Count > 0)
        {
            var verifierDocuments = wouldBeBodyOnly.SelectMany(projectId => debt[projectId]).ToImmutableHashSet();
            var verifierSlices = await ExtractAsync(basis.Solution, verifierDocuments, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var mismatches = ImmutableHashSet.CreateBuilder<ProjectId>();
            foreach (var projectId in wouldBeBodyOnly)
            {
                foreach (var documentId in debt[projectId])
                {
                    var filePath = basis.Solution.GetDocument(documentId)?.FilePath;
                    if (filePath is null)
                    {
                        throw new InvalidOperationException("Cascade verification cannot classify a debt document with no file path.");
                    }

                    var path = Path.GetFullPath(filePath);
                    if (!verifierSlices.TryGetValue(path, out var fresh))
                    {
                        throw new InvalidOperationException($"Cascade verification returned no file-fact slice for '{path}'.");
                    }
                    var current = CascadeGateVerification.CurrentPathSlice(_baseResult, overlay, path);
                    if (!CascadeGateVerification.Matches(current, fresh))
                    {
                        mismatches.Add(projectId);
                        break;
                    }
                }
            }

            if (mismatches.Count > 0)
            {
                // The extraction was one batch over all would-be skipped debt. Installing all of its
                // path slices is safe; ownership remains per-origin, so unrelated Unknown/Changed debt
                // is retained even when it overlaps a freshly paid path.
                overlay = overlay.SetItems(verifierSlices);
                graphOverlay = graphOverlay.Replace(verifierSlices);
                catalog = catalog
                    .ReplaceEmitters(
                        basis.Solution,
                        verifierSlices.Values.SelectMany(slice => slice.ProjectSurfaces.IsDefault ? [] : slice.ProjectSurfaces)
                    )
                    .MarkGateDisabled(mismatches);
                foreach (var projectId in mismatches)
                {
                    states[projectId] = SurfaceState.Changed;
                }
            }

            foreach (var projectId in wouldBeBodyOnly)
            {
                debt.Remove(projectId);
            }
        }

        var candidate = new FactSnapshot(
            basis.Revision.Next(),
            basis.Solution,
            _baseResult,
            overlay,
            DirtySet.FromContributions(basis.Solution, debt.ToImmutable()),
            basis.Delta with
            {
                SurfaceStates = states.ToImmutable(),
            },
            catalog,
            _graphBase,
            graphOverlay
        );
        cancellationToken.ThrowIfCancellationRequested();
        return ReferenceEquals(Interlocked.CompareExchange(ref _currentSnapshot, candidate, basis), basis);
    }

    // Re-extract classified outstanding cascades. Unknown origins remain disclosed: their source-generator
    // facts may be stale, so a source-document-only coarse pass is not allowed to clear their debt.
    // Returns true only when this exact basis reference was published; false means no payable work or a
    // newer snapshot superseded the candidate.
    public async Task<bool> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var refined = await RefineUnknownSurfacesAsync(projects: null, cancellationToken);
        var basis = CaptureSnapshot();
        if (basis.Dirty.PendingDocuments.Count == 0)
        {
            return refined;
        }

        var payableOrigins = basis
            .Dirty.PendingByOrigin.Keys.Where(projectId =>
                basis.Delta.SurfaceStates.GetValueOrDefault(projectId) != SurfaceState.Unknown
                && basis.Surfaces.Projects.TryGetValue(projectId, out var partition)
                && partition.IsClassifiable
                && partition.RequiresCoarseReconciliation
            )
            .ToImmutableHashSet();
        if (payableOrigins.Count == 0)
        {
            return refined;
        }

        var pending = payableOrigins.SelectMany(projectId => basis.Dirty.PendingByOrigin[projectId]).ToImmutableHashSet();
        var slices = await ExtractAsync(basis.Solution, pending, cancellationToken);
        var graphOverlay = basis.GraphOverlay.Replace(slices);
        var surfaces = basis
            .Surfaces.ReplaceEmitters(basis.Solution, slices.Values.SelectMany(s => s.ProjectSurfaces.IsDefault ? [] : s.ProjectSurfaces))
            .MarkReconciled(payableOrigins);
        var remainingDebt = basis.Dirty.PendingByOrigin.RemoveRange(payableOrigins);
        var remainingStates = basis.Delta.SurfaceStates.RemoveRange(payableOrigins);
        var candidate = new FactSnapshot(
            basis.Revision.Next(),
            basis.Solution,
            _baseResult,
            basis.Overlay.SetItems(slices),
            DirtySet.FromContributions(basis.Solution, remainingDebt),
            new SnapshotDelta(
                slices.Keys.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase),
                pending.Select(d => d.ProjectId).ToImmutableHashSet(),
                ImmutableHashSet<string>.Empty,
                remainingStates
            ),
            surfaces,
            _graphBase,
            graphOverlay
        );

        cancellationToken.ThrowIfCancellationRequested();
        return ReferenceEquals(Interlocked.CompareExchange(ref _currentSnapshot, candidate, basis), basis);
    }

    public void Dispose() => _workspace.Dispose();

    // Re-extract the given documents in ONE batched call. The overlay's replacement grain is the file,
    // and ExtractFromDocumentsByFileAsync returns the batch already partitioned by file path (grouped
    // from the per-SourceModel extraction results, preserving each TypeRelation/Dispatch emitter path).
    // Batching is what makes the cascade affordable: the
    // per-call setup the old one-call-per-path loop re-paid per file (the DI method-name set,
    // XmlDiMiner.Mine, a compilation bind per file) is paid once per reconcile, and extraction runs
    // Parallel across the whole batch. A file linked into several projects still contributes ALL its
    // DocumentIds' emissions to its one slice, mirroring the cold pass where each project context
    // extracts its copy.
    //
    // Cancellation-safe: extraction returns private immutable slices. The caller checks cancellation
    // immediately before its publication CAS, so a cancelled re-extraction changes no current state.
    private async Task<Dictionary<string, FileFacts>> ExtractAsync(
        Solution solution,
        IReadOnlyCollection<DocumentId> documents,
        CancellationToken cancellationToken
    )
    {
        if (documents.Count == 0)
        {
            return new Dictionary<string, FileFacts>(StringComparer.OrdinalIgnoreCase);
        }

        // FileFacts replacement is path-global, so every selected path must be rebound in every linked
        // project context even when only one context belongs to the payable origin. Otherwise replacing
        // Shared.cs(A)'s slice would erase Shared.cs(C)'s still-current emissions.
        var expanded = documents
            .SelectMany(documentId =>
            {
                var path = solution.GetDocument(documentId)?.FilePath;
                return path is null ? [documentId] : DocumentsAtPath(Path.GetFullPath(path));
            })
            .Distinct()
            .ToArray();
        return await _extractFiles(solution, expanded, _solutionPath, _rules, cancellationToken, _interner);
    }

    private IReadOnlyCollection<DocumentId> DocumentsAtPath(string fullPath) =>
        _documentsByPath.TryGetValue(fullPath, out var documents) ? documents : [];

    private static FileFacts EmptyFileFacts() => new([], [], [], [], [], [], [], []);

    internal static AnalysisResult MergeFacts(AnalysisResult baseResult, ImmutableDictionary<string, FileFacts> overlay) =>
        FactSnapshot.MaterializeFacts(baseResult, overlay);
}

// One overlaid file's facts — the overlay's value type, and the per-file slice
// SolutionAnalyzer.ExtractFromDocumentsByFileAsync returns. Slices carry only facts the file itself
// emitted: no XML-mined or rules-static DI registrations (those live on the BASE side only — they are
// appended by ExtractFromSourceSet, which the by-file path deliberately bypasses), and
// TypeRelations/Dispatch are exactly the file's own emissions, with matching emitter FilePath,
// grouped per source before flattening.
internal sealed record FileFacts(
    ImmutableArray<SourceFileInfo> SourceFiles,
    ImmutableArray<DiRegistrationInfo> DiRegistrations,
    ImmutableArray<SymbolFact> Symbols,
    ImmutableArray<ReferenceFact> References,
    ImmutableArray<TypeRelationFact> TypeRelations,
    ImmutableArray<DispatchFact> Dispatch,
    ImmutableArray<AllocationFact> Allocations,
    // This file's compile-health rows: exactly ONE when the re-extraction saw error diagnostics in it,
    // and EMPTY when it did not. Empty is the load-bearing case — it is what clears a base flag on
    // merge when a broken file is fixed.
    ImmutableArray<FileCompileHealth> CompileHealth,
    // One contribution per Roslyn project context for this emitter. Default preserves compatibility
    // with hand-built fixtures, which then deliberately fail closed under the surface gate.
    ImmutableArray<ProjectSurfaceContribution> ProjectSurfaces = default
);

internal delegate Task<Dictionary<string, FileFacts>> ResidentFileExtractor(
    Solution solution,
    IReadOnlyCollection<DocumentId> documents,
    string solutionPath,
    RuleSet rules,
    CancellationToken cancellationToken,
    StringInterner? interner
);
