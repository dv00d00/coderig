using System.Diagnostics;
using System.Globalization;
using Rig.Analysis;
using Rig.Analysis.Rules;
using Rig.Cli.Live;

namespace Rig.Tests.Live;

// OPT-IN MEASUREMENT HARNESS — not a unit test, and it does NOT run in the normal suite (silent no-op without
// its env var), the same shape as Analysis/ResidentWorkspaceTrial.
//
// It answers ONE question, asked by the `tree` live slice and not answerable from a playground: on a REAL
// solution, what does the whole-store hazard-augmented effect set cost to build IN MEMORY?
//
// Why it matters, precisely: `tree --view hazards` consumes that set, and on the store path it is the single
// most expensive cached artifact in the product (~18s cold from SQL). LiveFactSource.WarmQueryArtifacts
// deliberately does NOT warm it — no live query path read it before this slice — so the decision "should the
// background warmer now include it?" turns on this number and nothing else. Warming an 18-second artifact that
// most queries never touch would be a regression in the very thing warming exists to protect (the worker's
// next apply is what a long warm blocks), so the number has to be measured rather than assumed to carry over
// from the SQL path — in memory there is no SQL, and the SQL read was most of the store-path cost.
//
// The two artifacts are timed SEPARATELY and in the order a hazards query forces them, because they are not
// the same cost: `hazardEffects` is a whole-fact-set derivation, `graphHazardFindings` additionally needs the
// `derive`-shaped graph (delivery edges + cycle detection). Both are reported as first-access costs, which is
// what a query actually pays.
//
// Run it deliberately (a healthy, restored clone — never the primary checkout, which is being worked in):
//
//   $env:RIG_HAZARD_TRIAL_SOLUTION="C:\git\meddbase-main-application-2\MedDBase.slnx"
//   $env:RIG_HAZARD_TRIAL_RULES="C:\git\meddbase-analysis\rig.rules.json"
//   $env:RIG_HAZARD_TRIAL_BUILD_CACHE="C:\git\meddbase-analysis\.rig\dtb-cache"
//   $env:RIG_HAZARD_TRIAL_REPORT="<path to a log file>"
//   dotnet run --project tests/Rig.Tests -- --treenode-filter "/*/*/LiveHazardCostTrial/*"
//
// RIG_HAZARD_TRIAL_BUILD_CACHE matters for the SETUP arm only (the design-time builds); the numbers this
// harness exists for are pure in-memory derivation and do not depend on it.
public sealed class LiveHazardCostTrial
{
    [Test]
    public async Task Measure_the_in_memory_hazard_artifact_cost()
    {
        var solutionPath = Environment.GetEnvironmentVariable("RIG_HAZARD_TRIAL_SOLUTION");
        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            return; // opt-in harness; silent no-op in the normal suite
        }

        // To a FILE, never Console — TUnit does not surface console output in its default mode, and a
        // multi-minute measurement that prints nowhere is not a measurement.
        var reportPath =
            Environment.GetEnvironmentVariable("RIG_HAZARD_TRIAL_REPORT") ?? Path.Combine(Path.GetTempPath(), "rig-live-hazard-trial.log");
        void Say(string line)
        {
            try
            {
                File.AppendAllText(reportPath, line + Environment.NewLine);
            }
            catch (IOException) { }
        }

        var rulesPath = Environment.GetEnvironmentVariable("RIG_HAZARD_TRIAL_RULES");
        var buildCacheDir = Environment.GetEnvironmentVariable("RIG_HAZARD_TRIAL_BUILD_CACHE");
        var workingDirectory = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
        var rules = string.IsNullOrWhiteSpace(rulesPath)
            ? RuleSetLoader.Load(workingDirectory)
            : RuleSetLoader.Load(Path.GetDirectoryName(Path.GetFullPath(rulesPath))!, [Path.GetFullPath(rulesPath)]);

        Say($"# rig live hazard-cost trial{Environment.NewLine}[trial] solution: {solutionPath}");
        Say($"[trial] rules   : {rulesPath ?? "(cwd cascade)"}");

        // ---- SETUP: the facts. Not the measurement — just what a resident host already holds. ----
        var setup = Stopwatch.StartNew();
        var (facts, workspace) = await SolutionAnalyzer.AnalyzeRetainingWorkspaceAsync(
            solutionPath: solutionPath,
            rules: rules,
            progress: message => Say($"[setup] {message}"),
            excludeTests: true, // mirror `rig index`
            buildCacheDir: buildCacheDir
        );
        setup.Stop();
        using var _ = workspace;
        Say(
            $"[trial] SETUP analyze: {setup.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)}s | "
                + $"symbols={facts.Symbols?.Count ?? 0} refs={facts.References?.Count ?? 0}"
        );

        var live = new LiveFactSource(facts, rules);

        // ---- ARM 1: the artifacts a `reaches`/`tree` query needs — the set the warmer ALREADY warms. The
        // baseline the hazard numbers below have to be judged against. ----
        var warm = Stopwatch.StartNew();
        live.WarmQueryArtifacts(CancellationToken.None);
        warm.Stop();
        Say($"[trial] ARM 1 warmed query artifacts: {warm.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)}s");
        Say($"[trial]   per-artifact: {live.BuildTimeLine()}");

        // ---- ARM 2: the hazard-augmented whole-fact-set effect set — the `tree --view hazards` feed. ----
        var hazardWatch = Stopwatch.StartNew();
        var hazardEffects = live.HazardEffects;
        hazardWatch.Stop();
        Say(
            $"[trial] ARM 2 hazardEffects: {hazardWatch.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)}s "
                + $"| {hazardEffects.Count} effects"
        );

        // ---- ARM 3: the graph-tier findings (shaped graph + cycle detection on top). ----
        var graphWatch = Stopwatch.StartNew();
        var graphFindings = await live.GraphHazardFindingsAsync();
        graphWatch.Stop();
        Say(
            $"[trial] ARM 3 graphHazardFindings: {graphWatch.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)}s "
                + $"| {graphFindings.Count} findings"
        );

        Say($"[trial] per-artifact (all): {live.BuildTimeLine()}");
    }
}
