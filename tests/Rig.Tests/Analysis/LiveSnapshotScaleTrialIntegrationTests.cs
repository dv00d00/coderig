using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis.Text;
using Rig.Analysis;
using Rig.Analysis.Extraction;
using Rig.Analysis.Inventory;
using Rig.Analysis.Rules;
using Rig.Cli.Live;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Analysis;

// OPT-IN LEGACY BASELINE HARNESS — the normal suite returns before doing IO or emitting output.
//
// Default scale arm (one fresh test-process invocation per arm):
//   RIG_LIVE_TRIAL_ENABLED=1
//   RIG_LIVE_TRIAL_ENGINE=legacy
//   RIG_LIVE_TRIAL_PRESET=scale
//   RIG_LIVE_TRIAL_EDITS=50
//   RIG_LIVE_TRIAL_CHECKPOINTS=10,50
//   RIG_LIVE_TRIAL_REPORT=artifacts/live-scale/trials/live-trial.jsonl
//   dotnet run --project tests/Rig.IntegrationTests --no-build -- --maximum-parallel-tests 1 --treenode-filter "/*/*/LiveSnapshotScaleTrial/*"
//
// `snapshot` is deliberately rejected until the future same-binary engine arm exists. JSONL is appended
// and flushed after every milestone; the sibling Markdown file is regenerated from JSONL and is never the
// source of truth. Every trace step, including a multi-file batch, is one atomic resident publication.
public sealed partial class LiveSnapshotScaleTrial
{
    [Test]
    public async Task Measure_legacy_live_snapshot_scale_trial()
    {
        if (!IsEnabled(Environment.GetEnvironmentVariable(EnabledVariable)))
        {
            return;
        }

        var engine = ValidateEngine(Environment.GetEnvironmentVariable(EngineVariable));
        ValidateRuntimeEngine(engine, Environment.GetEnvironmentVariable("RIG_LIVE_ENGINE"));
        var config = TrialConfig.FromEnvironment(RepositoryRoot());
        var corpus = await PrepareCorpusAsync(config);
        var trace = await ReadTraceAsync(corpus.TracePath);
        var originals = CaptureOriginals(corpus.WorkingDirectory, trace);
        var environment = CaptureEnvironment();
        var runStarted = DateTimeOffset.UtcNow;
        var provenance = await RunProvenance.CreateAsync(
            engine,
            corpus.CorpusSha256,
            corpus.TraceSha256,
            corpus.WorkingDirectory,
            config.RulesPath,
            environment,
            runStarted
        );
        var rules = provenance.Rules;
        var report = new LiveTrialReport(config.ReportPath);

        ResidentIndex? index = null;
        try
        {
            await RunPhaseAsync(
                "initial-load",
                config,
                provenance,
                report,
                async () =>
                {
                    var interner = StringInterner.CreateDefault();
                    var (baseFacts, workspace) = await SolutionAnalyzer.AnalyzeRetainingWorkspaceAsync(
                        solutionPath: corpus.SolutionPath,
                        rules: rules,
                        excludeTests: true,
                        buildCacheDir: config.BuildCacheDirectory,
                        restore: true,
                        interner: interner
                    );
                    index = new ResidentIndex(workspace, baseFacts, corpus.SolutionPath, rules, interner: interner);
                    return Observe(index.CurrentFacts, index.UnreconciledProjects.Count);
                }
            );

            var querySeeds = trace.QuerySeeds.ToDictionary(seed => seed.Relation, StringComparer.Ordinal);
            await QueryAfterTraceEditAsync(
                publishedPhase: "disjoint-edit-published",
                phase: "first-unrelated-query",
                seed: querySeeds["disjoint"],
                trace: trace,
                index: index!,
                corpusRoot: corpus.WorkingDirectory,
                rules: rules,
                config: config,
                provenance: provenance,
                report: report
            );
            await QueryAfterTraceEditAsync(
                publishedPhase: "intersecting-edit-published",
                phase: "first-intersecting-query",
                seed: querySeeds["intersects"],
                trace: trace,
                index: index!,
                corpusRoot: corpus.WorkingDirectory,
                rules: rules,
                config: config,
                provenance: provenance,
                report: report
            );

            var surface = trace.Edits.First(edit => edit.Kind == "surface");
            await RunPhaseAsync(
                "eager-edit-application",
                config,
                provenance,
                report,
                async () =>
                {
                    await ApplyStepAsync(index!, corpus.WorkingDirectory, surface);
                    return Observe(index!.CurrentFacts, index.UnreconciledProjects.Count);
                }
            );
            await RunPhaseAsync(
                "full-reconciliation",
                config,
                provenance,
                report,
                async () =>
                {
                    await index!.ReconcileAsync();
                    return Observe(index.CurrentFacts, index.UnreconciledProjects.Count);
                }
            );
            await RevertStepAsync(index!, corpus.WorkingDirectory, surface);
            await index!.ReconcileAsync();

            var batch = trace.Edits.First(edit => edit.Kind == "batch" && edit.Mutations.Count >= 3);
            await RunPhaseAsync(
                "batch-edit-application",
                config,
                provenance,
                report,
                async () =>
                {
                    await ApplyStepAsync(index!, corpus.WorkingDirectory, batch);
                    return Observe(index!.CurrentFacts, index.UnreconciledProjects.Count);
                }
            );
            await RunPhaseAsync(
                "batch-reconciliation",
                config,
                provenance,
                report,
                async () =>
                {
                    await index!.ReconcileAsync();
                    return Observe(index.CurrentFacts, index.UnreconciledProjects.Count);
                }
            );
            await RevertStepAsync(index!, corpus.WorkingDirectory, batch);
            await index!.ReconcileAsync();

            var completed = 0;
            foreach (var checkpoint in config.Checkpoints)
            {
                var start = completed;
                await RunPhaseAsync(
                    $"generation-{checkpoint}",
                    config,
                    provenance,
                    report,
                    async () =>
                    {
                        AnalysisResult? lastQueryFacts = null;
                        FactGraphData? lastQueryGraph = null;
                        LiveFactSource? lastLive = null;
                        LiveQueryRunner.LiveAnswer? lastAnswer = null;
                        var lastDirtyProjects = 0;
                        for (var generation = start; generation < checkpoint; generation++)
                        {
                            var edit = trace.Edits[generation % trace.Edits.Count];
                            await ApplyStepAsync(index!, corpus.WorkingDirectory, edit);
                            lastQueryFacts = index!.CurrentFacts;
                            var live = new LiveFactSource(lastQueryFacts, rules);
                            lastLive = live;
                            var answer = await RunRenderedQueryAsync(querySeeds["intersects"], live, corpus.WorkingDirectory);
                            lastAnswer = answer;
                            lastQueryGraph = live.TraversalGraph;
                            lastDirtyProjects = index.UnreconciledProjects.Count;
                            if (generation + 1 < checkpoint)
                            {
                                await index.ReconcileAsync();
                                await RevertStepAsync(index, corpus.WorkingDirectory, edit);
                                await index.ReconcileAsync();
                            }
                        }

                        return new PhaseObservation(lastQueryFacts!, lastQueryGraph, lastDirtyProjects, lastAnswer, lastLive);
                    }
                );
                var finalEdit = trace.Edits[(checkpoint - 1) % trace.Edits.Count];
                await index!.ReconcileAsync();
                await RevertStepAsync(index, corpus.WorkingDirectory, finalEdit);
                await index.ReconcileAsync();
                completed = checkpoint;
            }

            Console.WriteLine($"[live-trial] complete jsonl={report.JsonlPath} markdown={report.MarkdownPath}");
        }
        finally
        {
            index?.Dispose();
            await RestoreOriginalsAsync(corpus.WorkingDirectory, originals);
        }
    }
}
