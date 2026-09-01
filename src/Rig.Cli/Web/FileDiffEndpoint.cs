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
        app.MapGet(
            "/api/file-diff",
            async (string? @base, string? head, string? file, bool? ignoreWhitespace) =>
            {
                if (string.IsNullOrWhiteSpace(@base) || string.IsNullOrWhiteSpace(head) || string.IsNullOrWhiteSpace(file))
                {
                    return Results.Problem(
                        title: "Missing base/head/file",
                        detail: "Provide ?base=<store>&head=<store>&file=<indexed path>.",
                        statusCode: 400
                    );
                }

                if (string.Equals(@base, head, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Problem(title: "Identical revisions", detail: "Base and head stores must differ.", statusCode: 400);
                }

                try
                {
                    return Results.Json(await BuildAsync(workingDirectory, @base, head, file, ignoreWhitespace == true));
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
                    return Results.Json(await BuildReviewFilesAsync(workingDirectory, @base, head));
                }
                catch (Exception ex)
                {
                    return Results.Problem(title: "Review file list failed", detail: ex.Message, statusCode: 400);
                }
            }
        );
    }

    internal static async Task<ReviewFilesResponseDto> BuildReviewFilesAsync(string workingDirectory, string baseStore, string headStore)
    {
        var baseInventoryTask = LoadInventoryAsync(workingDirectory, baseStore);
        var headInventoryTask = LoadInventoryAsync(workingDirectory, headStore);
        await Task.WhenAll(baseInventoryTask, headInventoryTask);
        var baseInventory = await baseInventoryTask;
        var headInventory = await headInventoryTask;

        var representative = headInventory.Files.Concat(baseInventory.Files).FirstOrDefault();
        if (representative is null)
        {
            throw new InvalidOperationException("Neither store contains an indexed source file from which to locate the Git work tree.");
        }

        var repo = await FindRepoAsync(representative);
        var baseFiles = RelativeFileMap(repo, baseInventory.Files);
        var headFiles = RelativeFileMap(repo, headInventory.Files);
        var nameStatus = await RunGitAsync(
            repo,
            "diff",
            "--name-status",
            "-z",
            "--find-renames",
            baseInventory.Commit,
            headInventory.Commit,
            "--"
        );
        var files = ParseNameStatus(nameStatus)
            .Select(change => ToReviewFile(change, baseFiles, headFiles))
            .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ReviewFilesResponseDto(baseStore, headStore, baseInventory.Commit, headInventory.Commit, files);
    }

    internal static async Task<FileDiffResponseDto> BuildAsync(
        string workingDirectory,
        string baseStore,
        string headStore,
        string file,
        bool ignoreWhitespace = false
    )
    {
        var baseRevisionTask = LoadRevisionAsync(workingDirectory, baseStore, file);
        var headRevisionTask = LoadRevisionAsync(workingDirectory, headStore, file);
        await Task.WhenAll(baseRevisionTask, headRevisionTask);
        var baseRevision = await baseRevisionTask;
        var headRevision = await headRevisionTask;

        var repo = await FindRepoAsync(file);
        var relativePath = RepoRelativePath(repo, file);
        // The browser renders patch hunks, not whole files. Avoid shipping/tokenizing both complete revisions:
        // on generated or legacy files that turned a tiny review into work proportional to total file size.
        var arguments = new List<string> { "diff", "--no-color", "--no-ext-diff", "--find-renames", $"--unified={ContextLines}" };
        if (ignoreWhitespace)
        {
            arguments.Add("--ignore-all-space");
        }

        arguments.Add(baseRevision.Commit);
        arguments.Add(headRevision.Commit);
        arguments.Add("--");
        arguments.Add(relativePath);
        var patch = await RunGitAsync(repo, arguments.ToArray());

        return new FileDiffResponseDto(file, relativePath, patch, ContextLines, baseRevision, headRevision);
    }

    private static async Task<FileDiffRevisionDto> LoadRevisionAsync(string workingDirectory, string store, string file)
    {
        // BuildResidentAsync performs the indexed-file membership check and returns the exact same semantic
        // projection as File view and `rig annotate`; the review surface must not grow a second effect model.
        var artifact = await FileEffectsQueryService.BuildResidentAsync(workingDirectory, file, store);
        await using var context = await OpenReadContextGatedAsync(new WorkspaceLocation(workingDirectory, store));
        var run = (await Reads.ListRunsAsync(context)).FirstOrDefault(candidate => candidate.SourceCommit is not null);
        if (run?.SourceCommit is not { } commit)
        {
            throw new InvalidOperationException($"Store '{store}' has no source commit; its line numbers cannot be placed on a Git diff.");
        }

        if (run.SourceDirty)
        {
            throw new InvalidOperationException(
                $"Store '{store}' was indexed from a dirty tree; Git cannot reproduce the source text that owns its semantic line numbers."
            );
        }

        if (!IsHexSha(commit))
        {
            throw new InvalidOperationException($"Store '{store}' has an invalid source commit.");
        }

        return new FileDiffRevisionDto(store, commit, Content: "", Effects: FileEffectsEndpoint.ToResponse(artifact));
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
        return new StoreInventory(commit, files);
    }

    private static Dictionary<string, string> RelativeFileMap(string repo, IReadOnlyList<string> files)
    {
        var result = new Dictionary<string, string>(PathComparer);
        foreach (var file in files)
        {
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
        var stablePath = change.Status == "M" && change.OldPath is not null && change.NewPath is not null;
        var reviewable = stablePath && oldFile is not null && newFile is not null && PathComparer.Equals(oldFile, newFile);
        var reason =
            reviewable ? null
            : change.Status is "A" or "D" or "R" or "C" ? "Added, deleted, renamed, and copied files need two-path review support."
            : "The file is not indexed at the same path in both stores.";
        return new ReviewFileDto(
            change.Status,
            change.NewPath ?? change.OldPath ?? "",
            change.OldPath,
            change.NewPath,
            oldFile,
            newFile,
            reviewable,
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

    private sealed record StoreInventory(string Commit, IReadOnlyList<string> Files);

    private sealed record NameStatusChange(string Status, string? OldPath, string? NewPath);
}
