using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Rig.Tests.Analysis;

internal sealed record LiveTrialFactCounts(
    int SourceFiles,
    int DiRegistrations,
    int Symbols,
    int References,
    int TypeRelations,
    int DispatchFacts,
    int Allocations,
    int CompilationHealthFiles,
    int PartialProjects
);

internal sealed record LiveTrialGraphCounts(int? CallEdges, int? ImplementsEdges, int? Methods, int? BaseEdges, int? MinedDispatchEdges);

internal sealed record LiveTrialWorkCounters(
    int? ActiveSnapshots,
    int? DirtyFiles,
    int? DirtyProjects,
    long? CompilationsRequested,
    long? CompilationsRealized,
    long? FilesExtracted,
    long? ProjectsExtracted,
    long? FactRowsScanned,
    long? GraphPartitionsBuilt,
    long? GraphPartitionsReused,
    long? ArtifactCacheHits,
    long? ArtifactCacheMisses,
    long? ArtifactCacheEvictions,
    long? SpeculativeTasksCompleted,
    long? SpeculativeTasksCancelled
);

internal sealed record LiveTrialRecord(
    int SchemaVersion,
    string RunId,
    int Sequence,
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
    string Phase,
    DateTimeOffset PhaseStartedUtc,
    DateTimeOffset PhaseCompletedUtc,
    double DurationMilliseconds,
    long ManagedLiveBytes,
    long WorkingSetBytes,
    long? PrivateBytes,
    long AllocatedBytesDelta,
    long SampledPeakManagedBytes,
    long SampledPeakWorkingSetBytes,
    long? SampledPeakPrivateBytes,
    int SamplingIntervalMilliseconds,
    string SamplingDisclosure,
    LiveTrialFactCounts Facts,
    LiveTrialGraphCounts Graph,
    LiveTrialWorkCounters Work,
    IReadOnlyDictionary<string, string> UnavailableMetrics,
    string FactHash,
    string? GraphHash,
    string? NormalizedResultHash
);

internal sealed class LiveTrialReport
{
    // v2->v3: publication milestones, semantic-set hashes, and engine-only measurement boundaries.
    internal const int CurrentSchemaVersion = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };
    private bool _truncatedTailDiscarded;

    public LiveTrialReport(string jsonlPath)
    {
        JsonlPath = Path.GetFullPath(jsonlPath);
        MarkdownPath = Path.ChangeExtension(JsonlPath, ".md");
    }

    public string JsonlPath { get; }
    public string MarkdownPath { get; }

    public async Task AppendAsync(LiveTrialRecord record, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(JsonlPath)!);
        _truncatedTailDiscarded |= TrimIncompleteTail();
        var line = JsonSerializer.Serialize(record, JsonOptions) + "\n";
        await using (
            var stream = new FileStream(
                JsonlPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough
            )
        )
        {
            var bytes = Encoding.UTF8.GetBytes(line);
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }

        await RegenerateMarkdownAsync(cancellationToken);
    }

    // A process can die in the middle of the one JSONL write that is in flight. Every earlier milestone is
    // already newline-terminated and flushed. Drop only that unterminated tail before resuming so a later
    // append cannot concatenate a new object onto the partial JSON and make the durable prefix unreadable.
    private bool TrimIncompleteTail()
    {
        if (!File.Exists(JsonlPath))
        {
            return false;
        }

        using var stream = new FileStream(JsonlPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        if (stream.Length == 0)
        {
            return false;
        }

        stream.Position = stream.Length - 1;
        if (stream.ReadByte() == (byte)'\n')
        {
            var start = 0L;
            for (var position = stream.Length - 2; position >= 0; position--)
            {
                stream.Position = position;
                if (stream.ReadByte() == (byte)'\n')
                {
                    start = position + 1;
                    break;
                }
            }

            var length = checked((int)(stream.Length - 1 - start));
            var bytes = new byte[length];
            stream.Position = start;
            stream.ReadExactly(bytes);
            try
            {
                _ =
                    JsonSerializer.Deserialize<LiveTrialRecord>(bytes, JsonOptions)
                    ?? throw new JsonException("The final JSONL row deserialized as null.");
                return false;
            }
            catch (JsonException)
            {
                stream.SetLength(start);
                stream.Flush(flushToDisk: true);
                return true;
            }
        }

        for (var position = stream.Length - 2; position >= 0; position--)
        {
            stream.Position = position;
            if (stream.ReadByte() != (byte)'\n')
            {
                continue;
            }

            stream.SetLength(position + 1);
            stream.Flush(flushToDisk: true);
            return true;
        }

        stream.SetLength(0);
        stream.Flush(flushToDisk: true);
        return true;
    }

    public IReadOnlyList<LiveTrialRecord> ReadAll()
    {
        if (!File.Exists(JsonlPath))
        {
            return [];
        }

        var records = new List<LiveTrialRecord>();
        var lines = File.ReadAllLines(JsonlPath);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                records.Add(
                    JsonSerializer.Deserialize<LiveTrialRecord>(line, JsonOptions)
                        ?? throw new InvalidDataException($"A null live-trial record was read from '{JsonlPath}'.")
                );
            }
            catch (JsonException) when (index == lines.Length - 1)
            {
                _truncatedTailDiscarded = true;
            }
        }
        return records;
    }

    private async Task RegenerateMarkdownAsync(CancellationToken cancellationToken)
    {
        var records = ReadAll();
        var builder = new StringBuilder();
        builder.AppendLine("# Live snapshot scale trial");
        builder.AppendLine();
        builder.AppendLine(
            "JSONL is the source of truth. Memory peaks are sampled in-process and may miss spikes shorter than the interval."
        );
        builder.AppendLine();
        if (_truncatedTailDiscarded)
        {
            builder.AppendLine(
                "Recovery disclosure: an unterminated or malformed final JSONL row was discarded; all prior flushed milestones were preserved."
            );
            builder.AppendLine();
        }

        foreach (var run in records.GroupBy(record => record.RunId, StringComparer.Ordinal))
        {
            var first = run.First();
            builder.AppendLine($"## Run `{Escape(first.RunId)}` — `{Escape(first.Engine)}`");
            builder.AppendLine();
            builder.AppendLine(
                $"Commit `{Escape(first.GitCommit)}` ({(first.GitDirty ? "dirty" : "clean")}); runtime `{Escape(first.RuntimeVersion)}`; "
                    + $"SDK `{Escape(first.SdkVersion)}`; GC `{Escape(first.GcMode)}`."
            );
            builder.AppendLine();
            builder.AppendLine(
                $"Corpus `{Escape(first.CorpusSha256)}`; trace `{Escape(first.TraceSha256)}`; rules `{Escape(first.RulesSha256 ?? "none")}`."
            );
            builder.AppendLine();
            builder.AppendLine(
                "| Phase | Duration ms | Managed live MiB | Peak WS MiB | Facts | Call edges | Dirty projects | Result hash |"
            );
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---|");
            foreach (var record in run.OrderBy(record => record.Sequence))
            {
                builder.AppendLine(
                    $"| {Escape(record.Phase)} | {Number(record.DurationMilliseconds)} | {Number(ToMib(record.ManagedLiveBytes))} | "
                        + $"{Number(ToMib(record.SampledPeakWorkingSetBytes))} | {TotalFacts(record.Facts)} | {record.Graph.CallEdges?.ToString(CultureInfo.InvariantCulture) ?? "n/a"} | "
                        + $"{record.Work.DirtyProjects?.ToString(CultureInfo.InvariantCulture) ?? "n/a"} | `{Escape(record.NormalizedResultHash ?? "n/a")}` |"
                );
            }
            builder.AppendLine();
            builder.AppendLine("Sampling: " + Escape(first.SamplingDisclosure));
            builder.AppendLine();
            builder.AppendLine("Unavailable structured metrics by phase:");
            builder.AppendLine();
            var unavailableByMetric = run.SelectMany(record =>
                    record.UnavailableMetrics.Select(metric => new
                    {
                        metric.Key,
                        metric.Value,
                        record.Phase,
                    })
                )
                .GroupBy(item => (item.Key, item.Value))
                .OrderBy(group => group.Key.Key, StringComparer.Ordinal);
            foreach (var unavailable in unavailableByMetric)
            {
                var phases = string.Join(
                    ", ",
                    unavailable.Select(item => item.Phase).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                );
                builder.AppendLine($"- `{Escape(unavailable.Key.Key)}` ({Escape(phases)}) — {Escape(unavailable.Key.Value)}");
            }
            builder.AppendLine();
            builder.AppendLine("Environment:");
            builder.AppendLine();
            foreach (var setting in first.Environment.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                builder.AppendLine($"- `{Escape(setting.Key)}` = `{Escape(setting.Value ?? "(unset)")}`");
            }
            builder.AppendLine();
        }

        var temporaryPath = MarkdownPath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, builder.ToString(), new UTF8Encoding(false), cancellationToken);
        File.Move(temporaryPath, MarkdownPath, overwrite: true);
    }

    private static string Escape(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal).Replace("`", "'", StringComparison.Ordinal);

    private static double ToMib(long bytes) => bytes / (1024d * 1024d);

    internal static string Number(double value) => value.ToString("F1", CultureInfo.InvariantCulture);

    private static int TotalFacts(LiveTrialFactCounts facts) =>
        facts.SourceFiles
        + facts.DiRegistrations
        + facts.Symbols
        + facts.References
        + facts.TypeRelations
        + facts.DispatchFacts
        + facts.Allocations;
}
