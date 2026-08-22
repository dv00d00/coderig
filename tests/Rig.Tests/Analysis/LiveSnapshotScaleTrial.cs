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
//   dotnet run --project tests/Rig.Tests --no-build -- --treenode-filter "/*/*/LiveSnapshotScaleTrial/*"
//
// `snapshot` is deliberately rejected until the future same-binary engine arm exists. JSONL is appended
// and flushed after every milestone; the sibling Markdown file is regenerated from JSONL and is never the
// source of truth. Current ResidentIndex has only a one-file ApplyEditAsync seam, so trace batches are applied
// sequentially and disclosed as such instead of pretending this baseline measures an atomic batch API.
public sealed class LiveSnapshotScaleTrial
{
    private const string EnabledVariable = "RIG_LIVE_TRIAL_ENABLED";
    private const string EngineVariable = "RIG_LIVE_TRIAL_ENGINE";
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions TraceJson = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

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

    internal static string ValidateEngine(string? engine) =>
        engine?.Trim().ToLowerInvariant() switch
        {
            "legacy" => "legacy",
            "snapshot" => throw new InvalidOperationException(
                "RIG_LIVE_TRIAL_ENGINE=snapshot is not implemented in this slice. Use 'legacy'; the future same-binary arm must enable snapshot explicitly."
            ),
            null or "" => throw new InvalidOperationException($"{EngineVariable} must be set to 'legacy' for an opted-in trial."),
            var value => throw new InvalidOperationException($"Unsupported {EngineVariable}='{value}'. Only 'legacy' is available."),
        };

    internal static void ValidateRuntimeEngine(string trialEngine, string? runtimeEngine)
    {
        if (
            !string.IsNullOrWhiteSpace(runtimeEngine)
            && !string.Equals(trialEngine, runtimeEngine.Trim(), StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new InvalidOperationException($"RIG_LIVE_ENGINE='{runtimeEngine}' disagrees with {EngineVariable}='{trialEngine}'.");
        }
    }

    internal static string ValidateQueryMode(string? mode) =>
        string.IsNullOrWhiteSpace(mode) || string.Equals(mode.Trim(), "reaches", StringComparison.OrdinalIgnoreCase)
            ? "reaches"
            : throw new InvalidOperationException($"Unsupported RIG_LIVE_TRIAL_QUERY_MODE='{mode}'. Only 'reaches' is available.");

    private static bool IsEnabled(string? value) =>
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);

    private static async Task QueryAfterTraceEditAsync(
        string publishedPhase,
        string phase,
        QuerySeed seed,
        EditTrace trace,
        ResidentIndex index,
        string corpusRoot,
        RuleSet rules,
        TrialConfig config,
        RunProvenance provenance,
        LiveTrialReport report
    )
    {
        var edit = trace.Edits.Single(candidate => candidate.Id == seed.EditId);
        try
        {
            await RunPhaseAsync(
                publishedPhase,
                config,
                provenance,
                report,
                async () =>
                {
                    await ApplyStepAsync(index, corpusRoot, edit);
                    return Observe(index.CurrentFacts, index.UnreconciledProjects.Count);
                }
            );
            await RunPhaseAsync(
                phase,
                config,
                provenance,
                report,
                async () =>
                {
                    var live = new LiveFactSource(index.CurrentFacts, rules);
                    var answer = await RunRenderedQueryAsync(seed, live, corpusRoot);
                    return new PhaseObservation(index.CurrentFacts, live.TraversalGraph, index.UnreconciledProjects.Count, answer, live);
                }
            );
        }
        finally
        {
            await RevertStepAsync(index, corpusRoot, edit);
            await index.ReconcileAsync();
        }
    }

    private static async Task<LiveQueryRunner.LiveAnswer> RunRenderedQueryAsync(
        QuerySeed seed,
        LiveFactSource facts,
        string workingDirectory
    )
    {
        var answer = await LiveQueryRunner.AnswerAsync($"reaches {seed.Pattern}", facts, workingDirectory);
        if (answer.Exit != 0)
        {
            throw new InvalidOperationException($"Live rendered query '{seed.Id}' exited {answer.Exit}: {answer.Err}");
        }
        if (string.IsNullOrWhiteSpace(answer.Out))
        {
            throw new InvalidOperationException($"Live rendered query '{seed.Id}' returned empty stdout.");
        }
        return answer;
    }

    private static string CanonicalAnswerEnvelope(LiveQueryRunner.LiveAnswer answer, string corpusRoot) =>
        JsonSerializer.Serialize(
            new
            {
                answer.Exit,
                Out = NormalizeAnswer(answer.Out, corpusRoot),
                Err = NormalizeAnswer(answer.Err, corpusRoot),
            }
        );

    private static string NormalizeAnswer(string value, string corpusRoot)
    {
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var fullRoot = Path.GetFullPath(corpusRoot);
        normalized = normalized.Replace(fullRoot, "<CORPUS>", StringComparison.Ordinal);
        return normalized.Replace(fullRoot.Replace('\\', '/'), "<CORPUS>", StringComparison.Ordinal);
    }

    private static async Task RunPhaseAsync(
        string phase,
        TrialConfig config,
        RunProvenance provenance,
        LiveTrialReport report,
        Func<Task<PhaseObservation>> operation
    )
    {
        var start = DateTimeOffset.UtcNow;
        await using var sampler = new PhaseMemorySampler(config.SamplingIntervalMilliseconds);
        var stopwatch = Stopwatch.StartNew();
        var observation = await operation();
        stopwatch.Stop();
        var phaseCompletedUtc = DateTimeOffset.UtcNow;
        var allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
        await sampler.StopAsync();
        ForceFullCollection();

        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var managedLiveBytes = GC.GetTotalMemory(forceFullCollection: false);
        var workingSetBytes = process.WorkingSet64;
        var privateBytes = TryPrivateBytes(process);
        GC.KeepAlive(observation.RetainedGeneration);

        // Canonicalization and report serialization are intentionally outside the stopwatch, allocation
        // endpoint, and peak sampler. The query generation remains strongly reachable through the forced-GC
        // live-set sample above, matching WatchHost's retained _liveFacts lifetime.
        var snapshot = Snapshot(observation, provenance.CorpusRoot);
        var unavailableMetrics = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var metric in UnavailableMetrics)
        {
            unavailableMetrics.Add(metric.Key, metric.Value);
        }
        if (privateBytes is null)
        {
            unavailableMetrics["privateBytes"] = "The current operating system/runtime did not expose process private bytes.";
        }
        if (sampler.PeakPrivateBytes is null)
        {
            unavailableMetrics["sampledPeakPrivateBytes"] =
                "The current operating system/runtime did not expose process private bytes while sampling.";
        }
        if (snapshot.GraphHash is null)
        {
            unavailableMetrics["graph"] =
                "No query ran in this milestone, so the legacy traversal graph was deliberately not materialized.";
            unavailableMetrics["graphHash"] = "No query ran in this milestone, so no graph hash exists.";
        }
        if (snapshot.NormalizedResultHash is null)
        {
            unavailableMetrics["normalizedResultHash"] = "This is a non-query milestone; no rendered result exists.";
        }
        var record = new LiveTrialRecord(
            SchemaVersion: LiveTrialReport.CurrentSchemaVersion,
            RunId: provenance.RunId,
            Sequence: provenance.NextSequence(),
            Engine: provenance.Engine,
            GitCommit: provenance.GitCommit,
            GitDirty: provenance.GitDirty,
            RuntimeVersion: provenance.RuntimeVersion,
            SdkVersion: provenance.SdkVersion,
            GcMode: provenance.GcMode,
            CorpusSha256: provenance.CorpusSha256,
            TraceSha256: provenance.TraceSha256,
            RulesSha256: provenance.RulesSha256,
            Environment: provenance.Environment,
            RunStartedUtc: provenance.RunStartedUtc,
            Phase: phase,
            PhaseStartedUtc: start,
            PhaseCompletedUtc: phaseCompletedUtc,
            DurationMilliseconds: stopwatch.Elapsed.TotalMilliseconds,
            ManagedLiveBytes: managedLiveBytes,
            WorkingSetBytes: workingSetBytes,
            PrivateBytes: privateBytes,
            AllocatedBytesDelta: provenance.AllocationDelta(allocatedAfter),
            SampledPeakManagedBytes: sampler.PeakManagedBytes,
            SampledPeakWorkingSetBytes: sampler.PeakWorkingSetBytes,
            SampledPeakPrivateBytes: sampler.PeakPrivateBytes,
            SamplingIntervalMilliseconds: config.SamplingIntervalMilliseconds,
            SamplingDisclosure: $"Process memory was sampled every {config.SamplingIntervalMilliseconds} ms during engine work; shorter spikes may be missed. ManagedLiveBytes is recorded after a forced blocking compacting collection while the query generation remains retained. Allocation delta is since the previous persisted milestone, includes intervening engine cleanup, and excludes canonical hashing/report serialization. Trace batches use the legacy one-file API sequentially.",
            Facts: snapshot.FactCounts,
            Graph: snapshot.GraphCounts,
            Work: EmptyWorkCounters(snapshot.DirtyProjects),
            UnavailableMetrics: unavailableMetrics,
            FactHash: snapshot.FactHash,
            GraphHash: snapshot.GraphHash,
            NormalizedResultHash: snapshot.NormalizedResultHash
        );
        await report.AppendAsync(record);
        Console.WriteLine(
            $"[live-trial] {phase} {record.DurationMilliseconds:F1}ms facts={TotalFacts(record.Facts)} edges={record.Graph.CallEdges?.ToString() ?? "n/a"} result={record.NormalizedResultHash ?? "n/a"} jsonl={report.JsonlPath}"
        );
        provenance.CommitAllocationBoundary();
    }

    private static PhaseObservation Observe(AnalysisResult facts, int dirtyProjects) => new(facts, null, dirtyProjects, null, null);

    private static PhaseSnapshot Snapshot(PhaseObservation observation, string corpusRoot)
    {
        var facts = observation.Facts;
        var graph = observation.Graph;
        var factRows = CanonicalFactRows(facts, corpusRoot);
        var factHash = HashRows(factRows);
        var graphHash = graph is null ? null : HashRows(CanonicalGraphRows(graph, corpusRoot));
        return new PhaseSnapshot(
            FactCountsOf(facts),
            graph is null ? new LiveTrialGraphCounts(null, null, null, null, null) : GraphCountsOf(graph),
            observation.DirtyProjects,
            factHash,
            graphHash,
            observation.Answer is null ? null : HashRows([CanonicalAnswerEnvelope(observation.Answer, corpusRoot)])
        );
    }

    private static async Task ApplyStepAsync(ResidentIndex index, string root, TraceEdit edit)
    {
        foreach (var mutation in edit.Mutations)
        {
            var path = ResolveCorpusPath(root, mutation.File);
            var current = await File.ReadAllTextAsync(path);
            ReplaceExactlyOnce(current, mutation.Marker, mutation.Replacement, edit.Id, mutation.File);
            var changed = current.Replace(mutation.Marker, mutation.Replacement, StringComparison.Ordinal);
            await File.WriteAllTextAsync(path, changed, Utf8NoBom);
            await index.ApplyEditAsync(path, SourceText.From(changed, Encoding.UTF8));
        }
    }

    private static async Task RevertStepAsync(ResidentIndex index, string root, TraceEdit edit)
    {
        foreach (var mutation in edit.Mutations.Reverse())
        {
            var path = ResolveCorpusPath(root, mutation.File);
            var current = await File.ReadAllTextAsync(path);
            ReplaceExactlyOnce(current, mutation.ReverseMarker, mutation.ReverseReplacement, edit.Id, mutation.File);
            var restored = current.Replace(mutation.ReverseMarker, mutation.ReverseReplacement, StringComparison.Ordinal);
            await File.WriteAllTextAsync(path, restored, Utf8NoBom);
            await index.ApplyEditAsync(path, SourceText.From(restored, Encoding.UTF8));
        }
    }

    private static void ReplaceExactlyOnce(string text, string marker, string replacement, string editId, string file)
    {
        var first = text.IndexOf(marker, StringComparison.Ordinal);
        var second = first < 0 ? -1 : text.IndexOf(marker, first + marker.Length, StringComparison.Ordinal);
        if (first < 0 || second >= 0)
        {
            throw new InvalidDataException(
                $"Trace {editId} expected exactly one marker in '{file}', found {(first < 0 ? 0 : 2)}. Replacement starts '{replacement[..Math.Min(40, replacement.Length)]}'."
            );
        }
    }

    private static Dictionary<string, byte[]> CaptureOriginals(string root, EditTrace trace) =>
        trace
            .Edits.SelectMany(edit => edit.Mutations)
            .Select(mutation => mutation.File)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(file => file, file => File.ReadAllBytes(ResolveCorpusPath(root, file)), StringComparer.Ordinal);

    private static async Task RestoreOriginalsAsync(string root, IReadOnlyDictionary<string, byte[]> originals)
    {
        foreach (var original in originals.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            await File.WriteAllBytesAsync(ResolveCorpusPath(root, original.Key), original.Value);
        }
    }

    private static string ResolveCorpusPath(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Trace path '{relative}' escapes corpus root '{root}'.");
        }
        return path;
    }

    private static async Task<PreparedCorpus> PrepareCorpusAsync(TrialConfig config)
    {
        string solutionPath;
        if (config.SolutionPath is not null)
        {
            solutionPath = Path.GetFullPath(config.SolutionPath);
        }
        else
        {
            var output = config.CorpusDirectory;
            var manifestExists = File.Exists(Path.Combine(output, "corpus-manifest.json"));
            if (ShouldGenerateCorpus(Directory.Exists(output), config.Regenerate))
            {
                var generated = await LiveScalePlayground.RunGeneratorAsync(config.Preset, output, config.Seed);
                if (generated.ExitCode != 0)
                {
                    throw new InvalidOperationException(generated.StandardOutput + Environment.NewLine + generated.StandardError);
                }
            }
            else if (!manifestExists)
            {
                throw new InvalidDataException(
                    $"Existing corpus '{output}' has no corpus-manifest.json. Set RIG_LIVE_TRIAL_REGENERATE=1 to replace it deliberately."
                );
            }
            solutionPath = Path.Combine(output, "LiveScale.slnx");
        }

        if (!File.Exists(solutionPath))
        {
            throw new FileNotFoundException("Live-trial solution was not found.", solutionPath);
        }
        var workingDirectory = Path.GetDirectoryName(solutionPath)!;
        var manifestPath = Path.Combine(workingDirectory, "corpus-manifest.json");
        var tracePath = config.TracePath ?? Path.Combine(workingDirectory, "edit-trace.json");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        var declaredPreset = manifest.RootElement.GetProperty("preset").GetString();
        var declaredSeed = manifest.RootElement.GetProperty("seed").GetUInt64();
        var declaredSolution = manifest.RootElement.GetProperty("solution").GetString()!;
        var declaredTrace = manifest.RootElement.GetProperty("editTrace").GetString()!;
        var manifestSolutionPath = Path.GetFullPath(Path.Combine(workingDirectory, declaredSolution));
        var manifestTracePath = Path.GetFullPath(Path.Combine(workingDirectory, declaredTrace));
        if (!string.Equals(manifestSolutionPath, solutionPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Manifest solution '{declaredSolution}' does not identify '{solutionPath}'.");
        }
        if (config.TracePath is null && !string.Equals(manifestTracePath, tracePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Manifest trace '{declaredTrace}' does not identify '{tracePath}'.");
        }
        if (config.SolutionPath is null && (declaredPreset != config.Preset || declaredSeed != config.Seed))
        {
            throw new InvalidDataException(
                $"Existing corpus is preset='{declaredPreset}', seed={declaredSeed}; requested preset='{config.Preset}', seed={config.Seed}. Set RIG_LIVE_TRIAL_REGENERATE=1 to replace it."
            );
        }
        var corpusHash = manifest.RootElement.GetProperty("corpusSha256").GetString()!;
        var computedCorpusHash = ComputeCorpusHash(workingDirectory, manifestPath);
        if (!string.Equals(corpusHash, computedCorpusHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Corpus hash '{computedCorpusHash}' does not match manifest '{corpusHash}'. Set RIG_LIVE_TRIAL_REGENERATE=1 only if replacement is intended."
            );
        }
        var traceHash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(tracePath)));
        var declaredTraceHash = manifest.RootElement.GetProperty("editTraceSha256").GetString();
        if (!string.Equals(traceHash, declaredTraceHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Trace hash '{traceHash}' does not match manifest '{declaredTraceHash}'.");
        }
        return new PreparedCorpus(solutionPath, workingDirectory, tracePath, corpusHash, traceHash);
    }

    internal static bool ShouldGenerateCorpus(bool directoryExists, bool regenerate) => regenerate || !directoryExists;

    private static string ComputeCorpusHash(string root, string manifestPath)
    {
        var manifestNode = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifestNode.Remove("corpusSha256");
        var provisionalManifest = manifestNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (
            var path in Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(path =>
                    !Path.GetRelativePath(root, path)
                        .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Any(segment => segment is "bin" or "obj" or ".rig")
                )
                .OrderBy(path => Path.GetRelativePath(root, path).Replace('\\', '/'), StringComparer.Ordinal)
        )
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var bytes = relative == "corpus-manifest.json" ? Encoding.UTF8.GetBytes(provisionalManifest) : File.ReadAllBytes(path);
            var contentHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            aggregate.AppendData(Encoding.UTF8.GetBytes(relative + "\0" + contentHash + "\n"));
        }
        return Convert.ToHexStringLower(aggregate.GetHashAndReset());
    }

    private static async Task<EditTrace> ReadTraceAsync(string path) =>
        JsonSerializer.Deserialize<EditTrace>(await File.ReadAllTextAsync(path), TraceJson)
        ?? throw new InvalidDataException($"Trace '{path}' is empty.");

    private static IReadOnlyDictionary<string, string?> CaptureEnvironment()
    {
        string[] names =
        [
            EnabledVariable,
            EngineVariable,
            "RIG_LIVE_ENGINE",
            "RIG_LIVE_TRIAL_PRESET",
            "RIG_LIVE_TRIAL_SEED",
            "RIG_LIVE_TRIAL_SOLUTION",
            "RIG_LIVE_TRIAL_TRACE",
            "RIG_LIVE_TRIAL_CORPUS",
            "RIG_LIVE_TRIAL_REGENERATE",
            "RIG_LIVE_TRIAL_REPORT",
            "RIG_LIVE_TRIAL_RULES",
            "RIG_LIVE_TRIAL_EDITS",
            "RIG_LIVE_TRIAL_CHECKPOINTS",
            "RIG_LIVE_TRIAL_QUERY_MODE",
            "RIG_LIVE_TRIAL_BUILD_CACHE",
            "RIG_LIVE_TRIAL_SAMPLING_MS",
            "DOTNET_gcServer",
            "DOTNET_GCConserveMemory",
            "DOTNET_GCHeapCount",
        ];
        return names.Order(StringComparer.Ordinal).ToDictionary(name => name, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
    }

    private static readonly IReadOnlyDictionary<string, string> UnavailableMetrics = new SortedDictionary<string, string>(
        new Dictionary<string, string>
        {
            ["activeSnapshots"] = "The legacy ResidentIndex API exposes no snapshot lease/count instrumentation.",
            ["dirtyFiles"] = "ResidentIndex exposes unreconciled project names, not its pending document/file set.",
            ["compilationsRequested"] = "No structured resident compilation counter is exposed.",
            ["compilationsRealized"] = "No structured resident compilation counter is exposed.",
            ["filesExtracted"] = "No structured per-phase extraction counter is exposed.",
            ["projectsExtracted"] = "No structured per-phase extraction counter is exposed.",
            ["factRowsScanned"] = "The flattened legacy query API exposes no scan counter.",
            ["graphPartitionsBuilt"] = "The legacy engine builds a whole graph and has no partition counter.",
            ["graphPartitionsReused"] = "The legacy engine has no cross-generation graph partition reuse counter.",
            ["artifactCacheHits"] = "BoundedArtifactMemo does not expose structured hit/miss counters.",
            ["artifactCacheMisses"] = "BoundedArtifactMemo does not expose structured hit/miss counters.",
            ["artifactCacheEvictions"] = "BoundedArtifactMemo does not expose structured eviction counters.",
            ["speculativeTasksCompleted"] = "The legacy harness drives ResidentIndex directly; no speculative scheduler exists here.",
            ["speculativeTasksCancelled"] = "The legacy harness drives ResidentIndex directly; no speculative scheduler exists here.",
        },
        StringComparer.Ordinal
    );

    private static LiveTrialWorkCounters EmptyWorkCounters(int dirtyProjects) =>
        new(
            ActiveSnapshots: null,
            DirtyFiles: null,
            DirtyProjects: dirtyProjects,
            CompilationsRequested: null,
            CompilationsRealized: null,
            FilesExtracted: null,
            ProjectsExtracted: null,
            FactRowsScanned: null,
            GraphPartitionsBuilt: null,
            GraphPartitionsReused: null,
            ArtifactCacheHits: null,
            ArtifactCacheMisses: null,
            ArtifactCacheEvictions: null,
            SpeculativeTasksCompleted: null,
            SpeculativeTasksCancelled: null
        );

    private static LiveTrialFactCounts FactCountsOf(AnalysisResult facts) =>
        new(
            facts.SourceFiles.Count,
            facts.DiRegistrations.Count,
            (facts.Symbols ?? []).Count,
            (facts.References ?? []).Count,
            (facts.TypeRelations ?? []).Count,
            (facts.DispatchFacts ?? []).Count,
            (facts.AllocationFacts ?? []).Count,
            facts.CompilationHealth?.Files.Count ?? 0,
            facts.CompilationHealth?.PartialProjects.Count ?? 0
        );

    private static LiveTrialGraphCounts GraphCountsOf(FactGraphData graph) =>
        new(
            graph.CallEdges.Count,
            graph.ImplementsEdges.Count,
            graph.Methods.Count,
            graph.BaseEdges?.Count ?? 0,
            graph.MinedDispatch?.Count ?? 0
        );

    private static IEnumerable<string> CanonicalFactRows(AnalysisResult facts, string root)
    {
        yield return "analysis|"
            + JsonSerializer.Serialize(
                new
                {
                    SolutionPath = Normalize(facts.SolutionPath, root),
                    facts.ProjectIdentity,
                    SourceProjectPath = facts.SourceProjectPath is null ? null : Normalize(facts.SourceProjectPath, root),
                }
            );
        foreach (var fact in facts.SourceFiles)
        {
            yield return "source|" + JsonSerializer.Serialize(fact with { FilePath = Normalize(fact.FilePath, root) });
        }
        foreach (var fact in facts.DiRegistrations)
        {
            yield return "di|" + JsonSerializer.Serialize(fact with { FilePath = Normalize(fact.FilePath, root) });
        }
        foreach (var fact in facts.Symbols ?? [])
        {
            yield return "symbol|" + JsonSerializer.Serialize(fact with { FilePath = Normalize(fact.FilePath, root) });
        }
        foreach (var fact in facts.References ?? [])
        {
            yield return "reference|" + JsonSerializer.Serialize(fact with { FilePath = Normalize(fact.FilePath, root) });
        }
        foreach (var fact in facts.TypeRelations ?? [])
        {
            yield return "relation|" + JsonSerializer.Serialize(fact);
        }
        foreach (var fact in facts.DispatchFacts ?? [])
        {
            yield return "dispatch|" + JsonSerializer.Serialize(fact);
        }
        foreach (var fact in facts.AllocationFacts ?? [])
        {
            yield return "allocation|" + JsonSerializer.Serialize(fact with { FilePath = Normalize(fact.FilePath, root) });
        }
        if (facts.CompilationHealth is null)
        {
            yield return "health|null";
            yield break;
        }
        yield return $"health|unlocated={facts.CompilationHealth.UnlocatedErrorCount}";
        foreach (var fact in facts.CompilationHealth.Files)
        {
            yield return "health-file|" + JsonSerializer.Serialize(fact with { FilePath = Normalize(fact.FilePath, root) });
        }
        foreach (var fact in facts.CompilationHealth.PartialProjects)
        {
            yield return "health-project|" + JsonSerializer.Serialize(fact);
        }
    }

    private static IEnumerable<string> CanonicalGraphRows(FactGraphData graph, string root)
    {
        foreach (var edge in graph.CallEdges)
        {
            yield return "call|" + JsonSerializer.Serialize(edge with { FilePath = Normalize(edge.FilePath, root) });
        }
        foreach (var edge in graph.ImplementsEdges)
        {
            yield return "implements|" + JsonSerializer.Serialize(edge);
        }
        foreach (var method in graph.Methods)
        {
            yield return "method|"
                + JsonSerializer.Serialize(method with { FilePath = method.FilePath is null ? null : Normalize(method.FilePath, root) });
        }
        foreach (var edge in graph.BaseEdges ?? [])
        {
            yield return "base|" + JsonSerializer.Serialize(edge);
        }
        foreach (var edge in graph.MinedDispatch ?? [])
        {
            yield return "dispatch|" + JsonSerializer.Serialize(edge);
        }
    }

    internal static string HashRows(IEnumerable<string> rows) => AggregateDigests(rows);

    // Semantic SET hash: each potentially large canonical row is reduced immediately to a fixed 32-byte
    // digest; duplicates collapse; only four-ulong values are retained/sorted. Rows are UTF-8; the aggregate
    // stream is the ordinal raw 32-byte digests in big-endian order, making the contract order-independent and
    // duplicate-insensitive without retaining or sorting scale-sized rows.
    private static string AggregateDigests(IEnumerable<string> rows)
    {
        var digests = new HashSet<Digest32>();
        foreach (var row in rows)
        {
            digests.Add(Digest32.From(SHA256.HashData(Encoding.UTF8.GetBytes(row))));
        }

        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> buffer = stackalloc byte[32];
        foreach (var digest in digests.Order())
        {
            digest.WriteTo(buffer);
            aggregate.AppendData(buffer);
        }
        return Convert.ToHexStringLower(aggregate.GetHashAndReset());
    }

    private readonly record struct Digest32(ulong A, ulong B, ulong C, ulong D) : IComparable<Digest32>
    {
        public static Digest32 From(ReadOnlySpan<byte> bytes) =>
            new(
                BinaryPrimitives.ReadUInt64BigEndian(bytes[..8]),
                BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(8, 8)),
                BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(16, 8)),
                BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(24, 8))
            );

        public int CompareTo(Digest32 other)
        {
            var comparison = A.CompareTo(other.A);
            if (comparison != 0)
                return comparison;
            comparison = B.CompareTo(other.B);
            if (comparison != 0)
                return comparison;
            comparison = C.CompareTo(other.C);
            return comparison != 0 ? comparison : D.CompareTo(other.D);
        }

        public void WriteTo(Span<byte> destination)
        {
            BinaryPrimitives.WriteUInt64BigEndian(destination[..8], A);
            BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(8, 8), B);
            BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(16, 8), C);
            BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(24, 8), D);
        }
    }

    private static string Normalize(string path, string root) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static void ForceFullCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private static long? TryPrivateBytes(Process process)
    {
        try
        {
            var bytes = process.PrivateMemorySize64;
            return bytes > 0 ? bytes : null;
        }
        catch (Exception exception) when (exception is PlatformNotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static int TotalFacts(LiveTrialFactCounts facts) =>
        facts.SourceFiles
        + facts.DiRegistrations
        + facts.Symbols
        + facts.References
        + facts.TypeRelations
        + facts.DispatchFacts
        + facts.Allocations;

    internal static LiveTrialRecord TestRecord(string runId, int sequence, string phase, string hash) =>
        new(
            LiveTrialReport.CurrentSchemaVersion,
            runId,
            sequence,
            "legacy",
            "abc123",
            false,
            "10.0.0",
            "10.0.100",
            "workstation",
            "corpus",
            "trace",
            null,
            new Dictionary<string, string?> { [EnabledVariable] = "1" },
            DateTimeOffset.UnixEpoch,
            phase,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddSeconds(1),
            1000,
            1,
            2,
            null,
            3,
            4,
            5,
            null,
            25,
            "sample disclosure",
            new LiveTrialFactCounts(1, 2, 3, 4, 5, 6, 7, 8, 9),
            new LiveTrialGraphCounts(1, 2, 3, 4, 5),
            EmptyWorkCounters(2),
            UnavailableMetrics,
            "fact",
            "graph",
            hash
        );

    private sealed record PhaseSnapshot(
        LiveTrialFactCounts FactCounts,
        LiveTrialGraphCounts GraphCounts,
        int DirtyProjects,
        string FactHash,
        string? GraphHash,
        string? NormalizedResultHash
    );

    private sealed record PhaseObservation(
        AnalysisResult Facts,
        FactGraphData? Graph,
        int DirtyProjects,
        LiveQueryRunner.LiveAnswer? Answer,
        object? RetainedGeneration
    );

    private sealed record PreparedCorpus(
        string SolutionPath,
        string WorkingDirectory,
        string TracePath,
        string CorpusSha256,
        string TraceSha256
    );

    private sealed record EditTrace(
        int SchemaVersion,
        ulong Seed,
        string ReplayPolicy,
        IReadOnlyList<TraceEdit> Edits,
        IReadOnlyList<QuerySeed> QuerySeeds
    );

    private sealed record TraceEdit(string Id, string Kind, string Scenario, string TargetClass, IReadOnlyList<TraceMutation> Mutations);

    private sealed record TraceMutation(
        string Project,
        string File,
        string Operation,
        string Marker,
        string Replacement,
        string ReverseMarker,
        string ReverseReplacement
    );

    private sealed record QuerySeed(string Id, string EditId, string DirtyFile, string QueryFile, string Relation, string Pattern);

    private sealed record TrialConfig(
        string Preset,
        ulong Seed,
        string? SolutionPath,
        string? TracePath,
        string CorpusDirectory,
        string ReportPath,
        string? RulesPath,
        string? BuildCacheDirectory,
        string QueryMode,
        bool Regenerate,
        int EditCount,
        IReadOnlyList<int> Checkpoints,
        int SamplingIntervalMilliseconds
    )
    {
        public static TrialConfig FromEnvironment(string repositoryRoot)
        {
            var preset = Environment.GetEnvironmentVariable("RIG_LIVE_TRIAL_PRESET")?.Trim() ?? "scale";
            var seed = ParsePositiveUlong("RIG_LIVE_TRIAL_SEED", 20260822);
            var editCount = ParsePositiveInt("RIG_LIVE_TRIAL_EDITS", 50);
            var checkpoints = ParseCheckpoints(Environment.GetEnvironmentVariable("RIG_LIVE_TRIAL_CHECKPOINTS"), editCount);
            var corpusDirectory = Path.GetFullPath(
                Environment.GetEnvironmentVariable("RIG_LIVE_TRIAL_CORPUS")
                    ?? Path.Combine(repositoryRoot, "artifacts", "live-scale", preset)
            );
            var reportPath = Path.GetFullPath(
                Environment.GetEnvironmentVariable("RIG_LIVE_TRIAL_REPORT")
                    ?? Path.Combine(repositoryRoot, "artifacts", "live-scale", "trials", "live-trial.jsonl")
            );
            return new TrialConfig(
                preset,
                seed,
                NullIfBlank(Environment.GetEnvironmentVariable("RIG_LIVE_TRIAL_SOLUTION")),
                NullIfBlank(Environment.GetEnvironmentVariable("RIG_LIVE_TRIAL_TRACE")),
                corpusDirectory,
                reportPath,
                NullIfBlank(Environment.GetEnvironmentVariable("RIG_LIVE_TRIAL_RULES")),
                NullIfBlank(Environment.GetEnvironmentVariable("RIG_LIVE_TRIAL_BUILD_CACHE")),
                ValidateQueryMode(Environment.GetEnvironmentVariable("RIG_LIVE_TRIAL_QUERY_MODE")),
                IsEnabled(Environment.GetEnvironmentVariable("RIG_LIVE_TRIAL_REGENERATE")),
                editCount,
                checkpoints,
                ParsePositiveInt("RIG_LIVE_TRIAL_SAMPLING_MS", 25)
            );
        }

        private static IReadOnlyList<int> ParseCheckpoints(string? value, int editCount)
        {
            var parsed = string.IsNullOrWhiteSpace(value)
                ? new[] { 10, 50 }
                : value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(part =>
                        int.TryParse(part, out var number) && number > 0
                            ? number
                            : throw new InvalidDataException($"Invalid checkpoint '{part}'.")
                    )
                    .ToArray();
            return parsed.Where(point => point <= editCount).Append(editCount).Distinct().Order().ToArray();
        }

        private static int ParsePositiveInt(string name, int fallback)
        {
            var value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(value) ? fallback
                : int.TryParse(value, out var number) && number > 0 ? number
                : throw new InvalidDataException($"{name} must be a positive integer.");
        }

        private static ulong ParsePositiveUlong(string name, ulong fallback)
        {
            var value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(value) ? fallback
                : ulong.TryParse(value, out var number) && number > 0 ? number
                : throw new InvalidDataException($"{name} must be a positive unsigned integer.");
        }

        private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private sealed record RunProvenance(
        string RunId,
        string Engine,
        string GitCommit,
        bool GitDirty,
        string RuntimeVersion,
        string SdkVersion,
        string GcMode,
        string CorpusSha256,
        string TraceSha256,
        string? RulesSha256,
        IReadOnlyDictionary<string, string?> Environment,
        DateTimeOffset RunStartedUtc,
        string CorpusRoot,
        RuleSet Rules
    )
    {
        private int _sequence;
        private long _allocationBoundary = GC.GetTotalAllocatedBytes(precise: true);

        public int NextSequence() => Interlocked.Increment(ref _sequence);

        public long AllocationDelta(long current) => Math.Max(0, current - Interlocked.Read(ref _allocationBoundary));

        public void CommitAllocationBoundary() => Interlocked.Exchange(ref _allocationBoundary, GC.GetTotalAllocatedBytes(precise: true));

        public static async Task<RunProvenance> CreateAsync(
            string engine,
            string corpusHash,
            string traceHash,
            string workingDirectory,
            string? rulesPath,
            IReadOnlyDictionary<string, string?> environment,
            DateTimeOffset runStarted
        )
        {
            var extras = rulesPath is null ? null : new[] { Path.GetFullPath(rulesPath) };
            var rules = RuleSetLoader.Load(workingDirectory, extras, out var loadedPaths);
            var rulesHash = loadedPaths.Count == 0 ? null : RulesFingerprint.ComputeFromPaths(loadedPaths);
            var gitCommit = (await RunProcessAsync("git", ["rev-parse", "HEAD"], RepositoryRoot())).Trim();
            var gitStatus = await RunProcessAsync("git", ["status", "--porcelain"], RepositoryRoot());
            var sdk = (await RunProcessAsync("dotnet", ["--version"], RepositoryRoot())).Trim();
            var gcMode = (GCSettings.IsServerGC ? "server" : "workstation") + $";latency={GCSettings.LatencyMode}";
            return new RunProvenance(
                Guid.NewGuid().ToString("N"),
                engine,
                gitCommit,
                !string.IsNullOrWhiteSpace(gitStatus),
                System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                sdk,
                gcMode,
                corpusHash,
                traceHash,
                rulesHash,
                environment,
                runStarted,
                workingDirectory,
                rules
            );
        }
    }

    private static async Task<string> RunProcessAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start '{executable}'.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{executable} exited {process.ExitCode}: {error}");
        }
        return output;
    }

    private sealed class PhaseMemorySampler : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _sampling;
        private readonly int _intervalMilliseconds;
        private long _peakManaged;
        private long _peakWorkingSet;
        private long _peakPrivate = -1;

        public PhaseMemorySampler(int intervalMilliseconds)
        {
            _intervalMilliseconds = intervalMilliseconds;
            Sample();
            _sampling = SampleAsync();
        }

        public long PeakManagedBytes => Interlocked.Read(ref _peakManaged);
        public long PeakWorkingSetBytes => Interlocked.Read(ref _peakWorkingSet);
        public long? PeakPrivateBytes => Interlocked.Read(ref _peakPrivate) is var value && value >= 0 ? value : null;

        public async Task StopAsync()
        {
            if (_stop.IsCancellationRequested)
            {
                return;
            }
            _stop.Cancel();
            try
            {
                await _sampling;
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            Sample();
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            _stop.Dispose();
        }

        private async Task SampleAsync()
        {
            while (true)
            {
                await Task.Delay(_intervalMilliseconds, _stop.Token);
                Sample();
            }
        }

        private void Sample()
        {
            UpdateMaximum(ref _peakManaged, GC.GetTotalMemory(forceFullCollection: false));
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            UpdateMaximum(ref _peakWorkingSet, process.WorkingSet64);
            var privateBytes = TryPrivateBytes(process);
            if (privateBytes.HasValue)
            {
                UpdateMaximum(ref _peakPrivate, privateBytes.Value);
            }
        }

        private static void UpdateMaximum(ref long location, long value)
        {
            var observed = Interlocked.Read(ref location);
            while (value > observed)
            {
                var previous = Interlocked.CompareExchange(ref location, value, observed);
                if (previous == observed)
                {
                    return;
                }
                observed = previous;
            }
        }
    }
}

public sealed class LiveSnapshotScaleTrialContractTests
{
    [Test]
    public void Engine_contract_accepts_only_the_legacy_arm()
    {
        LiveSnapshotScaleTrial.ValidateEngine("legacy").ShouldBe("legacy");
        Should
            .Throw<InvalidOperationException>(() => LiveSnapshotScaleTrial.ValidateEngine("snapshot"))
            .Message.ShouldContain("not implemented");
        Should
            .Throw<InvalidOperationException>(() => LiveSnapshotScaleTrial.ValidateEngine("future"))
            .Message.ShouldContain("Only 'legacy'");
        Should
            .Throw<InvalidOperationException>(() => LiveSnapshotScaleTrial.ValidateEngine(null))
            .Message.ShouldContain("RIG_LIVE_TRIAL_ENGINE");
        LiveSnapshotScaleTrial.ValidateRuntimeEngine("legacy", null);
        LiveSnapshotScaleTrial.ValidateRuntimeEngine("legacy", "legacy");
        Should
            .Throw<InvalidOperationException>(() => LiveSnapshotScaleTrial.ValidateRuntimeEngine("legacy", "snapshot"))
            .Message.ShouldContain("disagrees");
        LiveSnapshotScaleTrial.ValidateQueryMode(null).ShouldBe("reaches");
        LiveSnapshotScaleTrial.ValidateQueryMode("reaches").ShouldBe("reaches");
        Should
            .Throw<InvalidOperationException>(() => LiveSnapshotScaleTrial.ValidateQueryMode("tree"))
            .Message.ShouldContain("Only 'reaches'");
    }

    [Test]
    public void Corpus_reuse_requires_absence_or_an_explicit_regenerate_switch()
    {
        LiveSnapshotScaleTrial.ShouldGenerateCorpus(directoryExists: false, regenerate: false).ShouldBeTrue();
        LiveSnapshotScaleTrial.ShouldGenerateCorpus(directoryExists: true, regenerate: false).ShouldBeFalse();
        LiveSnapshotScaleTrial.ShouldGenerateCorpus(directoryExists: true, regenerate: true).ShouldBeTrue();
    }

    [Test]
    public async Task Jsonl_append_recovers_a_truncated_tail_and_regenerates_markdown()
    {
        var root = Directory.CreateTempSubdirectory("rig-live-trial-report-").FullName;
        try
        {
            var report = new LiveTrialReport(Path.Combine(root, "trial.jsonl"));
            var first = LiveSnapshotScaleTrial.TestRecord("run-a", 1, "initial-load", "hash-a");
            var second = LiveSnapshotScaleTrial.TestRecord("run-a", 2, "generation-1", "hash-b");

            await report.AppendAsync(first);
            File.ReadAllLines(report.JsonlPath).Length.ShouldBe(1);
            var firstRead = report.ReadAll();
            firstRead.Count.ShouldBe(1);
            firstRead[0].Phase.ShouldBe(first.Phase);
            firstRead[0].UnavailableMetrics.Keys.ShouldBe(first.UnavailableMetrics.Keys, ignoreOrder: false);

            await File.AppendAllTextAsync(report.JsonlPath, "{\"truncated\":");
            report.ReadAll().Select(record => record.Phase).ShouldBe(["initial-load"], ignoreOrder: false);
            await report.AppendAsync(second);

            File.ReadAllLines(report.JsonlPath).Length.ShouldBe(2);
            report.ReadAll().Select(record => record.Sequence).ShouldBe([1, 2], ignoreOrder: false);
            var markdown = File.ReadAllText(report.MarkdownPath);
            markdown.ShouldContain("initial-load");
            markdown.ShouldContain("generation-1");
            markdown.ShouldContain("malformed final JSONL row was discarded");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Canonical_hash_is_order_independent_and_content_sensitive()
    {
        LiveSnapshotScaleTrial.HashRows(["b", "a", "c"]).ShouldBe(LiveSnapshotScaleTrial.HashRows(["c", "b", "a"]));
        LiveSnapshotScaleTrial.HashRows(["a", "a", "b"]).ShouldBe(LiveSnapshotScaleTrial.HashRows(["b", "a"]));
        LiveSnapshotScaleTrial.HashRows(["b", "a", "c"]).ShouldNotBe(LiveSnapshotScaleTrial.HashRows(["b", "a", "changed"]));
        LiveTrialReport.Number(1234.5).ShouldBe("1234.5");
    }
}
