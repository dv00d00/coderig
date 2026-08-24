using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rig.Cli.EntryPoints;

namespace Rig.Cli.Caching;

// What `rig callers <x> --entrypoints` needs from the whole-store entry-point derivation: the derived EPs +
// promoted handoff origins, each with its route, capability requirements, declaration site and handler DocID.
//
// This is a SECOND projection of the derivation EpSiteCacheCodec already caches, not a second derivation. The
// site map that codec stores is keyed by (file,line) and so COLLAPSES two EPs declared at one site into one
// entry, and it carries no route at all — both losses are fatal to a listing that groups by (kind, route,
// file, line) and prints the route. Hence its own payload, its own namespace (EpRecordsCacheKey), and the
// SHARED EpSchema gate: one derivation change invalidates both projections or neither.
//
// Order is preserved verbatim (derived-then-promoted); see EntryPointContext.BuildEntryPointRecords for why
// that is load-bearing rather than incidental.
//
// Serializes via System.Text.Json (source-generated — AOT-safe, no reflection) and GZips the UTF-8 bytes, the
// same shape as GraphHazardFindingsCodec / TreeCacheCodec. Decode returns null on any corruption / schema
// drift, so a bad or stale blob is a cache MISS (recompute), never a command failure.
internal static class EntryPointRecordCodec
{
    // WhenWritingNull matters here beyond size: it is what makes a null `Requires` (ungated EP) round-trip as
    // null rather than as an empty list. DeploymentMap.ActiveServices distinguishes the two, so conflating
    // them would change the answer on a warm cache — the one thing caching must never do.
    private static readonly EntryPointRecordsJsonContext Context = new(
        new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }
    );

    public static byte[] Encode(IReadOnlyList<EntryPointContext.EntryPointRecord> records)
    {
        var payload = new EntryPointRecordsPayload(records);
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, Context.EntryPointRecordsPayload);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(json, offset: 0, count: json.Length);
        }

        return output.ToArray();
    }

    // Null on corruption/schema drift → treated as a cache miss (recompute).
    public static IReadOnlyList<EntryPointContext.EntryPointRecord>? Decode(byte[] blob)
    {
        try
        {
            using var input = new MemoryStream(blob);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var json = new MemoryStream();
            gzip.CopyTo(json);
            json.Position = 0;
            return JsonSerializer.Deserialize(json, Context.EntryPointRecordsPayload)?.Records;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or NotSupportedException)
        {
            return null;
        }
    }
}

internal sealed record EntryPointRecordsPayload(IReadOnlyList<EntryPointContext.EntryPointRecord> Records);

[JsonSerializable(typeof(EntryPointRecordsPayload))]
internal partial class EntryPointRecordsJsonContext : JsonSerializerContext { }
