using Microsoft.EntityFrameworkCore;
using Rig.Analysis.Rules;
using Rig.Cli.Caching;
using Rig.Cli.CommandLine;
using Rig.Cli.Live;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using static Rig.Cli.Graph.TraversalGraphLoader;

namespace Rig.Cli.Services;

// The store-backed counterpart of Rider's resident file-effects query. The cold CLI entry point deliberately
// projects one file; the resident entry point builds one solution-wide index and selects files from it.
internal static class FileEffectsQueryService
{
    internal sealed record MethodLocation(string Id, string Name, string Signature, int Line, int EndLine);

    internal sealed record Artifact(FileEffectReadModel Model, IReadOnlyDictionary<string, MethodLocation> Methods);

    private static readonly StringComparer FilePathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    // The one-shot path: preserve its file-granular symbol query so `annotate --cold` never materializes the
    // solution's complete method universe merely to render one file.
    internal static async Task<Artifact> BuildAsync(string workingDirectory, string filePath, string? storeRef = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var rules = RuleSetLoader.Load(workingDirectory, extraRules: [], loadedPaths: out var loadedRulePaths);
        var (context, storeDir) = await OpenReadContextGatedAsync(
            new WorkspaceLocation(WorkingDirectory: workingDirectory, StoreRef: storeRef),
            withStoreDir: true
        );
        await using var contextScope = context;

        var indexed = await context.SourceFiles.AsNoTracking().AnyAsync(file => file.FilePath == filePath && file.Status != "skipped");
        if (!indexed)
        {
            throw UnknownFile(filePath);
        }

        var symbols = await LoadSymbolsAsync(context, filePath, includeLambdas: false);
        var canonical = SymbolFactProjections.SelectCanonicalMethodFacts(symbols).ToArray();
        var locations = Locations(canonical);
        var selectors = Selectors(rules);
        if (selectors.Length == 0)
        {
            return new Artifact(new FileEffectReadModel(filePath, [], [], []), locations);
        }

        var rulesHash = RulesFingerprint.ComputeFromPaths(loadedRulePaths);
        var (graph, effects) = await LoadGraphAndEffectsAsync(context, rules, storeDir, rulesHash);
        var model =
            FileEffectReadModelIndex.Build(graph, canonical, effects, selectors, [filePath]).Find(filePath)
            ?? new FileEffectReadModel(filePath, selectors.Select(selector => selector.Family).ToArray(), [], []);
        return new Artifact(model, locations);
    }

    // The resident path resolves cheap key inputs first. SQLite is opened and schema-gated only inside the
    // solution-cache miss factory; every later file request is a dictionary lookup in the retained artifact.
    internal static async Task<Artifact> BuildResidentAsync(string workingDirectory, string filePath, string? storeRef = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var rules = RuleSetLoader.Load(workingDirectory, extraRules: [], loadedPaths: out var loadedRulePaths);
        var rulesHash = RulesFingerprint.ComputeFromPaths(loadedRulePaths);
        var workspace = new WorkspaceLocation(workingDirectory, storeRef);
        var storeDir = StoreLayout.ResolveReadStoreDir(workspace);
        var solution = await WarmStore.ResidentFileEffectsAsync(
            storeDir,
            rulesHash,
            async () =>
            {
                var (context, resolvedStoreDir) = await OpenReadContextGatedAsync(workspace, withStoreDir: true);
                await using var contextScope = context;
                return await BuildResidentSolutionAsync(context, rules, resolvedStoreDir, rulesHash);
            }
        );
        return solution.ForFile(filePath);
    }

    // WarmStoreWatcher already owns a gated context and has just warmed graph + invocations. This call uses
    // the exact same solution-cache gate as a racing endpoint request, so only one factory can run.
    internal static Task PrewarmResidentAsync(RigDbContext context, RuleSet rules, string storeDir, string rulesHash) =>
        WarmStore.ResidentFileEffectsAsync(storeDir, rulesHash, () => BuildResidentSolutionAsync(context, rules, storeDir, rulesHash));

    private static async Task<ResidentSolution> BuildResidentSolutionAsync(
        RigDbContext context,
        RuleSet rules,
        string storeDir,
        string rulesHash
    )
    {
        var indexedRows = await context
            .SourceFiles.AsNoTracking()
            .Where(file => file.Status != "skipped")
            .Select(file => file.FilePath)
            .ToListAsync();
        var indexedPaths = indexedRows.Distinct(FilePathComparer).OrderBy(path => path, FilePathComparer).ToArray();
        var symbols = await LoadSymbolsAsync(context, filePath: null, includeLambdas: true);
        var canonical = SymbolFactProjections.SelectCanonicalMethodFacts(symbols).ToArray();
        var methodsByFile = canonical
            .Where(method => !string.IsNullOrWhiteSpace(method.FilePath))
            .GroupBy(method => method.FilePath, FilePathComparer)
            .ToDictionary(group => group.Key, group => (IReadOnlyDictionary<string, MethodLocation>)Locations(group), FilePathComparer);
        var selectors = Selectors(rules);
        if (selectors.Length == 0)
        {
            return new ResidentSolution(indexedPaths, methodsByFile, families: [], index: null);
        }

        // Exactly one whole-store effect derivation and one labelled reverse projection per resident key,
        // mirroring LiveFactSource.Effects + LiveFactSource.FileEffects.
        var (graph, effects) = await LoadGraphAndEffectsAsync(context, rules, storeDir, rulesHash);
        var index = FileEffectReadModelIndex.Build(graph, symbols, effects, selectors, indexedPaths);
        return new ResidentSolution(indexedPaths, methodsByFile, selectors.Select(selector => selector.Family).ToArray(), index);
    }

    private static async Task<IReadOnlyList<SymbolFact>> LoadSymbolsAsync(RigDbContext context, string? filePath, bool includeLambdas)
    {
        var query = context
            .SymbolFacts.AsNoTracking()
            .Where(symbol => symbol.Kind == SymbolKinds.Method || (includeLambdas && symbol.Kind == "lambda"));
        if (filePath is not null)
        {
            query = query.Where(symbol => symbol.FilePath == filePath);
        }

        return await query
            .Select(symbol => new SymbolFact(
                symbol.SymbolId,
                symbol.Kind,
                symbol.Name,
                symbol.Namespace,
                symbol.ContainingSymbolId,
                symbol.Modifiers,
                symbol.TypeKind,
                symbol.Signature,
                symbol.FilePath,
                symbol.Line,
                symbol.EndLine,
                symbol.DefiningAssembly,
                symbol.IsOverride,
                symbol.BodyHash,
                symbol.SurfaceHash,
                symbol.IsIterator
            ))
            .ToListAsync();
    }

    private static async Task<(FactGraphData Graph, IReadOnlyList<DerivedEffect> Effects)> LoadGraphAndEffectsAsync(
        RigDbContext context,
        RuleSet rules,
        string storeDir,
        string rulesHash
    )
    {
        var graph = await WarmStore.GraphAsync(context, rules, storeDir, rulesHash);
        var invocations = await WarmStore.InvocationsAsync(context, storeDir);
        var epData = await Reads.LoadFactEntryPointDataAsync(context);
        var inputs = new SqlReachability.ReachInputs(
            graph,
            invocations,
            epData.CtorRefs,
            await Reads.LoadThrowRefsAsync(context),
            await Reads.LoadAllocationFactsAsync(context),
            epData
        );
        return (graph, QueryEffectDerivation.ForReach(rules, inputs, graph));
    }

    private static FileEffectSelector[] Selectors(RuleSet rules) =>
        ProviderCatalog
            .EffectfulFamilies(rules)
            .Select(family => new FileEffectSelector(
                family.Family,
                family.Providers.Select(provider => new EffectPredicate(provider)).ToArray()
            ))
            .ToArray();

    private static IReadOnlyDictionary<string, MethodLocation> Locations(IEnumerable<SymbolFact> methods) =>
        methods
            .GroupBy(method => method.SymbolId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var method = group.First();
                    return new MethodLocation(method.SymbolId, method.Name, method.Signature, method.Line, method.EndLine);
                },
                StringComparer.Ordinal
            );

    private static ArgumentException UnknownFile(string filePath) =>
        new("The requested path is not an indexed source file in this store.", nameof(filePath));

    private sealed class ResidentSolution
    {
        private static readonly IReadOnlyDictionary<string, MethodLocation> NoMethods = new Dictionary<string, MethodLocation>(
            StringComparer.Ordinal
        );

        private readonly IReadOnlyDictionary<string, string> _indexedPaths;
        private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, MethodLocation>> _methodsByFile;
        private readonly IReadOnlyList<string> _families;
        private readonly FileEffectReadModelIndex? _index;

        internal ResidentSolution(
            IEnumerable<string> indexedPaths,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, MethodLocation>> methodsByFile,
            IReadOnlyList<string> families,
            FileEffectReadModelIndex? index
        )
        {
            _indexedPaths = indexedPaths.ToDictionary(path => path, path => path, FilePathComparer);
            _methodsByFile = methodsByFile;
            _families = families;
            _index = index;
        }

        internal Artifact ForFile(string requestedPath)
        {
            if (!_indexedPaths.TryGetValue(requestedPath, out var indexedPath))
            {
                throw UnknownFile(requestedPath);
            }

            var model = _index?.Find(indexedPath) ?? new FileEffectReadModel(indexedPath, _families, [], []);
            return new Artifact(model, _methodsByFile.GetValueOrDefault(indexedPath) ?? NoMethods);
        }
    }
}
