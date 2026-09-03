using Buildalyzer;

namespace Rig.Analysis.Inventory;

// The slice of a project's design-time build that BuildWorkspaceFromResults actually consumes — the
// resolved references, source files, and MSBuild properties needed to construct a Roslyn project. It is
// deliberately a plain, serializable record (no Buildalyzer types) so the same workspace assembly can be
// driven from EITHER a fresh design-time build (FromAnalyzerResult) OR a cached/replayed result. That
// decoupling is the prerequisite for the design-time-build cache (skip the ~33-53% build phase when a
// project's inputs are unchanged); on its own this type changes no behaviour.
public sealed record ProjectBuildInfo(
    string? ProjectFilePath,
    IReadOnlyList<string> References,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<string> AnalyzerReferences,
    IReadOnlyList<string> PreprocessorSymbols,
    IReadOnlyDictionary<string, string> Properties,
    // Compiler inputs source generators consume. Nullable only for pre-field cache sidecars; a fresh
    // Buildalyzer result always writes arrays (possibly empty), and the cache rejects a legacy null.
    IReadOnlyList<string>? AdditionalFiles = null,
    IReadOnlyList<string>? AnalyzerConfigFiles = null
)
{
    // Projects the consumed fields out of Buildalyzer's IAnalyzerResult, normalising nullable
    // collections to empty so downstream code never null-checks.
    public static ProjectBuildInfo FromAnalyzerResult(IAnalyzerResult result) =>
        new(
            ProjectFilePath: result.ProjectFilePath,
            References: result.References?.ToArray() ?? [],
            ProjectReferences: result.ProjectReferences?.ToArray() ?? [],
            SourceFiles: result.SourceFiles?.ToArray() ?? [],
            AnalyzerReferences: result.AnalyzerReferences?.ToArray() ?? [],
            PreprocessorSymbols: result.PreprocessorSymbols?.ToArray() ?? [],
            Properties: result.Properties ?? new Dictionary<string, string>(StringComparer.Ordinal),
            AdditionalFiles: NormalizePaths(result.AdditionalFiles, result.ProjectFilePath),
            AnalyzerConfigFiles: CompilerOptionPaths(result.CompilerArguments, "analyzerconfig", result.ProjectFilePath)
        );

    private static string[] CompilerOptionPaths(IReadOnlyList<string>? arguments, string option, string? projectFilePath)
    {
        if (arguments is null)
        {
            return [];
        }

        var prefixes = new[] { $"/{option}:", $"-{option}:" };
        var paths = arguments
            .Select(argument => argument.Trim())
            .Where(argument => prefixes.Any(prefix => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Select(argument =>
            {
                var colon = argument.IndexOf(':');
                return argument[(colon + 1)..].Trim().Trim('"');
            })
            .Where(path => path.Length > 0)
            .ToArray();
        return NormalizePaths(paths, projectFilePath);
    }

    private static string[] NormalizePaths(IEnumerable<string>? paths, string? projectFilePath)
    {
        var baseDirectory = string.IsNullOrEmpty(projectFilePath) ? null : Path.GetDirectoryName(Path.GetFullPath(projectFilePath));
        return (paths ?? [])
            .Select(path =>
                Path.IsPathFullyQualified(path) ? Path.GetFullPath(path)
                : baseDirectory is not null ? Path.GetFullPath(path, baseDirectory)
                : path
            )
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
