using Microsoft.EntityFrameworkCore;
using Rig.Cli.Caching;
using Rig.Cli.CommandLine;
using Rig.Cli.Commands;
using Rig.Cli.Impact;
using static Rig.Cli.Graph.TraversalGraphLoader;

namespace Rig.Cli.Web;

// Projects the internal ImpactCacheArtifact (Rig.Cli.Impact's diff types) to the flat /api/impact JSON DTO.
// Pure projection — reuses ImpactEngine.FqnForCard so each EP carries the same queryable dotted name the
// CLI's affected-EP cards show.
internal static class ImpactMapper
{
    public static async Task<ImpactResponseDto> ToResponseAsync(
        string workingDirectory,
        string baseStore,
        string headStore,
        ImpactCacheArtifact art
    )
    {
        var d = art.Diff;
        var sites = art.FqnSites;
        var baseStems = d
            .PerEp.SelectMany(delta =>
                delta.Removed.Select(effect => effect.Enclosing).Concat(delta.HazardsRemovedOrEmpty.Select(hazard => hazard.Enclosing))
            )
            .ToHashSet(StringComparer.Ordinal);
        var headStems = d
            .PerEp.SelectMany(delta =>
                delta.Added.Select(effect => effect.Enclosing).Concat(delta.HazardsAddedOrEmpty.Select(hazard => hazard.Enclosing))
            )
            .ToHashSet(StringComparer.Ordinal);
        var baseLocationsTask = LoadUniqueLocationsAsync(workingDirectory, baseStore, baseStems);
        var headLocationsTask = LoadUniqueLocationsAsync(workingDirectory, headStore, headStems);
        await Task.WhenAll(baseLocationsTask, headLocationsTask);
        var baseLocations = await baseLocationsTask;
        var headLocations = await headLocationsTask;
        return new ImpactResponseDto(
            Base: Prov(art.BaseProvenance),
            Head: Prov(art.HeadProvenance),
            AddedEps: d.Ep?.Added.Select(a => new ImpactKindRouteDto(Kind: a.Kind, Route: a.Route)).ToList() ?? [],
            RemovedEps: d.Ep?.Removed.Select(a => new ImpactKindRouteDto(Kind: a.Kind, Route: a.Route)).ToList() ?? [],
            AffectedEpCount: d.AffectedEps.Count,
            PerEp: d.PerEp.Select(p => new ImpactEpDeltaDto(
                    Kind: p.Kind,
                    Route: p.Route,
                    Fqn: ImpactEngine.FqnForCard(route: p.Route, filePath: p.FilePath, line: p.Line, idBySite: sites),
                    File: string.IsNullOrEmpty(p.FilePath) ? null : p.FilePath,
                    Line: p.Line,
                    BaseEffects: p.BaseEffects,
                    BranchEffects: p.BranchEffects,
                    Added: p.Added.Select(e => new ImpactEffectDto(
                            Provider: e.Provider,
                            Operation: e.Operation,
                            Resource: e.Resource,
                            Enclosing: e.Enclosing,
                            File: headLocations.GetValueOrDefault(e.Enclosing)?.File,
                            Line: headLocations.GetValueOrDefault(e.Enclosing)?.Line ?? 0
                        ))
                        .ToList(),
                    Removed: p.Removed.Select(e => new ImpactEffectDto(
                            Provider: e.Provider,
                            Operation: e.Operation,
                            Resource: e.Resource,
                            Enclosing: e.Enclosing,
                            File: baseLocations.GetValueOrDefault(e.Enclosing)?.File,
                            Line: baseLocations.GetValueOrDefault(e.Enclosing)?.Line ?? 0
                        ))
                        .ToList(),
                    HazardsAdded: p.HazardsAddedOrEmpty.Select(hz => new ImpactHazardDto(
                            Type: hz.Type,
                            Cell: hz.Cell,
                            Enclosing: hz.Enclosing,
                            Confidence: hz.Confidence,
                            File: headLocations.GetValueOrDefault(hz.Enclosing)?.File,
                            Line: headLocations.GetValueOrDefault(hz.Enclosing)?.Line ?? 0
                        ))
                        .ToList(),
                    HazardsRemoved: p.HazardsRemovedOrEmpty.Select(hz => new ImpactHazardDto(
                            Type: hz.Type,
                            Cell: hz.Cell,
                            Enclosing: hz.Enclosing,
                            Confidence: hz.Confidence,
                            File: baseLocations.GetValueOrDefault(hz.Enclosing)?.File,
                            Line: baseLocations.GetValueOrDefault(hz.Enclosing)?.Line ?? 0
                        ))
                        .ToList(),
                    SharedMutationOnPath: p.SharedMutationOnPath,
                    // Amplification (looped_effect) delta — the terse per-(EP x provider:operation) entries.
                    AmplificationsAdded: p.AmplificationsAddedOrEmpty.Select(a => new ImpactAmplificationDto(
                            Provider: a.Provider,
                            Operation: a.Operation,
                            Sites: a.Sites
                        ))
                        .ToList(),
                    AmplificationsRemoved: p.AmplificationsRemovedOrEmpty.Select(a => new ImpactAmplificationDto(
                            Provider: a.Provider,
                            Operation: a.Operation,
                            Sites: a.Sites
                        ))
                        .ToList()
                ))
                .ToList()
        );
    }

    private static async Task<IReadOnlyDictionary<string, SourceLocation>> LoadUniqueLocationsAsync(
        string workingDirectory,
        string store,
        IReadOnlySet<string> relevantStems
    )
    {
        if (relevantStems.Count == 0)
        {
            return new Dictionary<string, SourceLocation>(StringComparer.Ordinal);
        }

        await using var context = await OpenReadContextGatedAsync(new WorkspaceLocation(workingDirectory, store));
        var rows = await context
            .SymbolFacts.AsNoTracking()
            .Where(symbol => symbol.Kind == "method" && symbol.FilePath != "" && symbol.Line > 0)
            .Select(symbol => new
            {
                symbol.SymbolId,
                symbol.FilePath,
                symbol.Line,
            })
            .ToArrayAsync();
        var result = new Dictionary<string, SourceLocation>(StringComparer.Ordinal);
        foreach (
            var group in rows.Where(row => relevantStems.Contains(ImpactEngine.StripParams(row.SymbolId)))
                .GroupBy(row => ImpactEngine.StripParams(row.SymbolId), StringComparer.Ordinal)
        )
        {
            var locations = group.Select(row => new SourceLocation(row.FilePath, row.Line)).Distinct().ToArray();
            if (locations.Length == 1)
            {
                result[group.Key] = locations[0];
            }
        }

        return result;
    }

    private static ImpactProvenanceDto Prov(StoreProvenance p) =>
        new(Branch: p.Branch, Commit: p.ShortCommit, Label: p.ShortCommit is null ? p.Fallback : $"{p.Branch ?? "?"} ({p.ShortCommit})");

    private sealed record SourceLocation(string File, int Line);
}
