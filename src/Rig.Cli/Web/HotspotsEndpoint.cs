using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Rig.Cli.Commands;
using Rig.Cli.Services;

namespace Rig.Cli.Web;

internal static class HotspotsEndpoint
{
    internal const int DefaultTop = 50;
    internal const int MinTop = 1;
    internal const int MaxTop = 500;

    internal sealed record Request(string Sort, int Top, bool NoLambdas, bool Intrinsic);

    public static void MapHotspots(this WebApplication app, string workingDirectory)
    {
        app.MapGet(
            "/api/hotspots",
            async (string? sort, int? top, bool? noLambdas, bool? intrinsic, string? store) =>
            {
                if (!TryValidate(sort, top, noLambdas, intrinsic, out var request, out var error))
                {
                    return Results.Json(error, statusCode: StatusCodes.Status400BadRequest);
                }

                try
                {
                    var artifact = await HotspotsQueryService.BuildAsync(
                        workingDirectory,
                        storeRef: NullIfBlank(store),
                        intrinsic: request!.Intrinsic
                    );
                    return Results.Json(ToResponse(artifact, request));
                }
                catch (Exception ex)
                {
                    return Results.Json(
                        Error($"Hotspots query failed: {ex.Message}"),
                        statusCode: StatusCodes.Status400BadRequest
                    );
                }
            }
        );
    }

    internal static bool TryValidate(
        string? sort,
        int? top,
        bool? noLambdas,
        bool? intrinsic,
        out Request? request,
        out HotspotsErrorDto? error
    )
    {
        var selectedSort = string.IsNullOrWhiteSpace(sort) ? "density" : sort.Trim().ToLowerInvariant();
        if (!HotspotsCommand.Sorts.Contains(selectedSort, StringComparer.Ordinal))
        {
            request = null;
            error = Error($"Invalid sort '{sort}'. Expected one of: {string.Join(", ", HotspotsCommand.Sorts)}.");
            return false;
        }

        var selectedTop = top ?? DefaultTop;
        if (selectedTop is < MinTop or > MaxTop)
        {
            request = null;
            error = Error($"Invalid top '{selectedTop}'. Expected {MinTop}..{MaxTop}.");
            return false;
        }

        request = new Request(selectedSort, selectedTop, noLambdas ?? false, intrinsic ?? false);
        error = null;
        return true;
    }

    internal static HotspotsResponseDto ToResponse(HotspotsQueryService.HotspotArtifact artifact, Request request)
    {
        var rows = HotspotsCommand.SelectRows(artifact.Rows, request.Sort, request.Top, request.NoLambdas);
        return new HotspotsResponseDto(
            Sort: request.Sort,
            Top: request.Top,
            NoLambdas: request.NoLambdas,
            Intrinsic: request.Intrinsic,
            HiddenIntrinsic: artifact.HiddenIntrinsic,
            Rows: rows.Select(Map).ToList()
        );
    }

    private static HotspotRowDto Map(Rig.Domain.Functions.FactHotspotReport.Row r) =>
        new(
            r.Id,
            r.Name,
            r.File,
            r.Line,
            r.Lines,
            r.CallerMethods,
            r.IncomingCallSites,
            r.CalleeMethods,
            r.OutgoingCallSites,
            r.EffectSites,
            r.EffectKinds,
            r.EffectSitesPer100Lines,
            r.HazardSites,
            r.HazardKinds,
            r.AmplificationSites,
            r.ResidualDispatchFan,
            r.DispatchIncomingEdges,
            r.DispatchRank,
            r.IsGenerated,
            r.IsLambda
        );

    private static HotspotsErrorDto Error(string message) => new(message, HotspotsCommand.Sorts, MinTop, MaxTop);

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
