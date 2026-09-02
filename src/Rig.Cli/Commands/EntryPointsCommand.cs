using System.CommandLine;
using Rig.Analysis.Rules;
using Rig.Cli.CommandLine;
using Rig.Cli.Rendering;
using static Rig.Cli.EntryPoints.EntryPointContext;
using static Rig.Cli.Graph.TraversalGraphLoader;
using static Rig.Cli.Rendering.EntryPointListRenderer;

namespace Rig.Cli.Commands;

// `rig entrypoints` — list the rule-detected entry points (page/action/class-inheritance + promoted async-
// handoff origins), the SAME set derive/callers/impact build, grouped by kind and — when a deployments.json
// is present — attributed to the services that host them. `--format tsv` emits one row per entry point.
//
// TODO(test): cover this command — (1) the listed set equals `rig derive`'s entry-point set (Derived +
// promoted origins, deduped); (2) tsv columns (kind, route, file, line, requires, loaded/active services);
// (3) deployment attribution when deployments.json is present vs absent; (4) --limit/--store honoured.
internal static class EntryPointsCommand
{
    internal static Command Build(TextWriter output, TextWriter error, string workingDirectory)
    {
        var rules = CommonOptions.Rules();
        var format = CommonOptions.Format();
        var limit = CommonOptions.Limit();
        var store = CommonOptions.Store();
        var cmd = new Command(name: "entrypoints", description: "List the rule-detected entry points, grouped by kind.")
        {
            rules,
            format,
            limit,
            store,
        };
        cmd.SetAction(pr =>
            CommandGuard.RunGuardedAsync(
                workingDirectory,
                error,
                () =>
                    RunAsync(
                        new Options(
                            ExtraRules: CommonOptions.RulesOf(pr.GetValue(rules)),
                            Format: pr.GetValue(format),
                            Limit: pr.GetValue(limit)
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

    // Bound option values for `rig entrypoints`. IO wiring (output, error, workingDirectory, storeRef)
    // lives in CommandIo; only the command-specific user options live here.
    private sealed record Options(IReadOnlyList<string> ExtraRules, string? Format, int? Limit);

    private static async Task<int> RunAsync(Options opts, CommandIo io)
    {
        var tsv = CommonOptions.IsTsv(opts.Format);
        var max = opts.Limit ?? int.MaxValue; // --limit absent => unbounded (this IS the listing)

        var rules = RuleSetLoader.Load(io.WorkspaceLocation.WorkingDirectory, opts.ExtraRules, loadedPaths: out var loadedRulePaths);
        await using var context = await OpenReadContextGatedAsync(io.WorkspaceLocation);

        // The whole-store EP record set, through the SAME artifact-cache entry `callers --entrypoints` uses —
        // this listing IS that derivation with no question attached, so it must not pay it per invocation
        // (see EntryPointContext.LoadOrDeriveEntryPointRecordsAsync).
        var source = Live.StoreQueryFactSource.Borrowing(context, io.WorkspaceLocation);
        using var epCache = source.OpenArtifactCache(useCache: true);
        var epRecords = await LoadOrDeriveEntryPointRecordsAsync(
            source: source,
            cache: epCache,
            rulesHash: RulesFingerprint.ComputeFromPaths(loadedRulePaths),
            rules: rules
        );

        // The full entry-point set: rule-detected EPs + promoted async-handoff origins (what callers
        // --entrypoints / impact seed from), deduped + sorted by (kind, route) for a stable listing.
        // The group key IS four of the record's six fields and DocId is a function of the other two, so
        // First() is the same row the old projection rebuilt field-by-field off the key.
        var eps = epRecords
            .GroupBy(e => (e.Kind, e.Route, e.FilePath, e.Line))
            .Select(g => g.First())
            .OrderBy(e => e.Kind, StringComparer.Ordinal)
            .ThenBy(e => e.Route, StringComparer.Ordinal)
            .ToList();

        var deployments = await LoadDeploymentsAsync(context, io.WorkspaceLocation.WorkingDirectory);

        // --format tsv: one row per EP — kind, route, file, line, requires, loaded services, active services,
        // fqn (the last new: the queryable dotted name, == route when the route already is the FQN, falls back
        // to route for sites with no indexed method). The two service columns are comma-joined; empty without
        // deployments.json.
        if (tsv)
        {
            foreach (var e in eps.Take(max))
            {
                var loaded = deployments.ServicesForFile(e.FilePath);
                var active = deployments.ActiveServices(loadedServices: loaded, requires: e.Requires);
                io.TextOutput.Output.WriteLine(
                    $"{e.Kind}\t{e.Route}\t{e.FilePath}\t{e.Line}\t{string.Join(',', e.Requires ?? [])}\t{string.Join(',', loaded)}\t{string.Join(',', active)}\t{FqnOrRoute(e)}"
                );
            }

            return 0;
        }

        io.TextOutput.Output.WriteLine($"Entry points: {eps.Count}");
        foreach (var kindGroup in eps.GroupBy(e => e.Kind, StringComparer.Ordinal).OrderByDescending(g => g.Count()))
        {
            io.TextOutput.Output.WriteLine($"{Indent.L1}{kindGroup.Key}: {kindGroup.Count()}");
            foreach (var e in kindGroup.Take(max))
            {
                WriteEntryPointLine(
                    io.TextOutput.Output,
                    deployments,
                    route: e.Route,
                    filePath: e.FilePath,
                    line: e.Line,
                    requires: e.Requires,
                    fqn: FqnOrRoute(e)
                );
            }

            WriteSampleTruncationNote(io.TextOutput.Output, total: kindGroup.Count(), shown: max, kind: kindGroup.Key);
        }

        if (!deployments.IsEmpty)
        {
            WriteServiceSummary(eps.Select(e => (e.Kind, (string?)e.FilePath, e.Requires)), deployments, io.TextOutput.Output);
        }

        return 0;
    }
}
