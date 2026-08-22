using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Rig.Cli.Rendering;

// Resolves the SOURCE TEXT behind a stored (file, line, endLine) triple and renders it with a line-number
// gutter — the one place rig turns a DocID location into quotable code.
//
// The correctness problem this exists to solve: a rig store is COMMIT-SCOPED (runs.SourceCommit /
// SourceDirty) but symbol_facts carry an ABSOLUTE FilePath and line numbers frozen at index time. If the
// working tree has moved since, reading the file off disk yields the WRONG LINES for those numbers, and
// rendering them confidently would be exactly the kind of quietly-wrong answer rig exists to avoid. So the
// resolution order is fixed and disclosed:
//
//   1. WORKING TREE — only when the store is CLEAN (SourceDirty false), its SourceCommit is the current HEAD
//      of the git repo containing the file, AND that file has no uncommitted edits (`git diff --quiet HEAD
//      -- <file>`). The per-FILE check is not belt-and-braces: a repo at the right HEAD with unrelated local
//      edits is the normal state of a dev machine, and without it an edited file's disk lines get rendered
//      against the store's frozen line numbers. Verified on the real MedDBase tree, which sits at the store's
//      commit with 8 modified files.
//   2. GIT BLOB     — otherwise read the exact indexed revision out of git (`git show <commit>:<relpath>`,
//      run at the repo root). The rendered output is MARKED "(from git <shortsha>)" so the reader knows it
//      is not their working tree.
//   3. REFUSE       — no git repo, no stored commit, the commit/file is gone, or the stored line is past
//      the end of the revision's file. The caller still prints file:line; this returns a one-line reason
//      instead of text. Never render lines that cannot be attributed to the store's revision.
//
// Instance-scoped (not static) so the per-invocation caches — repo root/HEAD per directory, file text per
// (revision, path) — live and die with one command run and never leak across tests or a long-lived server.
internal sealed class SourceRenderer
{
    // Absurd-output guard: a 1,600-line god class is not a useful answer. Beyond this many lines the tail is
    // dropped and an explicit "… truncated N lines" marker is rendered in its place.
    internal const int DefaultMaxLines = 400;

    // Displayed length of a commit sha, matching the 12 chars `rig runs` shows.
    private const int ShortShaLength = 12;

    private readonly string? _storeCommit;
    private readonly bool _storeDirty;
    private readonly Dictionary<string, RepoInfo?> _repos = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>?> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _clean = new(StringComparer.OrdinalIgnoreCase);

    // `storeCommit` / `storeDirty` come from the run being read (RunSummary.SourceCommit/SourceDirty).
    // A null commit means the store carries no provenance (pre-stamping index, or a non-git source), which
    // makes every location unattributable — the resolver then refuses rather than guessing.
    internal SourceRenderer(string? storeCommit, bool storeDirty)
    {
        _storeCommit = string.IsNullOrWhiteSpace(storeCommit) ? null : storeCommit!.Trim();
        _storeDirty = storeDirty;
    }

    // Resolve the declaration text for [startLine, endLine] of `filePath` (absolute, as stored), padded by
    // `context` lines either side and capped at `maxLines`. Never throws: every failure comes back as an
    // Unavailable snippet carrying a short reason.
    internal SourceSnippet Resolve(string filePath, int startLine, int endLine, int context = 0, int maxLines = DefaultMaxLines)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return SourceSnippet.Unavailable("the store has no file path for this symbol");
        }

        if (startLine <= 0)
        {
            return SourceSnippet.Unavailable("the store has no line number for this symbol");
        }

        if (_storeCommit is null)
        {
            return SourceSnippet.Unavailable("the store records no source commit, so its line numbers cannot be attributed to a revision");
        }

        var repo = RepoFor(filePath);
        if (repo is null)
        {
            return SourceSnippet.Unavailable(
                $"'{Path.GetFileName(filePath)}' is not inside a git work tree, so the indexed revision cannot be read"
            );
        }

        var relative = RepoRelativePath(repo.Root, filePath);
        if (relative is null)
        {
            return SourceSnippet.Unavailable($"'{filePath}' resolves outside the git work tree at {repo.Root}");
        }

        var first = Math.Max(val1: 1, val2: startLine - context);
        var last = Math.Max(val1: endLine <= 0 ? startLine : endLine, val2: startLine) + context;

        // 1. FAST PATH — clean store, commit IS the current HEAD, and THIS file is unmodified: disk is the
        //    indexed revision, so read it directly (no git process per file, no blob decode).
        if (!_storeDirty && ShaMatches(repo.Head, _storeCommit) && File.Exists(filePath) && MatchesHead(repo.Root, relative))
        {
            var working = ReadWorkingTree(filePath);
            if (working is not null)
            {
                return Slice(working, first, last, maxLines, SourceOrigin.WorkingTree, commit: null);
            }
        }

        // 2. GIT BLOB PATH — read the exact indexed revision out of the object store.
        var blob = ReadGitBlob(repo.Root, _storeCommit, relative);
        if (blob is null)
        {
            return SourceSnippet.Unavailable(
                $"git could not read {relative} at {Short(_storeCommit)} (commit or file missing from this work tree)"
            );
        }

        return Slice(blob, first, last, maxLines, SourceOrigin.GitBlob, commit: _storeCommit);
    }

    // Render a resolved snippet: one line per source line, right-aligned line-number gutter, then the
    // truncation marker if the tail was dropped. An Unavailable snippet renders its one-line reason instead
    // of text — the caller has already printed file:line, so the location is never lost.
    internal static void Render(TextWriter output, SourceSnippet snippet, string indent)
    {
        if (snippet.Origin == SourceOrigin.Unavailable)
        {
            output.WriteLine($"{indent}(source unavailable: {snippet.Reason})");
            return;
        }

        var gutter = snippet.Lines.Count == 0 ? 1 : snippet.Lines[^1].Number.ToString(CultureInfo.InvariantCulture).Length;
        foreach (var line in snippet.Lines)
        {
            output.WriteLine($"{indent}{line.Number.ToString(CultureInfo.InvariantCulture).PadLeft(gutter)} | {line.Text}");
        }

        if (snippet.TruncatedCount > 0)
        {
            output.WriteLine($"{indent}{new string(' ', gutter)} … truncated {snippet.TruncatedCount} lines");
        }
    }

    // The disclosure chip for a snippet's header line: empty for the working tree (what the user already
    // sees in their editor), an explicit git marker otherwise. A DIRTY store is disclosed too — its facts
    // were extracted from uncommitted edits, so even the exact commit's blob may not be what was indexed.
    internal string OriginMarker(SourceSnippet snippet) =>
        snippet.Origin switch
        {
            SourceOrigin.GitBlob when _storeDirty => $" (from git {Short(snippet.Commit)}; store indexed a DIRTY tree — source may differ)",
            SourceOrigin.GitBlob => $" (from git {Short(snippet.Commit)})",
            _ => "",
        };

    private static SourceSnippet Slice(IReadOnlyList<string> lines, int first, int last, int maxLines, SourceOrigin origin, string? commit)
    {
        if (first > lines.Count)
        {
            return SourceSnippet.Unavailable(
                $"line {first} is past the end of the file at this revision ({lines.Count} lines) — store and source disagree"
            );
        }

        var end = Math.Min(last, lines.Count);
        var count = end - first + 1;
        var shown = Math.Min(count, Math.Max(val1: 1, val2: maxLines));
        var slice = new SourceLine[shown];
        for (var i = 0; i < shown; i++)
        {
            slice[i] = new SourceLine(Number: first + i, Text: lines[first + i - 1]);
        }

        return new SourceSnippet(origin, slice, TruncatedCount: count - shown, Commit: commit);
    }

    private IReadOnlyList<string>? ReadWorkingTree(string filePath)
    {
        if (_files.TryGetValue(filePath, out var cached))
        {
            return cached;
        }

        IReadOnlyList<string>? lines;
        try
        {
            lines = File.ReadAllLines(filePath);
        }
        catch (IOException)
        {
            lines = null;
        }
        catch (UnauthorizedAccessException)
        {
            lines = null;
        }

        _files[filePath] = lines;
        return lines;
    }

    private IReadOnlyList<string>? ReadGitBlob(string root, string commit, string relativePath)
    {
        var key = $"{commit}:{relativePath}";
        if (_files.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var text = RunGit(root, trim: false, "show", key);
        var lines = text is null ? null : SplitLines(text);
        _files[key] = lines;
        return lines;
    }

    // git wants a forward-slash path relative to the work-tree root; the store holds an absolute Windows
    // path with backslashes. Returns null when the file resolves outside the root (a different work tree).
    private static string? RepoRelativePath(string root, string filePath)
    {
        var relative = Path.GetRelativePath(NormalizeMacPath(root), NormalizeMacPath(filePath)).Replace(oldChar: '\\', newChar: '/');
        return relative.StartsWith("../", StringComparison.Ordinal) || relative == ".." || Path.IsPathRooted(relative) ? null : relative;
    }

    private static string NormalizeMacPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!OperatingSystem.IsMacOS())
        {
            return fullPath;
        }

        return fullPath.StartsWith("/var/", StringComparison.Ordinal) || fullPath.StartsWith("/tmp/", StringComparison.Ordinal)
            ? "/private" + fullPath
            : fullPath;
    }

    // Repo root + HEAD for the work tree containing `filePath`, cached per directory (many symbols share a
    // directory, and each probe is a process spawn). Null when the path is not in a git work tree or git is
    // unavailable — the refuse path.
    private RepoInfo? RepoFor(string filePath)
    {
        var dir = NearestExistingDirectory(filePath);
        if (dir is null)
        {
            return null;
        }

        if (_repos.TryGetValue(dir, out var cached))
        {
            return cached;
        }

        var root = RunGit(dir, trim: true, "rev-parse", "--show-toplevel");
        var info = root is null ? null : new RepoInfo(Root: Path.GetFullPath(root), Head: RunGit(dir, trim: true, "rev-parse", "HEAD"));
        _repos[dir] = info;
        return info;
    }

    // A deleted/moved file still has an existing ANCESTOR directory inside the repo; git run there answers
    // for the same work tree. (Spawning a process with a non-existent working directory just throws.)
    private static string? NearestExistingDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        while (dir is not null && !Directory.Exists(dir))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir;
    }

    // Prefix-tolerant sha comparison: the store may carry a full 40-char sha while a caller/test supplies a
    // short one (and vice versa). Requires at least 7 chars so a stray prefix can never "match".
    private static bool ShaMatches(string? left, string? right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
        {
            return false;
        }

        var shortest = Math.Min(left!.Length, right!.Length);
        return shortest >= 7
            && left.AsSpan(start: 0, length: shortest).Equals(right.AsSpan(start: 0, length: shortest), StringComparison.OrdinalIgnoreCase);
    }

    private static string Short(string? sha) =>
        string.IsNullOrEmpty(sha) ? "?"
        : sha!.Length <= ShortShaLength ? sha
        : sha.Substring(startIndex: 0, length: ShortShaLength);

    private static IReadOnlyList<string> SplitLines(string text)
    {
        // git hands back the raw blob bytes, so a UTF-8 BOM survives into the first line; strip it so the
        // first rendered line is not prefixed with an invisible ﻿.
        var body = text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
        var lines = body.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].EndsWith('\r'))
            {
                lines[i] = lines[i][..^1];
            }
        }

        // A trailing newline yields one phantom empty element; drop it so line counts match the file's.
        return lines.Length > 1 && lines[^1].Length == 0 ? lines[..^1] : lines;
    }

    // True when the work-tree copy of `relativePath` is IDENTICAL to HEAD (no staged or unstaged edit).
    // `git diff --quiet` exits 0 for "no difference", 1 for "differs", >1 for an error — an error (or git
    // being unavailable) is treated as "differs", i.e. it degrades to the git-blob path, never to a wrong
    // render. Cached per file: one process spawn per rendered file at most.
    private bool MatchesHead(string root, string relativePath)
    {
        var key = $"{root}|{relativePath}";
        if (_clean.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var clean = RunGitCore(root, "diff", "--quiet", "HEAD", "--", relativePath).ExitCode == 0;
        _clean[key] = clean;
        return clean;
    }

    // Shell out to git, mirroring GitProvenanceProbe: best-effort by contract — any failure (git absent, not
    // a work tree, unknown revision) is a null, never an exception. `trim` is false for `show` (leading
    // whitespace of the first source line is DATA) and true for the rev-parse probes.
    private static string? RunGit(string workingDirectory, bool trim, params string[] args)
    {
        var (exitCode, stdout) = RunGitCore(workingDirectory, args);
        return exitCode == 0 ? (trim ? stdout.Trim() : stdout) : null;
    }

    // Exit code + stdout of a git invocation; (-1, "") when git could not be run at all.
    private static (int ExitCode, string StdOut) RunGitCore(string workingDirectory, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            };
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return (-1, "");
            }

            var stdout = proc.StandardOutput.ReadToEnd();
            proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return (proc.ExitCode, stdout);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return (-1, "");
        }
    }

    private sealed record RepoInfo(string Root, string? Head);
}

// Where a snippet's text came from — the attribution the renderer discloses.
internal enum SourceOrigin
{
    // The working-tree file, verified to be at the store's own commit (clean store, HEAD == SourceCommit).
    WorkingTree,

    // The store's indexed revision, read out of git. NOT necessarily what is on disk right now.
    GitBlob,

    // No attributable text; `Reason` says why. The location (file:line) is still valid.
    Unavailable,
}

internal readonly record struct SourceLine(int Number, string Text);

// A resolved (or refused) source range. `Lines` is the rendered slice in file order; `TruncatedCount` is how
// many lines of the requested range were dropped by the cap; `Commit` is set for a git-blob read.
internal sealed record SourceSnippet(
    SourceOrigin Origin,
    IReadOnlyList<SourceLine> Lines,
    int TruncatedCount = 0,
    string? Commit = null,
    string? Reason = null
)
{
    internal static SourceSnippet Unavailable(string reason) => new(SourceOrigin.Unavailable, Lines: [], Reason: reason);

    internal bool HasText => Origin != SourceOrigin.Unavailable;
}
