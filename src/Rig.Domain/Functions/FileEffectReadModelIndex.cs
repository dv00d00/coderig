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

public sealed record FileEffectCallSite(string EnclosingSymbolId, string TargetSymbolId, IReadOnlyList<FileEffectAggregate> Effects);

public sealed record FileEffectReadModel(
    string FilePath,
    string EffectSelector,
    IReadOnlyList<FileEffectMethod> Methods,
    IReadOnlyList<FileEffectCallSite> CallSites
);

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
        var selectedEffects = effects.Where(effect => selector.Predicates.Any(predicate => Matches(effect, predicate))).ToArray();
        var effectOwners = selectedEffects
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
        var invocationEdgesByFile = graph
            .CallEdges.Where(edge =>
                string.Equals(edge.Kind, "invocation", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(edge.FilePath)
            )
            .GroupBy(edge => edge.FilePath, FilePathComparer)
            .ToDictionary(group => group.Key, group => group.ToArray(), FilePathComparer);
        var selectedEffectsByFile = selectedEffects
            .Where(effect => !string.IsNullOrWhiteSpace(effect.FilePath))
            .GroupBy(effect => effect.FilePath, FilePathComparer)
            .ToDictionary(group => group.Key, group => group.ToArray(), FilePathComparer);
        var files = new Dictionary<string, FileEffectReadModel>(FilePathComparer);
        foreach (var filePath in knownFiles)
        {
            var fileMethods = (methodsByFile.GetValueOrDefault(filePath) ?? []).ToArray();
            var fileMethodIds = fileMethods.Select(method => method.SymbolId).ToHashSet(StringComparer.Ordinal);
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
            var callSites = BuildCallSites(
                invocationEdgesByFile.GetValueOrDefault(filePath) ?? [],
                selectedEffectsByFile.GetValueOrDefault(filePath) ?? [],
                reached,
                fileMethodIds,
                selector.Family
            );
            files[filePath] = new FileEffectReadModel(filePath, selector.Family, methods, callSites);
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

    private static IReadOnlyList<FileEffectCallSite> BuildCallSites(
        IReadOnlyList<CallEdge> fileInvocationEdges,
        IReadOnlyList<DerivedEffect> selectedEffects,
        IReadOnlyDictionary<string, int> reached,
        IReadOnlySet<string> fileMethodIds,
        string family
    )
    {
        var invocationEdges = fileInvocationEdges.Where(edge => fileMethodIds.Contains(edge.Caller)).ToArray();

        // A direct derived effect retains its owner + physical source site, but not the matched target.
        // Recover the target only when that site contains exactly one invocation edge. An expression such
        // as `Use(Read(), Other())` shares one line across several calls, so guessing there would turn a
        // semantic editor annotation into a false positive; the method summary remains available instead.
        var directTargets = invocationEdges
            .GroupBy(edge => new SourceSite(edge.Caller, edge.Line))
            .Where(group => group.Select(edge => edge.Callee).Distinct(StringComparer.Ordinal).Take(2).Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().Callee);
        var directSites = selectedEffects
            .Where(effect => effect.EnclosingSymbolId is not null && fileMethodIds.Contains(effect.EnclosingSymbolId))
            .Select(effect => new SourceSite(effect.EnclosingSymbolId!, effect.Line))
            .Where(directTargets.ContainsKey)
            .Select(site => new CallSiteKey(site.EnclosingSymbolId, directTargets[site]));

        // For an indirect call, the question a reader asks of a call site is "does going in here end in the
        // family?" — reverse REACHABILITY of the callee, not whether this particular call shortens the
        // distance. A strict `calleeDepth < callerDepth` test dropped every second effectful call out of one
        // body: the caller already owns the shorter distance through its first effectful callee, so a sibling
        // call whose own distance ties it went unmarked (IndexCommands.MaterializeGraphAsync kept
        // GraphMaterializer.BuildFromGraphAsync at 0 and silently lost
        // EntryPointContext.MaterializeEntryPointSitesAsync at 1). Dispatch remains the graph engine's
        // concern; Rider receives only the static DocIDs it can resolve against the current PSI invocation.
        var indirectSites = invocationEdges
            .Where(edge => reached.ContainsKey(edge.Callee))
            .Select(edge => new CallSiteKey(edge.Caller, edge.Callee));

        return Array.AsReadOnly(
            directSites
                .Concat(indirectSites)
                .Distinct()
                .OrderBy(site => site.EnclosingSymbolId, StringComparer.Ordinal)
                .ThenBy(site => site.TargetSymbolId, StringComparer.Ordinal)
                .Select(site => new FileEffectCallSite(
                    site.EnclosingSymbolId,
                    site.TargetSymbolId,
                    Array.AsReadOnly([new FileEffectAggregate(family, reached.TryGetValue(site.TargetSymbolId, out var depth) ? depth : 0)])
                ))
                .ToArray()
        );
    }

    private readonly record struct SourceSite(string EnclosingSymbolId, int Line);

    private readonly record struct CallSiteKey(string EnclosingSymbolId, string TargetSymbolId);
}
