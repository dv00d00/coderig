using System.CommandLine;
using System.Diagnostics;
using Rig.Analysis.Rules;
using Rig.Cli.CommandLine;
using Rig.Cli.Live;
using Rig.Cli.Rendering;
using Rig.Cli.Telemetry;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;
using static Rig.Cli.EntryPoints.EntryPointContext;
using static Rig.Cli.Rendering.SymbolNameFormatter;

namespace Rig.Cli.Commands;

// `rig path <from> <to>` — BFS the fact-derived, shaped call graph and print the first path found. Walks
// the SAME shaped graph as reaches/tree/callers so the path it reports is consistent with them; --raw
// bypasses shaping.
internal static class PathCommand
{
    internal static Command Build(TextWriter output, TextWriter error, string workingDirectory)
    {
        var from = CommonOptions.Pattern(name: "from", description: "Source method pattern.");
        var to = CommonOptions.Pattern(name: "to", description: "Target method pattern.");
        var async = CommonOptions.Async();
        var includeDelivery = CommonOptions.IncludeDelivery();
        var raw = CommonOptions.Raw();
        var rules = CommonOptions.Rules();
        var depth = CommonOptions.Depth();
        var format = CommonOptions.Format();
        var time = CommonOptions.Time();
        var maxNodes = CommonOptions.MaxNodes();
        var maxGenericWork = CommonOptions.MaxGenericWork();
        var store = CommonOptions.Store();
        var noLive = CommonOptions.NoLive();
        var cmd = new Command(name: "path", description: "Print the first call path from one method to another.")
        {
            from,
            to,
            async,
            includeDelivery,
            raw,
            rules,
            depth,
            format,
            time,
            maxNodes,
            maxGenericWork,
            store,
            noLive,
        };
        cmd.SetAction(pr =>
            CommandGuard.RunGuardedAsync(
                workingDirectory,
                error,
                async () =>
                {
                    var opts = new Options(
                        FromPattern: pr.GetValue(from)!,
                        ToPattern: pr.GetValue(to)!,
                        Async: pr.GetValue(async),
                        IncludeDelivery: pr.GetValue(includeDelivery),
                        Raw: pr.GetValue(raw),
                        ExtraRules: CommonOptions.RulesOf(pr.GetValue(rules)),
                        Depth: pr.GetValue(depth),
                        Format: pr.GetValue(format),
                        Time: pr.GetValue(time),
                        MaxNodes: CommonOptions.ResolveBudget(pr.GetValue(maxNodes)),
                        MaxGenericWork: CommonOptions.ResolveBudget(pr.GetValue(maxGenericWork))
                    );
                    var io = new CommandIo(
                        new TextOutput(Output: output, Error: error),
                        new WorkspaceLocation(WorkingDirectory: workingDirectory, StoreRef: pr.GetValue(store))
                    );
                    return await LiveRoute.TryAnswerAsync(LiveQueryVerbs.Path, opts, io, pr.GetValue(noLive)) ?? await RunAsync(opts, io);
                }
            )
        );
        return cmd;
    }

    // Bound option values for `rig path`. Raw user inputs (Format kept as the parsed string);
    // flag derivations (format -> tsv, mode) live at the top of RunAsync.
    // Internal (was private) so the LIVE query path can build the same options record for the same RunAsync.
    internal sealed record Options(
        string FromPattern,
        string ToPattern,
        bool Async,
        bool IncludeDelivery,
        bool Raw,
        IReadOnlyList<string> ExtraRules,
        int? Depth,
        string? Format,
        bool Time,
        int? MaxNodes = null,
        int? MaxGenericWork = null
    );

    // The CLI entry: answer off the .rig store, which is what every `rig path` invocation does. The source is
    // passed as a FACTORY, not an already-open source, purely to preserve ORDERING: the schema gate must still
    // fire where the old `await using var context = …` sat (after the rules load), not before it.
    private static Task<int> RunAsync(Options opts, CommandIo io) =>
        RunAsync(opts, io, () => StoreQueryFactSource.OpenAsync(io.WorkspaceLocation));

    // The command body, parameterized on WHERE the facts come from (IQueryFactSource) rather than on a
    // RigDbContext — so the SAME body answers off a saved store or off the resident live facts.
    internal static async Task<int> RunAsync(Options opts, CommandIo io, Func<Task<IQueryFactSource>> openSource)
    {
        var tsv = CommonOptions.IsTsv(opts.Format);
        var mode = CommonOptions.Mode(async: opts.Async, includeDelivery: opts.IncludeDelivery);

        using var timing = QueryTiming.Start(opts.Time, io.TextOutput.Error);

        // --raw bypasses all shaping (the exact unfiltered plumbing); else monomorphize factories + cut +
        // context-narrow, honoured symmetrically by the reverse/forward traversal.
        var rules = RuleSetLoader.Load(io.WorkspaceLocation.WorkingDirectory, opts.ExtraRules);
        var shaped = opts.Raw ? rules with { Factory = [], Cut = [], Context = [] } : rules;

        await using var source = await openSource();
        StoreAnswerDisclosure.WriteCompilationHealth();

        var graphWatch = Stopwatch.StartNew();
        var maxDepth = CommonOptions.DepthOrUnbounded(opts.Depth);
        // Any path from a `from` node lies entirely within that node's forward closure, so the BOUNDED
        // forward subgraph (loaded on disk via the derived edge views, sized to the result) finds the
        // same first path as the full graph. Falls back to the full EF graph when `rig graph` hasn't run;
        // an indexed live source instead materializes its forward closure through keyed caller partitions.
        DemandForwardGraphResult? demandLoad = null;
        FactGraphData graph;
        if (source is IDemandForwardPathFactSource demand)
        {
            demandLoad = await demand.LoadDemandForwardPathGraphAsync(
                opts.FromPattern,
                shaped,
                maxDepth,
                mode,
                classifyEventSubscriptions: !opts.Raw
            );
            graph = demandLoad.Graph;
        }
        else
        {
            graph = await source.LoadShapedTraversalGraphAsync(opts.FromPattern, SqlReachability.Direction.Forward, shaped);
        }
        // Reclassify event-subscription (`+=`) method-group edges to `handoff` — mirroring reaches/tree
        // (ReachesCommand/TreeCommand do the same). The handler genuinely runs LATER via the event, not
        // synchronously at the `+=` site, so it must be sync-cut by default and only crossed under --async.
        // Without this, `path`/`callers` walked a `+=` handler as a synchronous call (the 2026-06-16
        // over-reach). `--raw` bypasses all shaping, so it is gated the same way reaches/tree gate it.
        if (!opts.Raw && demandLoad?.EventSubscriptionsClassified != true)
        {
            graph = FactPathFinder.MarkEventSubscriptionHandoffs(graph, await source.EventSubscriptionSitesAsync());
        }

        graphWatch.Stop();
        timing.Record("graph load", graphWatch.Elapsed);

        // Seed disclosure (no-match) for the FROM endpoint, BEFORE the search: a `from` that resolved to
        // nothing yields an EMPTY forward slice, so the search below would report "No path from X to Y" —
        // blaming connectivity for what is really a resolution failure. Matched against the same graph the
        // traversal seeds off, through the shared FactPathFinder matcher; a matched LEAF is present in that
        // graph (methods == 1), so this can only fire when the pattern named nothing.
        var symbolIds = graph.Methods.Select(m => m.SymbolId).ToList();
        if (FactPathFinder.DistinctMatchTargets(symbolIds, opts.FromPattern).Count == 0)
        {
            SeedResolutionNotice.ReportNoMatch(io.TextOutput.Output, opts.FromPattern, endpoint: "from");
            return 1;
        }

        // Ambiguity disclosure for BOTH endpoints: the first path found runs between whichever matched
        // from/to pair connects — a multi-target pattern can silently pick the "wrong" endpoint. NOTE:
        // the graph is the FROM-side forward slice, so a to-pattern target outside it (no path exists)
        // is absent here — fine for disclosure: unreachable targets can't be picked into the answer.
        AmbiguityNotice.WarnIfAmbiguous(io.TextOutput.Error, opts.FromPattern, graph);
        AmbiguityNotice.WarnIfAmbiguous(io.TextOutput.Error, opts.ToPattern, graph);

        // LOAD DIAGNOSTIC, NOT AN ANSWER — and the one line in this command whose value depends on the fact
        // SOURCE rather than on the question. It reports the size of the subgraph that was LOADED: on a store
        // with `rig graph` run that is the SQL-bounded forward slice; on the same store without it, the full EF
        // graph; on the resident live source, the query-local demand graph. The three numbers differ by
        // construction and always have (the store path has reported two different values for the same query
        // since the bounded loader landed). The path itself, and every other line below, is identical.
        if (!tsv)
        {
            io.TextOutput.Output.WriteLine(
                $"Fact graph: {graph.CallEdges.Count} call edges, {graph.ImplementsEdges.Count} implements edges, {graph.Methods.Count} methods"
            );
        }

        var traversalWatch = Stopwatch.StartNew();
        var path = FactPathFinder.Find(graph, fromPattern: opts.FromPattern, toPattern: opts.ToPattern, maxDepth: maxDepth, mode: mode);
        // Phase 3 display-collapse: fold any monomorphized (`~mono`) step ids back to their base method id
        // before render (no-op until Phase 2's Materialize is wired into the load path).
        path = path is null ? null : MonomorphCollapse.CollapsePath(path);
        traversalWatch.Stop();
        timing.Record("traversal", traversalWatch.Elapsed);

        if (path is null)
        {
            // Seed disclosure for the TO endpoint: distinguish "the `to` pattern names nothing" from "both
            // endpoints exist but do not connect". `graph` is the FROM-side forward slice, so a `to` that
            // exists but is simply UNREACHABLE is absent from it — deciding no-match off the graph alone
            // would libel a real symbol as nonexistent. So the graph miss only triggers a STORE-WIDE check,
            // and only here on the negative path, where the query has already failed.
            if (
                FactPathFinder.DistinctMatchTargets(symbolIds, opts.ToPattern).Count == 0
                && !await source.SymbolExistsAnywhereAsync(opts.ToPattern)
            )
            {
                SeedResolutionNotice.ReportNoMatch(io.TextOutput.Output, opts.ToPattern, endpoint: "to");
                return 1;
            }

            if (!tsv)
            {
                io.TextOutput.Output.WriteLine($"No path from '{opts.FromPattern}' to '{opts.ToPattern}'.");
            }

            return 1;
        }

        var renderWatch = Stopwatch.StartNew();
        // --format tsv: one row per step (full DocIDs + paths for tooling), no deployment chrome. Columns:
        // depth, symbolId, edgeKind, handoffVia, fanout, loopKind, loopDetail, dispatchBasis, file, line, bindingHealth.
        if (tsv)
        {
            for (var i = 0; i < path.Count; i++)
            {
                var s = path[i];
                io.TextOutput.Output.WriteLine(
                    $"{i}\t{s.SymbolId}\t{s.Kind}\t{s.HandoffVia}\t{s.Fanout}\t{s.LoopKind}\t{s.LoopDetail}\t{s.DispatchBasis}\t{s.FilePath}\t{s.Line}\t{StoreAnswerDisclosure.BindingHealth(s.FilePath)}"
                );
            }

            renderWatch.Stop();
            timing.Record("render", renderWatch.Elapsed);

            return 0;
        }

        // Deployment/EP chip on the from-node (path[0]): which service(s) host this entry point.
        // Opt-in via deployments.json; no-op otherwise.
        var pathDeployments = await source.LoadDeploymentsAsync(io.WorkspaceLocation.WorkingDirectory);
        var pathEpContext = await source.BuildEpContextAsync(
            graph: graph,
            workingDirectory: io.WorkspaceLocation.WorkingDirectory,
            extraRules: opts.ExtraRules,
            rules: rules,
            deployments: pathDeployments,
            // The store path's BuildEpContextAsync default: `path` never loaded the EP fact bundle itself (it
            // loads the graph only), so there is nothing to thread and tier 3 loads its own, exactly as before.
            epData: null
        );

        // Admitted external LEAVES in this graph (external-node admission) — a path may now terminate at
        // one, and it must not read as first-party code. Built from the graph the path was found over, so
        // the marker cannot disagree with the traversal that produced the step.
        var externalPathNodes = graph.Methods.Where(m => m.IsExternal).Select(m => m.SymbolId).ToHashSet(StringComparer.Ordinal);

        io.TextOutput.Output.WriteLine($"Path '{opts.FromPattern}' -> '{opts.ToPattern}' ({path.Count} nodes):");
        for (var i = 0; i < path.Count; i++)
        {
            var step = path[i];
            var loop = step.LoopKind is null ? "" : $" | loop {step.LoopKind}: {ShortLoop(step.LoopDetail)}";
            var kindBase = step.HandoffVia is not null ? $"⤳ handoff via {ShortName(step.HandoffVia)}" : step.Kind;
            if (step.DispatchBasis == "heuristic")
            {
                kindBase += " (heuristic)";
            }

            var kind = step.Fanout > 1 ? $"{kindBase} ×{step.Fanout} fan-out" : kindBase;
            var via =
                i == 0
                    ? HeaderSuffix(pathEpContext, step.SymbolId)
                    : $"  [{kind}{loop}{(step.FilePath is null ? "" : $" @ {ShortenPath(step.FilePath)}:{step.Line}")}]";
            // External-node admission: a path may now END at an admitted library/BCL leaf. Tag it with the
            // ONE shared marker so the terminal step is not read as first-party code.
            var external = externalPathNodes.Contains(step.SymbolId) ? ExternalTag : "";
            var compileError = StoreAnswerDisclosure.HasCompileError(step.FilePath) ? "  ~compile-error" : "";
            io.TextOutput.Output.WriteLine($"{Indent.Of(i + 1)}{step.SymbolId}{external}{via}{compileError}");
        }

        renderWatch.Stop();
        timing.Record("render", renderWatch.Elapsed);

        return 0;
    }
}
