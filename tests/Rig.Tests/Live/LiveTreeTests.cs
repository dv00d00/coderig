using System.Globalization;
using System.Text;
using Rig.Analysis.Rules;
using Rig.Cli;
using Rig.Cli.Caching;
using Rig.Cli.CommandLine;
using Rig.Cli.Commands;
using Rig.Cli.Live;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Live;

// The `tree` half of the live-answer gate, and the hardest of the four: `tree` is the only migrated traversal
// that is CACHED, so this file has to prove two different things rather than one.
//
//  1. EQUALITY WITH THE STORE, across the VIEW/FORMAT MATRIX. `tree` is not one code path but five or six —
//     the default effectful-paths render, `--view full` (which additionally loads and promotes unresolved
//     LIBRARY call sites to leaf nodes), `--view hazards` (which THROWS AWAY the traversal's effects and
//     replaces them with the whole-store hazard-augmented set, plus the graph-tier findings), `--view effects`,
//     `--view summary`, and `--format tsv`. Each reaches for something different from the fact source, so a
//     single default-view comparison would leave most of the migration unmeasured. All of them are compared
//     byte-for-byte here, on two playgrounds.
//
//     TRUNCATION is covered on purpose (`--limit 3`, `--depth 2`): a tree is the only answer with truncation
//     semantics, TraceNode.TruncationCause is CACHED state, and `maxNodes`/`maxDepth` are cache-key axes — so a
//     key mistake would show up exactly as a ⋯elided leaf served for the wrong budget. The `⋯elided` marker is
//     asserted present on the store side of those cases, so the coverage cannot silently lapse.
//
//  2. THE CACHE. On the store path a tree forest lands in `.rig/cache.db`; on the live path there is no such
//     file and there must not be — facts change per edit, so an artifact keyed on a store's identity would be
//     a liability. The replacement is a per-GENERATION memo (BoundedArtifactMemo on LiveFactSource), keyed by
//     the SAME QueryCacheKeys.TreeCacheKey function with the store-identity axis replaced by the constant
//     "live". Three things are asserted about it, because "it caches" is not the interesting claim:
//       * The_live_tree_memo_is_keyed_by_query_shape — the memo is EMPTY before the query and holds an entry
//         under the exactly-predicted key after it; a second query differing ONLY in --limit lands in a
//         DIFFERENT slot with a genuinely different (smaller) forest. That is the wrong-key-collision proof:
//         one query's tree cannot be served for another, because the two never share a slot.
//       * A_live_tree_query_never_touches_the_disk_query_cache — cache.db does not appear (or change) across a
//         live tree query, and DOES appear for the equivalent store query. The negative half is the one that
//         matters and it is measured, not asserted by inspection of the code.
//       * The repeat query returns a byte-identical answer, so the memo cannot be serving a stale/partial
//         artifact.
//
// Intrinsic hiding is counted AFTER the rendered tree-method filter. Besides making the note truthful, this
// removes the last whole-generation dependency from ordinary live trees: a disjoint dirty project's alloc or
// throw cannot change this query's stderr. Store/live parity is therefore byte-for-byte on both streams.
//
// `path`'s loader-dependent load banner has no analogue here — `tree` prints no such line, so nothing else is
// excluded. If any other line differs, the test fails with that line.
//
// Measurements go to the file named by RIG_LIVE_REPORT, never Console — TUnit swallows console output.
public sealed class LiveTreeTests
{
    private static readonly object ReportLock = new();

    // One comparison: the CLI invocation, the equivalent live Options record, and the marker proving the STORE
    // side actually answered (anti-vacuity — two empty answers compare equal and prove nothing).
    // `RequiredInAnswer` is the FEATURE-IS-ON guard: a regression that dropped (say) the deployment chip on
    // BOTH paths would still compare equal and pass, so a case that exists to cover a feature names the string
    // that proves the feature rendered.
    private sealed record TreeCase(
        string[] StoreArgs,
        TreeCommand.Options LiveOptions,
        string StoreMustContain,
        int ExpectedStoreExit = 0,
        string? RequiredInAnswer = null
    );

    // The DEFAULT `rig tree <pattern>` options record — the one LiveQueryRunner builds. Every matrix case is a
    // `with` over this, so a case can only differ from the CLI invocation in the flag it means to vary.
    private static TreeCommand.Options DefaultOptions(string pattern) =>
        new TreeCommand.Options(
            FromPattern: pattern,
            View: "paths",
            Async: false,
            IncludeDelivery: false,
            Raw: false,
            Files: false,
            Signatures: false,
            Plain: false,
            Guards: false,
            ExtraRules: [],
            Depth: null,
            Limit: null,
            Only: CommonOptions.FilterSet(null),
            Exclude: CommonOptions.FilterSet(null),
            Intrinsic: false,
            ExcludeNamespaces: CommonOptions.NamespacePrefixes(null),
            NoCache: false,
            Gate: true,
            Amplification: true,
            Time: false,
            Format: null,
            Suppress: null
        );

    // ---- DeepChain: a 7-project reference chain, one DB effect five project hops down through an interface.
    // Under the DEFAULT rule set it derives no *effect* rules, so its trees exercise the STRUCTURE + the
    // "nothing reachable" and truncation arms rather than the effect rendering.
    private static readonly string[] DeepChainPatterns = ["HomePage.Show", "BookingController.Book", "BookingService.Book", "Db.Query"];

    // ---- EntryPointEffects: the effect-rich playground (EF Core, Redis, HttpClient, a real C# event with
    // delivery edges, loops, cycles) shipping its own rig.rules.json — the one that makes the effect render,
    // the seam summaries and the hazard set non-trivial.
    private static readonly string[] EffectRichPatterns =
    [
        "TeamWorkflow.LoadTeamSummaryAsync",
        "TeamWorkflow.CreateTeamAsync",
        "TeamWorkflow.ProcessBatchAsync",
        "TeamsController.Create",
        "TeamRepository.AddAsync",
    ];

    [Test]
    public async Task Live_tree_equals_the_store_answer_on_deep_chain()
    {
        using var playground = await DeepChainPlayground.CreateAsync();

        // DEPLOYMENT ATTRIBUTION ON, for the same reason the sibling files turn it on: without a
        // deployments.json both paths short-circuit to DeploymentMap.Empty and the EP-render context is never
        // built, so the live EpSiteKindAsync arm this slice added would go completely unexercised.
        await File.WriteAllTextAsync(
            Path.Combine(playground.WorkingDirectory, "deployments.json"),
            """{ "services": [ { "name": "web", "host": "Web/Web.csproj", "kind": "site" } ] }"""
        );

        await AssertLiveEqualsStoreAsync(
            "DeepChain/tree",
            playground.WorkingDirectory,
            playground.SolutionPath,
            [
                // DeepChain derives no effects under the default rules, so the DEFAULT view's honest answer for
                // every one of these patterns is the everything-pruned disclosure — a real answer, and a
                // specific enough one to serve as the anti-vacuity marker.
                .. DeepChainPatterns.Select(p => new TreeCase(["tree", p], DefaultOptions(p), $"No effects reachable from '{p}'.")),
                // …but a pruned-to-nothing answer renders no NODES at all, so the structural view is added for
                // every pattern too — otherwise this playground would only ever compare a one-line disclosure.
                //
                // The ▶/⟦service⟧ chip is NOT asserted here even though deployments are configured: a `tree`
                // chip needs a node that is a RULE-DETECTED entry point, and DeepChain has none (measured — its
                // `--view full` renders no chip on either path). The EpSiteKindAsync arm is covered on
                // EntryPointEffects instead, where the controller actions are real EPs.
                .. DeepChainPatterns.Select(p => new TreeCase(
                    ["tree", p, "--view", "full"],
                    DefaultOptions(p) with
                    {
                        View = "full",
                    },
                    p.Split('.')[^1]
                )),
            ]
        );
    }

    [Test]
    public async Task Live_tree_equals_the_store_answer_on_the_effect_rich_playground()
    {
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();

        // DEPLOYMENT ATTRIBUTION ON. Without a deployments.json both paths short-circuit to
        // DeploymentMap.Empty and the EP-render context is never built, so EpSiteKindAsync — the seam member
        // this slice added, whose only consumer is the ▶/⟦service⟧ chip — would go entirely unexercised. It is
        // turned on HERE rather than on DeepChain because a `tree` chip needs a node that is a RULE-DETECTED
        // entry point: DeepChain has none, TeamsController's ASP.NET actions do.
        await File.WriteAllTextAsync(
            Path.Combine(playground.WorkingDirectory, "deployments.json"),
            """{ "services": [ { "name": "api", "host": "EntryPointEffects.Api/EntryPointEffects.Api.csproj", "kind": "site" } ] }"""
        );

        await AssertLiveEqualsStoreAsync(
            "EntryPointEffects/tree",
            playground.WorkingDirectory,
            playground.SolutionPath,
            [
                // The default tree view has no "From:" header (only --view summary/effects do): the root NODE
                // line is the marker, which is also what proves the pattern resolved.
                .. EffectRichPatterns.Select(p => new TreeCase(["tree", p], DefaultOptions(p), p)),
                // …and the entry-point case, asserted to actually RENDER the chip on both sides — a regression
                // that dropped it on BOTH would otherwise still compare equal and pass.
                new TreeCase(
                    ["tree", "TeamsController.Create", "--view", "full"],
                    DefaultOptions("TeamsController.Create") with
                    {
                        View = "full",
                    },
                    "TeamsController.Create",
                    RequiredInAnswer: "⟦api (site)⟧"
                ),
            ]
        );
    }

    // THE VIEW/FORMAT MATRIX — the actual coverage of this slice. Driven through TreeCommand rather than
    // LiveQueryRunner because the live query surface deliberately exposes no flags yet (the same reason
    // LivePathCallersTests drives `callers --entrypoints` directly): the CODE PATHS are what must agree, and
    // gating the comparison on a future flag-parsing slice would leave them unmeasured until then.
    [Test]
    public async Task Live_tree_equals_the_store_answer_across_the_view_and_format_matrix()
    {
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        const string Pattern = "TeamWorkflow.ProcessBatchAsync";
        var baseline = DefaultOptions(Pattern);

        TreeCase[] matrix =
        [
            // The default effectful-paths render.
            new(["tree", Pattern], baseline, "TeamWorkflow.ProcessBatchAsync  {🧵 parallel:fanout Tasks.Parallel}"),
            // --view full: every reachable method AND the unresolved LIBRARY call sites promoted to leaves —
            // the only arm that reads LibraryCallSitesAsync, whose live twin is new in this slice.
            new(["tree", Pattern, "--view", "full"], baseline with { View = "full" }, "TeamWorkflow.ProcessBatchAsync"),
            // --view hazards: replaces the traversal's effects with the WHOLE-STORE hazard-augmented set and
            // appends the graph-tier findings. The expensive arm, and the one whose live feeds are new here.
            new(["tree", Pattern, "--view", "hazards"], baseline with { View = "hazards" }, "TeamWorkflow.ProcessBatchAsync"),
            // --view effects / --view summary: the two collapsed projections (they render hazard/amplification
            // sections too, so they are not merely a subset of the default render).
            new(["tree", Pattern, "--view", "effects"], baseline with { View = "effects" }, "effectful method(s), source order"),
            new(["tree", Pattern, "--view", "summary"], baseline with { View = "summary" }, "Reachable methods:"),
            // --format tsv: the machine-readable DFS rows — a wholly separate emit path that returns before the
            // deployment map and the seam are ever computed.
            new(["tree", Pattern, "--format", "tsv"], baseline with { Format = "tsv" }, "\t"),
            // TRUNCATION, both causes. maxNodes and maxDepth are cache-key axes and TruncationCause is cached
            // state, so these are the cases a key mistake would surface in.
            new(["tree", Pattern, "--limit", "2", "--view", "full"], baseline with { Limit = 2, View = "full" }, "⋯elided"),
            // …and the DEPTH cause, on the one pattern here with a tree deep enough to cut (ProcessBatchAsync's
            // is a single level, so any --depth >= 1 elides nothing and the case would be vacuous).
            new(
                ["tree", "TeamWorkflow.LoadTeamSummaryAsync", "--depth", "1", "--view", "full"],
                DefaultOptions("TeamWorkflow.LoadTeamSummaryAsync") with
                {
                    Depth = 1,
                    View = "full",
                },
                "⋯elided"
            ),
            // A pattern that matches nothing: the store must say so and exit 1, and the live path must produce
            // the identical refusal rather than an empty success.
            new(
                ["tree", "NoSuchSymbolAnywhereInEntryPointEffects"],
                DefaultOptions("NoSuchSymbolAnywhereInEntryPointEffects"),
                "No symbol matches 'NoSuchSymbolAnywhereInEntryPointEffects'.",
                ExpectedStoreExit: 1
            ),
        ];

        await AssertLiveEqualsStoreAsync("EntryPointEffects/matrix", playground.WorkingDirectory, playground.SolutionPath, matrix);
    }

    // The same matrix over DeepChain, whose deep interface chain and (default-rules) EFFECT-FREE graph exercise
    // the arms the effect-rich playground cannot: the "matched but everything pruned" disclosure, and a
    // `--view full` structural tree with no effect leaves at all.
    [Test]
    public async Task Live_tree_equals_the_store_answer_across_the_matrix_on_deep_chain()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        const string Pattern = "HomePage.Show";
        var baseline = DefaultOptions(Pattern);

        TreeCase[] matrix =
        [
            new(["tree", Pattern], baseline, "No effects reachable from 'HomePage.Show'."),
            new(["tree", Pattern, "--view", "full"], baseline with { View = "full" }, "HomePage.Show"),
            new(["tree", Pattern, "--view", "hazards"], baseline with { View = "hazards" }, ""),
            new(["tree", Pattern, "--view", "summary"], baseline with { View = "summary" }, "Reachable methods:"),
            new(["tree", Pattern, "--format", "tsv"], baseline with { Format = "tsv" }, "\t"),
            new(["tree", Pattern, "--limit", "2", "--view", "full"], baseline with { Limit = 2, View = "full" }, "⋯elided"),
        ];

        await AssertLiveEqualsStoreAsync("DeepChain/matrix", playground.WorkingDirectory, playground.SolutionPath, matrix);
    }

    // THE CACHE-KEY PROOF. The live memo replaces `.rig/cache.db`, so the question "could one query's tree be
    // served for another?" has to be answered about the memo, not about the disk cache. It is answered by
    // predicting the key from OUTSIDE the command — the same QueryCacheKeys.TreeCacheKey the store path uses,
    // with the store-identity axis set to "live" — and showing that the artifact lands under exactly that key,
    // and that a query differing only in --limit lands under a different one with a different forest.
    [Test]
    public async Task The_live_tree_memo_is_keyed_by_query_shape()
    {
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        var workingDirectory = playground.WorkingDirectory;
        var rules = RuleSetLoader.Load(workingDirectory, extraRules: null, loadedPaths: out var loadedPaths);
        var rulesHash = RulesFingerprint.ComputeFromPaths(loadedPaths);

        await using var host = await WatchHost.StartAsync(
            solutionPath: playground.SolutionPath,
            rules: rules,
            buildCacheDir: null,
            output: new StringWriter(),
            watch: false,
            workingDirectory: workingDirectory
        );
        var live = new LiveFactSource(await host.GetCurrentFactsAsync(), rules);

        const string Pattern = "TeamWorkflow.ProcessBatchAsync";
        var unboundedKey = QueryCacheKeys.TreeCacheKey(
            storeKey: "live",
            rulesHash: rulesHash,
            fromPattern: Pattern,
            maxDepth: CommonOptions.DepthOrUnbounded(null),
            maxNodes: FactPathFinder.DefaultTreeNodeBudget,
            mode: CommonOptions.Mode(async: false),
            raw: false
        );
        var cappedKey = QueryCacheKeys.TreeCacheKey(
            storeKey: "live",
            rulesHash: rulesHash,
            fromPattern: Pattern,
            maxDepth: CommonOptions.DepthOrUnbounded(null),
            maxNodes: 1,
            mode: CommonOptions.Mode(async: false),
            raw: false
        );
        // The two shapes differ in the key material, so they can never share a slot. Asserted rather than
        // assumed: this is the whole no-collision argument, and it rests on maxNodes being IN the key.
        cappedKey.Value.ShouldNotBe(unboundedKey.Value);

        // Nothing memoized before the first query.
        live.ArtifactMemo.Get(unboundedKey.Value).ShouldBeNull();

        var first = await RunLiveAsync(DefaultOptions(Pattern), live, workingDirectory);
        var unbounded = live.ArtifactMemo.Get(unboundedKey.Value) as TreeCachePayload;
        unbounded.ShouldNotBeNull("the live tree forest was not memoized under the predicted key");
        live.ArtifactMemo.Get(cappedKey.Value).ShouldBeNull("a query that was never asked has a memo entry");

        // The repeat query is served from the memo and must be byte-identical — a memo that returns a
        // different (or partial) answer than the computation it stands in for is worse than no memo.
        var repeat = await RunLiveAsync(DefaultOptions(Pattern), live, workingDirectory);
        repeat.Out.ShouldBe(first.Out);
        repeat.Err.ShouldBe(first.Err);

        // …and the DIFFERENT shape gets its own slot with a genuinely different forest (fewer nodes, because
        // the node budget capped it). If the key were missing the maxNodes axis, this assertion is what fails.
        var capped = await RunLiveAsync(DefaultOptions(Pattern) with { Limit = 1, View = "full" }, live, workingDirectory);
        var cappedPayload = live.ArtifactMemo.Get(cappedKey.Value) as TreeCachePayload;
        cappedPayload.ShouldNotBeNull("the budget-capped forest was not memoized under its own key");
        NodeCount(cappedPayload!.Forest)
            .ShouldBeLessThan(
                NodeCount(unbounded!.Forest),
                $"the capped forest ({NodeCount(cappedPayload.Forest)} nodes) is not smaller than the unbounded one "
                    + $"({NodeCount(unbounded.Forest)} nodes) — the two slots hold the same tree, so this proves nothing."
            );
        capped.Out.ShouldContain("⋯elided");
        first.Out.ShouldNotContain("⋯elided");

        Report(
            $"[live/tree memo] unbounded={NodeCount(unbounded.Forest)} nodes / {unbounded.Effects.Count} effects, "
                + $"capped={NodeCount(cappedPayload.Forest)} nodes; derived layer: {live.BuildTimeLine()}"
        );
    }

    // The live path must never read or write `.rig/cache.db`. Measured from the OUTSIDE (does the file exist,
    // and has it changed?) rather than argued from the code, because "we didn't mean to write there" is exactly
    // the kind of claim that quietly stops being true.
    [Test]
    public async Task A_live_tree_query_never_touches_the_disk_query_cache()
    {
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        var workingDirectory = playground.WorkingDirectory;
        var indexLog = new StringWriter();
        (await CliApplication.RunAsync(["index", playground.SolutionPath], indexLog, indexLog, workingDirectory)).ShouldBe(
            0,
            indexLog.ToString()
        );

        // Searched, not hard-coded: a store dir is per-COMMIT (`.rig/<id>/`), so cache.db lives beside the
        // rig.db it belongs to rather than at `.rig/cache.db`. The fingerprint is every cache.db under `.rig`
        // with its size, so both "a new one appeared" and "an existing one grew" are visible.
        var before = CacheDbFingerprint(workingDirectory);

        var rules = RuleSetLoader.Load(workingDirectory);
        await using var host = await WatchHost.StartAsync(
            solutionPath: playground.SolutionPath,
            rules: rules,
            buildCacheDir: null,
            output: new StringWriter(),
            watch: false,
            workingDirectory: workingDirectory
        );

        // A hazards tree: the arm that touches the MOST cached artifacts on the store path (the forest, both
        // render sidecars, the hazard-effect set, the graph-tier findings and the EP-site map all have cache.db
        // slots there), so if any live arm leaked onto disk this is the query that would show it.
        var live = new LiveFactSource(await host.GetCurrentFactsAsync(), rules);
        await RunLiveAsync(DefaultOptions("TeamWorkflow.ProcessBatchAsync") with { View = "hazards" }, live, workingDirectory);
        await RunLiveAsync(DefaultOptions("TeamWorkflow.ProcessBatchAsync") with { View = "full" }, live, workingDirectory);

        CacheDbFingerprint(workingDirectory).ShouldBe(before, "a live tree query wrote to a .rig cache.db");

        // The positive control: the SAME query against the store does create it. Without this the assertion
        // above would also pass if cache.db simply never existed on this platform.
        var storeOut = new StringWriter();
        var storeErr = new StringWriter();
        (
            await CliApplication.RunAsync(
                ["tree", "TeamWorkflow.ProcessBatchAsync", "--view", "hazards"],
                storeOut,
                storeErr,
                workingDirectory
            )
        ).ShouldBe(0, storeOut.ToString() + storeErr.ToString());
        CacheDbFingerprint(workingDirectory)
            .ShouldNotBe(before, "the STORE path did not write a cache.db — the negative assertion above is vacuous");
    }

    // THE STORE PATH IS UNCHANGED — the other half of the cache claim, and the one a refactor is most likely to
    // break silently. The disk cache now runs through IQueryArtifactCache instead of an inline QueryCache, so
    // its ROUND TRIP (encode -> cache.db -> decode -> render) has to still produce the same answer as the cold
    // compute did. A cold run then a warm run of the SAME query must be byte-identical: the warm one takes the
    // full-hit branch (forest + locations + seam from cache.db, NO graph load at all), which is a genuinely
    // different code path through the renderer, not a repeat of the first.
    //
    // `--view hazards` on purpose: it is the query that populates the most slots (forest, locations, the
    // hazards-namespaced seam, the hazard-effect set and the graph-tier findings), so a codec or key regression
    // has the most surface to show up on.
    [Test]
    public async Task The_store_tree_answer_is_identical_cold_and_warm()
    {
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        var workingDirectory = playground.WorkingDirectory;
        var indexLog = new StringWriter();
        (await CliApplication.RunAsync(["index", playground.SolutionPath], indexLog, indexLog, workingDirectory)).ShouldBe(
            0,
            indexLog.ToString()
        );

        string[] args = ["tree", "TeamWorkflow.ProcessBatchAsync", "--view", "hazards"];
        var coldOut = new StringWriter();
        var coldErr = new StringWriter();
        (await CliApplication.RunAsync(args, coldOut, coldErr, workingDirectory)).ShouldBe(0, coldOut.ToString() + coldErr.ToString());
        // Anti-vacuity: the cold run must have produced a real hazards answer AND populated the cache, or
        // "identical" below would just be two cold runs agreeing.
        coldOut.ToString().ShouldContain("⚠ n_plus_1(high)");
        CacheDbFingerprint(workingDirectory).ShouldNotBe("(none)", "the cold store run wrote no cache.db — the warm run below is not warm");

        var warmOut = new StringWriter();
        var warmErr = new StringWriter();
        (await CliApplication.RunAsync(args, warmOut, warmErr, workingDirectory)).ShouldBe(0, warmOut.ToString() + warmErr.ToString());
        warmOut.ToString().ShouldBe(coldOut.ToString());
        warmErr.ToString().ShouldBe(coldErr.ToString());

        // …and --no-cache, which must bypass every slot and still give the same answer.
        var uncachedOut = new StringWriter();
        var uncachedErr = new StringWriter();
        (await CliApplication.RunAsync([.. args, "--no-cache"], uncachedOut, uncachedErr, workingDirectory)).ShouldBe(
            0,
            uncachedOut.ToString() + uncachedErr.ToString()
        );
        uncachedOut.ToString().ShouldBe(coldOut.ToString());
        uncachedErr.ToString().ShouldBe(coldErr.ToString());
    }

    // `tree` is DISPATCHED by the live query surface (not rejected as unsupported), and the usage banner names
    // it. A verb that is implemented but unroutable would pass every parity test above and still be unreachable
    // for a user, which is why this is asserted separately.
    [Test]
    public async Task The_live_query_surface_routes_tree()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        var rules = RuleSetLoader.Load(playground.WorkingDirectory);
        await using var host = await WatchHost.StartAsync(
            solutionPath: playground.SolutionPath,
            rules: rules,
            buildCacheDir: null,
            output: new StringWriter(),
            watch: false,
            workingDirectory: playground.WorkingDirectory
        );

        var tree = await host.AnswerQueryAsync("tree BookingController.Book");
        tree.ShouldNotContain("live: unsupported query");
        // DeepChain derives no effects under the default rules, so the DEFAULT (effectful-paths) view's honest
        // answer is the pruned-everything disclosure — which is itself a real answer and the right one.
        tree.ShouldContain("No effects reachable from 'BookingController.Book'.");

        var blank = await host.AnswerQueryAsync("tree");
        blank.ShouldContain("`tree` needs an entry-point pattern");

        LiveQueryRunner.Usage.ShouldContain("`tree <pattern>`");
    }

    // The two LiveReads twins this slice added, asserted directly against a real store rather than only through
    // the rendered tree — the same discipline LiveFactSourceParityTests applies to every other twin. A rendering
    // can agree while a projection is subtly wrong (an extra row that nothing happens to render).
    [Test]
    public async Task The_new_LiveReads_twins_match_the_store()
    {
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        var workingDirectory = playground.WorkingDirectory;
        var indexLog = new StringWriter();
        (await CliApplication.RunAsync(["index", playground.SolutionPath], indexLog, indexLog, workingDirectory)).ShouldBe(
            0,
            indexLog.ToString()
        );

        var rules = RuleSetLoader.Load(workingDirectory);
        await using var host = await WatchHost.StartAsync(
            solutionPath: playground.SolutionPath,
            rules: rules,
            buildCacheDir: null,
            output: new StringWriter(),
            watch: false,
            workingDirectory: workingDirectory
        );
        var facts = await host.GetCurrentFactsAsync();

        await using var source = (StoreQueryFactSource)
            await StoreQueryFactSource.OpenAsync(new WorkspaceLocation(WorkingDirectory: workingDirectory));

        // Bounded to the whole method universe, which is what `--view full` on a big tree approaches.
        var enclosing = LiveReads.DeadCodeMethods(facts).Select(m => m.SymbolId).ToList();
        var storeLibCalls = await source.LibraryCallSitesAsync(enclosing);
        var liveLibCalls = LiveReads.LibraryCallSites(facts, enclosing);
        storeLibCalls.Count.ShouldBeGreaterThan(0, "no library call sites in the store — the comparison would be vacuous");
        liveLibCalls
            .Select(Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ShouldBe(storeLibCalls.Select(Key).OrderBy(k => k, StringComparer.Ordinal));

        // The static-field universe (the static_init_capture gate's feed).
        var storeStatics = await Reads.LoadStaticFieldIdsAsync(await OpenRawContextAsync(workingDirectory));
        var liveStatics = LiveReads.StaticFieldIds(facts);
        storeStatics.Count.ShouldBeGreaterThan(0, "no static fields in the store — the comparison would be vacuous");
        liveStatics.OrderBy(x => x, StringComparer.Ordinal).ShouldBe(storeStatics.OrderBy(x => x, StringComparer.Ordinal));

        Report($"[live/tree twins] libCalls={storeLibCalls.Count}, staticFields={storeStatics.Count}");

        static string Key(SymbolRef r) => $"{r.Enclosing}|{r.Target}|{r.FilePath}|{r.Line}|{r.EnclosingGuards}";
    }

    // Every `cache.db` under the working directory's `.rig`, with its size — the observable footprint of the
    // disk query cache. "(none)" when the cache has never been opened.
    private static string CacheDbFingerprint(string workingDirectory)
    {
        var rig = Path.Combine(workingDirectory, ".rig");
        if (!Directory.Exists(rig))
        {
            return "(none)";
        }

        var found = Directory
            .EnumerateFiles(rig, "cache.db", SearchOption.AllDirectories)
            .Select(f => $"{Path.GetRelativePath(rig, f)}:{new FileInfo(f).Length}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        return found.Count == 0 ? "(none)" : string.Join(";", found);
    }

    private static async Task<Rig.Storage.Storage.RigDbContext> OpenRawContextAsync(string workingDirectory) =>
        await Rig.Cli.Graph.TraversalGraphLoader.OpenReadContextGatedAsync(new WorkspaceLocation(WorkingDirectory: workingDirectory));

    private static int NodeCount(IEnumerable<TraceNode> nodes) => nodes.Sum(n => 1 + NodeCount(n.Children));

    // Run one tree query against the resident facts, through the SAME TreeCommand body the CLI uses.
    private static async Task<(int Exit, string Out, string Err)> RunLiveAsync(
        TreeCommand.Options options,
        LiveFactSource live,
        string workingDirectory
    )
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var source = new LiveQueryFactSource(live);
        var exit = await TreeCommand.RunAsync(
            options,
            new CommandIo(new TextOutput(Output: output, Error: error), new WorkspaceLocation(WorkingDirectory: workingDirectory)),
            () => Task.FromResult<IQueryFactSource>(source)
        );
        return (exit, output.ToString(), error.ToString());
    }

    // Index the tree to a real store in the SAME working directory the live host uses (so both sides resolve
    // the identical rig.rules.json / deployments.json), then compare the two renderings case by case.
    private static async Task AssertLiveEqualsStoreAsync(
        string label,
        string workingDirectory,
        string solutionPath,
        IReadOnlyList<TreeCase> cases
    )
    {
        var indexLog = new StringWriter();
        (await CliApplication.RunAsync(["index", solutionPath], indexLog, indexLog, workingDirectory)).ShouldBe(0, indexLog.ToString());

        var rules = RuleSetLoader.Load(workingDirectory);
        await using var host = await WatchHost.StartAsync(
            solutionPath: solutionPath,
            rules: rules,
            buildCacheDir: null,
            output: new StringWriter(),
            watch: false,
            workingDirectory: workingDirectory
        );
        var live = new LiveFactSource(await host.GetCurrentFactsAsync(), rules);

        var differences = new StringBuilder();
        var compared = 0;
        foreach (var query in cases)
        {
            var name = string.Join(' ', query.StoreArgs);
            var storeOut = new StringWriter();
            var storeErr = new StringWriter();
            var storeExit = await CliApplication.RunAsync(query.StoreArgs, storeOut, storeErr, workingDirectory);

            var answer = await RunLiveAsync(query.LiveOptions, live, workingDirectory);
            Report($"[live/parity] {label} '{name}' STORE (exit {storeExit}):{Environment.NewLine}{storeOut}{storeErr}");

            // Anti-vacuity: every case must be a real, expected answer on the store side.
            storeExit.ShouldBe(
                query.ExpectedStoreExit,
                $"[{label}] '{name}' did not resolve as expected on the STORE side — the comparison would be vacuous.{storeOut}{storeErr}"
            );
            if (query.StoreMustContain.Length > 0)
            {
                storeOut.ToString().ShouldContain(query.StoreMustContain);
            }

            // Both streams are byte-for-byte; ambiguity and filter/guard disclosures are answer-local too.
            Diff(differences, label, name, "stdout", storeOut.ToString(), answer.Out);
            Diff(differences, label, name, "stderr", storeErr.ToString(), answer.Err);
            answer.Exit.ShouldBe(storeExit, $"[{label}] '{name}': exit code differs (store={storeExit}, live={answer.Exit}).");
            if (query.RequiredInAnswer is { } required)
            {
                answer
                    .Out.Contains(required, StringComparison.Ordinal)
                    .ShouldBeTrue($"[{label}] '{name}': the live answer is missing '{required}' — that feature is not being exercised.");
            }

            compared++;
        }

        compared.ShouldBe(cases.Count);
        Report($"[live/parity] {label}: {compared} case(s) compared, derived layer: {live.BuildTimeLine()}");
        differences.Length.ShouldBe(0, $"[{label}] the LIVE tree answer differs from the STORE answer:{Environment.NewLine}{differences}");
    }

    // Line-by-line, because a whole-blob mismatch message on a long tree is unreadable and the useful
    // information is WHICH line moved.
    private static void Diff(StringBuilder into, string label, string query, string stream, string store, string live)
    {
        if (string.Equals(store, live, StringComparison.Ordinal))
        {
            return;
        }

        var storeLines = store.Split(Environment.NewLine);
        var liveLines = live.Split(Environment.NewLine);
        into.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}--- [{label}] {query} ({stream}) ---");
        var shown = 0;
        for (var i = 0; i < Math.Max(storeLines.Length, liveLines.Length) && shown < 40; i++)
        {
            var s = i < storeLines.Length ? storeLines[i] : "<absent>";
            var l = i < liveLines.Length ? liveLines[i] : "<absent>";
            if (!string.Equals(s, l, StringComparison.Ordinal))
            {
                shown++;
                into.Append(
                    CultureInfo.InvariantCulture,
                    $"{Environment.NewLine}  line {i + 1} STORE: {s}{Environment.NewLine}  line {i + 1} LIVE : {l}"
                );
            }
        }
    }

    // Measurements and the compared answers, to a FILE (RIG_LIVE_REPORT) — never Console, which TUnit swallows
    // in its default mode. Assertion failures never depend on this: the differing lines go in the message.
    private static void Report(string block)
    {
        var path = Environment.GetEnvironmentVariable("RIG_LIVE_REPORT");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        lock (ReportLock)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    File.AppendAllText(path, block + Environment.NewLine);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(10);
                }
            }
        }
    }
}
