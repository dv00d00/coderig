using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Rig.Analysis.Extraction;
using Rig.Domain.Data;
using RuleSet = Rig.Domain.Data.RuleSet;

namespace Rig.Analysis.Inventory;

// The converging overlay for the resident index (live-background-index slice 3). Holds the retained
// RigWorkspace from the cold load, the BASE AnalysisResult that load produced, and a per-FILE overlay
// of re-extracted facts. On an edit: the edited file is re-extracted IMMEDIATELY (the eager arm —
// correct for its own binding by construction, since the retained Solution holds every dependency as
// live source), while the sound cascade (the changed project plus all transitive MSBuild-reference
// dependents) is recorded as UNRECONCILED and re-extracted by ReconcileAsync. Between the two,
// CurrentFacts is servable and UnreconciledProjects is the disclosure a caller must surface — cascade
// latency becomes a disclosure problem, not an answer-blocking one.
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

    // Host-lifetime string interner shared by every re-extraction: a re-extracted generation's retained
    // strings alias the previous generation's instead of duplicating the whole string set per edit (the
    // measured duplication is ~0.9 GB per full-cascade generation on MedDBase). Pass the SAME instance
    // the cold boot used (WatchCommand does) so overlay strings also alias the BASE facts'. The table
    // only grows — bounded by the union of values ever seen — which is the deliberate trade: dropping it
    // per generation would forfeit exactly the cross-generation sharing it exists for.
    private readonly StringInterner? _interner;

    // FilePath -> that file's latest re-extracted facts. OrdinalIgnoreCase: Windows paths, and the
    // loader itself sorts sources OrdinalIgnoreCase.
    private readonly Dictionary<string, FileFacts> _overlay = new(StringComparer.OrdinalIgnoreCase);

    // Documents owed to the converging cascade but not yet re-extracted since the edit that dirtied
    // them. Eagerly re-extracted documents are removed: their facts are current w.r.t. the newest
    // Solution snapshot.
    private readonly HashSet<DocumentId> _pendingDocuments = [];

    private AnalysisResult? _mergedFacts;

    public ResidentIndex(
        RigWorkspace workspace,
        AnalysisResult baseResult,
        string solutionPath,
        RuleSet rules,
        IDirtySetPolicy? eagerPolicy = null,
        IDirtySetPolicy? cascadePolicy = null,
        StringInterner? interner = null
    )
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _baseResult = baseResult ?? throw new ArgumentNullException(nameof(baseResult));
        _solutionPath = solutionPath ?? throw new ArgumentNullException(nameof(solutionPath));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _eagerPolicy = eagerPolicy ?? new ChangedFilesOnlyPolicy();
        _cascadePolicy = cascadePolicy ?? new ProjectCascadePolicy();
        _interner = interner ?? StringInterner.CreateDefault();
    }

    public Solution CurrentSolution => _workspace.CurrentSolution;

    // The disclosure: projects whose documents are owed to the cascade but not yet re-extracted.
    // Rendering this is NOT this class's job — a caller surfaces it alongside any answer served from
    // CurrentFacts while it is non-empty.
    public IReadOnlyCollection<string> UnreconciledProjects
    {
        get
        {
            var solution = CurrentSolution;
            return _pendingDocuments
                .Select(d => solution.GetProject(d.ProjectId)?.Name)
                .Where(name => name is not null)
                .Select(name => name!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }
    }

    // Base facts with every overlaid file's facts REPLACED (never appended). Recomputed lazily after
    // any edit/reconcile; callers get a consistent snapshot.
    public AnalysisResult CurrentFacts => _mergedFacts ??= MergeFacts();

    // Apply one file edit: mutate the retained workspace, re-extract the edited file at once (eager
    // arm), and record the outstanding sound cascade as unreconciled.
    public async Task ApplyEditAsync(string filePath, SourceText newText, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newText);
        var fullPath = Path.GetFullPath(filePath);
        var documentIds = CurrentSolution.GetDocumentIdsWithFilePath(fullPath);
        if (documentIds.IsEmpty)
        {
            throw new ArgumentException($"No document in the retained workspace has the path '{fullPath}'.", nameof(filePath));
        }

        foreach (var documentId in documentIds)
        {
            _workspace.ChangeDocumentText(documentId, newText);
        }

        string[] changed = [fullPath];
        var eager = _eagerPolicy.DocumentsToReextract(CurrentSolution, changed);
        await ReextractAsync(eager, cancellationToken);

        // The cascade the eager arm did NOT cover is owed; the eagerly covered documents are current
        // against the newest snapshot, so they are also settled for any PREVIOUS edit's cascade.
        var eagerSet = new HashSet<DocumentId>(eager);
        foreach (var documentId in _cascadePolicy.DocumentsToReextract(CurrentSolution, changed))
        {
            if (!eagerSet.Contains(documentId))
            {
                _pendingDocuments.Add(documentId);
            }
        }

        _pendingDocuments.ExceptWith(eagerSet);
        _mergedFacts = null;
    }

    // Re-extract the outstanding cascade and clear the disclosure. Plain awaitable — the host owns
    // scheduling/backgrounding (a later slice).
    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        if (_pendingDocuments.Count == 0)
        {
            return;
        }

        var pending = _pendingDocuments.ToArray();
        await ReextractAsync(pending, cancellationToken);
        _pendingDocuments.Clear();
        _mergedFacts = null;
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
    // Cancellation-safe: the overlay is written only AFTER the whole batch has extracted, so a
    // cancelled re-extraction leaves the overlay (and _pendingDocuments, cleared by the caller only on
    // completion) untouched.
    private async Task ReextractAsync(IReadOnlyCollection<DocumentId> documents, CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
        {
            return;
        }

        var slices = await SolutionAnalyzer.ExtractFromDocumentsByFileAsync(
            solution: CurrentSolution,
            documents: documents,
            solutionPath: _solutionPath,
            rules: _rules,
            cancellationToken: cancellationToken,
            interner: _interner
        );

        foreach (var (filePath, facts) in slices)
        {
            _overlay[filePath] = facts;
        }
    }

    private AnalysisResult MergeFacts()
    {
        if (_overlay.Count == 0)
        {
            return _baseResult;
        }

        bool Overlaid(string path) => _overlay.ContainsKey(path);
        var overlayEntries = _overlay.Values.ToArray();

        // --- Path-carrying kinds: base rows for non-overlaid files + the overlay's rows. REPLACE, never
        // append. DI registrations whose FilePath is empty or non-.cs (static rules mappings, XML-mined
        // ones) live on the BASE side only — FileFacts.From filters them out of overlay entries, and
        // their paths are never overlay keys, so they pass through the base filter exactly once.
        var sourceFiles = _baseResult
            .SourceFiles.Where(f => !Overlaid(f.FilePath))
            .Concat(overlayEntries.SelectMany(e => e.SourceFiles))
            .ToArray();
        var diRegistrations = _baseResult
            .DiRegistrations.Where(r => r.FilePath.Length == 0 || !Overlaid(r.FilePath))
            .Concat(overlayEntries.SelectMany(e => e.DiRegistrations))
            .ToArray();
        var symbols = (_baseResult.Symbols ?? [])
            .Where(s => !Overlaid(s.FilePath))
            .Concat(overlayEntries.SelectMany(e => e.Symbols))
            .ToArray();
        var references = (_baseResult.References ?? [])
            .Where(r => !Overlaid(r.FilePath))
            .Concat(overlayEntries.SelectMany(e => e.References))
            .ToArray();
        var allocations = (_baseResult.AllocationFacts ?? [])
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
        var baseHealth = _baseResult.CompilationHealth ?? CompilationHealth.Empty;
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
        var typeRelations = (_baseResult.TypeRelations ?? [])
            .Where(r => !Overlaid(r.FilePath))
            .Concat(overlayEntries.SelectMany(e => e.TypeRelations))
            .ToArray();
        var dispatchFacts = (_baseResult.DispatchFacts ?? [])
            .Where(d => !Overlaid(d.FilePath))
            .Concat(overlayEntries.SelectMany(e => e.Dispatch))
            .ToArray();

        return _baseResult with
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
    IReadOnlyList<SourceFileInfo> SourceFiles,
    IReadOnlyList<DiRegistrationInfo> DiRegistrations,
    IReadOnlyList<SymbolFact> Symbols,
    IReadOnlyList<ReferenceFact> References,
    IReadOnlyList<TypeRelationFact> TypeRelations,
    IReadOnlyList<DispatchFact> Dispatch,
    IReadOnlyList<AllocationFact> Allocations,
    // This file's compile-health rows: exactly ONE when the re-extraction saw error diagnostics in it,
    // and EMPTY when it did not. Empty is the load-bearing case — it is what clears a base flag on
    // merge when a broken file is fixed.
    IReadOnlyList<FileCompileHealth> CompileHealth
);
