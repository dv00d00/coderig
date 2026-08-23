using System.CommandLine;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Rig.Cli.CommandLine;
using Rig.Cli.Rendering;
using Rig.Cli.Telemetry;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using static Rig.Cli.Graph.TraversalGraphLoader;
using static Rig.Cli.Rendering.SymbolNameFormatter;

namespace Rig.Cli.Commands;

// `rig show <pattern>` — print the SOURCE of a matched symbol's declaration, not just its DocID + file:line.
// Every other rig command answers "where"; this one answers "what does it actually say", so an agent reading
// through rig can quote the code instead of reporting that it cannot.
//
// The source text is resolved by SourceRenderer against the store's OWN commit (see that file): the working
// tree is read only when it provably IS the indexed revision, otherwise the exact blob comes out of git and
// is marked as such, otherwise the location prints with a one-line reason and NO source. Lines that cannot be
// attributed to the store's revision are never rendered.
internal static class ShowCommand
{
    // Multi-match cap for the explicit --all path. `show` prints whole declarations, so an unqualified
    // pattern matching dozens of symbols would otherwise flood the terminal; the footer discloses the cap.
    private const int DefaultLimit = 5;

    internal static Command Build(TextWriter output, TextWriter error, string workingDirectory)
    {
        var pattern = CommonOptions.Pattern(name: "pattern", description: "Symbol pattern (case-insensitive substring over DocIDs).");
        var context = new Option<int>("--context")
        {
            Description = "Extra source lines to show either side of the declaration (default 0).",
            DefaultValueFactory = _ => 0,
        };
        var limit = CommonOptions.Limit(DefaultLimit);
        var all = new Option<bool>("--all")
        {
            Description =
                "Render ambiguous matches (subject to --limit) instead of requiring an exact conceptual-symbol selection.",
        };
        // No --rules here, deliberately: `show` renders stored locations and has no rule-driven behaviour,
        // so accepting the flag would be a no-op that silently implies it changed something.
        var format = CommonOptions.Format();
        var time = CommonOptions.Time();
        var store = CommonOptions.Store();
        var cmd = new Command(name: "show", description: "Print the source of a matched symbol's declaration.")
        {
            pattern,
            context,
            limit,
            all,
            format,
            time,
            store,
        };
        cmd.SetAction(pr =>
            CommandGuard.RunGuardedAsync(
                workingDirectory,
                error,
                () =>
                    RunAsync(
                        new Options(
                            Pattern: pr.GetValue(pattern)!,
                            Context: pr.GetValue(context),
                            Limit: pr.GetValue(limit),
                            All: pr.GetValue(all),
                            Format: pr.GetValue(format),
                            Time: pr.GetValue(time)
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

    // Bound option values for `rig show`.
    private sealed record Options(string Pattern, int Context, int Limit, bool All, string? Format, bool Time);

    // A matched declaration: the stored location the renderer resolves source for.
    private sealed record Declaration(string SymbolId, string Kind, string FilePath, int Line, int EndLine);

    private static async Task<int> RunAsync(Options opts, CommandIo io)
    {
        var tsv = CommonOptions.IsTsv(opts.Format);
        var output = io.TextOutput.Output;

        using var timing = QueryTiming.Start(opts.Time, io.TextOutput.Error);

        await using var context = await OpenReadContextGatedAsync(io.WorkspaceLocation);

        var lookupWatch = Stopwatch.StartNew();
        var matches = await MatchDeclarationsAsync(context, opts.Pattern);
        lookupWatch.Stop();
        timing.Record("symbol lookup", lookupWatch.Elapsed);

        if (matches.Count == 0)
        {
            io.TextOutput.Error.WriteLine($"No indexed symbol matches '{opts.Pattern}'.");
            return 1;
        }

        var distinctTargets = FactPathFinder.DistinctMatchTargets(matches.Select(m => m.SymbolId), opts.Pattern);
        if (!opts.All && AmbiguityNotice.RequireSelection(io.TextOutput.Error, opts.Pattern, distinctTargets))
        {
            return 1;
        }

        // --all explicitly opts into the traversal-style union and retains its disclosure.
        if (opts.All)
        {
            AmbiguityNotice.WarnIfAmbiguous(io.TextOutput.Error, opts.Pattern, distinctTargets);
        }

        // Source provenance is a property of the STORE (per-commit), so it is read once for the whole render.
        var runs = await Reads.ListRunsAsync(context);
        var run = runs.FirstOrDefault(r => r.SourceCommit is not null) ?? runs.FirstOrDefault();
        var renderer = new SourceRenderer(storeCommit: run?.SourceCommit, storeDirty: run?.SourceDirty ?? false);

        var renderWatch = Stopwatch.StartNew();
        var shown = matches.Take(opts.Limit).ToList();
        for (var i = 0; i < shown.Count; i++)
        {
            var decl = shown[i];
            var snippet = renderer.Resolve(filePath: decl.FilePath, startLine: decl.Line, endLine: decl.EndLine, context: opts.Context);

            if (tsv)
            {
                WriteTsv(output, decl, snippet);
                continue;
            }

            if (i > 0)
            {
                output.WriteLine();
            }

            output.WriteLine(
                $"{PrettyGenericName(ShortName(decl.SymbolId))}  {decl.FilePath}:{LineRange(decl)}{renderer.OriginMarker(snippet)}"
            );
            SourceRenderer.Render(output, snippet, Indent.L1);
        }

        renderWatch.Stop();
        timing.Record("render", renderWatch.Elapsed);

        if (!tsv && matches.Count > shown.Count)
        {
            output.WriteLine();
            output.WriteLine($"{Indent.L1}(showing {shown.Count} of {matches.Count} — use --limit to raise)");
        }

        return 0;
    }

    // --format tsv: one row per SOURCE LINE (symbolId, file, line, origin, text) so a tool can consume the
    // text directly; a refusal emits a single `unavailable` row carrying the reason instead. Text is the last
    // column because a source line may itself contain tabs.
    private static void WriteTsv(TextWriter output, Declaration decl, SourceSnippet snippet)
    {
        if (!snippet.HasText)
        {
            output.WriteLine($"{decl.SymbolId}\t{decl.FilePath}\t{decl.Line}\tunavailable\t{snippet.Reason}");
            return;
        }

        var origin = snippet.Origin == SourceOrigin.GitBlob ? "git" : "worktree";
        foreach (var line in snippet.Lines)
        {
            output.WriteLine($"{decl.SymbolId}\t{decl.FilePath}\t{line.Number}\t{origin}\t{line.Text}");
        }

        if (snippet.TruncatedCount > 0)
        {
            output.WriteLine($"{decl.SymbolId}\t{decl.FilePath}\t{decl.Line}\ttruncated\t{snippet.TruncatedCount}");
        }
    }

    private static string LineRange(Declaration decl) => decl.EndLine > decl.Line ? $"{decl.Line}-{decl.EndLine}" : decl.Line.ToString();

    // Resolve the pattern to declarations, with the CLI's usual semantics: the indexed-symbol substring
    // search (Reads.SearchSymbolsAsync — FTS-backed when the graph exists), then EXACT MATCH WINS, mirroring
    // FactPathFinder.MatchNodes so a fully-qualified name resolves to exactly its member instead of also
    // dragging in every symbol it is a prefix of. The declaration RANGE (EndLine) is not carried by the
    // search projection, so it is fetched for the matched ids only, keyed on the indexed SymbolId column.
    private static async Task<IReadOnlyList<Declaration>> MatchDeclarationsAsync(RigDbContext context, string pattern)
    {
        var hits = await Reads.SearchSymbolsAsync(context, pattern: pattern, kind: null, limit: int.MaxValue);
        var ids = hits.Select(h => h.SymbolId).Distinct(StringComparer.Ordinal).Where(id => IsExactMatch(id, pattern)).ToList();
        if (ids.Count == 0)
        {
            ids = hits.Select(h => h.SymbolId).Distinct(StringComparer.Ordinal).ToList();
        }

        // Drop a matched LAMBDA whose container also matched: its lines already sit inside the container's
        // rendered range, so it would print the same code again under the same short name. Mirrors how
        // BuildTree/DistinctMatchTargets drop contained lambdas of a matched container.
        var matchedIds = ids.ToHashSet(StringComparer.Ordinal);
        var idSet = ids.Where(id => !IsContainedLambdaOfMatched(id, matchedIds)).ToHashSet(StringComparer.Ordinal);
        var rows = await context
            .SymbolFacts.AsNoTracking()
            .Where(s => idSet.Contains(s.SymbolId))
            .Select(s => new Declaration(s.SymbolId, s.Kind, s.FilePath, s.Line, s.EndLine))
            .ToListAsync();

        // Dedupe across runs (a symbol indexed by several multi-target project siblings).
        return rows.GroupBy(r => r.SymbolId, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(r => r.SymbolId, StringComparer.Ordinal)
            .ToList();
    }

    // A symbol matches EXACTLY when the pattern is its full DocID or its param-free FQN (the form rig renders
    // and a user pastes back). Case-insensitive, mirroring FactPathFinder.MatchNodes — which lives in Domain
    // and is internal there, hence re-expressed here over the shared FqnFromDocId reduction.
    // A synthetic lambda id is `{containerMemberId}~λ{ordinal}` (FactExtractor).
    private static bool IsContainedLambdaOfMatched(string symbolId, HashSet<string> matched)
    {
        var marker = symbolId.IndexOf("~λ", StringComparison.Ordinal);
        return marker > 0 && matched.Contains(symbolId.Substring(startIndex: 0, length: marker));
    }

    private static bool IsExactMatch(string symbolId, string pattern) =>
        string.Equals(symbolId, pattern, StringComparison.OrdinalIgnoreCase)
        || string.Equals(FqnFromDocId(symbolId), pattern, StringComparison.OrdinalIgnoreCase);
}
