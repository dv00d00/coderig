using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Rig.Cli.CommandLine;
using Rig.Cli.Web;
using Rig.Domain.Functions;

namespace Rig.Cli.Services;

internal sealed record ServeMarker(int Port, string Url, int Pid, string WorkingDirectory, DateTimeOffset StartedUtc);

internal sealed class ServeMarkerLease : IDisposable
{
    private static readonly JsonSerializerOptions MarkerJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly ServeMarker _marker;
    private bool _disposed;

    private ServeMarkerLease(string path, ServeMarker marker)
    {
        _path = path;
        _marker = marker;
    }

    internal static ServeMarkerLease Publish(string workingDirectory, int port, string url)
    {
        var rigDirectory = StoreLayout.RigDir(workingDirectory);
        Directory.CreateDirectory(rigDirectory);
        var path = Path.Combine(rigDirectory, AnnotateResidentTransport.MarkerFileName);
        var marker = new ServeMarker(
            port,
            url,
            Environment.ProcessId,
            AnnotateResidentTransport.CanonicalPath(workingDirectory),
            DateTimeOffset.UtcNow
        );
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(marker, MarkerJson));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }

        return new ServeMarkerLease(path, marker);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (AnnotateResidentTransport.ReadMarker(_path) == _marker)
            {
                File.Delete(_path);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal static class AnnotateResidentTransport
{
    internal const string MarkerFileName = "serve.json";
    private static readonly HttpClient Client = new(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false })
    {
        Timeout = TimeSpan.FromMinutes(2),
    };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal sealed record Result(FileEffectsQueryService.Artifact? Artifact, string? Url, string? Failure);

    internal static async Task<Result> TryGetAsync(
        WorkspaceLocation location,
        string filePath,
        string? explicitHost,
        CancellationToken cancellationToken = default
    )
    {
        var candidates = new List<string>();
        string? failure = null;
        if (!string.IsNullOrWhiteSpace(explicitHost))
        {
            candidates.Add(explicitHost.Trim());
        }

        var markerPath = Path.Combine(StoreLayout.RigDir(location.WorkingDirectory), MarkerFileName);
        try
        {
            var marker = ReadMarker(markerPath);
            if (marker is not null)
            {
                if (!IsAlive(marker.Pid))
                {
                    DeleteIfUnchanged(markerPath, marker);
                    failure = "the discovered rig serve process is no longer running";
                }
                else if (!candidates.Contains(marker.Url, StringComparer.OrdinalIgnoreCase))
                {
                    candidates.Add(marker.Url);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            failure = "the rig serve marker could not be read";
        }

        foreach (var candidate in candidates)
        {
            var attempt = await TryHostAsync(location, filePath, candidate, cancellationToken);
            if (attempt.Artifact is not null)
            {
                return attempt;
            }

            failure ??= attempt.Failure;
        }

        return new Result(null, null, failure);
    }

    internal static ServeMarker? ReadMarker(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var marker =
            JsonSerializer.Deserialize<ServeMarker>(File.ReadAllText(path), Json)
            ?? throw new JsonException("The rig serve marker is empty.");
        if (
            marker.Port is < 1 or > 65535
            || marker.Pid < 1
            || string.IsNullOrWhiteSpace(marker.Url)
            || string.IsNullOrWhiteSpace(marker.WorkingDirectory)
            || marker.StartedUtc == default
        )
        {
            throw new JsonException("The rig serve marker is incomplete.");
        }

        return marker;
    }

    internal static string CanonicalPath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        return string.Equals(full, root, PathComparison) ? full : Path.TrimEndingDirectorySeparator(full);
    }

    internal static FileEffectsQueryService.Artifact ToArtifact(FileEffectsResponseDto response)
    {
        var declarations = response.Declarations ?? throw new ArgumentException("The response omitted declarations.", nameof(response));
        var locations = declarations
            .GroupBy(method => method.Id, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var method = group.First();
                    return new FileEffectsQueryService.MethodLocation(
                        method.Id,
                        method.Name,
                        method.Signature,
                        method.Line,
                        method.EndLine
                    );
                },
                StringComparer.Ordinal
            );
        var model = new FileEffectReadModel(
            response.File,
            response.Families.ToArray(),
            response
                .Methods.Select(method => new FileEffectMethod(
                    method.Id,
                    method
                        .Effects.Select(effect => new FileEffectAggregate(
                            effect.Family,
                            effect.NearestDepth,
                            effect.ViaDispatchOnly,
                            effect.Looped
                        ))
                        .ToArray()
                ))
                .ToArray(),
            response
                .Sites.Select(site => new FileEffectCallSite(
                    site.EnclosingMethodId,
                    site.TargetMethodId,
                    site.Line,
                    site.Effects.Select(effect => new FileEffectAggregate(
                            effect.Family,
                            effect.NearestDepth,
                            effect.ViaDispatchOnly,
                            effect.Looped
                        ))
                        .ToArray()
                ))
                .ToArray()
        );
        return new FileEffectsQueryService.Artifact(model, locations);
    }

    private static async Task<Result> TryHostAsync(
        WorkspaceLocation location,
        string filePath,
        string host,
        CancellationToken cancellationToken
    )
    {
        if (!TryNormalizeHost(host, out var baseUri))
        {
            return new Result(null, null, "the annotate host must be a localhost HTTP(S) URL");
        }

        try
        {
            var storeQuery = Query("store", location.StoreRef);
            using var metaResponse = await Client.GetAsync(new Uri(baseUri, "/api/meta" + storeQuery), cancellationToken);
            if (!metaResponse.IsSuccessStatusCode)
            {
                return new Result(null, null, $"rig serve rejected the requested store ({(int)metaResponse.StatusCode})");
            }

            var meta = await metaResponse.Content.ReadFromJsonAsync<RigMetaResponseDto>(Json, cancellationToken);
            if (
                meta is null
                || string.IsNullOrWhiteSpace(meta.WorkingDirectory)
                || string.IsNullOrWhiteSpace(meta.StoreDirectory)
                || !SamePath(meta.WorkingDirectory, location.WorkingDirectory)
            )
            {
                return new Result(null, null, "rig serve belongs to a different working directory");
            }

            var expectedStore = StoreLayout.ResolveReadStoreDir(location);
            if (!SamePath(meta.StoreDirectory, expectedStore))
            {
                return new Result(null, null, "rig serve resolved a different store");
            }

            var query = "?file=" + Uri.EscapeDataString(filePath) + Append("store", location.StoreRef);
            using var effectsResponse = await Client.GetAsync(new Uri(baseUri, "/api/file-effects" + query), cancellationToken);
            if (!effectsResponse.IsSuccessStatusCode)
            {
                var body = await effectsResponse.Content.ReadAsStringAsync(cancellationToken);
                var detail = body.Length > 200 ? body[..200] : body;
                return new Result(null, null, $"rig serve file-effects failed ({(int)effectsResponse.StatusCode}): {detail}");
            }

            var response = await effectsResponse.Content.ReadFromJsonAsync<FileEffectsResponseDto>(Json, cancellationToken);
            if (
                response is null
                || string.IsNullOrWhiteSpace(response.File)
                || response.Families is null
                || response.Methods is null
                || response.Sites is null
                || response.Declarations is null
                || response.Families.Any(string.IsNullOrWhiteSpace)
                || response.Methods.Any(method =>
                    method is null
                    || string.IsNullOrWhiteSpace(method.Id)
                    || method.Effects is null
                    || method.Effects.Any(effect => effect is null || string.IsNullOrWhiteSpace(effect.Family) || effect.NearestDepth < 0)
                )
                || response.Sites.Any(site =>
                    site is null
                    || string.IsNullOrWhiteSpace(site.EnclosingMethodId)
                    || site.TargetMethodId is null
                    || site.Effects is null
                    || site.Effects.Any(effect => effect is null || string.IsNullOrWhiteSpace(effect.Family) || effect.NearestDepth < 0)
                )
                || response.Declarations.Any(method =>
                    method is null || string.IsNullOrWhiteSpace(method.Id) || method.Name is null || method.Signature is null
                )
                || !SamePath(response.File, filePath)
            )
            {
                return new Result(null, null, "rig serve returned a mismatched file-effects payload");
            }

            return new Result(ToArtifact(response), baseUri.GetLeftPart(UriPartial.Authority), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
            when (ex
                    is HttpRequestException
                        or TaskCanceledException
                        or JsonException
                        or IOException
                        or InvalidOperationException
                        or ArgumentException
                        or NotSupportedException
            )
        {
            return new Result(null, null, $"rig serve request failed: {ex.Message}");
        }
    }

    private static bool TryNormalizeHost(string host, out Uri baseUri)
    {
        if (
            Uri.TryCreate(host, UriKind.Absolute, out var parsed)
            && parsed.IsLoopback
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
        )
        {
            baseUri = new UriBuilder(parsed)
            {
                Path = "/",
                Query = "",
                Fragment = "",
            }.Uri;
            return true;
        }

        baseUri = null!;
        return false;
    }

    private static string Query(string name, string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : "?" + name + "=" + Uri.EscapeDataString(value);

    private static string Append(string name, string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : "&" + name + "=" + Uri.EscapeDataString(value);

    private static bool SamePath(string left, string right) => string.Equals(CanonicalPath(left), CanonicalPath(right), PathComparison);

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void DeleteIfUnchanged(string path, ServeMarker marker)
    {
        try
        {
            if (ReadMarker(path) == marker)
            {
                File.Delete(path);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
    }
}
