using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rig.Cli.Commands;
using Rig.Cli.Effects;
using Rig.Cli.Services;

namespace Rig.Cli.Caching;

// What /api/file-findings computes and caches: ONE FILE's tiers 1-3 (hazards, amplifications, cross-method
// anchors) plus the flag that says whether tier 3 was derived at all. The inputs are already cached
// (HazardEffectsCacheKey, GraphHazardFindingsCacheKey, WarmStore's graph + invocation table) but the
// per-file derivation on top of them — the tier-3 anchor pairing over the whole-store effect set — is not,
// and it is the ~2s a repeat request must not pay. Keyed by FileFindingsCacheKey.
//
// Serializes via System.Text.Json (source-generated — AOT-safe, no reflection) over flat DTOs and GZips the
// UTF-8 bytes, the same shape as GraphHazardFindingsCodec / TreeCacheCodec. Decode returns null on any
// corruption / schema drift, so a bad or stale blob is a cache MISS (recompute), never a request failure.
//
// Provider/Operation ARE round-tripped here, unlike in GraphHazardFindingsCodec: the amplification tier
// groups by `provider:operation`, so dropping them would decode every looped_effect row with an empty
// grouping cell.
internal static class FileFindingsCodec
{
    private static readonly FileFindingsJsonContext Context = new(
        new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }
    );

    public static byte[] Encode(FileFindingsQueryService.Findings findings)
    {
        var payload = new FileFindingsPayload(
            findings.Hazards.Select(Map).ToArray(),
            findings.Amplifications.Select(Map).ToArray(),
            findings.Anchors.Select(Map).ToArray(),
            findings.CrossMethodDerived
        );
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, Context.FileFindingsPayload);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(json, offset: 0, count: json.Length);
        }

        return output.ToArray();
    }

    // Null on corruption/schema drift → treated as a cache miss (recompute).
    public static FileFindingsQueryService.Findings? Decode(byte[] blob)
    {
        try
        {
            using var input = new MemoryStream(blob);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var json = new MemoryStream();
            gzip.CopyTo(json);
            json.Position = 0;
            var payload = JsonSerializer.Deserialize(json, Context.FileFindingsPayload);
            return payload is null
                ? null
                : new FileFindingsQueryService.Findings(
                    payload.Hazards.Select(Unmap).ToArray(),
                    payload.Amplifications.Select(Unmap).ToArray(),
                    payload.Anchors.Select(Unmap).ToArray(),
                    payload.CrossMethodDerived
                );
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static FileFindingDto Map(DeriveCommand.HazardFinding f) =>
        new(
            Type: f.Type,
            Confidence: f.Confidence,
            Reason: f.Reason,
            Context: f.Context,
            Detail: f.Detail,
            Enclosing: f.Enclosing,
            FilePath: f.FilePath,
            Line: f.Line,
            Provider: f.Provider,
            Operation: f.Operation
        );

    private static DeriveCommand.HazardFinding Unmap(FileFindingDto f) =>
        new(
            Type: f.Type,
            Confidence: f.Confidence,
            Reason: f.Reason,
            Context: f.Context,
            Detail: f.Detail,
            Enclosing: f.Enclosing,
            FilePath: f.FilePath,
            Line: f.Line,
            Provider: f.Provider,
            Operation: f.Operation
        );

    private static FileAnchorFindingDto Map(CrossMethodAmplificationDataset.AnchorFinding a) =>
        new(
            Caller: a.Caller,
            FilePath: a.FilePath,
            Line: a.Line,
            IterationKind: a.IterationKind,
            WitnessProvider: a.WitnessProvider,
            WitnessOperation: a.WitnessOperation,
            WitnessResource: a.WitnessResource,
            WitnessDepth: a.WitnessDepth,
            Guards: a.Guards,
            DispatchBasis: a.DispatchBasis,
            DispatchDegree: a.DispatchDegree,
            IterationDetail: a.IterationDetail
        );

    private static CrossMethodAmplificationDataset.AnchorFinding Unmap(FileAnchorFindingDto a) =>
        new(
            Caller: a.Caller,
            FilePath: a.FilePath,
            Line: a.Line,
            IterationKind: a.IterationKind,
            WitnessProvider: a.WitnessProvider,
            WitnessOperation: a.WitnessOperation,
            WitnessResource: a.WitnessResource,
            WitnessDepth: a.WitnessDepth,
            Guards: a.Guards,
            DispatchBasis: a.DispatchBasis,
            DispatchDegree: a.DispatchDegree,
            IterationDetail: a.IterationDetail
        );
}

// The serializable wire shapes — flat twins of DeriveCommand.HazardFinding and
// CrossMethodAmplificationDataset.AnchorFinding (both are nested records the serializer can't be pointed at
// directly). AnchorFinding.Confidence and .Evidence are DERIVED, so they are deliberately not stored.
internal sealed record FileFindingDto(
    string Type,
    string Confidence,
    string Reason,
    string Context,
    string Detail,
    string Enclosing,
    string FilePath,
    int Line,
    string Provider,
    string Operation
);

internal sealed record FileAnchorFindingDto(
    string Caller,
    string FilePath,
    int Line,
    string IterationKind,
    string WitnessProvider,
    string WitnessOperation,
    string WitnessResource,
    int WitnessDepth,
    // Raw evidence, stored: Evidence is derived from these three plus WitnessDepth, so it is not.
    string? Guards,
    string? DispatchBasis,
    int DispatchDegree,
    string IterationDetail
);

internal sealed record FileFindingsPayload(
    IReadOnlyList<FileFindingDto> Hazards,
    IReadOnlyList<FileFindingDto> Amplifications,
    IReadOnlyList<FileAnchorFindingDto> Anchors,
    bool CrossMethodDerived
);

[JsonSerializable(typeof(FileFindingsPayload))]
internal partial class FileFindingsJsonContext : JsonSerializerContext { }
