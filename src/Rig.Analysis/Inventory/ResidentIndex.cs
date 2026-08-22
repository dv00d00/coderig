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
        ResidentFileExtractor? extractFiles = null
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
        _currentSnapshot = new FactSnapshot(
            new FactRevision(0),
            workspace.CurrentSolution,
            _baseResult,
            ImmutableDictionary.Create<string, FileFacts>(StringComparer.OrdinalIgnoreCase),
            DirtySet.Empty,
            SnapshotDelta.Empty
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

    // Base facts with every overlaid file's facts REPLACED (never appended). Recomputed lazily after
    // any edit/reconcile; callers get a consistent snapshot.
    public AnalysisResult CurrentFacts => CaptureSnapshot().FlattenedFacts;

    // Apply one file edit by building a complete candidate from the captured generation. Neither the
    // retained workspace nor the published overlay/dirty state moves until the final reference-CAS.
    public async Task ApplyEditAsync(string filePath, SourceText newText, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newText);
        var basis = CaptureSnapshot();
        var fullPath = Path.GetFullPath(filePath);
        var documentIds = basis.Solution.GetDocumentIdsWithFilePath(fullPath);
        if (documentIds.IsEmpty)
        {
            throw new ArgumentException($"No document in the retained workspace has the path '{fullPath}'.", nameof(filePath));
        }

        var solution = basis.Solution;
        foreach (var documentId in documentIds)
        {
            solution = solution.WithDocumentText(documentId, newText, PreservationMode.PreserveValue);
        }

        string[] changed = [fullPath];
        var eager = _eagerPolicy.DocumentsToReextract(solution, changed);
        var slices = await ExtractAsync(solution, eager, cancellationToken);
        var overlay = basis.Overlay.SetItems(slices);

        // The cascade the eager arm did NOT cover is owed; the eagerly covered documents are current
        // against the newest snapshot, so they are also settled for any PREVIOUS edit's cascade.
        var eagerSet = eager.ToImmutableHashSet();
        var cascade = _cascadePolicy.DocumentsToReextract(solution, changed);
        var pending = basis.Dirty.PendingDocuments.ToBuilder();
        foreach (var documentId in cascade)
        {
            if (!eagerSet.Contains(documentId))
            {
                pending.Add(documentId);
            }
        }

        pending.ExceptWith(eagerSet);
        var candidate = new FactSnapshot(
            basis.Revision.Next(),
            solution,
            _baseResult,
            overlay,
            DirtySet.From(solution, pending),
            new SnapshotDelta(
                slices.Keys.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase),
                cascade.Select(d => d.ProjectId).ToImmutableHashSet(),
                ImmutableHashSet<string>.Empty,
                SurfaceState.Unknown
            )
        );

        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(Interlocked.CompareExchange(ref _currentSnapshot, candidate, basis), basis))
        {
            throw new InvalidOperationException("The resident edit was superseded by a newer fact snapshot.");
        }
    }

    // Re-extract the outstanding cascade and clear the disclosure. Returns true only when this exact
    // basis reference was published; false means no work or a newer snapshot superseded the candidate.
    public async Task<bool> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var basis = CaptureSnapshot();
        if (basis.Dirty.PendingDocuments.Count == 0)
        {
            return false;
        }

        var pending = basis.Dirty.PendingDocuments;
        var slices = await ExtractAsync(basis.Solution, pending, cancellationToken);
        var candidate = new FactSnapshot(
            basis.Revision.Next(),
            basis.Solution,
            _baseResult,
            basis.Overlay.SetItems(slices),
            DirtySet.Empty,
            new SnapshotDelta(
                slices.Keys.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase),
                pending.Select(d => d.ProjectId).ToImmutableHashSet(),
                ImmutableHashSet<string>.Empty,
                SurfaceState.Unknown
            )
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

        return await _extractFiles(solution, documents, _solutionPath, _rules, cancellationToken, _interner);
    }

    internal static AnalysisResult MergeFacts(AnalysisResult baseResult, ImmutableDictionary<string, FileFacts> overlay)
    {
        if (overlay.Count == 0)
        {
            return baseResult;
        }

        bool Overlaid(string path) => overlay.ContainsKey(path);
        var overlayEntries = overlay.Values.ToArray();

        // --- Path-carrying kinds: base rows for non-overlaid files + the overlay's rows. REPLACE, never
        // append. DI registrations whose FilePath is empty or non-.cs (static rules mappings, XML-mined
        // ones) live on the BASE side only — FileFacts.From filters them out of overlay entries, and
        // their paths are never overlay keys, so they pass through the base filter exactly once.
        var sourceFiles = baseResult
            .SourceFiles.Where(f => !Overlaid(f.FilePath))
            .Concat(overlayEntries.SelectMany(e => e.SourceFiles))
            .ToArray();
        var diRegistrations = baseResult
            .DiRegistrations.Where(r => r.FilePath.Length == 0 || !Overlaid(r.FilePath))
            .Concat(overlayEntries.SelectMany(e => e.DiRegistrations))
            .ToArray();
        var symbols = (baseResult.Symbols ?? [])
            .Where(s => !Overlaid(s.FilePath))
            .Concat(overlayEntries.SelectMany(e => e.Symbols))
            .ToArray();
        var references = (baseResult.References ?? [])
            .Where(r => !Overlaid(r.FilePath))
            .Concat(overlayEntries.SelectMany(e => e.References))
            .ToArray();
        var allocations = (baseResult.AllocationFacts ?? [])
            .Where(a => !Overlaid(a.FilePath))
            .Concat(overlayEntries.SelectMany(e => e.Allocations))
            .ToArray();

        // --- Compile health, merged by exactly the same replace-per-file rule as the facts above, and
        // for exactly the same reason: a health row is a per-file fact, and a STALE one is a ghost. In a
        // resident process the stale direction is the dangerous one — a flag that only ever accumulates
        // would claim a fixed file is still broken for the whole process lifetime, and a marker that
        // lies "safe" trains the reader to ignore it. So a re-extracted file's rows come from the
        // overlay ONLY: it contributes one row if it still has errors and NONE if it is clean, which is
        // what makes broken -> fixed -> broken track the real state.
        //
        // PartialProjects and UnlocatedErrorCount pass through from the base. Both are project- and
        // compilation-level (no compilation at all, a generator that never ran, a diagnostic with no
        // source location), so no per-FILE re-extraction can either produce or retire one — that takes a
        // fresh cold load, which is exactly what a new base result is.
        var baseHealth = baseResult.CompilationHealth ?? CompilationHealth.Empty;
        var compilationHealth = baseHealth with
        {
            Files = baseHealth
                .Files.Where(f => !Overlaid(f.FilePath))
                .Concat(overlayEntries.SelectMany(e => e.CompileHealth))
                .OrderBy(f => f.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };

        // --- TypeRelations/Dispatch are provenance-bearing FACT EMISSIONS, so keep every surviving
        // per-file row. Several files may legitimately emit the same semantic edge (partial types,
        // inherited implementations); collapsing here would discard the second ownership key and a
        // later edit could no longer retire the emitters independently. Graph projections erase
        // FilePath and dedupe on semantic edge identity at their own boundary.
        var typeRelations = (baseResult.TypeRelations ?? [])
            .Where(r => !Overlaid(r.FilePath))
            .Concat(overlayEntries.SelectMany(e => e.TypeRelations))
            .ToArray();
        var dispatchFacts = (baseResult.DispatchFacts ?? [])
            .Where(d => !Overlaid(d.FilePath))
            .Concat(overlayEntries.SelectMany(e => e.Dispatch))
            .ToArray();

        return baseResult with
        {
            SourceFiles = sourceFiles,
            DiRegistrations = diRegistrations,
            Symbols = symbols,
            References = references,
            TypeRelations = typeRelations,
            DispatchFacts = dispatchFacts,
            AllocationFacts = allocations,
            CompilationHealth = compilationHealth,
        };
    }
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
    ImmutableArray<FileCompileHealth> CompileHealth
);

internal delegate Task<Dictionary<string, FileFacts>> ResidentFileExtractor(
    Solution solution,
    IReadOnlyCollection<DocumentId> documents,
    string solutionPath,
    RuleSet rules,
    CancellationToken cancellationToken,
    StringInterner? interner
);
