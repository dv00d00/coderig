using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Rig.Analysis.Inventory;

// Per-project sidecar cache of the design-time build output (ProjectBuildInfo), keyed by an input
// fingerprint (BuildInputFingerprint). On a HIT the expensive out-of-process design-time build is
// skipped entirely and the cached references/sources/options are replayed — Roslyn still reads the
// actual source + reference bytes fresh, so a hit is only safe because the fingerprint already proved
// the build INPUTS (refs/options/file-set) are unchanged and Roslyn subsequently compiled the project
// without errors. Best-effort: any IO/JSON failure degrades to a miss (rebuild); the cache can never block,
// corrupt, or wrong an index. Sidecars live OUTSIDE the per-commit store so they persist/shared across
// indexes.
internal sealed class BuildResultCache(string cacheDirectory, string? framework = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // IO PORT: the sidecar's stored payload (fingerprint + build output) if one exists and parses, else null
    // (absent or garbled → treated as a miss). Deliberately does NOT compare fingerprints — whether it still
    // matches is the pure BuildCacheDecision.Decide, kept out of the IO so it can be tested in isolation.
    public StoredBuild? Load(string projectFilePath)
    {
        try
        {
            var path = SidecarPath(projectFilePath);
            return File.Exists(path) ? JsonSerializer.Deserialize<StoredBuild>(File.ReadAllText(path), JsonOptions) : null;
        }
        catch
        {
            return null; // unreadable/garbled sidecar → treat as miss, rebuild
        }
    }

    // A successful Buildalyzer result is only a CANDIDATE: write it unadmitted so a crash or concurrent
    // reader cannot replay it before Roslyn verifies the project's actual compilation. Buildalyzer failure
    // rejects immediately, while its ProjectBuildInfo remains usable by the current disclosed partial run.
    public string? StoreCandidate(string projectFilePath, string fingerprint, ProjectBuildInfo info, bool buildalyzerSucceeded)
    {
        if (!buildalyzerSucceeded)
        {
            Reject(projectFilePath);
            return null;
        }

        var candidateId = Guid.NewGuid().ToString("N");
        var path = SidecarPath(projectFilePath);
        using var sidecarLock = AcquireSidecarLock(path);
        if (sidecarLock is null)
        {
            return null;
        }

        var candidate = new StoredBuild(Fingerprint: fingerprint, Info: info, Admitted: false, CandidateId: candidateId);
        return WriteAtomically(path, candidate) ? candidateId : null;
    }

    // Roslyn compiled THIS candidate without errors. The exact token prevents one process/run from admitting
    // a same-fingerprint payload staged later by another writer.
    public bool PromoteCandidate(string projectFilePath, string candidateId)
    {
        var path = SidecarPath(projectFilePath);
        using var sidecarLock = AcquireSidecarLock(path);
        if (sidecarLock is null)
        {
            return false;
        }

        var stored = Load(projectFilePath);
        if (stored is null || stored.Admitted || !string.Equals(stored.CandidateId, candidateId, StringComparison.Ordinal))
        {
            return false;
        }

        return WriteAtomically(path, stored with { Admitted = true });
    }

    // Failed Buildalyzer or Roslyn compilation invalidates candidates and admitted hits alike.
    public void Reject(string projectFilePath)
    {
        try
        {
            var path = SidecarPath(projectFilePath);
            using var sidecarLock = AcquireSidecarLock(path);
            if (sidecarLock is not null)
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cache IO. The failed result itself is never serialized as admitted.
        }
    }

    // The token comparison + replacement is one cross-process critical section. Without this small lock,
    // another writer could stage a new token between PromoteCandidate's Load and atomic Move (TOCTOU).
    private static FileStream? AcquireSidecarLock(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        }
        catch
        {
            return null;
        }

        var lockPath = path + ".lock";
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < 39)
            {
                Thread.Sleep(5);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    // Same-directory unique temp + rename means a concurrent reader sees the old complete JSON or the new
    // complete JSON, never a truncate-and-rewrite fragment. Promotion and candidate staging share this path.
    private static bool WriteAtomically(string path, StoredBuild stored)
    {
        string? tempPath = null;
        try
        {
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tempPath, JsonSerializer.Serialize(stored, JsonOptions));
            File.Move(tempPath, path, overwrite: true);
            tempPath = null;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (tempPath is not null)
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Best-effort temp cleanup; temp files are never considered by Load.
                }
            }
        }
    }

    // Stable filename from the normalised project path (content of the path, not the project).
    private string SidecarPath(string projectFilePath)
    {
        // v2 carries additional files + analyzer-config paths into the reconstructed Roslyn project.
        // A v1 hit omits generator MSBuild options and can invent failures such as Razor RZ3600.
        var identity = "build-output:v2\n" + Path.GetFullPath(projectFilePath);
        if (framework is not null)
        {
            identity += $"\nframework:{framework.ToUpperInvariant()}";
        }

        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
        return Path.Combine(cacheDirectory, key + ".json");
    }
}
