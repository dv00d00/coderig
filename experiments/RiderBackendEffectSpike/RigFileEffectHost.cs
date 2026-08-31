extern alias runtimeSerialization;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.ReSharper.Feature.Services.Daemon;
using DataContractAttribute = runtimeSerialization::System.Runtime.Serialization.DataContractAttribute;
using DataContractJsonSerializer = runtimeSerialization::System.Runtime.Serialization.Json.DataContractJsonSerializer;
using DataMemberAttribute = runtimeSerialization::System.Runtime.Serialization.DataMemberAttribute;

namespace CodeRig.Rider;

/// <summary>
/// Non-blocking client for the resident rig host. Daemon passes only inspect the bounded cache; all
/// filesystem discovery and named-pipe IO run on a background task and complete by invalidating the daemon.
/// </summary>
internal sealed class RigFileEffectHost
{
    private const int Protocol = 1;
    private const int TimeoutMilliseconds = 2_000;
    private const int MaxFrameBytes = 16 * 1024 * 1024;
    private const int CacheCapacity = 128;

    private static readonly TimeSpan ExactCacheDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan NonExactCacheDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private readonly Dictionary<CacheKey, CacheEntry> _cache = new();
    private readonly HashSet<CacheKey> _inFlight = new();
    private readonly IDaemon _daemon;

    public RigFileEffectHost(IDaemon daemon)
    {
        _daemon = daemon;
    }

    public bool TryGet(string filePath, string snapshotToken, out FileEffectReadModel model)
    {
        var key = new CacheKey(filePath, snapshotToken);
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.ExpiresUtc > DateTime.UtcNow)
                {
                    model = entry.Model;
                    return true;
                }

                _cache.Remove(key);
            }
        }

        model = null;
        return false;
    }

    public void Request(string filePath, string snapshotToken)
    {
        var key = new CacheKey(filePath, snapshotToken);
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached) && cached.ExpiresUtc > DateTime.UtcNow)
                return;
            if (!_inFlight.Add(key))
                return;
        }

        // Scheduling is the last operation performed on the daemon thread. Root discovery, hashing and every
        // pipe operation begin inside Task.Run, so Execute never blocks on filesystem or host IO.
        _ = Task.Run(() => LoadAsync(key));
    }

    private async Task LoadAsync(CacheKey key)
    {
        var requestId = Guid.NewGuid().ToString("N");
        try
        {
            var workingDirectory = FindWorkingDirectory(key.FilePath);
            if (workingDirectory == null)
                throw new InvalidOperationException("no parent containing .git or .rig was found");

            var pipeName = PipeNameFor(workingDirectory);
            var request = new FileEffectRequest
            {
                Protocol = Protocol,
                Verb = "file-effects",
                WorkingDirectory = workingDirectory,
                RequestId = requestId,
                FilePath = key.FilePath,
                ClientSnapshotToken = key.SnapshotToken,
            };

            FileEffectResponse response;
            using (var timeout = new CancellationTokenSource())
            using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                timeout.CancelAfter(TimeoutMilliseconds);
                await pipe.ConnectAsync(TimeoutMilliseconds, timeout.Token).ConfigureAwait(false);
                await WriteFrameAsync(pipe, Serialize(request), timeout.Token).ConfigureAwait(false);
                var payload = await ReadFrameAsync(pipe, timeout.Token).ConfigureAwait(false);
                response = Deserialize<FileEffectResponse>(payload);
            }

            ValidateResponse(response, key, requestId);
            var exact =
                string.Equals(response.Status, "ok", StringComparison.Ordinal)
                && string.Equals(response.SourceStatus, "exact", StringComparison.Ordinal);
            var negative =
                string.Equals(response.Status, "declined", StringComparison.Ordinal)
                || (
                    string.Equals(response.Status, "ok", StringComparison.Ordinal)
                    && (
                        string.Equals(response.SourceStatus, "stale", StringComparison.Ordinal)
                        || string.Equals(response.SourceStatus, "unindexed", StringComparison.Ordinal)
                        || string.Equals(response.SourceStatus, "ambiguous", StringComparison.Ordinal)
                    )
                );
            if (!exact && !negative)
                throw new InvalidDataException($"unsupported response status '{response.Status}/{response.SourceStatus}'");

            var methods = exact
                ? (response.Methods ?? Array.Empty<FileEffectMethod>())
                    .Select(method =>
                    {
                        if (string.IsNullOrWhiteSpace(method.SymbolId) || string.IsNullOrWhiteSpace(method.Family))
                            throw new InvalidDataException("an exact response contained an incomplete method row");
                        return new FileEffectRow(method.SymbolId, method.Family, method.NearestDepth);
                    })
                    .ToArray()
                : Array.Empty<FileEffectRow>();
            var callSites = exact
                ? (response.CallSites ?? Array.Empty<FileEffectCallSite>())
                    .Select(callSite =>
                    {
                        // An EMPTY target is well-formed: the row is an effect observed at a call into external
                        // library code, which has no in-solution node to name. The line still identifies the
                        // invocation, and the target is only ever needed to separate two targets on one line.
                        if (
                            string.IsNullOrWhiteSpace(callSite.EnclosingSymbolId)
                            || string.IsNullOrWhiteSpace(callSite.Family)
                            || callSite.Line <= 0
                        )
                            throw new InvalidDataException("an exact response contained an incomplete call-site row");
                        return new FileEffectCallSiteRow(
                            callSite.EnclosingSymbolId,
                            callSite.TargetSymbolId ?? string.Empty,
                            callSite.Line,
                            callSite.Family,
                            callSite.NearestDepth
                        );
                    })
                    .ToArray()
                : Array.Empty<FileEffectCallSiteRow>();
            var cacheDuration = exact ? ExactCacheDuration : NonExactCacheDuration;
            Cache(key, new FileEffectReadModel(methods, callSites), DateTime.UtcNow.Add(cacheDuration));
            Console.WriteLine(
                $"[CodeRig Rider] file-effects {response.Status}/{response.SourceStatus}: "
                    + $"methods={methods.Length}, callSites={callSites.Length}, "
                    + $"generation={response.GraphGeneration}, file={key.FilePath}"
            );
        }
        catch (Exception exception)
        {
            Cache(
                key,
                new FileEffectReadModel(Array.Empty<FileEffectRow>(), Array.Empty<FileEffectCallSiteRow>()),
                DateTime.UtcNow.Add(FailureCacheDuration)
            );
            Console.WriteLine($"[CodeRig Rider] file-effects unavailable: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            lock (_gate)
                _inFlight.Remove(key);
            _daemon.Invalidate("rig file-effect response arrived");
        }
    }

    private void Cache(CacheKey key, FileEffectReadModel model, DateTime expiresUtc)
    {
        lock (_gate)
        {
            _cache[key] = new CacheEntry(model, expiresUtc, DateTime.UtcNow);
            if (_cache.Count <= CacheCapacity)
                return;

            var oldest = _cache.OrderBy(pair => pair.Value.CreatedUtc).First().Key;
            _cache.Remove(oldest);
        }
    }

    private static void ValidateResponse(FileEffectResponse response, CacheKey key, string requestId)
    {
        if (response == null)
            throw new InvalidDataException("host returned an empty JSON response");
        if (response.Protocol != Protocol)
            throw new InvalidDataException($"protocol mismatch in response ({response.Protocol})");
        if (!string.Equals(response.RequestId, requestId, StringComparison.Ordinal))
            throw new InvalidDataException("response requestId does not match the request");
        if (!string.Equals(response.FilePath, key.FilePath, StringComparison.Ordinal))
            throw new InvalidDataException("response filePath does not match the request");
        if (!string.Equals(response.ClientSnapshotToken, key.SnapshotToken, StringComparison.Ordinal))
            throw new InvalidDataException("response clientSnapshotToken does not match the request");
    }

    private static string FindWorkingDirectory(string filePath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? "");
        while (directory != null)
        {
            var git = Path.Combine(directory.FullName, ".git");
            var rig = Path.Combine(directory.FullName, ".rig");
            if (Directory.Exists(git) || File.Exists(git) || Directory.Exists(rig) || File.Exists(rig))
                return NormalizeDirectory(directory.FullName);
            directory = directory.Parent;
        }

        return null;
    }

    private static string PipeNameFor(string workingDirectory)
    {
        byte[] hash;
        using (var sha = SHA256.Create())
            hash = sha.ComputeHash(Encoding.UTF8.GetBytes(NormalizeDirectory(workingDirectory)));
        return "rig-live-" + BitConverter.ToString(hash, 0, 8).Replace("-", "").ToLowerInvariant();
    }

    private static string NormalizeDirectory(string directory)
    {
        var full = Path.GetFullPath(directory);
        var root = Path.GetPathRoot(full) ?? "";
        while (
            full.Length > root.Length
            && (full[full.Length - 1] == Path.DirectorySeparatorChar || full[full.Length - 1] == Path.AltDirectorySeparatorChar)
        )
            full = full.Substring(0, full.Length - 1);
        return Environment.OSVersion.Platform == PlatformID.Win32NT ? full.ToLowerInvariant() : full;
    }

    private static byte[] Serialize<T>(T value)
    {
        using (var stream = new MemoryStream())
        {
            new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
            return stream.ToArray();
        }
    }

    private static T Deserialize<T>(byte[] payload)
    {
        using (var stream = new MemoryStream(payload, writable: false))
            return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
    }

    private static async Task WriteFrameAsync(Stream stream, byte[] payload, CancellationToken cancellationToken)
    {
        var header = BitConverter.GetBytes(payload.Length);
        if (!BitConverter.IsLittleEndian)
            Array.Reverse(header);
        await stream.WriteAsync(header, 0, header.Length, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false);
        if (!BitConverter.IsLittleEndian)
            Array.Reverse(header);
        var length = BitConverter.ToInt32(header, 0);
        if (length < 0 || length > MaxFrameBytes)
            throw new InvalidDataException($"invalid response frame length {length}");
        var payload = new byte[length];
        await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        return payload;
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var chunk = await stream.ReadAsync(buffer, read, buffer.Length - read, cancellationToken).ConfigureAwait(false);
            if (chunk == 0)
                throw new EndOfStreamException("host closed the pipe before completing its response");
            read += chunk;
        }
    }

    private sealed class CacheKey : IEquatable<CacheKey>
    {
        public CacheKey(string filePath, string snapshotToken)
        {
            FilePath = filePath;
            SnapshotToken = snapshotToken;
        }

        public string FilePath { get; }
        public string SnapshotToken { get; }

        public bool Equals(CacheKey other) =>
            other != null
            && string.Equals(FilePath, other.FilePath, StringComparison.Ordinal)
            && string.Equals(SnapshotToken, other.SnapshotToken, StringComparison.Ordinal);

        public override bool Equals(object obj) => Equals(obj as CacheKey);

        public override int GetHashCode()
        {
            unchecked
            {
                return (StringComparer.Ordinal.GetHashCode(FilePath) * 397) ^ StringComparer.Ordinal.GetHashCode(SnapshotToken);
            }
        }
    }

    private sealed class CacheEntry
    {
        public CacheEntry(FileEffectReadModel model, DateTime expiresUtc, DateTime createdUtc)
        {
            Model = model;
            ExpiresUtc = expiresUtc;
            CreatedUtc = createdUtc;
        }

        public FileEffectReadModel Model { get; }
        public DateTime ExpiresUtc { get; }
        public DateTime CreatedUtc { get; }
    }

    [DataContract]
    private sealed class FileEffectRequest
    {
        [DataMember(Name = "protocol", IsRequired = true)]
        public int Protocol { get; set; }

        [DataMember(Name = "verb", IsRequired = true)]
        public string Verb { get; set; }

        [DataMember(Name = "workingDirectory", IsRequired = true)]
        public string WorkingDirectory { get; set; }

        [DataMember(Name = "requestId", IsRequired = true)]
        public string RequestId { get; set; }

        [DataMember(Name = "filePath", IsRequired = true)]
        public string FilePath { get; set; }

        [DataMember(Name = "clientSnapshotToken", IsRequired = true)]
        public string ClientSnapshotToken { get; set; }
    }

    [DataContract]
    private sealed class FileEffectResponse
    {
        [DataMember(Name = "protocol", IsRequired = true)]
        public int Protocol { get; set; }

        [DataMember(Name = "status", IsRequired = true)]
        public string Status { get; set; }

        [DataMember(Name = "requestId", IsRequired = true)]
        public string RequestId { get; set; }

        [DataMember(Name = "filePath", IsRequired = true)]
        public string FilePath { get; set; }

        [DataMember(Name = "clientSnapshotToken", IsRequired = true)]
        public string ClientSnapshotToken { get; set; }

        [DataMember(Name = "graphGeneration", IsRequired = true)]
        public long GraphGeneration { get; set; }

        [DataMember(Name = "sourceStatus", IsRequired = true)]
        public string SourceStatus { get; set; }

        [DataMember(Name = "methods", IsRequired = true)]
        public FileEffectMethod[] Methods { get; set; }

        [DataMember(Name = "callSites", IsRequired = true)]
        public FileEffectCallSite[] CallSites { get; set; }

        [DataMember(Name = "reason", IsRequired = true)]
        public string Reason { get; set; }
    }

    [DataContract]
    private sealed class FileEffectMethod
    {
        [DataMember(Name = "symbolId", IsRequired = true)]
        public string SymbolId { get; set; }

        [DataMember(Name = "family", IsRequired = true)]
        public string Family { get; set; }

        [DataMember(Name = "nearestDepth", IsRequired = true)]
        public int NearestDepth { get; set; }
    }

    [DataContract]
    private sealed class FileEffectCallSite
    {
        [DataMember(Name = "enclosingSymbolId", IsRequired = true)]
        public string EnclosingSymbolId { get; set; }

        [DataMember(Name = "targetSymbolId", IsRequired = true)]
        public string TargetSymbolId { get; set; }

        [DataMember(Name = "line", IsRequired = true)]
        public int Line { get; set; }

        [DataMember(Name = "family", IsRequired = true)]
        public string Family { get; set; }

        [DataMember(Name = "nearestDepth", IsRequired = true)]
        public int NearestDepth { get; set; }
    }
}
