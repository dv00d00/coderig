using Microsoft.EntityFrameworkCore;
using Rig.Analysis.Rules;
using Rig.Cli.Caching;
using Rig.Cli.CommandLine;
using Rig.Cli.Live;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;
using static Rig.Cli.Graph.TraversalGraphLoader;

namespace Rig.Cli.Services;

// The store-backed counterpart of Rider's resident file-effects query. Its public surface is one physical
// indexed file; store opening, rule vocabulary, whole-graph warm-up and the reverse projection stay hidden.
internal static class FileEffectsQueryService
{
    internal sealed record MethodLocation(string Id, string Name, string Signature, int Line, int EndLine);

    internal sealed record Artifact(FileEffectReadModel Model, IReadOnlyDictionary<string, MethodLocation> Methods);

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
            throw new ArgumentException("The requested path is not an indexed source file in this store.", nameof(filePath));
        }

        var rulesHash = RulesFingerprint.ComputeFromPaths(loadedRulePaths);
        return await WarmStore.FileEffectsAsync(
            storeDir,
            rulesHash,
            filePath,
            async () =>
            {
                // Deliberately query only this file's declarations. The graph is solution-wide because the
                // answer is transitive, but selecting a file must not materialize every solution symbol.
                var symbols = await context
                    .SymbolFacts.AsNoTracking()
                    .Where(symbol => symbol.Kind == SymbolKinds.Method && symbol.FilePath == filePath)
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
                var canonical = SymbolFactProjections.SelectCanonicalMethodFacts(symbols).ToArray();
                var locations = canonical.ToDictionary(
                    symbol => symbol.SymbolId,
                    symbol => new MethodLocation(symbol.SymbolId, symbol.Name, symbol.Signature, symbol.Line, symbol.EndLine),
                    StringComparer.Ordinal
                );

                var selectors = ProviderCatalog
                    .EffectfulFamilies(rules)
                    .Select(family => new FileEffectSelector(
                        family.Family,
                        family.Providers.Select(provider => new EffectPredicate(provider)).ToArray()
                    ))
                    .ToArray();
                if (selectors.Length == 0)
                {
                    return new Artifact(new FileEffectReadModel(filePath, [], [], []), locations);
                }

                // The file model is sync-cut, so delivery/handoff edges present in the derive-shaped warm
                // graph cannot become reachable file effects. Reuse that resident graph plus raw effect
                // inputs rather than loading another whole graph for every file click.
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
                var effects = QueryEffectDerivation.ForReach(rules, inputs, graph);
                var model =
                    FileEffectReadModelIndex.Build(graph, canonical, effects, selectors, [filePath]).Find(filePath)
                    ?? new FileEffectReadModel(filePath, selectors.Select(selector => selector.Family).ToArray(), [], []);
                return new Artifact(model, locations);
            }
        );
    }
}
