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
        var nameStatus = await RunGitAsync(
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
        var files = ParseNameStatus(nameStatus)
            .Select(change => ToReviewFile(change, baseFiles, headFiles))
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

        if (run.SourceDirty)
        {
            throw new InvalidOperationException(
                $"Store '{store}' was indexed from a dirty tree; its changed-file list is not reproducible."
            );
        }

        if (!IsHexSha(commit))
        {
            throw new InvalidOperationException($"Store '{store}' has an invalid source commit.");
        }

        var files = await context
            .SourceFiles.AsNoTracking()
            .Where(file => file.Status != "skipped")
            .Select(file => file.FilePath)
            .Distinct()
            .ToArrayAsync();
        return new StoreInventory(commit, run.SolutionPath, files);
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

    private static ReviewFileDto ToReviewFile(
        NameStatusChange change,
        IReadOnlyDictionary<string, string> baseFiles,
        IReadOnlyDictionary<string, string> headFiles
    )
    {
        var oldFile = change.OldPath is not null && baseFiles.TryGetValue(change.OldPath, out var indexedOld) ? indexedOld : null;
        var newFile = change.NewPath is not null && headFiles.TryGetValue(change.NewPath, out var indexedNew) ? indexedNew : null;
        var semanticReady = oldFile is not null && newFile is not null;
        var reason = semanticReady ? null : "Semantic annotations are unavailable for one or both revisions.";
        return new ReviewFileDto(
            change.Status,
            change.NewPath ?? change.OldPath ?? "",
            change.OldPath,
            change.NewPath,
            oldFile,
            newFile,
            Reviewable: true,
            semanticReady,
            reason
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

    private sealed record StoreInventory(string Commit, string? SolutionPath, IReadOnlyList<string> Files);

    private sealed record ReviewInventory(string Repo, StoreInventory Base, StoreInventory Head, IReadOnlyList<ReviewFileDto> Files);

    private sealed record NameStatusChange(string Status, string? OldPath, string? NewPath);
}
