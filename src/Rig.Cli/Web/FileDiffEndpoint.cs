using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Rig.Cli.CommandLine;
using Rig.Cli.Services;
using Rig.Storage.Queries;
using static Rig.Cli.Graph.TraversalGraphLoader;

namespace Rig.Cli.Web;

internal static class FileDiffEndpoint
{
    private const int ContextLines = 20;

    internal static void MapFileDiff(this WebApplication app, string workingDirectory)
    {
        // A review session opens many files from one immutable commit pair. Keep the exact Git inventory and
        // store-to-path join once per pair; otherwise every j/k navigation repeats the repository-wide
        // --find-copies-harder scan (an N+1 over changed files). Failed loads are evicted so repair can retry.
        var inventories = new ConcurrentDictionary<(string Base, string Head), Task<ReviewInventory>>();
        async Task<ReviewInventory> LoadInventoryCachedAsync(string baseStore, string headStore)
        {
            var key = (baseStore, headStore);
            var task = inventories.GetOrAdd(key, _ => LoadReviewInventoryAsync(workingDirectory, baseStore, headStore));
            try
            {
                return await task;
            }
            catch
            {
                inventories.TryRemove(new KeyValuePair<(string Base, string Head), Task<ReviewInventory>>(key, task));
                throw;
            }
        }

        app.MapGet(
            "/api/review-source",
            async (string? @base, string? head, string? file, string? side) =>
            {
                if (
                    string.IsNullOrWhiteSpace(@base)
                    || string.IsNullOrWhiteSpace(head)
                    || string.IsNullOrWhiteSpace(file)
                    || side is not ("base" or "head")
                    || string.Equals(@base, head, StringComparison.OrdinalIgnoreCase)
                )
                {
                    return Results.Problem(
                        title: "Invalid review source request",
                        detail: "Provide distinct base/head stores, a changed file and side=base|head.",
                        statusCode: 400
                    );
                }

                try
                {
                    // Shares the already loaded immutable inventory; never re-derive annotations or scan
                    // every changed file merely to switch from hunks to source.
                    var inventory = await LoadInventoryCachedAsync(@base, head);
                    var changed = ResolveChangedFile(inventory.Files, file);
                    var path = side == "base" ? changed.OldPath : changed.NewPath;
                    var store = side == "base" ? @base : head;
                    var commit = side == "base" ? inventory.Base.Commit : inventory.Head.Commit;
                    var language = System.IO.Path.GetExtension(path ?? changed.Path).Equals(".cs", StringComparison.OrdinalIgnoreCase)
                        ? "csharp"
                        : "text";
                    var blob = path is null
                        ? new ReviewSourceBlob("not-present", null, null, "This file does not exist in the selected revision.")
                        : await ReadReviewBlobAsync(inventory.Repo, commit, path);
                    return Results.Json(
                        new ReviewSourceResponseDto(
                            changed.Path,
                            side,
                            store,
                            commit,
                            path,
                            language,
                            blob.State,
                            blob.Content,
                            blob.ByteLength,
                            blob.Reason
                        )
                    );
                }
                catch (Exception ex)
                {
                    return Results.Problem(title: "Review source failed", detail: ex.Message, statusCode: 400);
                }
            }
        );

        app.MapGet(
            "/api/file-diff",
            async (string? @base, string? head, string? file, bool? ignoreWhitespace) =>
            {
                if (string.IsNullOrWhiteSpace(@base) || string.IsNullOrWhiteSpace(head) || string.IsNullOrWhiteSpace(file))
                {
                    return Results.Problem(
                        title: "Missing base/head/file",
                        detail: "Provide ?base=<store>&head=<store>&file=<changed path>.",
                        statusCode: 400
                    );
                }

                if (string.Equals(@base, head, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Problem(title: "Identical revisions", detail: "Base and head stores must differ.", statusCode: 400);
                }

                try
                {
                    var inventory = await LoadInventoryCachedAsync(@base, head);
                    return Results.Json(await BuildAsync(workingDirectory, @base, head, file, inventory, ignoreWhitespace == true));
                }
                catch (Exception ex)
                {
                    return Results.Problem(title: "File diff failed", detail: ex.Message, statusCode: 400);
                }
            }
        );

        app.MapGet(
            "/api/review-files",
            async (string? @base, string? head) =>
            {
                if (string.IsNullOrWhiteSpace(@base) || string.IsNullOrWhiteSpace(head))
                {
                    return Results.Problem(title: "Missing base/head", detail: "Provide ?base=<store>&head=<store>.", statusCode: 400);
                }

                if (string.Equals(@base, head, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Problem(title: "Identical revisions", detail: "Base and head stores must differ.", statusCode: 400);
                }

                try
                {
                    var inventory = await LoadInventoryCachedAsync(@base, head);
                    return Results.Json(BuildReviewFiles(@base, head, inventory));
                }
                catch (Exception ex)
                {
                    return Results.Problem(title: "Review file list failed", detail: ex.Message, statusCode: 400);
                }
            }
        );
    }

    private static ReviewFilesResponseDto BuildReviewFiles(string baseStore, string headStore, ReviewInventory inventory) =>
        new(baseStore, headStore, inventory.Base.Commit, inventory.Head.Commit, inventory.Files);

    private const int MaxReviewSourceBytes = 4 * 1024 * 1024;

    private static async Task<ReviewSourceBlob> ReadReviewBlobAsync(string repo, string commit, string path)
    {
        // Both parts originate in the validated store/inventory, never a caller-supplied ref or disk path.
        var objectName = $"{commit}:{path}";
        try
        {
            var size = long.Parse(
                (await RunGitAsync(repo, "cat-file", "-s", objectName)).Trim(),
                System.Globalization.CultureInfo.InvariantCulture
            );
            if (size > MaxReviewSourceBytes)
            {
                return new(
                    "too-large",
                    null,
                    size,
                    "Full-file preview is limited to 4 MiB. The exact file is not truncated; use the diff or open this revision locally."
                );
            }

            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = repo,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in new[] { "cat-file", "blob", objectName })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Git could not be started.");
            var errorTask = process.StandardError.ReadToEndAsync();
            var bytes = new byte[checked((int)size)];
            await process.StandardOutput.BaseStream.ReadExactlyAsync(bytes);
            await process.WaitForExitAsync();
            var error = await errorTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException(error.Trim());
            if (bytes.Contains((byte)0))
                return new("binary", null, size, "Binary files cannot be displayed as source.");
            var lines = bytes.Count(value => value == (byte)'\n') + (bytes.Length > 0 && bytes[^1] != (byte)'\n' ? 1 : 0);
            if (lines > 20_000)
            {
                return new(
                    "too-large",
                    null,
                    size,
                    "Full-file preview is limited to 20,000 lines. The exact file is not truncated; use the diff or open this revision locally."
                );
            }
            try
            {
                // Strict UTF-8 avoids silently replacing undecodable bytes and calling that exact source.
                return new("available", new UTF8Encoding(false, true).GetString(bytes), size, null);
            }
            catch (DecoderFallbackException)
            {
                return new("binary", null, size, "This file is binary or uses an unsupported text encoding (UTF-8 is required).");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return new("unavailable", null, null, $"Exact Git source is unavailable: {ex.Message}");
        }
    }

    private sealed record ReviewSourceBlob(string State, string? Content, long? ByteLength, string? Reason);

    private static async Task<FileDiffResponseDto> BuildAsync(
        string workingDirectory,
        string baseStore,
        string headStore,
        string file,
        ReviewInventory inventory,
        bool ignoreWhitespace = false
    )
    {
        var changed = ResolveChangedFile(inventory.Files, file);
        var baseRevisionTask = LoadRevisionAsync(workingDirectory, baseStore, inventory.Base.Commit, changed.OldPath, changed.OldFile);
        var headRevisionTask = LoadRevisionAsync(workingDirectory, headStore, inventory.Head.Commit, changed.NewPath, changed.NewFile);
        await Task.WhenAll(baseRevisionTask, headRevisionTask);
        var baseRevision = await baseRevisionTask;
        var headRevision = await headRevisionTask;

        // The browser renders patch hunks, not whole files. Avoid shipping/tokenizing both complete revisions:
        // on generated or legacy files that turned a tiny review into work proportional to total file size.
        var arguments = new List<string>
        {
            "diff",
            "--no-color",
            "--no-ext-diff",
            "--find-renames",
            "--find-copies",
            "--find-copies-harder",
            $"--unified={ContextLines}",
        };
        if (ignoreWhitespace)
        {
            arguments.Add("--ignore-all-space");
        }

        if (changed.Status is "R" or "C")
        {
            // Both paths are still supplied below so Git can discover the relationship, while the filter
            // prevents a modified copy source from becoming a second, unrelated patch in this one-file DTO.
            arguments.Add($"--diff-filter={changed.Status}");
        }

        arguments.Add(inventory.Base.Commit);
        arguments.Add(inventory.Head.Commit);
        arguments.Add("--");
        if (changed.OldPath is not null)
        {
            arguments.Add(changed.OldPath);
        }

        if (changed.NewPath is not null && !PathComparer.Equals(changed.NewPath, changed.OldPath))
        {
            arguments.Add(changed.NewPath);
        }

        var patch = await RunGitAsync(inventory.Repo, arguments.ToArray());
        var language = Path.GetExtension(changed.Path).Equals(".cs", StringComparison.OrdinalIgnoreCase) ? "csharp" : "text";

        return new FileDiffResponseDto(
            changed.Path,
            changed.Path,
            changed.Status,
            changed.OldPath,
            changed.NewPath,
            language,
            patch,
            ContextLines,
            baseRevision,
            headRevision
        );
    }

    private static async Task<FileDiffRevisionDto> LoadRevisionAsync(
        string workingDirectory,
        string store,
        string commit,
        string? path,
        string? file
    )
    {
        if (path is null)
        {
            return new FileDiffRevisionDto(store, commit, "not-present", Path: null, File: null, Content: "", Effects: null);
        }

        if (file is null)
        {
            return new FileDiffRevisionDto(store, commit, "not-indexed", path, File: null, Content: "", Effects: null);
        }

        // BuildResidentAsync performs the indexed-file membership check and returns the exact same semantic
        // projection as File view and `rig annotate`; the review surface must not grow a second effect model.
        var artifact = await FileEffectsQueryService.BuildResidentAsync(workingDirectory, file, store);
        return new FileDiffRevisionDto(
            store,
            commit,
            "available",
            path,
            file,
            Content: "",
            Effects: FileEffectsEndpoint.ToResponse(artifact)
        );
    }

    private static async Task<ReviewInventory> LoadReviewInventoryAsync(string workingDirectory, string baseStore, string headStore)
    {
        var baseInventoryTask = LoadInventoryAsync(workingDirectory, baseStore);
        var headInventoryTask = LoadInventoryAsync(workingDirectory, headStore);
        await Task.WhenAll(baseInventoryTask, headInventoryTask);
        var baseInventory = await baseInventoryTask;
        var headInventory = await headInventoryTask;

        var representative =
            headInventory.Files.Concat(baseInventory.Files).FirstOrDefault(Path.IsPathRooted)
            ?? headInventory.SolutionPath
            ?? baseInventory.SolutionPath
            ?? throw new InvalidOperationException("Neither store identifies a source path from which to locate the Git work tree.");
        var repo = await FindRepoAsync(representative);
        var baseFiles = RelativeFileMap(repo, baseInventory.Files);
        var headFiles = RelativeFileMap(repo, headInventory.Files);
        // The dirty sets are subsets of the indexed files, so the same repo-relative conversion serves both.
        var baseDirty = RelativeFileMap(repo, baseInventory.DirtyFiles);
        var headDirty = RelativeFileMap(repo, headInventory.DirtyFiles);
        // Changed-line counts need the SAME rename/copy detection and the same commit pair, or the two scans
        // disagree about which rows exist and the join lands counts on the wrong file. Run them together: the
        // --find-copies-harder scan is the expensive part and this pair is loaded once per review session.
        var nameStatusTask = RunGitAsync(
            repo,
            "diff",
            "--name-status",
            "-z",
            "--find-renames",
            "--find-copies",
            "--find-copies-harder",
            baseInventory.Commit,
            headInventory.Commit,
            "--"
        );
        var numstatTask = RunGitAsync(
            repo,
            "diff",
            "--numstat",
            "-z",
            "--find-renames",
            "--find-copies",
            "--find-copies-harder",
            baseInventory.Commit,
            headInventory.Commit,
            "--"
        );
        await Task.WhenAll(nameStatusTask, numstatTask);
        var nameStatus = await nameStatusTask;
        var counts = ParseNumstat(await numstatTask);
        var files = ParseNameStatus(nameStatus)
            .Select(change => ToReviewFile(change, baseFiles, headFiles, baseDirty, headDirty, counts))
            .OrderBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new ReviewInventory(repo, baseInventory, headInventory, files);
    }

    private static ReviewFileDto ResolveChangedFile(IReadOnlyList<ReviewFileDto> files, string requested)
    {
        var normalized = requested.Replace('\\', '/');
        var matches = files
            .Where(candidate =>
                PathComparer.Equals(candidate.Path, normalized)
                || PathComparer.Equals(candidate.OldPath, normalized)
                || PathComparer.Equals(candidate.NewPath, normalized)
                || PathComparer.Equals(candidate.OldFile, requested)
                || PathComparer.Equals(candidate.NewFile, requested)
            )
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"File '{requested}' is not in the Git diff for the selected revisions."),
            _ => throw new InvalidOperationException(
                $"File identity '{requested}' is ambiguous in the Git diff for the selected revisions."
            ),
        };
    }

    private static async Task<StoreInventory> LoadInventoryAsync(string workingDirectory, string store)
    {
        await using var context = await OpenReadContextGatedAsync(new WorkspaceLocation(workingDirectory, store));
        var run = (await Reads.ListRunsAsync(context)).FirstOrDefault(candidate => candidate.SourceCommit is not null);
        if (run?.SourceCommit is not { } commit)
        {
            throw new InvalidOperationException($"Store '{store}' has no source commit; it cannot participate in Git review.");
        }

        if (!IsHexSha(commit))
        {
            throw new InvalidOperationException($"Store '{store}' has an invalid source commit.");
        }

        // Dirtiness is read per FILE, from the bit the indexer recorded — never from `git status` here, which
        // answers "dirty now" and would clear a file that was dirty at index time but has been committed
        // since. The run-level SourceDirty flag is deliberately not consulted: it cannot say WHICH file is
        // off-commit, and the git diff itself never depended on the store, so it is no grounds to refuse.
        var rows = await context
            .SourceFiles.AsNoTracking()
            .Where(file => file.Status != "skipped")
            .Select(file => new { file.FilePath, file.Dirty })
            .Distinct()
            .ToArrayAsync();
        var files = rows.Select(row => row.FilePath).Distinct().ToArray();
        // One run indexing a path from uncommitted source taints it; another run's clean row cannot clear it.
        var dirtyFiles = rows.Where(row => row.Dirty).Select(row => row.FilePath).Distinct().ToArray();
        return new StoreInventory(commit, run.SolutionPath, files, dirtyFiles);
    }

    private static Dictionary<string, string> RelativeFileMap(string repo, IReadOnlyList<string> files)
    {
        var result = new Dictionary<string, string>(PathComparer);
        foreach (var file in files)
        {
            // Source-generator output is recorded as a project-relative pseudo-path with no location on disk, so it
            // has no Git revision to review. Skip it rather than resolving it against the serve process directory.
            if (!Path.IsPathRooted(file))
            {
                continue;
            }

            var relative = RepoRelativePath(repo, file);
            result.TryAdd(relative, file);
        }

        return result;
    }

    private static IReadOnlyList<NameStatusChange> ParseNameStatus(string value)
    {
        var tokens = value.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<NameStatusChange>();
        for (var index = 0; index < tokens.Length; )
        {
            var statusToken = tokens[index++];
            if (statusToken.Length == 0)
            {
                continue;
            }

            var status = statusToken[0].ToString();
            if (status is "R" or "C")
            {
                if (index + 1 >= tokens.Length)
                {
                    throw new InvalidOperationException("Git returned an incomplete rename/copy record.");
                }

                result.Add(new NameStatusChange(status, tokens[index++], tokens[index++]));
            }
            else
            {
                if (index >= tokens.Length)
                {
                    throw new InvalidOperationException("Git returned an incomplete changed-file record.");
                }

                var path = tokens[index++];
                result.Add(
                    status == "A" ? new NameStatusChange(status, OldPath: null, NewPath: path)
                    : status == "D" ? new NameStatusChange(status, OldPath: path, NewPath: null)
                    : new NameStatusChange(status, path, path)
                );
            }
        }

        return result;
    }

    // `--numstat -z` does NOT frame like `--name-status -z`: a normal record is ONE NUL-terminated token of
    // "<added>\t<removed>\t<path>", while a rename/copy ends that token right after the second tab (an empty
    // path field) and follows it with the old path and the new path as two further NUL tokens. Keyed on the
    // same path ToReviewFile uses for its Path — the new path where there is one — so the counts land on the
    // row Git's rename detection produced.
    internal static IReadOnlyDictionary<string, NumstatCounts> ParseNumstat(string value)
    {
        var tokens = value.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var result = new Dictionary<string, NumstatCounts>(PathComparer);
        for (var index = 0; index < tokens.Length; )
        {
            // A path may itself contain a tab, so only the two count fields are split off.
            var fields = tokens[index++].Split('\t', 3);
            if (fields.Length < 3)
            {
                throw new InvalidOperationException("Git returned an incomplete changed-line record.");
            }

            var path = fields[2];
            if (path.Length == 0)
            {
                if (index + 1 >= tokens.Length)
                {
                    throw new InvalidOperationException("Git returned an incomplete rename/copy changed-line record.");
                }

                index++;
                path = tokens[index++];
            }

            result[path] = new NumstatCounts(ParseCount(fields[0]), ParseCount(fields[1]));
        }

        return result;
    }

    // A binary file reports "-" for both counts: the number of changed lines is unknown, not zero.
    private static int? ParseCount(string value) => int.TryParse(value, out var parsed) ? parsed : null;

    private static ReviewFileDto ToReviewFile(
        NameStatusChange change,
        IReadOnlyDictionary<string, string> baseFiles,
        IReadOnlyDictionary<string, string> headFiles,
        IReadOnlyDictionary<string, string> baseDirty,
        IReadOnlyDictionary<string, string> headDirty,
        IReadOnlyDictionary<string, NumstatCounts> counts
    )
    {
        var oldFile = change.OldPath is not null && baseFiles.TryGetValue(change.OldPath, out var indexedOld) ? indexedOld : null;
        var newFile = change.NewPath is not null && headFiles.TryGetValue(change.NewPath, out var indexedNew) ? indexedNew : null;
        var indexedBothSides = oldFile is not null && newFile is not null;
        // Either side indexed from uncommitted source makes the annotations facts about something other than
        // the revision under review, so the caveat rides the file it belongs to and no other.
        var dirty =
            (change.OldPath is not null && baseDirty.ContainsKey(change.OldPath))
            || (change.NewPath is not null && headDirty.ContainsKey(change.NewPath));
        var semanticReady = indexedBothSides && !dirty;
        var reason =
            !indexedBothSides ? "Semantic annotations are unavailable for one or both revisions."
            : dirty ? "Indexed from uncommitted source, so annotations are not at this revision."
            : null;
        var path = change.NewPath ?? change.OldPath ?? "";
        var counted = counts.TryGetValue(path, out var found) ? found : null;
        return new ReviewFileDto(
            change.Status,
            path,
            change.OldPath,
            change.NewPath,
            oldFile,
            newFile,
            Reviewable: true,
            semanticReady,
            reason,
            counted?.Additions,
            counted?.Deletions
        );
    }

    private static async Task<string> FindRepoAsync(string file)
    {
        var directory =
            NearestExistingDirectory(file) ?? throw new InvalidOperationException($"No existing directory contains indexed file '{file}'.");
        var root = (await RunGitAsync(directory, "rev-parse", "--show-toplevel")).Trim();
        return Path.GetFullPath(root);
    }

    private static string RepoRelativePath(string root, string file)
    {
        var relative = Path.GetRelativePath(NormalizeMacPath(root), NormalizeMacPath(file)).Replace('\\', '/');
        if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException($"Indexed file '{file}' is outside Git work tree '{root}'.");
        }

        return relative;
    }

    private static string? NearestExistingDirectory(string file)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(file));
        while (directory is not null && !Directory.Exists(directory))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return directory;
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

    private static bool IsHexSha(string value) => value.Length is >= 7 and <= 64 && value.All(character => char.IsAsciiHexDigit(character));

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static async Task<string> RunGitAsync(string workingDirectory, params string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            };
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start) ?? throw new InvalidOperationException("Git could not be started.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"git {arguments[0]} failed: {stderr.Trim()}");
            }

            return stdout;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException)
        {
            throw new InvalidOperationException("Git is required to render an exact file diff.", exception);
        }
    }

    private sealed record StoreInventory(
        string Commit,
        string? SolutionPath,
        IReadOnlyList<string> Files,
        IReadOnlyList<string> DirtyFiles
    );

    private sealed record ReviewInventory(string Repo, StoreInventory Base, StoreInventory Head, IReadOnlyList<ReviewFileDto> Files);

    private sealed record NameStatusChange(string Status, string? OldPath, string? NewPath);

    internal sealed record NumstatCounts(int? Additions, int? Deletions);
}
