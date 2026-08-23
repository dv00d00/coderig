using System.Diagnostics;
using System.Threading;
using Rig.Domain.Data;

namespace Rig.Cli.Git;

// Captures the source-control provenance of an index (commit / branch / dirty-state) by shelling out to
// git, returning the Rig.Domain GitProvenance record. The shell-out lives in the CLI (not the storage or
// domain layer) — same pattern as ImpactCommand's git diff. Best-effort by contract: any failure (not a
// work tree, git not on PATH, detached/empty repo) returns GitProvenance.None so `rig index` never fails
// because of git. See docs/design-impact-behavioral-diff.md §4.5.
internal static class GitProvenanceProbe
{
    private static readonly AsyncLocal<Action<string>?> FreshnessProbeObserver = new();

    // Provenance for the work tree containing `pathInsideRepo` (a solution/project path or a directory).
    public static GitProvenance Capture(string pathInsideRepo)
    {
        var dir = File.Exists(pathInsideRepo) ? Path.GetDirectoryName(pathInsideRepo) ?? pathInsideRepo : pathInsideRepo;

        var commit = Run(dir, "rev-parse", "HEAD");
        if (string.IsNullOrEmpty(commit))
        {
            return GitProvenance.None; // not a git work tree, or git unavailable — provenance simply absent
        }

        var branch = Run(dir, "rev-parse", "--abbrev-ref", "HEAD");
        var status = Run(dir, "status", "--porcelain");
        return new GitProvenance(
            Commit: commit,
            Branch: string.IsNullOrEmpty(branch) ? null : branch,
            Dirty: !string.IsNullOrEmpty(status)
        );
    }

    // Freshness checks differ from index-time provenance capture in one deliberate way: rig's own output
    // directory is excluded from dirty-state detection. Keep Capture above unchanged so indexing still
    // stamps the complete source state it was invoked against.
    internal static GitProvenance CaptureFreshness(string repositoryRoot, string rigDirectory)
    {
        FreshnessProbeObserver.Value?.Invoke(repositoryRoot);
        var arguments = new List<string> { "status", "--porcelain=v2", "--branch", "--untracked-files=all" };
        var excludedRigPath = RelativeChildPath(repositoryRoot, rigDirectory);
        arguments.Add("--");
        arguments.Add(".");
        if (excludedRigPath is not null)
        {
            arguments.Add($":(top,exclude,literal){excludedRigPath}");
        }

        var status = Run(repositoryRoot, arguments.ToArray());
        if (status is null)
        {
            return GitProvenance.None;
        }

        string? commit = null;
        string? branch = null;
        var dirty = false;
        foreach (var line in status.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("# branch.oid ", StringComparison.Ordinal))
            {
                var value = line["# branch.oid ".Length..];
                commit = value == "(initial)" ? null : value;
            }
            else if (line.StartsWith("# branch.head ", StringComparison.Ordinal))
            {
                var value = line["# branch.head ".Length..];
                branch = value == "(detached)" ? null : value;
            }
            else if (!line.StartsWith("# ", StringComparison.Ordinal))
            {
                dirty = true;
            }
        }

        return commit is null ? GitProvenance.None : new GitProvenance(Commit: commit, Branch: branch, Dirty: dirty);
    }

    // Resolve without spawning git in the ordinary repository/worktree shape (.git directory or gitfile).
    // The rev-parse fallback retains support for unusual externally-configured work trees.
    internal static string? ResolveRepositoryRoot(string pathInsideRepo)
    {
        string? directory;
        try
        {
            var fullPath = Path.GetFullPath(pathInsideRepo);
            directory = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath);
        }
        catch
        {
            return null;
        }

        if (directory is null || !Directory.Exists(directory))
        {
            return null;
        }

        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            var marker = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(marker) || File.Exists(marker))
            {
                return current.FullName;
            }
        }

        var resolved = Run(directory, "rev-parse", "--show-toplevel");
        return string.IsNullOrWhiteSpace(resolved) ? null : Path.GetFullPath(resolved);
    }

    // AsyncLocal keeps parallel tests isolated while making the number of distinct-repository probes
    // observable without timing or process-list assertions.
    internal static IDisposable ObserveFreshnessProbes(Action<string> observer)
    {
        var previous = FreshnessProbeObserver.Value;
        FreshnessProbeObserver.Value = observer;
        return new ObserverScope(previous);
    }

    private static string? RelativeChildPath(string parentDirectory, string candidateDirectory)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(parentDirectory), Path.GetFullPath(candidateDirectory));
        if (
            relative == "."
            || Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)
        )
        {
            return null;
        }

        return relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string? Run(string workingDir, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return null;
            }

            var stdout = proc.StandardOutput.ReadToEnd();
            proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return proc.ExitCode == 0 ? stdout.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private sealed class ObserverScope(Action<string>? previous) : IDisposable
    {
        public void Dispose() => FreshnessProbeObserver.Value = previous;
    }
}
