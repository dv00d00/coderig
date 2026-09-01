using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
            async (string? @base, string? head, string? file) =>
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
                    return Results.Json(await BuildAsync(workingDirectory, @base, head, file));
                }
                catch (Exception ex)
                {
                    return Results.Problem(title: "File diff failed", detail: ex.Message, statusCode: 400);
                }
            }
        );
    }

    internal static async Task<FileDiffResponseDto> BuildAsync(string workingDirectory, string baseStore, string headStore, string file)
    {
        var baseRevisionTask = LoadRevisionAsync(workingDirectory, baseStore, file);
        var headRevisionTask = LoadRevisionAsync(workingDirectory, headStore, file);
        await Task.WhenAll(baseRevisionTask, headRevisionTask);
        var baseRevision = await baseRevisionTask;
        var headRevision = await headRevisionTask;

        var repo = await FindRepoAsync(file);
        var relativePath = RepoRelativePath(repo, file);
        baseRevision = baseRevision with { Content = await RunGitAsync(repo, "show", $"{baseRevision.Commit}:{relativePath}") };
        headRevision = headRevision with { Content = await RunGitAsync(repo, "show", $"{headRevision.Commit}:{relativePath}") };
        var patch = await RunGitAsync(
            repo,
            "diff",
            "--no-color",
            "--no-ext-diff",
            "--find-renames",
            $"--unified={ContextLines}",
            baseRevision.Commit,
            headRevision.Commit,
            "--",
            relativePath
        );

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
}
