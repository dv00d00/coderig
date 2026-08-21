using Rig.Domain.Data;

namespace Rig.Cli.CommandLine;

// The stderr footer note for facts extracted from a tree that did not fully compile, and the status-line
// segments that go with it. Joins the existing stderr-notice family (AmbiguityNotice,
// SeedResolutionNotice, EffectDerivation.WriteIntrinsicNote): STDERR so it never corrupts tsv/llm
// parsing on stdout while staying visible to a human on every format.
//
// Why the notice is its own class rather than a line inside a command: it must be emittable from any
// surface that serves facts, including one that loaded no graph at all — the same requirement that
// shaped AmbiguityNotice. Today only the live host (`rig watch`) calls it; the store-backed surfaces are
// the follow-on slice (docs/spikes/failed-compilation-disclosure-spec.md §7), which needs the
// source_files/runs schema columns and a re-index.
//
// Wording rules, each one deliberate (spec §3.2):
//   - "may be MISSING or WRONG", never "is wrong". A file can carry an error while the queried answer is
//     byte-identical to the clean tree; this is a DOUBT marker, and overclaiming makes it a liar in the
//     other direction.
//   - the project-level lines are phrased as RECALL warnings, because a project that produced no facts
//     makes an ABSENCE argument unsound: "no callers" / "unreachable" is not evidence for symbols
//     declared there. That is a strictly worse failure than a doubtful presence, so it gets its own line.
//   - the FILE count is quantified, over exactly the population it names (indexed files). The FACT impact
//     is NOT quantified — an invented number there would be the same defect as the intrinsic-effects
//     count that had to be dropped for overstating by 8x.
//   - no escape-hatch flag is named. `rig files --compile-errors` is specced but NOT implemented in this
//     slice, and a note that teaches a flag which does not exist is worse than one that teaches none.
internal static class CompilationHealthNotice
{
    // The population the "N of M" ratio is taken over: the DISTINCT paths of files this analysis indexed.
    //
    // A SET, not a count, and that is not decoration. On the unrestored MedDBase clone the first cut
    // printed "10648 of 10565 indexed file(s)" — a numerator larger than its denominator — because
    // Roslyn reports diagnostics in files rig did not index (obj/ AssemblyInfo, anything the classifier
    // skipped), while the denominator counted only indexed rows. A ratio whose halves are drawn from
    // different populations is exactly the defect that forced the intrinsic-effects count to be dropped;
    // deriving both halves from ONE set makes it impossible by construction, and the files outside the
    // set are disclosed separately rather than folded in or hidden.
    internal static IReadOnlySet<string> IndexedFileSet(AnalysisResult facts)
    {
        var indexed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in facts.SourceFiles)
        {
            if (string.Equals(file.Status, "indexed", StringComparison.Ordinal))
            {
                indexed.Add(file.FilePath);
            }
        }

        return indexed;
    }

    // The status-line segments: what is TRUE about compile health, quantified. Empty when the tree
    // compiled — the disclosure has to be silent on a healthy tree or it trains the reader to ignore it.
    internal static IReadOnlyList<string> StatusSegments(CompilationHealth? health, IReadOnlySet<string> indexedFiles)
    {
        if (health is null || health.IsClean)
        {
            return [];
        }

        var segments = new List<string>();
        var (flaggedIndexed, flaggedOutside) = Split(health, indexedFiles);
        if (flaggedIndexed > 0)
        {
            var outside = flaggedOutside > 0 ? $" (+{flaggedOutside} outside the indexed set)" : "";
            segments.Add($"{flaggedIndexed} of {indexedFiles.Count} indexed file(s) had compile errors{outside}");
        }
        else if (flaggedOutside > 0)
        {
            segments.Add($"{flaggedOutside} file(s) outside the indexed set had compile errors");
        }

        var noFacts = health.NoFactProjects.Count();
        if (noFacts > 0)
        {
            segments.Add($"{noFacts} project(s) produced NO facts");
        }

        var generatorFailures = health.GeneratorFailures.Count();
        if (generatorFailures > 0)
        {
            segments.Add($"{generatorFailures} project(s) missing generated code");
        }

        if (segments.Count == 0)
        {
            // Only location-less diagnostics: no file and no project can carry them, but staying silent
            // would be the exact failure this note exists to remove.
            segments.Add($"{health.UnlocatedErrorCount} compile error(s) with no source location");
        }

        return segments;
    }

    // The footer note lines, or empty when there is nothing to disclose. UNCONDITIONAL by design: it
    // fires whenever the analysis carries any compile error or any partial project, whether or not the
    // rendered answer happened to mention an affected file. Per-file locality is what a chip gives; this
    // is what gives completeness, and the known blind spots (a lost dispatch edge has no file to flag;
    // location-less project failures have no file rows; Roslyn suppresses some follow-on diagnostics)
    // are covered by nothing else.
    internal static IReadOnlyList<string> Note(CompilationHealth? health, IReadOnlySet<string> indexedFiles)
    {
        if (health is null || health.IsClean)
        {
            return [];
        }

        var lines = new List<string>();
        var (flaggedIndexed, flaggedOutside) = Split(health, indexedFiles);
        if (flaggedIndexed > 0)
        {
            var outside = flaggedOutside > 0 ? $", plus {flaggedOutside} file(s) outside the indexed set" : "";
            lines.Add(
                $"note: these facts come from a tree that did not fully compile — {flaggedIndexed} of {indexedFiles.Count} "
                    + $"indexed file(s) had compile errors{outside} ({health.TotalErrorCount} error diagnostic(s) in total), "
                    + "so facts from them may be MISSING or WRONG."
            );
        }
        else if (flaggedOutside > 0)
        {
            // Errors, but none in a file this analysis indexed. Still disclosed: on an unrestored tree
            // this is Roslyn reporting against obj/ AssemblyInfo files, which is evidence that references
            // resolved nowhere — so it says nothing about which facts are wrong, only that something is.
            lines.Add(
                $"note: these facts come from a tree that did not fully compile — {flaggedOutside} file(s) outside the "
                    + $"indexed set had compile errors ({health.TotalErrorCount} error diagnostic(s) in total), so facts "
                    + "may be MISSING or WRONG."
            );
        }
        else if (health.UnlocatedErrorCount > 0)
        {
            lines.Add(
                $"note: these facts come from a tree that did not fully compile — {health.UnlocatedErrorCount} error "
                    + "diagnostic(s) with no source location, so facts may be MISSING or WRONG."
            );
        }

        var noFacts = health.NoFactProjects.ToArray();
        if (noFacts.Length > 0)
        {
            lines.Add(
                $"note: {noFacts.Length} project(s) produced NO facts at all ({Describe(noFacts)}) — anything declared "
                    + "there is absent from these facts, so \"no callers\" / \"unreachable\" is NOT evidence for those symbols."
            );
        }

        var generatorFailures = health.GeneratorFailures.ToArray();
        if (generatorFailures.Length > 0)
        {
            lines.Add(
                $"note: {generatorFailures.Length} project(s) lost their generated code ({Describe(generatorFailures)}) — "
                    + "generated types are absent from these facts, so \"no callers\" / \"unreachable\" is NOT evidence for "
                    + "symbols declared in generated code."
            );
        }

        return lines;
    }

    // Flagged files that ARE in the indexed set, and flagged files that are not. The second bucket is
    // real and must not be silently dropped — on an unrestored tree it is Roslyn shouting about obj/
    // AssemblyInfo files, which is evidence the tree resolved nothing — but it also cannot be counted
    // against the indexed population without producing a nonsense ratio.
    private static (int Indexed, int Outside) Split(CompilationHealth health, IReadOnlySet<string> indexedFiles)
    {
        var indexed = 0;
        foreach (var file in health.Files)
        {
            if (indexedFiles.Contains(file.FilePath))
            {
                indexed++;
            }
        }

        return (indexed, health.Files.Count - indexed);
    }

    // `Name: reason` per project, capped so a solution-wide failure cannot turn one note into a wall.
    private static string Describe(IReadOnlyList<ProjectCompileFailure> failures)
    {
        const int MaxNamed = 5;
        var named = string.Join(", ", failures.Take(MaxNamed).Select(f => $"{f.ProjectName}: {f.Reason}"));
        return failures.Count > MaxNamed ? $"{named}, +{failures.Count - MaxNamed} more" : named;
    }
}
