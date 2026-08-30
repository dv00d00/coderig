using Rig.Domain.Data;

namespace Rig.Domain.Functions;

public sealed record FileEffectSelector
{
    public FileEffectSelector(string family, IReadOnlyList<EffectPredicate> predicates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentNullException.ThrowIfNull(predicates);
        if (predicates.Count == 0)
        {
            throw new ArgumentException("At least one effect predicate is required.", nameof(predicates));
        }

        if (predicates.Any(predicate => predicate is null || string.IsNullOrWhiteSpace(predicate.Provider)))
        {
            throw new ArgumentException("Every effect predicate must name a provider.", nameof(predicates));
        }

        Family = family;
        Predicates = Array.AsReadOnly(predicates.ToArray());
    }

    public string Family { get; }

    public IReadOnlyList<EffectPredicate> Predicates { get; }
}

public sealed record FileEffectAggregate(string Family, int NearestDepth);

public sealed record FileEffectMethod(string SymbolId, IReadOnlyList<FileEffectAggregate> Effects);

public sealed record FileEffectReadModel(string FilePath, string EffectSelector, IReadOnlyList<FileEffectMethod> Methods);

/// <summary>Projects one effect family's multi-source reverse closure into immutable, Rider-ready per-file read models.</summary>
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
        IEnumerable<DerivedEffect> effects,
        FileEffectSelector selector,
        IEnumerable<string>? indexedFilePaths = null
    )
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(selector);

        var symbolRows = symbols.ToArray();
        var methodsByFile = SymbolFactProjections
            .SelectCanonicalMethodFacts(symbolRows)
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol.FilePath))
            .GroupBy(symbol => symbol.FilePath, FilePathComparer)
            .ToDictionary(group => group.Key, group => group.AsEnumerable(), FilePathComparer);
        var effectOwners = effects
            .Where(effect => selector.Predicates.Any(predicate => Matches(effect, predicate)))
            .Select(effect => effect.EnclosingSymbolId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        // One traversal for the UNION of every exact predicate in the named family. Projecting a file below
        // is therefore only a dictionary join, never a forward graph walk per method.
        var reached = FactPathFinder.ReachedByAny(
            graph,
            effectOwners,
            maxDepth: int.MaxValue,
            maxNodes: int.MaxValue,
            narrowDispatch: true,
            mode: FactPathFinder.TraversalMode.SyncCut
        );

        var knownFiles = (indexedFilePaths ?? symbolRows.Select(symbol => symbol.FilePath))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(FilePathComparer)
            .OrderBy(path => path, FilePathComparer);
        var files = new Dictionary<string, FileEffectReadModel>(FilePathComparer);
        foreach (var filePath in knownFiles)
        {
            var fileMethods = methodsByFile.GetValueOrDefault(filePath) ?? [];
            var methods = Array.AsReadOnly(
                fileMethods
                    .Where(symbol => reached.ContainsKey(symbol.SymbolId))
                    .OrderBy(symbol => symbol.SymbolId, StringComparer.Ordinal)
                    .Select(symbol => new FileEffectMethod(
                        symbol.SymbolId,
                        Array.AsReadOnly([new FileEffectAggregate(selector.Family, reached[symbol.SymbolId])])
                    ))
                    .ToArray()
            );
            files[filePath] = new FileEffectReadModel(filePath, selector.Family, methods);
        }

        return new FileEffectReadModelIndex(files);
    }

    public FileEffectReadModel? Find(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return _files.GetValueOrDefault(filePath);
    }

    private static bool Matches(DerivedEffect effect, EffectPredicate predicate) =>
        string.Equals(effect.Provider, predicate.Provider, StringComparison.Ordinal)
        && (predicate.Operation is null || string.Equals(effect.Operation, predicate.Operation, StringComparison.Ordinal));
}
