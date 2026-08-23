using System.Globalization;
using System.Text;
using Rig.Analysis.Rules;
using Rig.Cli;
using Rig.Cli.CommandLine;
using Rig.Cli.Commands;
using Rig.Cli.Live;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Live;

// The `path` / `callers` half of the live-answer gate, mirroring LiveReachesTests: for each query the same tree
// is indexed to a real .rig store and asked through the CLI, then asked through the resident live source, and
// the two renderings are compared as text. Separate file (not an extension of LiveReachesTests) because these
// two commands take a DIFFERENT load — the graph-only LoadShapedTraversalGraphAsync, with no effect derivation
// at all — and `callers` walks it in REVERSE, which nothing on the live path had exercised before this slice.
//
// What makes the comparison non-trivial here, and what it is actually proving:
//
//  1. REVERSE DIRECTION. The store's bounded loader narrows the load to the REVERSE closure of the target;
//     the live source has no SQL to bound with and hands back the whole shaped graph. Reverse reachability is
//     set-based BFS, and `callers` layers a forward-verification pass, a reverse FRONTIER computation and an
//     async-handoff probe on top — each of which reads `graph.CallEdges` / `graph.Methods` WHOLESALE, not just
//     the reachable slice. So "the traversal narrows the superset identically" is a claim about several
//     distinct computations, not one, and this is where it is measured.
//
//  2. THE `path` LOAD BANNER IS EXCLUDED, and only it. `path` prints "Fact graph: N call edges, M implements
//     edges, K methods" — the size of the subgraph that was LOADED, not an answer. It has ALWAYS depended on
//     the loader rather than the question: the same store reports the bounded forward slice with `rig graph`
//     run and the full EF graph without it. The live source is a third such value. That line is therefore
//     dropped from BOTH sides and the drop is asserted to be the ONLY difference — every other line of the
//     path, including the deployment chip on the from-node, is compared byte-for-byte.
//
//  3. THE NEGATIVE PATHS ARE COVERED ON PURPOSE. `path`'s two seed-resolution arms are the reason
//     SymbolExistsAnywhereAsync exists at all: "the `to` pattern names nothing" must stay distinguishable from
//     "both endpoints exist but do not connect", and on the live path that probe runs over the in-memory
//     symbol facts instead of the store's FTS index. A case of each is compared.
//
//  4. ONE MEASURED STDERR EXCEPTION, pinned rather than wished away — `path`'s AMBIGUITY note for a `to`
//     pattern that resolves OUTSIDE the from-node's slice. AmbiguityNotice is evaluated against the LOADED
//     graph, so a multi-target `to` that the bounded forward slice does not contain draws no note from the
//     SQL-bounded store, while the live path (whole graph) names all its targets. Neither side is right, and
//     the tell is that THE STORE DISAGREES WITH ITSELF — same store, same question, the two arms of its own
//     loader:
//
//         rig index --no-graph DeepChain.slnx && rig path Db.Query Book
//           note: pattern 'Book' matched 6 distinct symbols (…)
//           Fact graph: 5 call edges, 4 implements edges, 17 methods
//         rig graph && rig path Db.Query Book
//           Fact graph: 0 call edges, 4 implements edges, 1 methods        (no note)
//
//     The live answer is byte-identical to the store's EF-fallback arm — i.e. this is the SAME pre-existing
//     bug as the `--intrinsic` hint LiveReachesTests pins (a disclosure computed off whatever set got loaded
//     instead of off the pattern's resolution scope), surfaced by a second command. The honest fix resolves
//     the ambiguity disclosure independently of the loaded subgraph, which changes the STORE path's output and
//     is therefore not this slice's business. Until then it is opted out per case (AmbiguityNoteMayDiffer) and
//     the asymmetry is asserted to be one-directional: the live side may ADD the note, never drop it.
//
// Measurements go to the file named by RIG_LIVE_REPORT, never Console — TUnit swallows console output in its
// default mode.
public sealed class LivePathCallersTests
{
    private static readonly object ReportLock = new();

    // One comparison: what the CLI is asked, what the live surface is asked, and the marker that proves the
    // STORE side actually answered (anti-vacuity — two empty answers would compare equal and prove nothing).
    //
    // `AmbiguityNoteMayDiffer` opts a single case out of the byte-exact STDERR comparison for the ambiguity
    // note ONLY — see the note-4 block in the class header. It is per-case on purpose: every other case still
    // compares stderr byte-for-byte, so a genuine disclosure divergence cannot hide behind a blanket filter.
    private sealed record Case(
        string[] StoreArgs,
        string LiveQuery,
        string StoreMustContain,
        int ExpectedStoreExit = 0,
        bool AmbiguityNoteMayDiffer = false
    );

    // ---- DeepChain: a 7-project reference chain, one DB effect five project hops down through an interface.
    // For `callers` that means the reverse walk crosses reverse interface dispatch at two seams.
    private static readonly Case[] DeepChainCallers =
    [
        new(["callers", "Db.Query"], "callers Db.Query", "Methods that reach 'Db.Query'"),
        new(
            ["callers", "PatientRepository.GetById"],
            "callers PatientRepository.GetById",
            "Methods that reach 'PatientRepository.GetById'"
        ),
        new(["callers", "BookingService.Book"], "callers BookingService.Book", "Methods that reach 'BookingService.Book'"),
        new(["callers", "ChannelBase.Notify"], "callers ChannelBase.Notify", "Methods that reach 'ChannelBase.Notify'"),
        new(["callers", "HomePage.Show"], "callers HomePage.Show", "Methods that reach 'HomePage.Show'"),
    ];

    private static readonly Case[] DeepChainPaths =
    [
        new(["path", "HomePage.Show", "Db.Query"], "path HomePage.Show Db.Query", "Path 'HomePage.Show' -> 'Db.Query'"),
        new(
            ["path", "BookingController.Book", "PatientRepository.GetById"],
            "path BookingController.Book PatientRepository.GetById",
            "Path 'BookingController.Book' -> 'PatientRepository.GetById'"
        ),
        new(["path", "BookingService.Book", "Db.Query"], "path BookingService.Book Db.Query", "Path 'BookingService.Book' -> 'Db.Query'"),
        // NEGATIVE 1: both endpoints exist, nothing connects them (the chain runs the other way). Exercises the
        // arm where the graph miss triggers the store-wide existence probe and the probe says "yes, it exists".
        new(
            ["path", "Db.Query", "HomePage.Show"],
            "path Db.Query HomePage.Show",
            "No path from 'Db.Query' to 'HomePage.Show'.",
            ExpectedStoreExit: 1
        ),
        // NEGATIVE 2: the `to` pattern names nothing at all — the probe says "no", so the answer is the
        // no-symbol disclosure rather than a connectivity claim.
        new(
            ["path", "HomePage.Show", "NoSuchSymbolAnywhereInDeepChain"],
            "path HomePage.Show NoSuchSymbolAnywhereInDeepChain",
            "No symbol matches 'NoSuchSymbolAnywhereInDeepChain' (the 'to' endpoint).",
            ExpectedStoreExit: 1
        ),
        // NEGATIVE 3, and the case that FOUND note (4): `Book` resolves to six symbols, none of which is in
        // `Db.Query`'s (empty) forward slice. The SQL-bounded store therefore stays silent about the ambiguity
        // while the live path names all six — a divergence the store also has against its own EF-fallback arm.
        new(
            ["path", "Db.Query", "Book"],
            "path Db.Query Book",
            "No path from 'Db.Query' to 'Book'.",
            ExpectedStoreExit: 1,
            AmbiguityNoteMayDiffer: true
        ),
    ];

    // ---- EntryPointEffects: the effect-rich playground (EF Core, Redis, HttpClient, a real C# event with
    // delivery edges, loops, cycles). Neither `path` nor `callers` derives effects, but its graph exercises the
    // shaping seams — event-subscription handoff reclassification, cycles, delivery edges — that DeepChain's
    // straight chain does not, and those are exactly what a reverse walk can get wrong.
    private static readonly Case[] EffectRichCallers =
    [
        new(["callers", "TeamRepository.AddAsync"], "callers TeamRepository.AddAsync", "Methods that reach 'TeamRepository.AddAsync'"),
        new(
            ["callers", "BillingClient.LoadInvoicesAsync"],
            "callers BillingClient.LoadInvoicesAsync",
            "Methods that reach 'BillingClient.LoadInvoicesAsync'"
        ),
        new(["callers", "SavePublisher.Raise"], "callers SavePublisher.Raise", "Methods that reach 'SavePublisher.Raise'"),
        new(["callers", "CycleFixture.MutualA"], "callers CycleFixture.MutualA", "Methods that reach 'CycleFixture.MutualA'"),
        new(
            ["callers", "TeamWorkflow.ProcessBatchAsync"],
            "callers TeamWorkflow.ProcessBatchAsync",
            "Methods that reach 'TeamWorkflow.ProcessBatchAsync'"
        ),
    ];

    private static readonly Case[] EffectRichPaths =
    [
        // Through the single-implementation INTERFACE seam (`ITeamRepository` -> `TeamRepository`), the shape a
        // `path` answer most often has to cross and the one a shaping difference would break.
        new(
            ["path", "TeamsController.CreateViaInterface", "TeamRepository.AddAsync"],
            "path TeamsController.CreateViaInterface TeamRepository.AddAsync",
            "Path 'TeamsController.CreateViaInterface' -> 'TeamRepository.AddAsync'"
        ),
        new(
            ["path", "TeamsController.Get", "BillingClient.LoadInvoicesAsync"],
            "path TeamsController.Get BillingClient.LoadInvoicesAsync",
            "Path 'TeamsController.Get' -> 'BillingClient.LoadInvoicesAsync'"
        ),
        new(
            ["path", "TeamsController.Create", "TeamWorkflow.CreateTeamAsync"],
            "path TeamsController.Create TeamWorkflow.CreateTeamAsync",
            "Path 'TeamsController.Create' -> 'TeamWorkflow.CreateTeamAsync'"
        ),
        new(
            ["path", "CycleFixture.MutualA", "CycleFixture.MutualB"],
            "path CycleFixture.MutualA CycleFixture.MutualB",
            "Path 'CycleFixture.MutualA' -> 'CycleFixture.MutualB'"
        ),
    ];

    [Test]
    public async Task Live_callers_equals_the_store_answer_on_deep_chain()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        await AssertLiveEqualsStoreAsync("DeepChain/callers", playground.WorkingDirectory, playground.SolutionPath, DeepChainCallers);
    }

    [Test]
    public async Task Live_callers_equals_the_store_answer_on_the_effect_rich_playground()
    {
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        await AssertLiveEqualsStoreAsync(
            "EntryPointEffects/callers",
            playground.WorkingDirectory,
            playground.SolutionPath,
            EffectRichCallers
        );
    }

    [Test]
    public async Task Live_path_equals_the_store_answer_on_deep_chain()
    {
        using var playground = await DeepChainPlayground.CreateAsync();

        // DEPLOYMENT ATTRIBUTION ON, for the same reason LiveReachesTests turns it on: without a
        // deployments.json both paths short-circuit to DeploymentMap.Empty and BuildEpContextAsync returns null
        // before doing any work, so the live EP-context branch would never execute and the comparison would
        // silently cover none of it. `path` renders that context as the chip on its from-node.
        await File.WriteAllTextAsync(
            Path.Combine(playground.WorkingDirectory, "deployments.json"),
            """{ "services": [ { "name": "web", "host": "Web/Web.csproj", "kind": "site" } ] }"""
        );

        await AssertLiveEqualsStoreAsync(
            "DeepChain/path",
            playground.WorkingDirectory,
            playground.SolutionPath,
            DeepChainPaths,
            // …and asserted to be ON, not merely configured: a regression that dropped the chip on BOTH paths
            // would still compare equal. Only the positive cases render a from-node, so the guard is scoped to
            // the answers that have one.
            requiredWhenExitZero: "⟦web (site)⟧"
        );
    }

    [Test]
    public async Task Live_path_equals_the_store_answer_on_the_effect_rich_playground()
    {
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        await AssertLiveEqualsStoreAsync("EntryPointEffects/path", playground.WorkingDirectory, playground.SolutionPath, EffectRichPaths);
    }

    // `callers --entrypoints` is driven through the COMMAND rather than through LiveQueryRunner, because the
    // live query surface deliberately exposes no flags yet. It is compared anyway, and separately, because it
    // is the only consumer of the two entry-point members this slice added to IQueryFactSource — and their live
    // arm is the riskiest thing in the slice: it re-derives the handoff entry points from an in-memory graph
    // projection, where the store's fast arm reads the `call_edges` table `rig graph` materialized instead.
    [Test]
    public async Task Live_callers_entrypoints_equals_the_store_answer()
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
        var live = new LiveFactSource(await host.GetCurrentFactsAsync(), rules);

        var differences = new StringBuilder();
        var compared = 0;
        string[] targets = ["TeamRepository.AddAsync", "BillingClient.LoadInvoicesAsync", "TeamWorkflow.LoadTeamSummaryAsync"];
        foreach (var target in targets)
        {
            var storeOut = new StringWriter();
            var storeErr = new StringWriter();
            var storeExit = await CliApplication.RunAsync(["callers", target, "--entrypoints"], storeOut, storeErr, workingDirectory);

            var liveOut = new StringWriter();
            var liveErr = new StringWriter();
            var source = new LiveQueryFactSource(live);
            var liveExit = await CallersCommand.RunAsync(
                new CallersCommand.Options(
                    ToPattern: target,
                    RootsOnly: false,
                    EntrypointsOnly: true,
                    IncludeReverseOnly: false,
                    Async: false,
                    IncludeDelivery: false,
                    Raw: false,
                    ExtraRules: [],
                    Depth: null,
                    Format: null,
                    Limit: null,
                    Time: false
                ),
                new CommandIo(new TextOutput(Output: liveOut, Error: liveErr), new WorkspaceLocation(WorkingDirectory: workingDirectory)),
                () => Task.FromResult<IQueryFactSource>(source)
            );

            Report($"[live/callers --entrypoints] '{target}' STORE (exit {storeExit}):{Environment.NewLine}{storeOut}{storeErr}");
            // Anti-vacuity: the rule-detected EP set must be non-empty on the store side, or "equal" means
            // "both said nothing" — and the target must be reachable from a real entry point, which is the
            // whole thing this lens reports.
            storeExit.ShouldBe(
                0,
                $"'{target}' has no rule-detected entry points on the STORE side — the comparison would be vacuous.{storeOut}{storeErr}"
            );
            storeOut.ToString().ShouldContain($"Rule-detected entry points reaching '{target}'");
            Diff(differences, "callers --entrypoints", target, "stdout", storeOut.ToString(), liveOut.ToString());
            Diff(
                differences,
                "callers --entrypoints",
                target,
                "stderr",
                AnswerStreamParity.WithoutImmutableStoreDisclosure(storeErr.ToString()),
                liveErr.ToString()
            );
            liveExit.ShouldBe(storeExit, $"'{target}': exit code differs (store={storeExit}, live={liveExit}).");
            compared++;
        }

        compared.ShouldBe(targets.Length);
        differences.Length.ShouldBe(
            0,
            $"the LIVE `callers --entrypoints` answer differs from the STORE answer:{Environment.NewLine}{differences}"
        );
    }

    // The `rig watch --query` surface for the two new verbs: they are DISPATCHED (not rejected as unsupported),
    // the two-argument form is tokenized, and a malformed invocation says what is supported instead of guessing
    // at endpoints. A verb that is implemented but unroutable would pass every parity test above and still be
    // unreachable for a user, which is why this is asserted separately.
    [Test]
    public async Task The_live_query_surface_routes_path_and_callers()
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

        var callers = await host.AnswerQueryAsync("callers Db.Query");
        callers.ShouldContain("Methods that reach 'Db.Query'");
        callers.ShouldContain("PatientRepository.GetById");

        var path = await host.AnswerQueryAsync("path HomePage.Show Db.Query");
        path.ShouldContain("Path 'HomePage.Show' -> 'Db.Query'");

        // Quoted grouping, so a pattern containing spaces survives as ONE endpoint. The DocID here has none,
        // but the quotes must not end up inside the pattern either — an answer proves they were stripped.
        var quoted = await host.AnswerQueryAsync("""path "HomePage.Show" "Db.Query" """);
        quoted.ShouldContain("Path 'HomePage.Show' -> 'Db.Query'");

        // Malformed: one endpoint is not two. Rejected with the usage banner, not silently half-answered.
        var oneEndpoint = await host.AnswerQueryAsync("path HomePage.Show");
        oneEndpoint.ShouldContain("`path` needs exactly two patterns");
        oneEndpoint.ShouldContain("`path <from> <to>`");
        var noTarget = await host.AnswerQueryAsync("callers");
        noTarget.ShouldContain("`callers` needs a target pattern");

        // The usage banner names every verb it routes — an inaccurate banner is a worse failure than a missing
        // feature, because it tells the user the feature does not exist.
        LiveQueryRunner.Usage.ShouldContain("`reaches <pattern>`");
        LiveQueryRunner.Usage.ShouldContain("`path <from> <to>`");
        LiveQueryRunner.Usage.ShouldContain("`callers <to>`");
    }

    // Index the tree to a real store in the SAME working directory the live host uses (so both sides resolve
    // the identical rig.rules.json / deployments.json), then compare the two renderings case by case.
    private static async Task AssertLiveEqualsStoreAsync(
        string label,
        string workingDirectory,
        string solutionPath,
        IReadOnlyList<Case> cases,
        string? requiredWhenExitZero = null
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
            var storeOut = new StringWriter();
            var storeErr = new StringWriter();
            var storeExit = await CliApplication.RunAsync(query.StoreArgs, storeOut, storeErr, workingDirectory);

            var answer = await LiveQueryRunner.AnswerAsync(query.LiveQuery, live, workingDirectory);
            Report($"[live/parity] {label} '{query.LiveQuery}' STORE (exit {storeExit}):{Environment.NewLine}{storeOut}{storeErr}");

            // Anti-vacuity: every case must be a real, expected answer on the store side.
            storeExit.ShouldBe(
                query.ExpectedStoreExit,
                $"[{label}] '{query.LiveQuery}' did not resolve as expected on the STORE side — the comparison would be vacuous.{storeOut}{storeErr}"
            );
            storeOut.ToString().ShouldContain(query.StoreMustContain);

            // stdout with the `path` LOAD banner dropped from both sides — see the header note (2). Everything
            // else, including the deployment chip and every path step, still has to match exactly.
            Diff(differences, label, query.LiveQuery, "stdout", WithoutLoadBanner(storeOut.ToString()), WithoutLoadBanner(answer.Out));
            // stderr byte-for-byte, EXCEPT the one opted-out case where the ambiguity note legitimately
            // differs (header note 4) — and there the asymmetry is asserted to be one-directional.
            if (query.AmbiguityNoteMayDiffer)
            {
                Diff(
                    differences,
                    label,
                    query.LiveQuery,
                    "stderr",
                    WithoutAmbiguityNote(AnswerStreamParity.WithoutImmutableStoreDisclosure(storeErr.ToString())),
                    WithoutAmbiguityNote(answer.Err)
                );
                if (storeErr.ToString().Contains(AmbiguityNote, StringComparison.Ordinal))
                {
                    answer
                        .Err.Contains(AmbiguityNote, StringComparison.Ordinal)
                        .ShouldBeTrue($"[{label}] '{query.LiveQuery}': the store disclosed an ambiguity and the live path did not.");
                }
            }
            else
            {
                Diff(
                    differences,
                    label,
                    query.LiveQuery,
                    "stderr",
                    AnswerStreamParity.WithoutImmutableStoreDisclosure(storeErr.ToString()),
                    answer.Err
                );
            }
            // …and the excluded line is the ONLY difference of its kind: both sides must have emitted one
            // (a dropped banner on one side only would otherwise hide inside the exclusion).
            (
                storeOut.ToString().Contains(LoadBanner, StringComparison.Ordinal)
                == answer.Out.Contains(LoadBanner, StringComparison.Ordinal)
            ).ShouldBeTrue($"[{label}] '{query.LiveQuery}': one side emitted the '{LoadBanner}' banner and the other did not.");

            answer.Exit.ShouldBe(storeExit, $"[{label}] '{query.LiveQuery}': exit code differs (store={storeExit}, live={answer.Exit}).");
            if (requiredWhenExitZero is not null && storeExit == 0)
            {
                answer
                    .Out.Contains(requiredWhenExitZero, StringComparison.Ordinal)
                    .ShouldBeTrue(
                        $"[{label}] '{query.LiveQuery}': the live answer is missing '{requiredWhenExitZero}' — that feature is not being exercised."
                    );
            }

            compared++;
        }

        compared.ShouldBe(cases.Count);
        Report($"[live/parity] {label}: {compared} case(s) compared, derived layer: {live.BuildTimeLine()}");
        differences.Length.ShouldBe(0, $"[{label}] the LIVE answer differs from the STORE answer:{Environment.NewLine}{differences}");
    }

    // `path`'s load diagnostic — the ONE line whose value is a property of the fact source rather than of the
    // question. See the header note (2) for why it is excluded rather than reconciled.
    private const string LoadBanner = "Fact graph:";

    // AmbiguityNotice's disclosure, evaluated against the LOADED graph and therefore loader-dependent — see
    // header note (4). Excluded per case, never globally.
    private const string AmbiguityNote = "note: pattern '";

    private static string WithoutAmbiguityNote(string stream) =>
        string.Join(
            Environment.NewLine,
            stream.Split(Environment.NewLine).Where(line => !line.StartsWith(AmbiguityNote, StringComparison.Ordinal))
        );

    private static string WithoutLoadBanner(string stream) =>
        string.Join(
            Environment.NewLine,
            stream.Split(Environment.NewLine).Where(line => !line.StartsWith(LoadBanner, StringComparison.Ordinal))
        );

    // Line-by-line, because a whole-blob mismatch message on a long rendering is unreadable and the useful
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
        for (var i = 0; i < Math.Max(storeLines.Length, liveLines.Length); i++)
        {
            var s = i < storeLines.Length ? storeLines[i] : "<absent>";
            var l = i < liveLines.Length ? liveLines[i] : "<absent>";
            if (!string.Equals(s, l, StringComparison.Ordinal))
            {
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
