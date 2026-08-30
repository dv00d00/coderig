using Rig.Domain.Data;

namespace Rig.Domain.Functions;

public sealed record FileEffectMethod(string SymbolId, string Name, int Line, int EndLine, int NearestDepth);

public sealed record FileEffectReadModel(string FilePath, string EffectSelector, IReadOnlyList<FileEffectMethod> Methods);

/// <summary>
/// Throwaway Rider-host spike: projects one multi-source reverse effect closure into immutable per-file read models.
/// </summary>
public sealed class FileEffectReadModelIndex
{
    private static readonly StringComparer FilePathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly IReadOnlyDictionary<string, FileEffectReadModel> _files;

    private FileEffectReadModelIndex(IReadOnlyDictionary<string, FileEffectReadModel> files)
    {
        _files = files;
    }

    public static FileEffectReadModelIndex Build(
        FactGraphData graph,
        IEnumerable<SymbolFact> symbols,
        IEnumerable<DerivedEffect> selectedEffects,
        string effectSelector
    )
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(selectedEffects);
        ArgumentException.ThrowIfNullOrWhiteSpace(effectSelector);

        var methodsByFile = SymbolFactProjections
            .SelectCanonicalMethodFacts(symbols)
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol.FilePath))
            .GroupBy(symbol => symbol.FilePath, FilePathComparer)
            .ToArray();
        var effectOwners = selectedEffects
            .Select(effect => effect.EnclosingSymbolId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var reached = FactPathFinder.ReachedByAny(
            graph,
            effectOwners,
            maxDepth: int.MaxValue,
            maxNodes: int.MaxValue,
            narrowDispatch: true,
            mode: FactPathFinder.TraversalMode.SyncCut
        );

        var files = new Dictionary<string, FileEffectReadModel>(FilePathComparer);
        foreach (var file in methodsByFile)
        {
            var methods = Array.AsReadOnly(
                file.Where(symbol => reached.ContainsKey(symbol.SymbolId))
                    .OrderBy(symbol => symbol.Line)
                    .ThenBy(symbol => symbol.SymbolId, StringComparer.Ordinal)
                    .Select(symbol => new FileEffectMethod(
                        symbol.SymbolId,
                        symbol.Name,
                        symbol.Line,
                        symbol.EndLine,
                        reached[symbol.SymbolId]
                    ))
                    .ToArray()
            );
            files[file.Key] = new FileEffectReadModel(file.Key, effectSelector, methods);
        }

        return new FileEffectReadModelIndex(files);
    }

    public FileEffectReadModel? Find(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return _files.GetValueOrDefault(filePath);
    }
}
