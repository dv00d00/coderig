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
    // `only` / `exclude` / `includeIntrinsic` are the SHARED selection (?only=&exclude=&intrinsic=), applied
    // through ImpactEngine.Select — the same call `rig impact` makes — so the browser, the CLI and the CI gates
    // read one filtered view. The cached artifact stays unfiltered, so selection is a post-cache pass here.
    public static async Task<ImpactResponseDto> ToResponseAsync(
        string workingDirectory,
        string baseStore,
        string headStore,
        ImpactCacheArtifact art,
        HashSet<string> only,
        HashSet<string> exclude,
        bool includeIntrinsic
    )
    {
        var view = ImpactEngine.Select(art.Diff, only: only, exclude: exclude, includeIntrinsic: includeIntrinsic);
        var sites = art.FqnSites;
        // The location maps are keyed on STORE IDENTITY alone and are filter-INDEPENDENT by construction (the
        // whole per-store method map, not a stem-keyed subset), which is what makes them memoizable across
        // filter toggles — the stem sets are NOT filter-independent, since they are read off PerEp's
        // added/removed lists. Both sides are started before either is awaited, but two COLD scans serialize
        // behind the warm cache's gate (measured ~1.0 s each on MedDBase, then a hit on every later request).
        var baseLocationsTask = LoadUniqueLocationsAsync(workingDirectory, baseStore);
        var headLocationsTask = LoadUniqueLocationsAsync(workingDirectory, headStore);
        var baseCompilationTask = WebCompilationHealth.LoadAsync(workingDirectory, baseStore);
        var headCompilationTask = WebCompilationHealth.LoadAsync(workingDirectory, headStore);
        await Task.WhenAll(baseLocationsTask, headLocationsTask, baseCompilationTask, headCompilationTask);
        var baseLocations = await baseLocationsTask;
        var headLocations = await headLocationsTask;
        var baseCompilation = await baseCompilationTask;
        var headCompilation = await headCompilationTask;
        return new ImpactResponseDto(
            Base: Prov(art.BaseProvenance),
            Head: Prov(art.HeadProvenance),
            AddedEps: view.Diff.Ep?.Added.Select(a => new ImpactKindRouteDto(Kind: a.Kind, Route: a.Route)).ToList() ?? [],
            RemovedEps: view.Diff.Ep?.Removed.Select(a => new ImpactKindRouteDto(Kind: a.Kind, Route: a.Route)).ToList() ?? [],
            AffectedEpCount: view.AffectedEpCount,
            BehavioralEpCount: view.BehavioralEpCount,
            HiddenIntrinsic: view.HiddenIntrinsic,
            ExtractionCompatible: art.BaseProvenance.IsExtractionCompatibleWith(art.HeadProvenance),
            BaseCompileErrors: WebCompilationHealth.ToDto(baseCompilation),
            HeadCompileErrors: WebCompilationHealth.ToDto(headCompilation),
            PerEp: view.PerEp.Select(p => new ImpactEpDeltaDto(
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
                            File: headLocations.GetValueOrDefault(ImpactEngine.StripParams(e.Enclosing))?.File,
                            Line: headLocations.GetValueOrDefault(ImpactEngine.StripParams(e.Enclosing))?.Line ?? 0,
                            BindingHealth: WebCompilationHealth.BindingHealth(
                                headCompilation,
                                headLocations.GetValueOrDefault(ImpactEngine.StripParams(e.Enclosing))?.File
                            )
                        ))
                        .ToList(),
                    Removed: p.Removed.Select(e => new ImpactEffectDto(
                            Provider: e.Provider,
                            Operation: e.Operation,
                            Resource: e.Resource,
                            Enclosing: e.Enclosing,
                            File: baseLocations.GetValueOrDefault(ImpactEngine.StripParams(e.Enclosing))?.File,
                            Line: baseLocations.GetValueOrDefault(ImpactEngine.StripParams(e.Enclosing))?.Line ?? 0,
                            BindingHealth: WebCompilationHealth.BindingHealth(
                                baseCompilation,
                                baseLocations.GetValueOrDefault(ImpactEngine.StripParams(e.Enclosing))?.File
                            )
                        ))
                        .ToList(),
                    HazardsAdded: p.HazardsAddedOrEmpty.Select(hz => new ImpactHazardDto(
                            Type: hz.Type,
                            Cell: hz.Cell,
                            Enclosing: hz.Enclosing,
                            Confidence: hz.Confidence,
                            File: headLocations.GetValueOrDefault(ImpactEngine.StripParams(hz.Enclosing))?.File,
                            Line: headLocations.GetValueOrDefault(ImpactEngine.StripParams(hz.Enclosing))?.Line ?? 0,
                            BindingHealth: WebCompilationHealth.BindingHealth(
                                headCompilation,
                                headLocations.GetValueOrDefault(ImpactEngine.StripParams(hz.Enclosing))?.File
                            )
                        ))
                        .ToList(),
                    HazardsRemoved: p.HazardsRemovedOrEmpty.Select(hz => new ImpactHazardDto(
                            Type: hz.Type,
                            Cell: hz.Cell,
                            Enclosing: hz.Enclosing,
                            Confidence: hz.Confidence,
                            File: baseLocations.GetValueOrDefault(ImpactEngine.StripParams(hz.Enclosing))?.File,
                            Line: baseLocations.GetValueOrDefault(ImpactEngine.StripParams(hz.Enclosing))?.Line ?? 0,
                            BindingHealth: WebCompilationHealth.BindingHealth(
                                baseCompilation,
                                baseLocations.GetValueOrDefault(ImpactEngine.StripParams(hz.Enclosing))?.File
                            )
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
                        .ToList(),
                    BindingHealth: EpBindingHealth(p, baseLocations, headLocations, baseCompilation, headCompilation)
                ))
                .ToList()
        );
    }

    // Every param-free method stem in ONE store mapped to its declaration site, or ABSENT when the stem has
    // more than one distinct site (an overload set spread over two files, a partial) — the mapper then renders
    // no location rather than an arbitrary one of them. Fail-closed by design: see ImpactReviewLocationTests.
    //
    // WHOLE-STORE and memoized per store identity for process lifetime (WarmStore.ImpactLocationsAsync): the
    // stem sets it used to be narrowed by come off PerEp's added/removed lists, so they SHRINK with the
    // effect filter — a stem-keyed memo would miss on every filter toggle, which is the one cost the
    // server-side filter has to avoid. The full map cannot depend on the filter, so it is memoizable.
    private static Task<IReadOnlyDictionary<string, SourceLocation>> LoadUniqueLocationsAsync(string workingDirectory, string store)
    {
        var location = new WorkspaceLocation(workingDirectory, store);
        return WarmStore.ImpactLocationsAsync(StoreLayout.ResolveReadStoreDir(location), () => LoadAsync(location));

        static async Task<IReadOnlyDictionary<string, SourceLocation>> LoadAsync(WorkspaceLocation location)
        {
            await using var context = await OpenReadContextGatedAsync(location);
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
            foreach (var group in rows.GroupBy(row => ImpactEngine.StripParams(row.SymbolId), StringComparer.Ordinal))
            {
                var locations = group.Select(row => new SourceLocation(row.FilePath, row.Line)).Distinct().ToArray();
                if (locations.Length == 1)
                {
                    result[group.Key] = locations[0];
                }
            }

            return result;
        }
    }

    private static ImpactProvenanceDto Prov(StoreProvenance p) =>
        new(
            Branch: p.Branch,
            Commit: p.ShortCommit,
            Label: p.ShortCommit is null ? p.Fallback : $"{p.Branch ?? "?"} ({p.ShortCommit})",
            ExtractionVersions: p.ExtractionVersionsOrEmpty,
            ProducingRigBuilds: p.ProducingRigBuildsOrEmpty
        );

    private static string EpBindingHealth(
        EpFootprintDelta delta,
        IReadOnlyDictionary<string, SourceLocation> baseLocations,
        IReadOnlyDictionary<string, SourceLocation> headLocations,
        CompilationHealthNotice.StoreSnapshot baseCompilation,
        CompilationHealthNotice.StoreSnapshot headCompilation
    )
    {
        if (baseCompilation.HasCompileError(delta.FilePath) || headCompilation.HasCompileError(delta.FilePath))
        {
            return "compile_error";
        }

        var headEnclosings = delta
            .Added.Select(effect => effect.Enclosing)
            .Concat(delta.HazardsAddedOrEmpty.Select(hazard => hazard.Enclosing));
        if (
            headEnclosings.Any(enclosing =>
                headCompilation.HasCompileError(headLocations.GetValueOrDefault(ImpactEngine.StripParams(enclosing))?.File)
            )
        )
        {
            return "compile_error";
        }

        var baseEnclosings = delta
            .Removed.Select(effect => effect.Enclosing)
            .Concat(delta.HazardsRemovedOrEmpty.Select(hazard => hazard.Enclosing));
        return baseEnclosings.Any(enclosing =>
            baseCompilation.HasCompileError(baseLocations.GetValueOrDefault(ImpactEngine.StripParams(enclosing))?.File)
        )
            ? "compile_error"
            : "ok";
    }

    private sealed record SourceLocation(string File, int Line);
}
