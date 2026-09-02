using Rig.Analysis.Rules;
using Rig.Cli.CommandLine;
using static Rig.Cli.EntryPoints.EntryPointContext;
using static Rig.Cli.Graph.TraversalGraphLoader;

namespace Rig.Cli.Services;

// The rule-detected entry-point listing for the web explorer — the SAME set `rig entrypoints` (and
// derive/callers/impact) build: L1 derived EPs + promoted async-handoff origins, deduped and sorted by
// (kind, route). Each carries the QUERYABLE fqn (what to pass as tree ?from=) beside its display route —
// the route form matches nothing, the fqn exact-resolves.
public static class EntryPointService
{
    public sealed record EntryPointView(string Kind, string Route, string Fqn, string? File, int Line);

    public static async Task<IReadOnlyList<EntryPointView>> ListAsync(
        string workingDirectory,
        string? storeRef = null,
        IReadOnlyList<string>? extraRules = null
    )
    {
        var rules = RuleSetLoader.Load(workingDirectory, extraRules ?? [], loadedPaths: out var loadedRulePaths);
        var ws = new WorkspaceLocation(WorkingDirectory: workingDirectory, StoreRef: storeRef);
        await using var context = await OpenReadContextGatedAsync(ws);

        // The whole-store EP record set, through the SAME artifact-cache entry `callers --entrypoints` uses:
        // this listing is a pure function of (store + rules), so it must not re-derive per request (~5s on the
        // 227-project store). Borrows the context the way HazardsService does, to reach the fact-source seam
        // without opening a second connection.
        var source = Live.StoreQueryFactSource.Borrowing(context, ws);
        using var epCache = source.OpenArtifactCache(useCache: true);
        var epRecords = await LoadOrDeriveEntryPointRecordsAsync(
            source: source,
            cache: epCache,
            rulesHash: RulesFingerprint.ComputeFromPaths(loadedRulePaths),
            rules: rules
        );

        return epRecords
            .GroupBy(e => (e.Kind, e.Route, e.FilePath, e.Line))
            // The group key IS four of the record's six fields and DocId is a function of the other two, so
            // First() is the same row the old projection rebuilt field-by-field off the key.
            .Select(g => g.First())
            .Select(e => new EntryPointView(
                Kind: e.Kind,
                Route: e.Route,
                Fqn: FqnOrRoute(e),
                File: string.IsNullOrEmpty(e.FilePath) ? null : e.FilePath,
                Line: e.Line
            ))
            .OrderBy(e => e.Kind, StringComparer.Ordinal)
            .ThenBy(e => e.Route, StringComparer.Ordinal)
            .ToList();
    }
}
