using Rig.Cli.CommandLine;
using Rig.Cli.Rendering;
using Rig.Storage.Queries;
using static Rig.Cli.Graph.TraversalGraphLoader;

namespace Rig.Cli.Services;

// Symbol name search for the web explorer's search box — the same FTS/LIKE search `rig symbols` runs
// (Reads.SearchSymbolsAsync), lifted so the CLI and web share it. Returns the DocID (Id) — which the tree
// endpoint's exact-match pattern resolves precisely — plus a short display name and location.
public static class SymbolSearchService
{
    public sealed record SymbolHit(string Id, string Kind, string Name, string? File, int Line);

    // Full-fidelity query row shared by the CLI's human/TSV/JSON renderers. The web search maps this to its
    // intentionally smaller navigation-picker DTO after applying web-only ranking.
    public sealed record SymbolRecord(
        string Id,
        string Kind,
        string Name,
        string Signature,
        string File,
        int Line,
        string Assembly
    );

    public sealed record SymbolQueryResult(int Total, IReadOnlyList<SymbolRecord> Symbols);

    // The one raw store query + lambda filter behind both CLI and web. It deliberately does NOT rank or cap:
    // the CLI preserves the store's ordinal SymbolId order, while the web picker applies navigation ranking.
    public static async Task<SymbolQueryResult> QueryAsync(
        string workingDirectory,
        string query,
        string? kind = null,
        bool noLambdas = false,
        string? storeRef = null
    )
    {
        await using var context = await OpenReadContextGatedAsync(
            new WorkspaceLocation(WorkingDirectory: workingDirectory, StoreRef: storeRef)
        );
        // Fetch beyond any presentation cap so Total is the true post-filter total. The LIKE fallback is
        // itself bounded to 5000 unique rows; the FTS path returns the full match set.
        var hits = await Reads.SearchSymbolsAsync(context, pattern: query, kind: kind, limit: int.MaxValue);
        var filtered = noLambdas ? hits.Where(h => !h.SymbolId.Contains("~λ", StringComparison.Ordinal)) : hits;
        var rows = filtered
            .Select(h => new SymbolRecord(
                Id: h.SymbolId,
                Kind: h.Kind,
                Name: SymbolNameFormatter.ShortName(h.SymbolId),
                Signature: h.Signature,
                File: h.FilePath,
                Line: h.Line,
                Assembly: h.DefiningAssembly
            ))
            .ToList();
        return new SymbolQueryResult(Total: rows.Count, Symbols: rows);
    }

    public static async Task<IReadOnlyList<SymbolHit>> SearchAsync(
        string workingDirectory,
        string query,
        string? kind = null,
        int limit = 25,
        bool noLambdas = true,
        string? storeRef = null
    )
    {
        var result = await QueryAsync(workingDirectory, query, kind, noLambdas, storeRef);

        // Rank for a NAVIGATION picker before applying the cap. The shared query orders by symbolid
        // (alphabetical), which puts DocID prefixes E:/F: (events/fields) ahead of M:/T: (methods/types) — so a
        // common term (e.g. "invoice") fills the 25-row cap with events/fields and buries the methods/types the
        // user actually navigates to. Re-rank: best name match first, then navigable kinds, then shorter (more
        // specific) names. Web-only — the CLI's alphabetical `rig symbols` order is unchanged.
        var q = query.Trim();
        return result
            .Symbols.OrderBy(h => NameRank(h.Name, q))
            .ThenBy(h => KindRank(h.Kind))
            .ThenBy(h => h.Name.Length)
            .ThenBy(h => h.Id, StringComparer.Ordinal)
            .Take(limit)
            .Select(h => new SymbolHit(Id: h.Id, Kind: h.Kind, Name: h.Name, File: h.File, Line: h.Line))
            .ToList();
    }

    // How well the display name matches the query: exact > prefix > contains > matched-only-via-DocID.
    private static int NameRank(string name, string q) =>
        name.Equals(q, StringComparison.OrdinalIgnoreCase) ? 0
        : name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 1
        : name.Contains(q, StringComparison.OrdinalIgnoreCase) ? 2
        : 3;

    // Navigable call-graph nodes (methods, types) first; then properties; events/fields/other last.
    private static int KindRank(string kind) =>
        kind is "method" or "type" ? 0
        : kind is "property" ? 1
        : 2;
}
