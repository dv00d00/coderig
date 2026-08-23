using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rig.Cli.Services;

namespace Rig.Cli.Caching;

// GZip + source-generated JSON, matching the other query artifact codecs. Corruption/schema drift is a
// cache miss, never a query failure.
internal static class HotspotsCodec
{
    private static readonly HotspotsJsonContext Context = new();

    internal static byte[] Encode(HotspotsQueryService.HotspotArtifact artifact)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(artifact, Context.HotspotArtifact);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(json, 0, json.Length);
        }

        return output.ToArray();
    }

    internal static HotspotsQueryService.HotspotArtifact? Decode(byte[] blob)
    {
        try
        {
            using var input = new MemoryStream(blob);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            return JsonSerializer.Deserialize(gzip, Context.HotspotArtifact);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or NotSupportedException)
        {
            return null;
        }
    }
}

[JsonSerializable(typeof(HotspotsQueryService.HotspotArtifact))]
internal partial class HotspotsJsonContext : JsonSerializerContext { }
