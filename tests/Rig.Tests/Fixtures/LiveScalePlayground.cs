using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;

namespace Rig.Tests.Fixtures;

public sealed class LiveScalePlayground : IDisposable
{
    private LiveScalePlayground(
        string rootDirectory,
        string workingDirectory,
        string solutionPath,
        string manifestPath,
        string editTracePath,
        string corpusHash,
        string editTraceHash,
        string generatorOutput
    )
    {
        RootDirectory = rootDirectory;
        WorkingDirectory = workingDirectory;
        SolutionPath = solutionPath;
        ManifestPath = manifestPath;
        EditTracePath = editTracePath;
        CorpusHash = corpusHash;
        EditTraceHash = editTraceHash;
        GeneratorOutput = generatorOutput;
    }

    public string RootDirectory { get; }
    public string WorkingDirectory { get; }
    public string SolutionPath { get; }
    public string ManifestPath { get; }
    public string EditTracePath { get; }
    public string CorpusHash { get; }
    public string EditTraceHash { get; }
    public string GeneratorOutput { get; }

    public static async Task<LiveScalePlayground> GenerateAsync(string preset = "smoke", ulong seed = 20260822)
    {
        var repositoryRoot = RepositoryRoot();
        var rawRoot = Directory.CreateTempSubdirectory("rig-live-scale-").FullName;
        var root = await CanonicalPathAsync(rawRoot);
        var output = Path.Combine(root, preset);

        var process = await RunGeneratorAsync(preset, output, seed);
        process.ExitCode.ShouldBe(0, process.StandardOutput + Environment.NewLine + process.StandardError);

        var manifestPath = Path.Combine(output, "corpus-manifest.json");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        var rootElement = manifest.RootElement;
        return new LiveScalePlayground(
            root,
            output,
            Path.Combine(output, "LiveScale.slnx"),
            manifestPath,
            Path.Combine(output, "edit-trace.json"),
            rootElement.GetProperty("corpusSha256").GetString()!,
            rootElement.GetProperty("editTraceSha256").GetString()!,
            process.StandardOutput.TrimEnd()
        );
    }

    public static Task<GeneratorProcessResult> RunGeneratorAsync(string preset, string output, ulong seed, bool includeGenerated = false)
    {
        var repositoryRoot = RepositoryRoot();
        var generatorProject = Path.Combine(repositoryRoot, "scripts", "LiveScaleGenerator", "LiveScaleGenerator.csproj");
        var arguments = new List<string>
        {
            "run",
            "--project",
            generatorProject,
            "--no-build",
            "--",
            "--preset",
            preset,
            "--output",
            output,
            "--seed",
            seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (includeGenerated)
        {
            arguments.Add("--include-generated");
        }
        return RunAsync("dotnet", arguments.ToArray(), repositoryRoot);
    }

    public Task RestoreAsync() => RunCheckedAsync("dotnet", ["restore", SolutionPath], WorkingDirectory);

    public IReadOnlyDictionary<string, byte[]> FileInventory() =>
        Directory
            .EnumerateFiles(WorkingDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !Relative(path).Split('/').Any(segment => segment is "bin" or "obj" or ".rig"))
            .OrderBy(Relative, StringComparer.Ordinal)
            .ToDictionary(Relative, File.ReadAllBytes, StringComparer.Ordinal);

    public string RecomputeCorpusHash()
    {
        var manifestNode = JsonNode.Parse(File.ReadAllText(ManifestPath))!.AsObject();
        manifestNode.Remove("corpusSha256");
        var provisionalManifest = manifestNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";

        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (relative, bytes) in FileInventory().OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var content = relative == "corpus-manifest.json" ? Encoding.UTF8.GetBytes(provisionalManifest) : bytes;
            var contentHash = Convert.ToHexStringLower(SHA256.HashData(content));
            aggregate.AppendData(Encoding.UTF8.GetBytes(relative + "\0" + contentHash + "\n"));
        }
        return Convert.ToHexStringLower(aggregate.GetHashAndReset());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Test cleanup is best effort; a failed test should retain its corpus for diagnosis.
        }
        catch (UnauthorizedAccessException)
        {
            // Test cleanup is best effort; a failed test should retain its corpus for diagnosis.
        }
    }

    private string Relative(string path) => Path.GetRelativePath(WorkingDirectory, path).Replace('\\', '/');

    private static async Task<string> CanonicalPathAsync(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.GetFullPath(path);
        }

        var result = await RunAsync("realpath", [path], Directory.GetCurrentDirectory());
        result.ExitCode.ShouldBe(0, result.StandardError);
        return result.StandardOutput.Trim();
    }

    private static async Task RunCheckedAsync(string executable, string[] arguments, string workingDirectory)
    {
        var result = await RunAsync(executable, arguments, workingDirectory);
        result.ExitCode.ShouldBe(0, result.StandardOutput + Environment.NewLine + result.StandardError);
    }

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static async Task<GeneratorProcessResult> RunAsync(string executable, string[] arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new GeneratorProcessResult(process.ExitCode, await outputTask, await errorTask);
    }

    public sealed record GeneratorProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
