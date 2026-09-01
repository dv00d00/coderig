using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Rig.Cli.CommandLine;
using Rig.Cli.Rendering;
using Rig.Cli.Services;
using Rig.Storage.Queries;
using static Rig.Cli.Graph.TraversalGraphLoader;

namespace Rig.Cli.Web;

internal static class FileEffectsEndpoint
{
    private const int DefaultFileLimit = 100;
    private const int MaxFileLimit = 500;
    private const int DefaultSourceLines = 240;
    private const int MaxSourceLines = SourceRenderer.DefaultMaxLines;
    private const int ShortShaLength = 12;

    internal static void MapFileEffects(this WebApplication app, string workingDirectory)
    {
        app.MapGet(
            "/api/files",
            async (string? q, int? limit, string? store) =>
            {
                try
                {
                    var selectedLimit = Math.Clamp(limit ?? DefaultFileLimit, 1, MaxFileLimit);
                    await using var context = await OpenReadContextGatedAsync(new WorkspaceLocation(workingDirectory, NullIfBlank(store)));
                    var query = context.SourceFiles.AsNoTracking().Where(file => file.Status != "skipped");
                    if (!string.IsNullOrWhiteSpace(q))
                    {
                        var pattern = q.Trim();
                        query = query.Where(file => file.FilePath.Contains(pattern));
                    }

                    var rows = await query
                        .Select(file => new
                        {
                            file.FilePath,
                            file.ProjectName,
                            file.Status,
                        })
                        .ToListAsync();
                    var files = rows.GroupBy(row => row.FilePath, FilePathComparer)
                        .OrderBy(group => group.Key, FilePathComparer)
                        .Select(group => new IndexedFileDto(
                            group.Key,
                            Path.GetFileName(group.Key),
                            group.Select(row => row.Status).FirstOrDefault() ?? "indexed",
                            group
                                .Select(row => row.ProjectName)
                                .Where(project => !string.IsNullOrWhiteSpace(project))
                                .Distinct(StringComparer.Ordinal)
                                .OrderBy(project => project, StringComparer.Ordinal)
                                .ToArray()
                        ))
                        .ToArray();
                    return Results.Json(new IndexedFilesResponseDto(files.Take(selectedLimit).ToArray(), files.Length, selectedLimit));
                }
                catch (Exception ex)
                {
                    return Results.Problem(title: "File inventory failed", detail: ex.Message, statusCode: 400);
                }
            }
        );

        app.MapGet(
            "/api/file-effects",
            async (string? file, string? store) =>
            {
                if (string.IsNullOrWhiteSpace(file))
                {
                    return Results.Problem(title: "Missing 'file'", detail: "Choose a path returned by /api/files.", statusCode: 400);
                }

                try
                {
                    var artifact = await FileEffectsQueryService.BuildAsync(workingDirectory, file, NullIfBlank(store));
                    return Results.Json(ToResponse(artifact));
                }
                catch (Exception ex)
                {
                    return Results.Problem(title: "File effects query failed", detail: ex.Message, statusCode: 400);
                }
            }
        );

        app.MapGet(
            "/api/file-source",
            async (string? file, int? start, int? count, string? store) =>
            {
                if (string.IsNullOrWhiteSpace(file))
                {
                    return Results.Problem(title: "Missing 'file'", detail: "Choose a path returned by /api/files.", statusCode: 400);
                }

                try
                {
                    var first = Math.Max(1, start ?? 1);
                    var take = Math.Clamp(count ?? DefaultSourceLines, 1, MaxSourceLines);
                    await using var context = await OpenReadContextGatedAsync(new WorkspaceLocation(workingDirectory, NullIfBlank(store)));
                    var indexed = await context.SourceFiles.AsNoTracking().AnyAsync(row => row.FilePath == file && row.Status != "skipped");
                    if (!indexed)
                    {
                        return Results.Problem(
                            title: "Unknown indexed file",
                            detail: "The requested path was not returned by this store's indexed file inventory.",
                            statusCode: 400
                        );
                    }

                    var runs = await Reads.ListRunsAsync(context);
                    var run = runs.FirstOrDefault(candidate => candidate.SourceCommit is not null) ?? runs.FirstOrDefault();
                    var storeDirty = run?.SourceDirty ?? false;
                    var renderer = new SourceRenderer(run?.SourceCommit, storeDirty);
                    // Ask for one look-ahead line. It is not returned; it only tells the client whether a next
                    // page exists without reading or counting the whole source file.
                    var snippet = renderer.Resolve(file, first, first + take, maxLines: take + 1);
                    var shown = snippet.Lines.Take(take).ToArray();
                    return Results.Json(
                        new FileSourceResponseDto(
                            file,
                            first,
                            shown.Length == 0 ? first : shown[^1].Number,
                            OriginName(snippet.Origin),
                            Short(snippet.Commit),
                            snippet.Reason,
                            shown.Select(line => new SourceLineDto(line.Number, line.Text)).ToArray(),
                            HasPrevious: first > 1,
                            HasMore: snippet.Lines.Count > take || snippet.TruncatedCount > 0,
                            StoreDirty: storeDirty
                        )
                    );
                }
                catch (Exception ex)
                {
                    return Results.Problem(title: "File source query failed", detail: ex.Message, statusCode: 400);
                }
            }
        );
    }

    internal static FileEffectsResponseDto ToResponse(FileEffectsQueryService.Artifact artifact)
    {
        var model = artifact.Model;
        var methods = model
            .Methods.Select(method =>
            {
                var location =
                    artifact.Methods.GetValueOrDefault(method.SymbolId)
                    ?? new FileEffectsQueryService.MethodLocation(
                        method.SymbolId,
                        SymbolNameFormatter.ShortName(method.SymbolId),
                        "",
                        0,
                        0
                    );
                return new FileEffectMethodDto(
                    method.SymbolId,
                    location.Name,
                    location.Signature,
                    location.Line,
                    location.EndLine,
                    method.Effects.Select(Map).ToArray()
                );
            })
            .OrderBy(method => method.Line)
            .ThenBy(method => method.Id, StringComparer.Ordinal)
            .ToArray();
        var sites = model
            .CallSites.Select(site => new FileEffectCallSiteDto(
                site.EnclosingSymbolId,
                site.TargetSymbolId,
                site.Line,
                site.Effects.Select(Map).ToArray()
            ))
            .OrderBy(site => site.Line)
            .ThenBy(site => site.TargetMethodId, StringComparer.Ordinal)
            .ToArray();
        return new FileEffectsResponseDto(
            model.FilePath,
            model.EffectSelectors,
            methods,
            sites,
            ColumnsAvailable: false,
            WitnessPathsIncluded: false
        );
    }

    private static FileEffectAggregateDto Map(Rig.Domain.Functions.FileEffectAggregate effect) => new(effect.Family, effect.NearestDepth);

    private static string OriginName(SourceOrigin origin) =>
        origin switch
        {
            SourceOrigin.WorkingTree => "worktree",
            SourceOrigin.GitBlob => "git",
            _ => "unavailable",
        };

    private static string? Short(string? sha) =>
        string.IsNullOrEmpty(sha) ? null
        : sha.Length <= ShortShaLength ? sha
        : sha[..ShortShaLength];

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static readonly StringComparer FilePathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
