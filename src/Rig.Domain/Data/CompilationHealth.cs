// Compile-health provenance for one analysis: WHICH FILES Roslyn reported error diagnostics in, and
// WHICH PROJECTS produced nothing at all. Recorded so a query served from these facts can disclose
// that they came from a tree that did not fully compile, instead of presenting a `0` as "there was no
// code" (docs/backlog/todo/live-index-serves-confident-answers-from-a-broken-compilation.md — a fresh
// unrestored MedDBase clone answered `callers … : 0` under `all projects reconciled` while emitting
// 2,387,334 error lines).
//
// Grain: per FILE, keyed on the file location of Roslyn's OWN error diagnostics, plus a per-PROJECT
// channel for the location-less failure classes. NO propagation to dependents and NO propagation to
// sibling files (docs/spikes/failed-compilation-disclosure-spec.md §4): Roslyn re-reports at every site
// where binding actually failed, so the diagnostic set is already the contamination closure, while
// propagation is measurably fatal (MedDBase: median 6 / p90 68 transitive dependents per project; two
// projects hold 66% of its references — one broken hub file would flag most of the store).
//
// NOT the same thing as a `!:` candidate DocID and must never be derived from one (spec §3.3): `!:`
// means "this one reference did not bind" and is COMMON in a tree that compiles fine (7.3% of files on
// a clean-commit MedDBase store); this record must be EMPTY in a tree that compiles.
namespace Rig.Domain.Data;

// One file's error-diagnostic evidence. Deliberately EVIDENCE, not a measure of damage: Roslyn
// suppresses some follow-on diagnostics once a symbol is an error type, so a file may mis-bind at more
// sites than ErrorCount suggests. Presence is the signal; the count is context.
public sealed record FileCompileHealth(
    string FilePath,
    // Error-severity diagnostics whose primary location is in this file.
    int ErrorCount,
    // Deduped, ordinal-sorted diagnostic ids, capped at 8 then `+N` — e.g. "CS0103,CS0246,+3".
    string ErrorCodes,
    // The lowest-positioned diagnostic's message, capped at ~200 chars.
    string FirstMessage
);

// A project that contributed less than its whole self, for a reason that has NO file location — so the
// per-file channel is structurally blind to it. Reason is one of the tokens below.
public sealed record ProjectCompileFailure(string ProjectName, string Reason)
{
    // The project has no Compilation at all: zero facts, zero source-file rows. The highest-severity
    // class, because zero facts read as "nothing declares or calls this".
    public const string NoCompilation = "no_compilation";

    // A source-generator project's compilation could not be emitted, so its generators never ran and
    // every type they would have generated is absent.
    public const string GeneratorEmit = "generator_emit";

    // The generator driver threw while running: the generated documents are skipped.
    public const string GeneratorRun = "generator_run";
}

public sealed record CompilationHealth(
    // Per-file rows, one per file that had >= 1 error diagnostic. A file with none has NO row — that is
    // what makes the resident overlay's replace-per-file merge clear a flag when a file is fixed.
    IReadOnlyList<FileCompileHealth> Files,
    // The location-less failure classes above.
    IReadOnlyList<ProjectCompileFailure> PartialProjects,
    // Error diagnostics with no source-file location (compilation-level, or reported against a tree the
    // workspace has no document for). Counted so TotalErrorCount stays truthful even though no file can
    // carry them.
    int UnlocatedErrorCount
)
{
    public static readonly CompilationHealth Empty = new([], [], 0);

    public int FileErrorCount
    {
        get
        {
            var total = 0;
            foreach (var file in Files)
            {
                total += file.ErrorCount;
            }

            return total;
        }
    }

    // Every error diagnostic seen, located or not. The number that must still be reported truthfully
    // when the printed DETAIL is capped — a silent truncation reads as "that was all of them".
    public int TotalErrorCount => FileErrorCount + UnlocatedErrorCount;

    public IEnumerable<ProjectCompileFailure> NoFactProjects => PartialProjects.Where(p => p.Reason == ProjectCompileFailure.NoCompilation);

    public IEnumerable<ProjectCompileFailure> GeneratorFailures =>
        PartialProjects.Where(p => p.Reason is ProjectCompileFailure.GeneratorEmit or ProjectCompileFailure.GeneratorRun);

    // Nothing to disclose. Checked rather than inferred from a count so a future failure class cannot
    // be added without deciding whether it makes the analysis unhealthy.
    public bool IsClean => Files.Count == 0 && PartialProjects.Count == 0 && UnlocatedErrorCount == 0;
}
