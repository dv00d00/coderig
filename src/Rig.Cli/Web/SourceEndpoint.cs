using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Rig.Cli.CommandLine;
using Rig.Cli.Rendering;
using Rig.Storage.Queries;
using static Rig.Cli.Graph.TraversalGraphLoader;

namespace Rig.Cli.Web;

// The /api/source endpoint — web equivalent of `rig show <pattern>`: the declaration SOURCE for one symbol,
// resolved by the SAME SourceRenderer the CLI drives (working tree only when it provably IS the indexed
// revision, otherwise the exact git blob marked as such, otherwise a one-line refusal). Errors surface as a
// 400 with the message (a missing store / unknown id is user error, not a 500) — matches RefsEndpoint.
//
// KEYED BY SYMBOL ID ONLY, deliberately: the endpoint takes `id` and looks the FilePath/Line/EndLine up in
// the store's own symbol facts. It must NEVER accept a client-supplied file path. `rig serve` is an HTTP
// server, and an endpoint that renders whatever path it is handed is an arbitrary-file-read primitive; the
// only paths this can ever render are paths already IN the store, put there by an index the user ran.
internal static class SourceEndpoint
{
    // Upper bound on the caller-supplied context padding. The renderer's own 400-line cap still applies on
    // top; this just stops `?context=100000` turning one chip click into a whole-file read.
    private const int MaxContext = 50;

    // Displayed sha length, matching SourceRenderer's marker and `rig runs`.
    private const int ShortShaLength = 12;

    public static void MapSource(this WebApplication app, string workingDirectory)
    {
        // `id` is a full DocID (exactly what the tree/callers payloads carry as node.id) — required. `store`
        // picks a specific commit/id (default LATEST, mirrors the other endpoints); `context` pads the
        // declaration range either side, mirroring `rig show --context`.
        app.MapGet(
            "/api/source",
            async (string? id, string? store, int? context) =>
            {
                var symbolId = NullIfBlank(id);
                if (symbolId is null)
                {
                    return Results.Problem(title: "Missing 'id'", detail: "Provide a ?id= symbol id (DocID).", statusCode: 400);
                }

                try
                {
                    await using var db = await OpenReadContextGatedAsync(
                        new WorkspaceLocation(WorkingDirectory: workingDirectory, StoreRef: NullIfBlank(store))
                    );

                    // The stored location for this EXACT id — the only input the renderer is ever given. A
                    // symbol can be indexed by several multi-target project siblings, so take the first row
                    // (they share the location), mirroring ShowCommand's dedupe.
                    var decl = await db
                        .SymbolFacts.AsNoTracking()
                        .Where(s => s.SymbolId == symbolId)
                        .Select(s => new
                        {
                            s.FilePath,
                            s.Line,
                            s.EndLine,
                        })
                        .FirstOrDefaultAsync();

                    if (decl is null)
                    {
                        return Results.Problem(
                            title: "Unknown symbol",
                            detail: $"No indexed symbol with id '{symbolId}' in this store.",
                            statusCode: 400
                        );
                    }

                    // Source provenance is a property of the STORE (per-commit) — read the same way `rig show`
                    // reads it: the first run carrying a commit, else the first run at all.
                    var runs = await Reads.ListRunsAsync(db);
                    var run = runs.FirstOrDefault(r => r.SourceCommit is not null) ?? runs.FirstOrDefault();
                    var storeDirty = run?.SourceDirty ?? false;
                    var renderer = new SourceRenderer(storeCommit: run?.SourceCommit, storeDirty: storeDirty);

                    var snippet = renderer.Resolve(
                        filePath: decl.FilePath,
                        startLine: decl.Line,
                        endLine: decl.EndLine,
                        context: Math.Clamp(context ?? 0, min: 0, max: MaxContext)
                    );

                    var response = new SourceResponseDto(
                        SymbolId: symbolId,
                        File: decl.FilePath,
                        Line: decl.Line,
                        EndLine: decl.EndLine,
                        Origin: OriginName(snippet.Origin),
                        Commit: Short(snippet.Commit),
                        TruncatedCount: snippet.TruncatedCount,
                        Reason: snippet.Reason,
                        Lines: snippet.Lines.Select(l => new SourceLineDto(Number: l.Number, Text: l.Text)).ToList(),
                        StoreDirty: storeDirty
                    );
                    return Results.Json(response);
                }
                catch (Exception ex)
                {
                    return Results.Problem(title: "Source query failed", detail: ex.Message, statusCode: 400);
                }
            }
        );
    }

    // The three words `rig show --format tsv` emits, so CLI and web name the same provenance identically.
    private static string OriginName(SourceOrigin origin) =>
        origin switch
        {
            SourceOrigin.WorkingTree => "worktree",
            SourceOrigin.GitBlob => "git",
            _ => "unavailable",
        };

    private static string? Short(string? sha) =>
        string.IsNullOrEmpty(sha) ? null
        : sha!.Length <= ShortShaLength ? sha
        : sha.Substring(startIndex: 0, length: ShortShaLength);

    // A blank query-string value (?store=) arrives as "" not null; normalize so the store lookup sees null
    // (LATEST). Duplicated from the other endpoints (private there) — small enough not to warrant extracting.
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
