using System.CommandLine;
using System.Diagnostics;
using Rig.Analysis.Rules;
using Rig.Cli.CommandLine;
using Rig.Cli.Live;
using Rig.Cli.Rendering;
using Rig.Cli.Services;
using Rig.Cli.Telemetry;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using static Rig.Cli.EntryPoints.EntryPointContext;
using static Rig.Cli.Rendering.EntryPointListRenderer;
using static Rig.Cli.Rendering.SymbolNameFormatter;

namespace Rig.Cli.Commands;

// `rig callers <to>` — reverse reachability over the fact graph: every method that can reach <to> (transitive
// callers, incl. reverse interface/override dispatch). DEFAULTS TO SYNCHRONOUS (handoffs cut) — the right lens
// for "who touches X"; `--async` also walks handoffs. --orphans filters to entry-point candidates (reachable
// methods with no predecessor); --entrypoints filters to the RULE-DETECTED entry points (the `rig derive` set)
// that reach the target. Walks the SAME shaped graph as path/reaches/tree; --raw bypasses shaping.
//
// RENDER ONLY. The answer — graph load, reverse closure, forward verification, the entry-point join, its cache
// routing, the async hint and the frontier — is CallersQueryService.ComputeAsync's, and arrives here as data
// with every partition already flagged. This file chooses which of those parts to show and how; it selects and
// computes nothing.
internal static class CallersCommand
{
    internal static Command Build(TextWriter output, TextWriter error, string workingDirectory)
    {
        var to = CommonOptions.Pattern(name: "to", description: "Target method pattern (who reaches this?).");
        var orphans = new Option<bool>("--orphans", "--roots")
        {
            Description =
                "Heuristic: all no-predecessor origins that reach the target (includes test/bench/unbound-interface origins). Superset of --entrypoints.",
        };
        var entrypoints = new Option<bool>("--entrypoints")
        {
            Description =
                "Precise: rule-detected entry points only (same set as `rig derive`). Subset of --roots; may miss test/bench or unbound-interface origins.",
        };
        var includeReverseOnly = new Option<bool>("--include-reverse-only")
        {
            // DIAGNOSTIC / hidden: the reverse closure is only a candidate generator — forward-verification is
            // the arbiter, so the forward-confirmed set IS the answer. The reverse-only remainder is a
            // CHA-over-approximation (no forward path) plus a small recall hedge for forward's own
            // interface/lambda misses; it is noise for the precise question and is hidden by default (no
            // footer). This flag is the debug escape hatch that lists it (e.g. when chasing a suspected
            // forward false-negative). Hidden from --help so it doesn't read as a normal lens.
            Description =
                "DIAGNOSTIC: list the reverse-only callers/roots/entry points (reverse-dispatch over-approximation, no forward path). Hidden by default — the forward-confirmed set is the answer.",
            Hidden = true,
        };
        var async = CommonOptions.Async();
        var includeDelivery = CommonOptions.IncludeDelivery();
        var raw = CommonOptions.Raw();
        var rules = CommonOptions.Rules();
        var depth = CommonOptions.Depth();
        var format = CommonOptions.Format();
        var limit = CommonOptions.Limit();
        var time = CommonOptions.Time();
        var maxNodes = CommonOptions.MaxNodes();
        var maxGenericWork = CommonOptions.MaxGenericWork();
        var store = CommonOptions.Store();
        var noLive = CommonOptions.NoLive();
        // --entrypoints memoizes the whole-store entry-point set (see EntryPointContext
        // .LoadOrDeriveEntryPointRecordsAsync); this is its bypass, the same flag `tree`/`impact` already
        // carry. The other two lenses cache nothing, so it is a no-op for them.
        var noCache = CommonOptions.NoCache();
        var cmd = new Command(name: "callers", description: "Reverse reachability: which methods reach the target.")
        {
            to,
            orphans,
            entrypoints,
            includeReverseOnly,
            async,
            includeDelivery,
            raw,
            rules,
            depth,
            format,
            limit,
            time,
            maxNodes,
            maxGenericWork,
            store,
            noLive,
            noCache,
        };
        // --orphans (the candidate heuristic) and --entrypoints (the precise rule set) are distinct lenses.
        cmd.Validators.Add(result =>
        {
            if (result.GetValue(orphans) && result.GetValue(entrypoints))
            {
                result.AddError("Options --orphans and --entrypoints can't be combined for 'rig callers'.");
            }
        });
        cmd.SetAction(pr =>
            CommandGuard.RunGuardedAsync(
                workingDirectory,
                error,
                async () =>
                {
                    var opts = new Options(
                        ToPattern: pr.GetValue(to)!,
                        RootsOnly: pr.GetValue(orphans),
                        EntrypointsOnly: pr.GetValue(entrypoints),
                        IncludeReverseOnly: pr.GetValue(includeReverseOnly),
                        Async: pr.GetValue(async),
                        IncludeDelivery: pr.GetValue(includeDelivery),
                        Raw: pr.GetValue(raw),
                        ExtraRules: CommonOptions.RulesOf(pr.GetValue(rules)),
                        Depth: pr.GetValue(depth),
                        Format: pr.GetValue(format),
                        Limit: pr.GetValue(limit),
                        Time: pr.GetValue(time),
                        MaxNodes: CommonOptions.ResolveBudget(pr.GetValue(maxNodes)),
                        MaxGenericWork: CommonOptions.ResolveBudget(pr.GetValue(maxGenericWork)),
                        NoCache: pr.GetValue(noCache)
                    );
                    var io = new CommandIo(
                        new TextOutput(Output: output, Error: error),
                        new WorkspaceLocation(WorkingDirectory: workingDirectory, StoreRef: pr.GetValue(store))
                    );
                    return await LiveRoute.TryAnswerAsync(LiveQueryVerbs.Callers, opts, io, pr.GetValue(noLive))
                        ?? await RunAsync(opts, io);
                }
            )
        );
        return cmd;
    }

    // Bound option values for `rig callers`. Raw user inputs (Format kept as the parsed string);
    // the flag derivations (tsv, max, maxDepth, mode) live at the top of RunAsync.
    // Internal (was private) so the LIVE query path can build the same options record for the same RunAsync.
    internal sealed record Options(
        string ToPattern,
        bool RootsOnly,
        bool EntrypointsOnly,
        bool IncludeReverseOnly,
        bool Async,
        bool IncludeDelivery,
        bool Raw,
        IReadOnlyList<string> ExtraRules,
        int? Depth,
        string? Format,
        int? Limit,
        bool Time,
        int? MaxNodes = null,
        int? MaxGenericWork = null,
        // --no-cache: bypass the --entrypoints artifact cache (the whole-store EP record set). Defaulted so
        // the live transport's JSON round-trip of this record, and every existing positional construction,
        // keep meaning "caching on" — which on the live path is the fact generation's memo, never cache.db.
        bool NoCache = false
    );

    // ShortName truncates at the parameter list, which drops the trailing `~λN` marker from a contained
    // lambda DocID. Preserve that marker in callers' human labels so a method and its lambda (or several
    // lambdas at different reverse depths) cannot render as duplicate nodes. TSV keeps the exact DocID.
    internal static string HumanNodeLabel(string symbolId)
    {
        var marker = symbolId.IndexOf("~λ", StringComparison.Ordinal);
        var label = ShortName(symbolId);
        return marker < 0 || label.Contains("~λ", StringComparison.Ordinal) ? label : label + symbolId[marker..];
    }

    // The graph shape is shared by store and live execution. Raw means "no traversal shaping", while
    // redirect remains load-bearing: it retains/rebinds an otherwise external convenience overload to the
    // first-party virtual hatch and was intentionally never part of raw's bypass.
    internal static RuleSet ShapeRules(Options opts, RuleSet rules) =>
        opts.Raw ? rules with { Factory = [], Cut = [], Context = [], MaterializedGraphCompatible = false } : rules;

    // The two mutually-exclusive flags plus their flag-less default, as the ONE lens value the engine takes.
    internal static CallersQueryService.CallersMode Lens(Options opts) =>
        opts.EntrypointsOnly ? CallersQueryService.CallersMode.EntryPoints
        : opts.RootsOnly ? CallersQueryService.CallersMode.Roots
        : CallersQueryService.CallersMode.Callers;

    // Human --entrypoints output contains a sync-only hint that probes the async reverse set, so its load must
    // reach that superset (CallersQueryService.DiscoveryModeFor) while execution stays on the user's mode. TSV
    // renders no hint, which is the one CLI-only economy on top of that rule: it does not pay for the superset,
    // and the engine consequently reports no async count for a TSV run.
    internal static FactPathFinder.TraversalMode DiscoveryMode(Options opts, bool tsv)
    {
        var executionMode = CommonOptions.Mode(async: opts.Async, includeDelivery: opts.IncludeDelivery);
        return tsv ? executionMode : CallersQueryService.DiscoveryModeFor(Lens(opts), executionMode);
    }

    // The CLI entry: answer off the .rig store, which is what every `rig callers` invocation does. The source
    // is passed as a FACTORY, not an already-open source, purely to preserve ORDERING: the schema gate must
    // still fire where the old `await using var context = …` sat (after the rules load), not before it.
    private static Task<int> RunAsync(Options opts, CommandIo io) =>
        RunAsync(opts, io, () => StoreQueryFactSource.OpenAsync(io.WorkspaceLocation));

    // The command body, parameterized on WHERE the facts come from (IQueryFactSource) rather than on a
    // RigDbContext — so the SAME body answers off a saved store or off the resident live facts. `callers` is
    // the first REVERSE-direction traversal to run on the live path.
    internal static async Task<int> RunAsync(Options opts, CommandIo io, Func<Task<IQueryFactSource>> openSource)
    {
        var tsv = CommonOptions.IsTsv(opts.Format);
        var max = opts.Limit ?? int.MaxValue; // --limit absent => unbounded
        var mode = CommonOptions.Mode(async: opts.Async, includeDelivery: opts.IncludeDelivery);

        using var timing = QueryTiming.Start(opts.Time, io.TextOutput.Error);

        // --raw bypasses shaping (the exact unfiltered reverse closure); else monomorphize factories + cut +
        // context, honoured symmetrically by the reverse traversal (a cut node yields no successors forward,
        // so it is never a predecessor in reverse).
        // loadedRulePaths: the cascade RuleSetLoader just resolved, reused for the --entrypoints cache key's
        // rule fingerprint instead of re-running the merge (RulesFingerprint.Compute -> ResolveLoadedPaths)
        // to re-discover the same files. Same hash, one rule-file parse.
        var rules = RuleSetLoader.Load(
            workingDirectory: io.WorkspaceLocation.WorkingDirectory,
            extraRules: opts.ExtraRules,
            loadedPaths: out var loadedRulePaths
        );
        var shaped = ShapeRules(opts, rules);

        await using var source = await openSource();
        StoreAnswerDisclosure.WriteCompilationHealth();

        var computation = await CallersQueryService.ComputeAsync(
            source: source,
            rules: rules,
            shaped: shaped,
            workingDirectory: io.WorkspaceLocation.WorkingDirectory,
            toPattern: opts.ToPattern,
            maxDepth: CommonOptions.DepthOrUnbounded(opts.Depth),
            mode: mode,
            discoveryMode: DiscoveryMode(opts, tsv),
            lens: Lens(opts),
            raw: opts.Raw,
            // The two inputs the EP-record cache key is a function of beyond the store itself: the rule
            // cascade's fingerprint (which --rules moves), and --no-cache, which turns the cache into one
            // that misses and drops.
            loadedRulePaths: loadedRulePaths,
            useCache: !opts.NoCache,
            maxNodes: opts.MaxNodes,
            maxGenericWork: opts.MaxGenericWork
        );

        // The engine's phases, into THIS command's `--time` table: one row per phase, in the order they ran,
        // and no row at all for a phase that did not (an absent `async probe` row is the tell that no second
        // reverse walk was paid). `render` is the only phase measured here, because it is the only one here.
        timing.Record("graph load", computation.GraphLoadElapsed);
        if (computation.DeploymentsElapsed is { } deploymentsElapsed)
        {
            timing.Record("deployments", deploymentsElapsed);
        }
        timing.Record("reverse closure", computation.ReverseClosureElapsed);
        if (computation.EntryPointsElapsed is { } entryPointsElapsed)
        {
            timing.Record("entry points", entryPointsElapsed);
        }
        if (computation.ForwardVerifyElapsed is { } forwardVerifyElapsed)
        {
            timing.Record("forward verify", forwardVerifyElapsed);
        }
        if (computation.AsyncProbeElapsed is { } asyncProbeElapsed)
        {
            timing.Record("async probe", asyncProbeElapsed);
        }

        // Ambiguity disclosure (all three modes): a multi-target pattern merges reverse-reach sets.
        AmbiguityNotice.WarnIfAmbiguous(io.TextOutput.Error, opts.ToPattern, computation.Graph);

        if (opts.EntrypointsOnly)
        {
            return RenderEntryPoints(
                computation,
                timing: timing,
                toPattern: opts.ToPattern,
                mode: mode,
                tsv: tsv,
                max: max,
                includeReverseOnly: opts.IncludeReverseOnly,
                output: io.TextOutput.Output
            );
        }

        // Deployment/EP context for the from-symbol annotations (opt-in via deployments.json). Only the
        // --orphans text listing uses it for the chip; tsv and the reachable listing don't, so it's built
        // lazily to avoid the EP-site derivation when it isn't read.
        EpRenderContext? epContext = tsv
            ? null
            : await source.BuildEpContextAsync(
                graph: computation.Graph,
                workingDirectory: io.WorkspaceLocation.WorkingDirectory,
                extraRules: opts.ExtraRules,
                rules: rules,
                deployments: await source.LoadDeploymentsAsync(io.WorkspaceLocation.WorkingDirectory),
                // The store path's BuildEpContextAsync default: `callers` loads the GRAPH only, so there is no
                // EP fact bundle to thread and tier 3 loads its own, exactly as before.
                epData: null
            );

        if (opts.RootsOnly)
        {
            var roots = computation.Roots;
            if (roots.Count == 0)
            {
                if (!tsv)
                {
                    io.TextOutput.Output.WriteLine(
                        $"No root callers (no-predecessor origins) reach '{opts.ToPattern}' (or no symbol matches)."
                    );
                }

                return 1;
            }

            var rootsRenderWatch = Stopwatch.StartNew();
            // Reverse-only roots are hidden by default (matching the text output); --include-reverse-only
            // surfaces them flagged false. --raw keeps the raw superset.
            if (tsv)
            {
                foreach (
                    var r in CallersReverseOnly.VisibleTsvRows(roots.Take(max).ToList(), r => !r.ForwardConfirmed, opts.IncludeReverseOnly)
                )
                {
                    io.TextOutput.Output.WriteLine($"{r.SymbolId}\t{(r.ForwardConfirmed ? "true" : "false")}");
                }

                rootsRenderWatch.Stop();
                timing.Record("render", rootsRenderWatch.Elapsed);

                return 0;
            }

            var rootsConfirmed = roots.Where(r => r.ForwardConfirmed).ToList();
            var rootsReverseOnly = roots.Where(r => !r.ForwardConfirmed).ToList();
            io.TextOutput.Output.WriteLine(
                $"Root callers (heuristic — no-predecessor origins) reaching '{opts.ToPattern}': {rootsConfirmed.Count}"
            );
            foreach (var r in rootsConfirmed.Take(max))
            {
                io.TextOutput.Output.WriteLine($"{Indent.L1}{r.SymbolId}{HeaderSuffix(epContext, r.SymbolId)}");
            }
            if (rootsConfirmed.Count > max)
            {
                io.TextOutput.Output.WriteLine($"{Indent.L1}… +{rootsConfirmed.Count - max} more (raise --limit)");
            }
            // Reverse-only = in the reverse closure but with NO forward path: a reverse-dispatch
            // over-approximation. HIDDEN by default (it's diagnostic noise — the confirmed set is the
            // answer); the hidden --include-reverse-only flag lists it as a recall escape hatch.
            if (opts.IncludeReverseOnly && rootsReverseOnly.Count > 0)
            {
                io.TextOutput.Output.WriteLine($"Reverse-only (no forward path found — confirm with `rig path`): {rootsReverseOnly.Count}");
                foreach (var r in rootsReverseOnly.Take(max))
                {
                    io.TextOutput.Output.WriteLine($"{Indent.L1}{r.SymbolId}{HeaderSuffix(epContext, r.SymbolId)}");
                }
            }

            rootsRenderWatch.Stop();
            timing.Record("render", rootsRenderWatch.Elapsed);

            return 0;
        }

        var reached = computation.Callers;
        if (reached.Count == 0)
        {
            if (!tsv)
            {
                io.TextOutput.Output.WriteLine($"No symbol matches '{opts.ToPattern}'.");
            }

            return 1;
        }

        // The BFS start nodes (depth=0, the matched target(s) and their lambdas) are separated from actual
        // upstream callers (depth≥1). The headline count and --limit budget reflect upstream callers only
        // — the matched nodes are the SUBJECT of the query, not its answer.
        var matched = reached.Where(r => r.Depth == 0).ToList();
        var confirmedCallers = reached.Where(r => r.Depth > 0 && r.ForwardConfirmed).ToList();
        var reverseOnlyCallers = reached.Where(r => r.Depth > 0 && !r.ForwardConfirmed).ToList();

        var renderWatch = Stopwatch.StartNew();
        // Depth-0 rows are the BFS start nodes (always forwardConfirmed=true). Reverse-only rows are
        // hidden by default, matching the text output; --include-reverse-only surfaces them.
        if (tsv)
        {
            foreach (
                var r in CallersReverseOnly.VisibleTsvRows(reached.Take(max).ToList(), r => !r.ForwardConfirmed, opts.IncludeReverseOnly)
            )
            {
                io.TextOutput.Output.WriteLine($"{r.Depth}\t{r.SymbolId}\t{(r.ForwardConfirmed ? "true" : "false")}");
            }

            renderWatch.Stop();
            timing.Record("render", renderWatch.Elapsed);

            return 0;
        }

        io.TextOutput.Output.WriteLine($"Methods that reach '{opts.ToPattern}': {confirmedCallers.Count}");
        if (matched.Count > 0)
        {
            io.TextOutput.Output.WriteLine($"{Indent.L1}Matched nodes ({matched.Count}):");
            foreach (var r in matched)
            {
                io.TextOutput.Output.WriteLine($"{Indent.L2}{HumanNodeLabel(r.SymbolId)}");
            }
        }
        foreach (var r in confirmedCallers.Take(max))
        {
            io.TextOutput.Output.WriteLine($"{Indent.L1}d{r.Depth}  {HumanNodeLabel(r.SymbolId)}");
        }
        if (confirmedCallers.Count > max)
        {
            io.TextOutput.Output.WriteLine($"{Indent.L1}… +{confirmedCallers.Count - max} more (raise --limit, or --format tsv for all)");
        }
        // Reverse-only = in the reverse closure but with NO forward path: a reverse-dispatch over-approximation
        // (a shared base/interface seam pulls in every caller of ANY override). HIDDEN by default (diagnostic
        // noise — the confirmed set is the answer); the hidden --include-reverse-only flag lists it.
        if (opts.IncludeReverseOnly && reverseOnlyCallers.Count > 0)
        {
            io.TextOutput.Output.WriteLine($"Reverse-only (no forward path found — confirm with `rig path`): {reverseOnlyCallers.Count}");
            foreach (var r in reverseOnlyCallers.Take(max))
            {
                io.TextOutput.Output.WriteLine($"{Indent.L1}d{r.Depth}  {HumanNodeLabel(r.SymbolId)}");
            }
        }

        renderWatch.Stop();
        timing.Record("render", renderWatch.Elapsed);

        return 0;
    }

    // `rig callers <to> --entrypoints` — the RULE-DETECTED entry points (same set `rig derive` emits) whose
    // body is in the REVERSE closure of <to>, i.e. the real entry points that actually reach the target code.
    // Default is synchronous-only; --async also counts scheduled paths.
    //
    // Pure render: the touching set, its forward-verification flags, the async-hint count and the frontier all
    // arrive on `computation`. What is decided here is presentation — which rows to show (reverse-only is
    // hidden unless --include-reverse-only), the TSV columns, the kind grouping, the deployment chips, and
    // which of the two 0-EP wordings the answer deserves.
    private static int RenderEntryPoints(
        CallersQueryService.CallersComputation computation,
        QueryTiming timing,
        string toPattern,
        FactPathFinder.TraversalMode mode,
        bool tsv,
        int max,
        bool includeReverseOnly,
        TextWriter output
    )
    {
        if (computation.ReachableCount == 0)
        {
            if (!tsv)
            {
                output.WriteLine($"No symbol matches '{toPattern}'.");
            }

            return 1;
        }

        var deployments = computation.Deployments;
        var touching = computation.EntryPoints;
        if (touching.Count == 0)
        {
            // The 0-EP answer is not a cheap answer: it paid a SECOND whole reverse walk (the async probe) and
            // the frontier scan, both accounted for by the engine's own phase rows — so the empty result still
            // reports its seconds instead of returning through an unmeasured exit.
            var emptyRenderWatch = Stopwatch.StartNew();
            if (!tsv)
            {
                // A target reachable ONLY via a handoff (background worker, actor message, event) would
                // otherwise be wrongly reported as dead/background-only — defeating the security-reachability
                // use case.
                if (computation.AsyncReachableEpCount > 0)
                {
                    output.WriteLine(
                        $"No entry points reach '{toPattern}' synchronously — but {computation.AsyncReachableEpCount} reach it via async/scheduled handoff. Re-run with --async."
                    );
                    emptyRenderWatch.Stop();
                    timing.Record("render", emptyRenderWatch.Elapsed);
                    return 1;
                }

                output.WriteLine($"No rule-detected entry points reach '{toPattern}'.");
                WriteFrontier(output, computation);
            }

            emptyRenderWatch.Stop();
            timing.Record("render", emptyRenderWatch.Elapsed);

            return 1;
        }

        var confirmed = touching.Where(hit => hit.ForwardConfirmed).Select(hit => hit.Record).ToList();
        var reverseOnly = touching.Where(hit => !hit.ForwardConfirmed).Select(hit => hit.Record).ToList();

        var renderWatch = Stopwatch.StartNew();
        // Reverse-only EPs (no forward path) are hidden by default, matching the text output; --include-reverse-only surfaces them.
        // Columns: kind, route, file, line, requires, loadedServices, activeServices, forwardConfirmed, fqn.
        if (tsv)
        {
            foreach (var hit in CallersReverseOnly.VisibleTsvRows(touching, isReverseOnly: r => !r.ForwardConfirmed, includeReverseOnly))
            {
                var e = hit.Record;
                var loaded = deployments.ServicesForFile(e.FilePath);
                var active = deployments.ActiveServices(loadedServices: loaded, requires: e.Requires);
                output.WriteLine(
                    $"{e.Kind}\t{e.Route}\t{e.FilePath}\t{e.Line}\t{string.Join(',', e.Requires ?? [])}\t{string.Join(',', loaded)}\t{string.Join(',', active)}\t{(hit.ForwardConfirmed ? "true" : "false")}\t{FqnOrRoute(e)}"
                );
            }
            renderWatch.Stop();
            timing.Record("render", renderWatch.Elapsed);
            return 0;
        }
        // Headline count is the PRECISE answer — confirmed (forward-verified) EPs only.
        output.WriteLine(
            $"Rule-detected entry points reaching '{toPattern}': {confirmed.Count}"
                + mode switch
                {
                    FactPathFinder.TraversalMode.AsyncExact => "  (--async: incl. scheduled paths; delivery fan-out excluded)",
                    FactPathFinder.TraversalMode.AsyncInclude => "  (--async --include-delivery: incl. delivery fan-out)",
                    _ => "",
                }
        );
        foreach (var kindGroup in confirmed.GroupBy(e => e.Kind, StringComparer.Ordinal).OrderByDescending(g => g.Count()))
        {
            output.WriteLine($"{Indent.L1}{kindGroup.Key}: {kindGroup.Count()}");
            foreach (var e in kindGroup)
            {
                WriteEntryPointLine(
                    output,
                    deployments,
                    route: e.Route,
                    filePath: e.FilePath,
                    line: e.Line,
                    requires: e.Requires,
                    fqn: FqnOrRoute(e)
                );
            }
        }
        // Defect 2 (non-zero under-report): even with sync EPs present, the async surface can reach MORE — a
        // target some EPs touch only via a scheduled/actor handoff. Compared against the sync REACHABLE set
        // (the whole touching set), not the confirmed headline, so the delta isolates the async contribution
        // rather than conflating it with the reverse-only partition. The precise per-EP confirmation lives on
        // the --async run, so this is a "go look" pointer, not a verified count.
        if (computation.AsyncReachableEpCount > touching.Count)
        {
            output.WriteLine(
                $"{Indent.L1}… +{computation.AsyncReachableEpCount - touching.Count} more entry point(s) reach this via async/scheduled handoff (not shown) — re-run with --async."
            );
        }

        // Reverse-only = in the reverse closure but with NO forward path: a reverse-dispatch over-approximation
        // (a shared base/interface seam — e.g. EntityBase.Delete — pulls in every caller of ANY override, which
        // can dwarf the real answer by 100s–1000s). HIDDEN by default — the headline confirmed set IS the
        // answer; the hidden --include-reverse-only flag lists it as a diagnostic recall escape hatch.
        if (includeReverseOnly && reverseOnly.Count > 0)
        {
            output.WriteLine($"Reverse-only (no forward path found — confirm with `rig path`): {reverseOnly.Count}");
            foreach (var kindGroup in reverseOnly.GroupBy(e => e.Kind, StringComparer.Ordinal).OrderByDescending(g => g.Count()))
            {
                output.WriteLine($"{Indent.L1}{kindGroup.Key}: {kindGroup.Count()}");
                foreach (var e in kindGroup)
                {
                    WriteEntryPointLine(
                        output,
                        deployments,
                        route: e.Route,
                        filePath: e.FilePath,
                        line: e.Line,
                        requires: e.Requires,
                        fqn: FqnOrRoute(e)
                    );
                }
            }
        }
        // The service summary reflects the precise answer (confirmed EPs).
        if (!deployments.IsEmpty)
        {
            WriteServiceSummary(confirmed.Select(t => (t.Kind, (string?)t.FilePath, t.Requires)), deployments, output);
        }

        renderWatch.Stop();
        timing.Record("render", renderWatch.Elapsed);

        return 0;
    }

    // Report the frontier under a 0-EP answer, so the zero is attributable instead of merely discouraging.
    private static void WriteFrontier(TextWriter output, CallersQueryService.CallersComputation computation)
    {
        const int MaxFrontierListed = 8;
        var frontier = computation.Frontier;
        if (frontier.Count == 0)
        {
            return; // every reverse-reachable method has a caller (e.g. a cycle) — nothing to attribute
        }

        // The target itself being the only frontier node means nothing in the solution calls it at all —
        // a materially different answer from "the chain runs up to a boundary", so say which one it is.
        var selfOnly = frontier.Count == 1 && computation.ReachableCount == 1;
        output.WriteLine(
            selfOnly
                ? $"{Indent.L1}Nothing in the analysed solution calls it — the chain is empty, not cut short."
                : $"{Indent.L1}The reverse chain tops out at {frontier.Count} method(s) with no in-solution caller:"
        );
        if (!selfOnly)
        {
            foreach (var m in frontier.Take(MaxFrontierListed))
            {
                var where = string.IsNullOrEmpty(m.FilePath) ? "" : $"  {m.FilePath}:{m.Line}";
                output.WriteLine($"{Indent.L2}{HumanNodeLabel(m.SymbolId)}{where}");
            }

            if (frontier.Count > MaxFrontierListed)
            {
                output.WriteLine($"{Indent.L2}… +{frontier.Count - MaxFrontierListed} more");
            }

            output.WriteLine(
                $"{Indent.L1}These are the BOUNDARY, not proof of dead code: something outside the static call graph"
                    + " may invoke them — template/Dom interpolation, reflection, DI-by-name, or an external caller."
            );
        }
    }
}

// Shared TSV row-visibility policy across all three `rig callers` lenses: reverse-only rows (no forward
// path) are dropped by default, matching the text output, and shown under --include-reverse-only.
internal static class CallersReverseOnly
{
    internal static IEnumerable<T> VisibleTsvRows<T>(IReadOnlyList<T> rows, Func<T, bool> isReverseOnly, bool includeReverseOnly) =>
        includeReverseOnly ? rows : rows.Where(r => !isReverseOnly(r));
}
