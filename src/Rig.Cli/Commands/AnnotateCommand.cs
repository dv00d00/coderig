using System.CommandLine;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Rig.Analysis.Rules;
using Rig.Cli.CommandLine;
using Rig.Cli.Rendering;
using Rig.Cli.Services;
using Rig.Cli.Telemetry;
using Rig.Domain.Functions;
using Rig.Storage.Queries;
using static Rig.Cli.Graph.TraversalGraphLoader;
using static Rig.Cli.Rendering.SymbolNameFormatter;

namespace Rig.Cli.Commands;

// `rig annotate <file>` — the file lens as TEXT: the indexed source with each effectful method and each
// marked call site annotated in place. The web lens answers the same question in a browser; an agent cannot
// use a browser, and `rig show` prints one declaration with no effect information at all, so reading a file
// meant either losing the annotations or losing the code.
//
// Same substrate as the browser lens (FileEffectsQueryService), so the two cannot disagree. A local `rig
// serve` can return its warm artifact over HTTP; otherwise the command builds the identical artifact cold.
//
// Two honest limits are printed rather than hidden: extraction mines LINES, not columns, so several calls on
// one line collapse into that line's badge list; and a badge says how far away the nearest effect is, not how
// many there are.
internal static class AnnotateCommand
{
    // A whole file is the point of this command, so the cap is high; it exists only so an agent that
    // annotates a 20k-line generated file gets a disclosed truncation instead of a flooded transcript.
    private const int DefaultMaxLines = 2_000;

    internal static Command Build(TextWriter output, TextWriter error, string workingDirectory)
    {
        var file = new Argument<string>("file")
        {
            Description = "Indexed C# file: a full path, or any substring that matches exactly one indexed file.",
        };
        var from = new Option<int>("--from") { Description = "First source line to render (default 1).", DefaultValueFactory = _ => 1 };
        // Nullable is intentional: an omitted bound means EOF, while 0 is a real parser value and must not
        // silently collapse the requested window to one line.
        var to = new Option<int?>("--to") { Description = "Last source line to render (default: to the end, subject to --limit)." };
        var method = new Option<string?>("--method")
        {
            Description = "Render only the declaration spans of methods whose name or DocID contains this (repeatable windows).",
        };
        var summary = new Option<bool>("--summary") { Description = "Print only the per-method effect table — no source lines." };
        var limit = CommonOptions.Limit(DefaultMaxLines);
        var format = CommonOptions.Format();
        var time = CommonOptions.Time();
        var store = CommonOptions.Store();
        var host = new Option<string?>("--host") { Description = "Use a local rig serve URL for resident file-effects queries." };
        var cold = new Option<bool>("--cold") { Description = "Force the existing in-process store query; do not contact rig serve." };

        // The filter set. Every one of these is a PREDICATE OVER THE PROJECTION, never a change to the
        // derivation — the reverse closure stays keyed on (store, rules, schema) and is shared by every
        // filtered view, warm or cold, so combinations cost nothing and none of them can move a badge's
        // number. `--intrinsic` and `--async` are deliberately NOT here: both would change the closure.
        var only = CommonOptions.Only();
        var exclude = CommonOptions.Exclude();
        var minDepth = new Option<int?>("--min-depth")
        {
            Description = "Keep only badges at least this many calls away (--min-depth 1 hides effects in the body itself).",
        };
        var maxDepth = new Option<int?>("--max-depth", "--maxdepth")
        {
            Description = "Keep only badges within this distance. Exact: nearest-depth is monotone, so this equals bounding the walk.",
        };
        var direct = new Option<bool>("--direct") { Description = "Keep only effects performed in the body itself (depth 0)." };
        var looped = new Option<bool>("--looped")
        {
            Description = "Keep only effects that run once per loop iteration (the amplification tier, marked `*`).",
        };
        var noDispatch = new Option<bool>("--no-dispatch")
        {
            Description =
                "Drop badges whose reach exists only through a dispatch hop with several candidate implementations (the ones marked `?`).",
        };
        var cmd = new Command(
            name: "annotate",
            description: "Print an indexed file's source annotated with the effects each method and call site reaches."
        )
        {
            file,
            from,
            to,
            method,
            summary,
            limit,
            format,
            time,
            store,
            host,
            cold,
            only,
            exclude,
            minDepth,
            maxDepth,
            direct,
            looped,
            noDispatch,
        };
        cmd.SetAction(pr =>
            CommandGuard.RunGuardedAsync(
                workingDirectory,
                error,
                () =>
                    RunAsync(
                        new Options(
                            File: pr.GetValue(file)!,
                            From: pr.GetValue(from),
                            To: pr.GetValue(to),
                            Method: pr.GetValue(method),
                            Summary: pr.GetValue(summary),
                            Limit: pr.GetValue(limit),
                            Format: pr.GetValue(format),
                            Time: pr.GetValue(time),
                            Host: pr.GetValue(host),
                            Cold: pr.GetValue(cold),
                            Only: pr.GetValue(only) ?? [],
                            Exclude: pr.GetValue(exclude) ?? [],
                            MinDepth: pr.GetValue(minDepth),
                            MaxDepth: pr.GetValue(maxDepth),
                            Direct: pr.GetValue(direct),
                            Looped: pr.GetValue(looped),
                            NoDispatch: pr.GetValue(noDispatch)
                        ),
                        new CommandIo(
                            new TextOutput(Output: output, Error: error),
                            new WorkspaceLocation(WorkingDirectory: workingDirectory, StoreRef: pr.GetValue(store))
                        )
                    )
            )
        );
        return cmd;
    }

    private sealed record Options(
        string File,
        int From,
        int? To,
        string? Method,
        bool Summary,
        int Limit,
        string? Format,
        bool Time,
        string? Host,
        bool Cold,
        IReadOnlyList<string> Only,
        IReadOnlyList<string> Exclude,
        int? MinDepth,
        int? MaxDepth,
        bool Direct,
        bool Looped,
        bool NoDispatch
    );

    private static async Task<int> RunAsync(Options opts, CommandIo io)
    {
        var tsv = CommonOptions.IsTsv(opts.Format);
        var output = io.TextOutput.Output;
        using var timing = QueryTiming.Start(opts.Time, io.TextOutput.Error);

        // Reject a contradictory range before opening the store or resolving the requested file. Apart from
        // producing the useful error, this keeps a typo from paying the whole solution-wide effects warm-up.
        if (opts.To is { } to && to < opts.From)
        {
            io.TextOutput.Error.WriteLine($"Invalid source range: --from {opts.From} is greater than --to {opts.To}.");
            return 1;
        }

        var resolveWatch = Stopwatch.StartNew();
        var resolved = await ResolveFileAsync(io.WorkspaceLocation, opts.File);
        resolveWatch.Stop();
        timing.Record("file lookup", resolveWatch.Elapsed);
        if (resolved.Candidates is { Count: > 1 })
        {
            io.TextOutput.Error.WriteLine($"'{opts.File}' matches {resolved.Candidates.Count} indexed files. Narrow the path:");
            foreach (var candidate in resolved.Candidates.Take(10))
            {
                io.TextOutput.Error.WriteLine($"{Indent.L1}{candidate}");
            }

            return 1;
        }

        if (resolved.FilePath is null)
        {
            io.TextOutput.Error.WriteLine($"No indexed source file matches '{opts.File}'.");
            return 1;
        }

        var filePath = resolved.FilePath;
        FileEffectsQueryService.Artifact? artifact = null;
        var transportWatch = Stopwatch.StartNew();
        if (!opts.Cold)
        {
            var resident = await AnnotateResidentTransport.TryGetAsync(io.WorkspaceLocation, filePath, opts.Host);
            artifact = resident.Artifact;
            if (artifact is not null)
            {
                io.TextOutput.Error.WriteLine($"transport: rig serve {resident.Url}");
            }
            else if (resident.Failure is not null)
            {
                io.TextOutput.Error.WriteLine($"(resident annotate unavailable: {resident.Failure}; falling back to cold.)");
            }
        }

        transportWatch.Stop();
        timing.Record("transport", transportWatch.Elapsed);
        if (artifact is null)
        {
            io.TextOutput.Error.WriteLine(opts.Cold ? "transport: cold (--cold)" : "transport: cold (start `rig serve` for warm calls)");
            var effectWatch = Stopwatch.StartNew();
            artifact = await FileEffectsQueryService.BuildAsync(
                io.WorkspaceLocation.WorkingDirectory,
                filePath,
                io.WorkspaceLocation.StoreRef
            );
            effectWatch.Stop();
            timing.Record("file effects", effectWatch.Elapsed);
        }

        // Token resolution needs the rule set (provider -> family), and nothing else here does, so it is
        // loaded only when a token was actually typed. Both transports resolve identically: the filter is a
        // predicate over the projection, so a warm and a cold call must produce the same filtered view.
        var filterNotes = new List<string>();
        var onlyFamilies = FileEffectFilterTokens.Resolution.Empty;
        var excludeFamilies = FileEffectFilterTokens.Resolution.Empty;
        if (opts.Only.Count > 0 || opts.Exclude.Count > 0)
        {
            var rules = RuleSetLoader.Load(io.WorkspaceLocation.WorkingDirectory, extraRules: [], loadedPaths: out _);
            onlyFamilies = FileEffectFilterTokens.Resolve(rules, opts.Only, "--only");
            excludeFamilies = FileEffectFilterTokens.Resolve(rules, opts.Exclude, "--exclude");
            filterNotes.AddRange(onlyFamilies.Notes);
            filterNotes.AddRange(excludeFamilies.Notes);
        }

        var filter = new FileEffectLens.LensFilter(
            // Null when the flag was absent; the resolved set (possibly empty) when it was typed.
            Only: opts.Only.Count > 0 ? onlyFamilies.Families : null,
            Exclude: opts.Exclude.Count > 0 ? excludeFamilies.Families : null,
            MinDepth: opts.MinDepth,
            MaxDepth: opts.MaxDepth,
            DirectOnly: opts.Direct,
            LoopedOnly: opts.Looped,
            HideDispatchOnly: opts.NoDispatch
        );

        // Ordering, per-line merging and the direct/distant distinction all come from the shared lens, so
        // this command cannot drift from the browser view or, later, the editor.
        var lens = FileEffectLens.Project(artifact, filter);
        foreach (var note in filterNotes)
        {
            io.TextOutput.Error.WriteLine(note);
        }
        var windowSelection = SelectWindows(opts, artifact, lens);
        if (windowSelection.Error is not null)
        {
            io.TextOutput.Error.WriteLine(windowSelection.Error);
            return 1;
        }

        if (opts.Summary)
        {
            RenderSummary(output, lens, tsv);
            return 0;
        }

        // Source provenance is a property of the STORE (per-commit) exactly as in `rig show`: the working tree
        // is read only when it provably IS the indexed revision, else the blob comes from git, else nothing.
        await using var context = await OpenReadContextGatedAsync(io.WorkspaceLocation);
        var runs = await Reads.ListRunsAsync(context);
        var run = runs.FirstOrDefault(candidate => candidate.SourceCommit is not null) ?? runs.FirstOrDefault();
        var renderer = new SourceRenderer(storeCommit: run?.SourceCommit, storeDirty: run?.SourceDirty ?? false);

        var renderWatch = Stopwatch.StartNew();
        if (!tsv)
        {
            RenderHeader(output, lens);
        }

        var first = true;
        foreach (var window in windowSelection.Windows)
        {
            var snippet = renderer.Resolve(filePath, startLine: window.From, endLine: window.To, maxLines: opts.Limit);
            if (tsv)
            {
                WriteTsv(output, lens, snippet);
                continue;
            }

            if (!first)
            {
                output.WriteLine();
            }

            first = false;
            RenderWindow(output, renderer, snippet, lens);
        }

        renderWatch.Stop();
        timing.Record("render", renderWatch.Elapsed);
        if (!tsv)
        {
            RenderFooter(output, lens);
        }

        return 0;
    }

    private sealed record WindowSelection(IReadOnlyList<(int From, int To)> Windows, string? Error = null);

    // Whether any rendered badge is dispatch-derived. The legend line is printed only then, so a file whose
    // every badge is a real call path carries no caveat a reader has to discount.
    private static bool AnyDispatchOnly(FileEffectLens.LensModel lens) =>
        lens
            .Methods.SelectMany(method => method.Badges)
            .Concat(lens.Lines.SelectMany(line => line.Badges))
            .Any(badge => badge.ViaDispatchOnly);

    // Same rule for the amplification mark: the legend line appears only in a file that has one, so the
    // vocabulary a reader must hold grows with what is actually on screen.
    private static bool AnyLooped(FileEffectLens.LensModel lens) =>
        lens.Methods.SelectMany(method => method.Badges).Concat(lens.Lines.SelectMany(line => line.Badges)).Any(badge => badge.Looped);

    // The line windows to render: --method spans when asked for, otherwise the single --from/--to range.
    // Diagnosis deliberately joins both halves of the shared artifact: Artifact.Methods is every declared
    // canonical method, while lens.Methods is the effectful subset rendered by web/editor/CLI alike.
    private static WindowSelection SelectWindows(Options opts, FileEffectsQueryService.Artifact artifact, FileEffectLens.LensModel lens)
    {
        if (string.IsNullOrWhiteSpace(opts.Method))
        {
            return new WindowSelection([(Math.Max(1, opts.From), opts.To ?? int.MaxValue)]);
        }

        var pattern = opts.Method.Trim();
        var declared = artifact
            .Methods.Values.Where(row =>
                row.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase)
                || row.Id.Contains(pattern, StringComparison.OrdinalIgnoreCase)
            )
            .OrderBy(row => row.Line)
            .ThenBy(row => row.Id, StringComparer.Ordinal)
            .ToArray();
        if (declared.Length == 0)
        {
            var candidates = artifact
                .Methods.Values.Select(row => row.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var candidateText = candidates.Length == 0 ? "(none)" : string.Join(", ", candidates);
            return new WindowSelection([], $"No declared method matches --method '{pattern}'. Declared candidates: {candidateText}.");
        }

        var declaredIds = declared.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        var matched = lens
            .Methods.Where(row => declaredIds.Contains(row.SymbolId))
            .Where(row =>
                row.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase)
                || row.SymbolId.Contains(pattern, StringComparison.OrdinalIgnoreCase)
            )
            .Where(row => row.Line > 0)
            .OrderBy(row => row.Line)
            .Select(row => (From: row.Line, To: row.EndLine > row.Line ? row.EndLine : row.Line))
            .ToArray();
        if (matched.Length == 0)
        {
            var names = string.Join(", ", declared.Select(row => row.Name).Distinct(StringComparer.Ordinal));
            return new WindowSelection(
                [],
                $"Declared method(s) matching --method '{pattern}' ({names}) have no effects in this store; use --from/--to to render them anyway."
            );
        }

        return new WindowSelection(matched);
    }

    // Pure text seams are internal so rendering-contract tests do not need a repository/store fixture. The
    // command remains orchestration; semantic family coverage still comes exclusively from FileEffectLens.
    internal static void RenderHeader(TextWriter output, FileEffectLens.LensModel lens)
    {
        output.WriteLine(
            $"{Path.GetFileName(lens.FilePath)}  {lens.Methods.Count} effectful method declaration(s), {lens.Lines.Count} distinct marked source line(s)"
        );
        output.WriteLine($"{Indent.L1}{lens.FilePath}");
        if (lens.PresentFamilies.Count > 0 || lens.RequestedFamilies.Count > 0)
        {
            var present = lens.PresentFamilies.Count == 0 ? "(none)" : string.Join(" ", lens.PresentFamilies);
            var absent =
                lens.AbsentRequestedFamilies.Count == 0
                    ? ""
                    : $"  (requested but absent: {string.Join(" ", lens.AbsentRequestedFamilies)})";
            output.WriteLine($"{Indent.L1}families present: {present}{absent}");
        }

        // A filtered view and a quiet file render identically, so the count of what was removed is part of the
        // output rather than something the reader has to remember typing. It goes in the HEADER, above the
        // data: `--summary` renders no footer, and a caveat that arrives after the table has already been read
        // is not a caveat.
        if (lens.Disclosure is { Active: true } filtered)
        {
            output.WriteLine(
                filtered.HidSomething
                    ? $"{Indent.L1}FILTERED: {filtered.HiddenBadges} badge(s) hidden, {filtered.HiddenMethods} method row(s) and {filtered.HiddenLines} line(s) dropped — this is NOT the whole file"
                    : $"{Indent.L1}FILTERED: the filter removed nothing — this is the whole file"
            );
        }

        output.WriteLine();
    }

    internal static void RenderFooter(TextWriter output, FileEffectLens.LensModel lens)
    {
        output.WriteLine();
        output.WriteLine($"{Indent.L1}line precision only — several calls on one line share that line's badges");
        output.WriteLine(
            $"{Indent.L1}distances: method badges start at the method; line badges start at the target/direct site — a visible in-solution call normally adds 1 at method grain"
        );
        if (!lens.ColumnsAvailable)
        {
            output.WriteLine($"{Indent.L1}no column facts — a badge marks the LINE, not the expression");
        }

        if (AnyDispatchOnly(lens))
        {
            output.WriteLine(
                $"{Indent.L1}`?` = that reach exists only through a dispatch hop with several candidate implementations — it MAY not be a real call"
            );
        }

        if (AnyLooped(lens))
        {
            output.WriteLine(
                $"{Indent.L1}`*` = that effect is inside a loop here — it runs once per ITERATION, not once per call (only ever on a `!` badge)"
            );
        }
    }

    private static void RenderSummary(TextWriter output, FileEffectLens.LensModel lens, bool tsv)
    {
        if (tsv)
        {
            WriteFactRows(output, lens);
            return;
        }

        RenderHeader(output, lens);
        foreach (var method in lens.Methods)
        {
            output.WriteLine($"{Indent.L1}{method.Line, 6}  {method.Name}  {FileEffectLens.LabelLine(method.Badges)}");
        }
    }

    // The annotated slice. A method declaration line gets a `@` header above it so the method's own summary
    // is readable without scrolling to the sidebar the browser lens has; every marked line carries its
    // badges in the gutter, left of the line number.
    private static void RenderWindow(TextWriter output, SourceRenderer renderer, SourceSnippet snippet, FileEffectLens.LensModel lens)
    {
        if (!snippet.HasText)
        {
            output.WriteLine($"{Indent.L1}(no source: {snippet.Reason})");
            return;
        }

        var headerByLine = lens
            .Methods.Where(method => method.Line > 0)
            .GroupBy(method => method.Line)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var labelByLine = lens.Lines.ToDictionary(line => line.Line, line => FileEffectLens.LabelLine(line.Badges));
        var gutter = Math.Max(6, labelByLine.Count == 0 ? 6 : labelByLine.Values.Max(label => label.Length));

        foreach (var line in snippet.Lines)
        {
            if (headerByLine.TryGetValue(line.Number, out var headers))
            {
                foreach (var header in headers)
                {
                    output.WriteLine($"{Indent.L1}{new string(' ', gutter)}  @ {header.Name}  {FileEffectLens.LabelLine(header.Badges)}");
                }
            }

            var badges = labelByLine.GetValueOrDefault(line.Number, "");
            output.WriteLine($"{Indent.L1}{badges.PadRight(gutter)}  {line.Number, 6}  {line.Text}");
        }

        if (snippet.TruncatedCount > 0)
        {
            output.WriteLine(
                $"{Indent.L1}{new string(' ', gutter)}  … truncated {snippet.TruncatedCount} line(s) — raise --limit or narrow --from/--to"
            );
        }

        output.WriteLine($"{Indent.L1}{new string(' ', gutter)}  source: {renderer.OriginMarker(snippet).Trim()}");
    }

    // --format tsv: `method` / `site` / `src` rows. Source text is last because a line can contain tabs.
    private static void WriteTsv(TextWriter output, FileEffectLens.LensModel lens, SourceSnippet snippet)
    {
        WriteFactRows(output, lens);

        if (!snippet.HasText)
        {
            output.WriteLine($"unavailable\t{lens.FilePath}\t{snippet.Reason}");
            return;
        }

        var labelByLine = lens.Lines.ToDictionary(line => line.Line, line => FileEffectLens.LabelLine(line.Badges));
        var origin = snippet.Origin == SourceOrigin.GitBlob ? "git" : "worktree";
        foreach (var line in snippet.Lines)
        {
            output.WriteLine($"src\t{line.Number}\t{origin}\t{labelByLine.GetValueOrDefault(line.Number, "")}\t{line.Text}");
        }

        if (snippet.TruncatedCount > 0)
        {
            output.WriteLine($"truncated\t{lens.FilePath}\t{snippet.TruncatedCount}");
        }
    }

    // The `method` / `site` rows both TSV paths share: the lens facts, without any source text.
    internal static void WriteFactRows(TextWriter output, FileEffectLens.LensModel lens)
    {
        foreach (var method in lens.Methods)
        {
            output.WriteLine(
                $"method\t{method.Line}\t{method.EndLine}\t{method.Name}\t{FileEffectLens.LabelLine(method.Badges)}\t{method.SymbolId}"
            );
        }

        foreach (var line in lens.Lines)
        {
            output.WriteLine($"site\t{line.Line}\t{FileEffectLens.LabelLine(line.Badges)}\t{string.Join(" ", line.Targets)}");
        }
    }

    private sealed record ResolvedFile(string? FilePath, IReadOnlyList<string>? Candidates);

    // Path resolution mirrors the browser lens's inventory query: substring over indexed paths, skipped files
    // excluded. An exact path wins outright; otherwise a single match is used and several are refused with
    // the list, because annotating the wrong same-named file is worse than one more keystroke.
    private static async Task<ResolvedFile> ResolveFileAsync(WorkspaceLocation location, string request)
    {
        await using var context = await OpenReadContextGatedAsync(location);
        var pattern = request.Trim();
        var paths = await context
            .SourceFiles.AsNoTracking()
            .Where(file => file.Status != "skipped" && file.FilePath.Contains(pattern))
            .Select(file => file.FilePath)
            .Distinct()
            .ToListAsync();
        var exact = paths.FirstOrDefault(path => string.Equals(path, pattern, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return new ResolvedFile(exact, null);
        }

        return paths.Count switch
        {
            0 => new ResolvedFile(null, null),
            1 => new ResolvedFile(paths[0], null),
            _ => new ResolvedFile(null, paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray()),
        };
    }
}
