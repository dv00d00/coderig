using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text.Json;
using Rig.Analysis.Rules;
using Rig.Cli;
using Rig.Cli.Commands;
using Rig.Cli.Live;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Live;

// THE SLICE THAT MAKES THE RESIDENT INDEX USABLE BY ANYTHING BUT ITSELF. Before this, `rig watch` kept facts
// ~0.75s current and answered only through its own stdin; an agent — which invokes one-shot commands — could
// not reach them at all. These tests pin the transport that closes that: a plain `rig reaches` in a watched
// directory answers from the resident index, and every way that can fail degrades to the store.
//
// What each claim is, and why it is a test rather than a sentence in a doc:
//
//  1. END TO END. A one-shot CLI invocation is answered by the resident host, and its stdout is BYTE-IDENTICAL
//     to what LiveQueryRunner produces in-process for the same question. Byte-identical is the whole bar: a
//     transport that reshapes the answer would make every existing live/store parity gate meaningless.
//  2. THE EDIT IS VISIBLE THROUGH THE TRANSPORT. Edit a file, and a one-shot query reflects it while the
//     STORE — unchanged, and asked in the same directory in the same second — does not. This is the entire
//     point of the program, and the store half is the anti-vacuity: without it, a test could pass against a
//     transport that answered from the store all along.
//  3. FALLBACK IS INVISIBLE WHEN THERE IS NO HOST. Same stdout, same stderr, same exit code as `--no-live`,
//     and no `live:` line anywhere. That is a HARD requirement, not a nicety: routing is on by default, so
//     every existing rig user runs this code path on every query, and it must cost and print nothing.
//  4. EVERY TRANSPORT FAILURE FALLS BACK, AND NONE HANGS. A host that closes without answering, a host that
//     never answers, a host watching a DIFFERENT tree — each ends with a store answer and a stated reason.
//     The wrong-tree case additionally asserts the host's serve callback was never INVOKED: a request about
//     another directory must not reach the query layer even to be rejected there.
//
// Measurements go to the file named by RIG_LIVE_REPORT, never Console (TUnit does not surface console output
// in its default mode — a lesson this program has paid for repeatedly).
public sealed class LiveTransportTests
{
    private static readonly object ReportLock = new();

    private const string SourceLinePrefix = "live: facts from resident index —";
    private const string CostLinePrefix = "live: derived layer built this generation:";

    // ---------------------------------------------------------------------------------------------------
    // 1. End to end: a one-shot CLI query answered by the resident host.
    // ---------------------------------------------------------------------------------------------------

    [Test]
    public async Task A_one_shot_cli_query_is_answered_by_the_resident_host_byte_identically()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        var rules = RuleSetLoader.Load(playground.WorkingDirectory);
        var hostLog = new StringWriter();

        await using var host = await WatchHost.StartAsync(
            solutionPath: playground.SolutionPath,
            rules: rules,
            buildCacheDir: null,
            output: hostLog,
            watch: false,
            workingDirectory: playground.WorkingDirectory
        );
        await using var server = LiveQueryServer.Start(playground.WorkingDirectory, host.ServeAsync, hostLog);
        (await server.WaitUntilReadyAsync(TimeSpan.FromSeconds(10))).ShouldBeTrue("the resident host never published its endpoint");

        // THE ROUTED INVOCATION. Note what is NOT here: no `rig index`, no `.rig/rig.db`. A store-backed
        // `rig reaches` in this directory would fail with "No .rig store found", so an answer at all is
        // already proof the resident host served it.
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = await CliApplication.RunAsync(["reaches", "HomePage.Show"], output, error, playground.WorkingDirectory);

        var routedOut = output.ToString();
        var routedErr = error.ToString();
        Report($"[transport/e2e] routed exit {exitCode}, stdout:{Environment.NewLine}{routedOut}stderr:{Environment.NewLine}{routedErr}");

        exitCode.ShouldBe(0, routedOut + routedErr);
        routedOut.ShouldContain("From: HomePage.Show");
        routedOut.ShouldContain("Reachable methods (<= depth 2147483647): 8");

        // THE SOURCE IS UNAMBIGUOUS, and it is the FIRST thing on stderr — ahead of the command's own notes.
        routedErr.Split(Environment.NewLine)[0].ShouldBe("live: facts from resident index — 0 file(s) applied | all projects reconciled");
        // …and it is NOT on stdout, deliberately: `--format tsv` has to survive routing, so stdout carries
        // only what the command wrote. A `live:` line there would break every awk consumer rig has.
        routedOut.ShouldNotContain("live:");

        // BYTE-IDENTICAL to the in-process live answer. Run second, so the derived layer is already warm and
        // its cost line (which the routed answer carries on stderr) does not appear on this side.
        var facts = await host.GetCurrentFactsAsync();
        var direct = await LiveQueryRunner.AnswerAsync("reaches HomePage.Show", new LiveFactSource(facts, rules), playground.WorkingDirectory);
        routedOut.ShouldBe(direct.Out, "the routed stdout is not byte-identical to the in-process live answer");
        StripHostLines(routedErr).ShouldBe(direct.Err, "the routed stderr is not the in-process live answer's stderr plus the host's own lines");
        exitCode.ShouldBe(direct.Exit);

        // THE ALLOWLIST, from both ends. Every routable verb reaches a switch arm…
        var live = new LiveFactSource(facts, rules);
        LiveQueryVerbs.Routable.ShouldBe(new HashSet<string> { "reaches", "path", "callers", "tree" }, ignoreOrder: true);
        foreach (var (verb, options) in RoutableRequests())
        {
            var served = await LiveQueryRunner.RunRequestAsync(Request(verb, options, playground.WorkingDirectory), live, playground.WorkingDirectory);
            served.DeclineReason.ShouldBeNull($"`{verb}` is in the routable set but the host declined it");
            served.Answer.ShouldNotBeNull();
        }

        // …and nothing else does. `derive` is a real rig command and a plausible thing to ask for, which is
        // exactly why it must be DECLINED server-side rather than reaching any command body.
        var refused = await LiveQueryRunner.RunRequestAsync(
            Request("derive", new ReachesCommand.Options("X", false, false, false, [], null, null, [], [], false, null, false), playground.WorkingDirectory),
            live,
            playground.WorkingDirectory
        );
        refused.Answer.ShouldBeNull();
        refused.DeclineReason.ShouldNotBeNull();
        refused.DeclineReason.ShouldContain("`derive` is not served from the resident index");

        // AND `--rules` IS DECLINED. The live artifacts are memoized under the rules the facts were extracted
        // with, and the two identity checks guarding those memos were written when the live surface had no
        // --rules flag — both say in so many words that they must be widened if it ever gained one. This
        // transport gives it one (a routed request carries ExtraRules), so the guard is the widening: obeying
        // it is impossible for extraction-time rules and would be silently wrong for the memos, and the store
        // applies --rules correctly one fallback away.
        var withRules = await LiveQueryRunner.RunRequestAsync(
            Request(
                LiveQueryVerbs.Reaches,
                new ReachesCommand.Options("HomePage.Show", false, false, false, ["C:\\nowhere\\extra.json"], null, null, [], [], false, null, false),
                playground.WorkingDirectory
            ),
            live,
            playground.WorkingDirectory
        );
        withRules.Answer.ShouldBeNull();
        withRules.DeclineReason.ShouldNotBeNull();
        withRules.DeclineReason.ShouldContain("--rules is not honoured by the resident index");

        // …and end to end, a `--rules` query in a watched directory lands on the store path with the reason
        // stated, rather than being answered off memos that ignored the flag.
        var (rulesExit, rulesOut, rulesErr) = await RunCliAsync(
            playground.WorkingDirectory,
            "reaches",
            "HomePage.Show",
            "--rules",
            Path.Combine(playground.WorkingDirectory, "does-not-exist.json")
        );
        Report($"[transport/rules] exit {rulesExit}:{Environment.NewLine}{rulesOut}{rulesErr}");
        rulesErr.ShouldContain("--rules is not honoured by the resident index");
        rulesOut.ShouldNotContain("From: HomePage.Show"); // the store path ran, and this playground has no store
        rulesExit.ShouldBe(2);
    }

    // ---------------------------------------------------------------------------------------------------
    // 2. + 4. + the case-insensitive-filter regression, on ONE booted playground (each of these needs a real
    //    store AND a real host in the same directory, which is the expensive part).
    // ---------------------------------------------------------------------------------------------------

    // THE POINT OF THE WHOLE SLICE, as one test: a one-shot CLI query reflects a disk edit that the store,
    // asked in the same directory moments later, does not.
    [Test]
    public async Task A_disk_edit_is_visible_through_the_transport_while_the_store_is_not()
    {
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        var hostLog = new StringWriter();

        // The STORE: indexed BEFORE the edit, so it holds pre-edit facts for the whole test — which is what
        // makes it the honest control. This is also the ordinary state of a store while someone is working.
        var indexLog = new StringWriter();
        (await CliApplication.RunAsync(["index", playground.SolutionPath], indexLog, indexLog, playground.WorkingDirectory)).ShouldBe(
            0,
            indexLog.ToString()
        );

        var rules = RuleSetLoader.Load(playground.WorkingDirectory);
        await using var host = await WatchHost.StartAsync(
            solutionPath: playground.SolutionPath,
            rules: rules,
            buildCacheDir: null,
            output: hostLog,
            watch: true,
            workingDirectory: playground.WorkingDirectory
        );
        await using var server = LiveQueryServer.Start(playground.WorkingDirectory, host.ServeAsync, hostLog);
        (await server.WaitUntilReadyAsync(TimeSpan.FromSeconds(10))).ShouldBeTrue("the resident host never published its endpoint");

        // BEFORE: routed, and AddAsync writes and commits without reading. Asserting the absence is the
        // anti-vacuity half — without it this test would also pass against a source that always said "read".
        var (beforeExit, beforeOut, beforeErr) = await RunCliAsync(playground.WorkingDirectory, "reaches", "TeamRepository.AddAsync");
        Report($"[transport/edit] BEFORE (routed) exit {beforeExit}:{Environment.NewLine}{beforeOut}{beforeErr}");
        beforeExit.ShouldBe(0, beforeOut + beforeErr);
        beforeErr.ShouldContain(SourceLinePrefix);
        beforeOut.ShouldContain("efcore pending_write");
        beforeOut.ShouldContain("efcore commit");
        beforeOut.ShouldNotContain("efcore read");

        // --only is CASE-INSENSITIVE in rig, and a JSON round-trip of a HashSet<string> silently loses the
        // comparer — so `--only EFCORE` would match nothing over the wire while matching everything in
        // process. Checked here rather than in its own test because it needs exactly this booted playground.
        var (filterExit, filterOut, filterErr) = await RunCliAsync(
            playground.WorkingDirectory,
            "reaches",
            "TeamRepository.AddAsync",
            "--only",
            "EFCORE"
        );
        Report($"[transport/filters] --only EFCORE (routed) exit {filterExit}:{Environment.NewLine}{filterOut}{filterErr}");
        filterExit.ShouldBe(0, filterOut + filterErr);
        filterErr.ShouldContain(SourceLinePrefix);
        filterOut
            .Contains("efcore commit", StringComparison.Ordinal)
            .ShouldBeTrue("an upper-case --only matched nothing over the transport: the case-insensitive filter comparer was lost in the round trip");

        // THE EDIT, on disk: AddAsync gains a read of the same DbSet before writing to it.
        var editedFilePath = Path.Combine(playground.WorkingDirectory, "EntryPointEffects.Api", "Services", "TeamRepository.cs");
        var originalText = await File.ReadAllTextAsync(editedFilePath);
        const string Marker = "public async Task AddAsync(Team team)\n    {\n";
        var normalized = originalText.Replace("\r\n", "\n", StringComparison.Ordinal);
        normalized.ShouldContain(Marker);
        await File.WriteAllTextAsync(
            editedFilePath,
            normalized.Replace(Marker, Marker + "        await _db.Teams.ToListAsync();\n", StringComparison.Ordinal)
        );

        // AFTER: poll the ONE-SHOT CLI answer, not an internal counter — what a caller is told is the only
        // thing this slice claims.
        var after = await WaitForCliAnswerAsync(
            playground.WorkingDirectory,
            ["reaches", "TeamRepository.AddAsync"],
            answer => answer.Contains("efcore read", StringComparison.Ordinal),
            TimeSpan.FromSeconds(120),
            "a one-shot `rig reaches` never reflected the disk edit"
        );
        Report($"[transport/edit] AFTER (routed):{Environment.NewLine}{after.Out}{after.Err}");
        after.Out.ShouldContain("efcore read");
        after.Out.ShouldContain("efcore pending_write"); // the pre-existing effects survive the edit
        after.Out.ShouldContain("efcore commit");
        after.Err.ShouldContain(SourceLinePrefix);

        // AND THE STORE, in the same directory, does NOT see it — which is what makes the routed answer worth
        // having, and what proves the routed answer did not come from the store.
        var (storeExit, storeOut, storeErr) = await RunCliAsync(playground.WorkingDirectory, "reaches", "TeamRepository.AddAsync", "--no-live");
        Report($"[transport/edit] AFTER (--no-live, store) exit {storeExit}:{Environment.NewLine}{storeOut}{storeErr}");
        storeExit.ShouldBe(0, storeOut + storeErr);
        storeOut
            .Contains("efcore read", StringComparison.Ordinal)
            .ShouldBeFalse("the STORE reflected the edit — then this test proves nothing about liveness");
        storeOut.ShouldContain("efcore pending_write");
        // --no-live means the store, and a store answer never claims a live source.
        storeErr.ShouldNotContain("live:");
        storeOut.ShouldNotContain("live:");
    }

    // ---------------------------------------------------------------------------------------------------
    // 3. Fallback with no host: byte-identical to --no-live, and silent.
    // ---------------------------------------------------------------------------------------------------

    [Test]
    public async Task With_no_resident_host_the_store_answer_is_unchanged_and_says_nothing_about_live()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        var indexLog = new StringWriter();
        (await CliApplication.RunAsync(["index", playground.SolutionPath], indexLog, indexLog, playground.WorkingDirectory)).ShouldBe(
            0,
            indexLog.ToString()
        );

        // Routing is ON here (no flag, no env) — there is simply no host, which is the state essentially every
        // rig invocation is in and the one that must be indistinguishable from rig before this slice.
        var (routedExit, routedOut, routedErr) = await RunCliAsync(playground.WorkingDirectory, "reaches", "HomePage.Show");
        var (forcedExit, forcedOut, forcedErr) = await RunCliAsync(playground.WorkingDirectory, "reaches", "HomePage.Show", "--no-live");
        Report($"[transport/fallback] no host exit {routedExit}, stdout:{Environment.NewLine}{routedOut}stderr:{Environment.NewLine}{routedErr}");

        routedExit.ShouldBe(0, routedOut + routedErr);
        routedOut.ShouldContain("From: HomePage.Show");

        // Identical on BOTH streams and the exit code. Not "close enough": the live/store parity gates
        // (LiveReachesTests, LivePathCallersTests, LiveTreeTests) compare store stderr against live stderr
        // exactly, so a stray line here would break them — and, more to the point, would be a regression
        // shipped to every user who has never run `rig watch`.
        routedOut.ShouldBe(forcedOut);
        routedErr.ShouldBe(forcedErr);
        routedExit.ShouldBe(forcedExit);

        // The source disclosure is a POSITIVE marker: present => resident facts, absent => the store, and
        // there is no third source it could have been.
        routedErr.ShouldNotContain("live:");
        routedOut.ShouldNotContain("live:");
    }

    // ---------------------------------------------------------------------------------------------------
    // 4. Transport failures. None of these needs a booted host — a stub endpoint reproduces each exactly, and
    //    cheaply enough that all four failure modes can be covered rather than argued about.
    // ---------------------------------------------------------------------------------------------------

    // A host whose endpoint is there but which closes without answering (a shutdown racing a query, a crash
    // between accept and write). The query must land on the store path, and must SAY why — this is the one
    // fallback that is a surprise to the user, so it is the one that gets a sentence.
    [Test]
    public async Task An_endpoint_that_closes_without_answering_falls_back_to_the_store_and_says_so()
    {
        var directory = Directory.CreateTempSubdirectory("rig-live-transport-").FullName;
        try
        {
            await using var stub = new StubEndpoint(LiveQueryTransport.PipeNameFor(directory), StubBehaviour.ReadThenHangUp);

            var watch = Stopwatch.StartNew();
            var (exitCode, standardOut, standardError) = await RunCliAsync(directory, "reaches", "Anything.AtAll");
            watch.Stop();
            Report($"[transport/dead-pipe] exit {exitCode} in {watch.Elapsed.TotalSeconds:F2}s:{Environment.NewLine}{standardOut}{standardError}");

            stub.Connections.ShouldBeGreaterThan(0, "the client never reached the stub endpoint — this test proves nothing");
            standardError.ShouldContain("live: a resident index is watching this directory but did not answer");
            standardError.ShouldContain("closed the connection without answering");
            standardError.ShouldContain("answering from the .rig store instead");
            // It reached the STORE path, which in an unindexed directory is rig's ordinary store error. That
            // is the proof of fallback: a transport failure produced rig's normal behaviour, not a new one.
            standardError.ShouldContain("No .rig store found");
            exitCode.ShouldBe(2);
            // No hang: the failure is detected by the pipe breaking, not by the deadline expiring.
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(20));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // A host that accepts and then never answers — a wedged process, the case a broken pipe cannot detect.
    // This is what the client deadline exists for, and the only thing it is for.
    [Test]
    public async Task An_endpoint_that_never_answers_gives_up_on_the_deadline_instead_of_hanging()
    {
        var directory = Directory.CreateTempSubdirectory("rig-live-transport-").FullName;
        try
        {
            // Accepts every connection and then holds it, answering nothing — the wedged host.
            await using var stub = new StubEndpoint(LiveQueryTransport.PipeNameFor(directory), StubBehaviour.AcceptAndGoSilent);

            var watch = Stopwatch.StartNew();
            var outcome = await LiveQueryClient.TryAskAsync(
                LiveQueryVerbs.Reaches,
                new ReachesCommand.Options("Anything.AtAll", false, false, false, [], null, null, [], [], false, null, false),
                directory,
                // 2s rather than the 30s default: the mechanism under test is the READ deadline, and a test
                // should not spend the production budget to watch it fire. It must stay comfortably above the
                // client's own 500ms connect retry, or the budget would expire during the CONNECT instead and
                // the test would be measuring the wrong thing.
                deadline: TimeSpan.FromSeconds(2)
            );
            watch.Stop();
            Report($"[transport/wedged] {outcome.Status} after {watch.Elapsed.TotalSeconds:F2}s: {outcome.Reason}");

            stub.Connections.ShouldBeGreaterThan(0, "the client never reached the stub endpoint — this test proves nothing");
            outcome.Status.ShouldBe(LiveRouteStatus.Failed);
            outcome.Answer.ShouldBeNull();
            outcome.Reason.ShouldNotBeNull();
            outcome.Reason.ShouldContain("did not answer within");
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // THE WRONG-TREE GUARD. The endpoint name is a 64-bit hash of the working directory, so a collision is
    // possible in principle and a host booted in the wrong place is possible in practice — and answering a
    // client about a tree it did not ask about would be the worst thing this transport could do. Constructed
    // here by publishing a host for directory A under the name derived from directory B, which is the only
    // way to reach the guard on purpose.
    [Test]
    public async Task A_host_watching_a_different_directory_refuses_to_answer()
    {
        var hostDirectory = Directory.CreateTempSubdirectory("rig-live-hostdir-").FullName;
        var clientDirectory = Directory.CreateTempSubdirectory("rig-live-clientdir-").FullName;
        try
        {
            var served = 0;
            await using var server = LiveQueryServer.Start(
                workingDirectory: hostDirectory,
                serve: (_, _) =>
                {
                    Interlocked.Increment(ref served);
                    return Task.FromResult(LiveServeResult.Answered(0, $"WRONG-TREE ANSWER{Environment.NewLine}", "", "live: bogus"));
                },
                log: new StringWriter(),
                // The collision, forced: this endpoint answers to the name a client in clientDirectory
                // computes, while the host itself is watching hostDirectory.
                pipeName: LiveQueryTransport.PipeNameFor(clientDirectory)
            );
            (await server.WaitUntilReadyAsync(TimeSpan.FromSeconds(10))).ShouldBeTrue();

            var (exitCode, standardOut, standardError) = await RunCliAsync(clientDirectory, "reaches", "Anything.AtAll");
            Report($"[transport/wrong-tree] exit {exitCode}:{Environment.NewLine}{standardOut}{standardError}");

            // The serve callback was never INVOKED: the request about another directory did not reach the
            // query layer at all, which is stronger than being rejected there.
            Volatile.Read(ref served).ShouldBe(0, "a request about another directory reached the host's query layer");
            standardOut.ShouldNotContain("WRONG-TREE ANSWER");
            standardError.ShouldContain("did not answer");
            standardError.ShouldContain("is watching");
            standardError.ShouldContain("No .rig store found"); // …and the store path ran instead
            exitCode.ShouldBe(2);
        }
        finally
        {
            Directory.Delete(hostDirectory, recursive: true);
            Directory.Delete(clientDirectory, recursive: true);
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // 5. The no-host cost — the one regression that would reach every existing user.
    // ---------------------------------------------------------------------------------------------------

    // Routing is on by default, so EVERY `rig reaches/path/callers/tree` invocation on every machine runs the
    // discovery probe. The whole added cost when no host exists is this call: an allowlist lookup, a SHA-256
    // of a short path, and one filesystem stat against the pipe namespace. Measured rather than asserted to be
    // small, and bounded loosely enough that the gate is about ORDER OF MAGNITUDE (microseconds, not
    // milliseconds) rather than about machine noise.
    [Test]
    public async Task Discovery_costs_a_single_probe_when_no_resident_host_exists()
    {
        var directory = Directory.CreateTempSubdirectory("rig-live-probe-").FullName;
        try
        {
            var options = new ReachesCommand.Options("Anything.AtAll", false, false, false, [], null, null, [], [], false, null, false);

            // Warm the JIT and the SHA-256 implementation out of the sample.
            for (var i = 0; i < 50; i++)
            {
                (await LiveQueryClient.TryAskAsync(LiveQueryVerbs.Reaches, options, directory)).Status.ShouldBe(LiveRouteStatus.NoHost);
            }

            const int Iterations = 1000;
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < Iterations; i++)
            {
                await LiveQueryClient.TryAskAsync(LiveQueryVerbs.Reaches, options, directory);
            }

            watch.Stop();
            var perCallMicroseconds = watch.Elapsed.TotalMicroseconds / Iterations;
            Report(
                $"[transport/no-host cost] {Iterations} discovery attempts with no host: "
                    + $"{watch.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)}ms total, "
                    + $"{perCallMicroseconds.ToString("F1", CultureInfo.InvariantCulture)}us per invocation"
            );

            // 1ms would already be invisible next to a store query (seconds); the real number is two orders
            // below that. The bound exists to catch a future change that turns the probe into a CONNECT
            // ATTEMPT — NamedPipeClientStream.ConnectAsync polls until its timeout, so that mistake would
            // cost the whole timeout on every invocation and this is where it would surface.
            // 50 ms, not the 1 ms this first asserted. The bound exists to catch ONE regression: probing with a
        // real connect budget, because NamedPipeClientStream.ConnectAsync POLLS until its timeout and would
        // then tax every rig invocation by that whole timeout. That failure mode is hundreds of milliseconds,
        // so 50 ms still catches it with two orders of margin over the ~50 us this actually costs — while a
        // 1 ms bound measured in wall-clock inside a 1060-test parallel suite flakes on machine load alone
        // (observed: passes 3/3 in isolation, failed once under a full-suite run). A perf gate that fails for
        // reasons unrelated to the thing it guards teaches people to re-run it, which is worse than no gate.
        perCallMicroseconds.ShouldBeLessThan(50_000);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // 6. Discovery is a pure function of the directory — the property that removes the whole stale-metadata
    //    class of bug a port file would have.
    // ---------------------------------------------------------------------------------------------------

    [Test]
    public void The_endpoint_name_is_derived_from_the_normalised_working_directory()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "rig-live-naming"));
        var withTrailingSeparator = root + Path.DirectorySeparatorChar;
        var withAltSeparators = root.Replace(Path.DirectorySeparatorChar, '/');

        LiveQueryTransport.PipeNameFor(root).ShouldStartWith("rig-live-");
        // A trailing separator and an alt separator are the SAME directory, so they must be the same host —
        // a client that spelled its cwd differently must not silently miss the endpoint and answer from the
        // (stale) store instead.
        LiveQueryTransport.PipeNameFor(withTrailingSeparator).ShouldBe(LiveQueryTransport.PipeNameFor(root));
        LiveQueryTransport.PipeNameFor(withAltSeparators).ShouldBe(LiveQueryTransport.PipeNameFor(root));
        if (OperatingSystem.IsWindows())
        {
            // Windows paths are case-insensitive, so `C:\Git\x` and `c:\git\x` are one tree. Not folded on
            // Unix, where they are genuinely two.
            LiveQueryTransport.PipeNameFor(root.ToUpperInvariant()).ShouldBe(LiveQueryTransport.PipeNameFor(root));
        }

        // Different directories are different hosts.
        LiveQueryTransport.PipeNameFor(Path.Combine(root, "a")).ShouldNotBe(LiveQueryTransport.PipeNameFor(Path.Combine(root, "b")));
        LiveQueryTransport.SameDirectory(withTrailingSeparator, withAltSeparators).ShouldBeTrue();
        LiveQueryTransport.SameDirectory(Path.Combine(root, "a"), Path.Combine(root, "b")).ShouldBeFalse();
    }

    // ---------------------------------------------------------------------------------------------------
    // helpers
    // ---------------------------------------------------------------------------------------------------

    private static IEnumerable<(string Verb, object Options)> RoutableRequests() =>
        [
            (LiveQueryVerbs.Reaches, new ReachesCommand.Options("HomePage.Show", false, false, false, [], null, null, [], [], false, null, false)),
            (LiveQueryVerbs.Path, new PathCommand.Options("HomePage.Show", "Db.Query", false, false, false, [], null, null, false)),
            (LiveQueryVerbs.Callers, new CallersCommand.Options("Db.Query", false, false, false, false, false, false, [], null, null, null, false)),
            (
                LiveQueryVerbs.Tree,
                new TreeCommand.Options(
                    FromPattern: "HomePage.Show",
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
                    Only: [],
                    Exclude: [],
                    Intrinsic: false,
                    ExcludeNamespaces: [],
                    NoCache: false,
                    Gate: true,
                    Amplification: true,
                    Time: false,
                    Format: null,
                    Suppress: null
                )
            ),
        ];

    private static LiveQueryRequest Request(string verb, object options, string workingDirectory) =>
        new(
            Protocol: LiveQueryTransport.Protocol,
            Verb: verb,
            WorkingDirectory: workingDirectory,
            Options: JsonSerializer.Serialize(options, options.GetType(), LiveQueryTransport.Json)
        );

    private enum StubBehaviour
    {
        // Drain the whole request, then hang up without writing a byte back — the client's read hits EOF.
        ReadThenHangUp,

        // Accept and hold the connection forever, reading and writing nothing — a WEDGED host, the one case
        // no broken pipe can signal.
        AcceptAndGoSilent,
    }

    // A bare endpoint under the name a client will compute, with no rig host behind it: the stand-in for a
    // host broken in one specific way.
    //
    // It keeps a listener ALWAYS ARMED and re-arms after every accept, mirroring LiveQueryServer, and that is
    // not incidental fidelity — a single-instance stub is STEALABLE. The client classifies a failed connect
    // with an existence probe, and on Windows a File.Exists against a live pipe consumes a pending accept, so
    // a one-instance stub can be emptied by the client's own diagnostics and then look like a host that
    // refuses to accept. Re-arming makes the failure mode under test the one the test names.
    private sealed class StubEndpoint : IAsyncDisposable
    {
        private readonly string _pipeName;
        private readonly StubBehaviour _behaviour;
        private readonly CancellationTokenSource _stop = new();
        private readonly List<Task> _handlers = [];
        private readonly Task _loop;
        private int _connections;

        internal StubEndpoint(string pipeName, StubBehaviour behaviour)
        {
            _pipeName = pipeName;
            _behaviour = behaviour;
            // The first instance is created SYNCHRONOUSLY, before the constructor returns, so the endpoint is
            // discoverable the moment the test has the object. Creating it inside the loop's Task.Run instead
            // made both failure tests race the thread pool under a full-suite run and report "no host".
            _loop = AcceptLoopAsync(New(), _stop.Token);
        }

        internal int Connections => Volatile.Read(ref _connections);

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            await IgnoreCancellation(_loop);
            Task[] handlers;
            lock (_handlers)
            {
                handlers = [.. _handlers];
            }

            foreach (var handler in handlers)
            {
                await IgnoreCancellation(handler);
            }

            _stop.Dispose();
        }

        private static async Task IgnoreCancellation(Task task)
        {
            try
            {
                await task;
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException) { }
        }

        private NamedPipeServerStream New() => new(_pipeName, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        private async Task AcceptLoopAsync(NamedPipeServerStream first, CancellationToken cancellationToken)
        {
            await Task.Yield(); // let the constructor return; the endpoint already exists
            var listener = first;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await listener.WaitForConnectionAsync(cancellationToken);
                    }
                    catch (IOException)
                    {
                        await listener.DisposeAsync();
                        listener = New();
                        continue;
                    }

                    var connected = listener;
                    listener = New();
                    var handler = HandleAsync(connected, cancellationToken);
                    lock (_handlers)
                    {
                        _handlers.Add(handler);
                    }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                await listener.DisposeAsync();
            }
        }

        private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
        {
            await using (pipe)
            {
                var frame = await LiveQueryTransport.ReadFrameAsync(pipe, cancellationToken);
                if (frame is null)
                {
                    return; // an existence probe, or a client that gave up: not a real request, so not counted
                }

                Interlocked.Increment(ref _connections);
                if (_behaviour == StubBehaviour.ReadThenHangUp)
                {
                    return; // disposal hangs up: the client's read sees EOF
                }

                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
        }
    }

    private static async Task<(int Exit, string Out, string Err)> RunCliAsync(string workingDirectory, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = await CliApplication.RunAsync(args, output, error, workingDirectory);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static async Task<(int Exit, string Out, string Err)> WaitForCliAnswerAsync(
        string workingDirectory,
        string[] args,
        Func<string, bool> until,
        TimeSpan timeout,
        string reason
    )
    {
        var deadline = DateTime.UtcNow + timeout;
        var last = (Exit: -1, Out: "", Err: "");
        while (DateTime.UtcNow < deadline)
        {
            last = await RunCliAsync(workingDirectory, args);
            if (until(last.Out))
            {
                return last;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"Timed out after {timeout.TotalSeconds:F0}s: {reason}. Last answer:{Environment.NewLine}{last.Out}{last.Err}");
    }

    // Drop the lines the HOST adds around a routed answer — the source disclosure and the per-generation
    // derived-layer cost — leaving exactly what the command itself wrote to stderr.
    private static string StripHostLines(string standardError) =>
        string.Join(
            Environment.NewLine,
            standardError
                .Split(Environment.NewLine)
                .Where(line => !line.StartsWith(SourceLinePrefix, StringComparison.Ordinal) && !line.StartsWith(CostLinePrefix, StringComparison.Ordinal))
        );

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
