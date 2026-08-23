using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Rig.Cli.Services;

namespace Rig.Cli.Web;

// Explicit entry-point behavior comparison. It deliberately does not guess peers: callers choose A and B,
// while the shared EffectsDiffQueryService guarantees the web and CLI resolve and compare them identically.
internal static class EffectsDiffEndpoint
{
    public static void MapEffectsDiff(this WebApplication app, string workingDirectory)
    {
        app.MapGet(
            "/api/effects-diff",
            async (string? a, string? b, string[]? only, string? store) =>
            {
                if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                {
                    return Results.Json(MissingResponse(a ?? "", b ?? ""), statusCode: StatusCodes.Status400BadRequest);
                }

                try
                {
                    var result = await EffectsDiffQueryService.BuildAsync(
                        workingDirectory: workingDirectory,
                        aPattern: a,
                        bPattern: b,
                        only: only,
                        storeRef: NullIfBlank(store)
                    );
                    var response = ToResponse(result);
                    return Results.Json(response, statusCode: result.Matched ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
                }
                catch (Exception ex)
                {
                    return Results.Json(MissingResponse(a, b, ex.Message), statusCode: StatusCodes.Status400BadRequest);
                }
            }
        );
    }

    internal static EffectsDiffResponseDto ToResponse(EffectsDiffQueryService.EffectsDiffQueryResult result) =>
        new(
            Label: result.Label,
            Matched: result.Matched,
            A: Target(result.A),
            B: Target(result.B),
            Common: result.Common.Select(Resource).ToList(),
            AOnly: result.AOnly.Select(Resource).ToList(),
            BOnly: result.BOnly.Select(Resource).ToList(),
            Error: Error(result)
        );

    private static EffectsDiffTargetDto Target(EffectsDiffQueryService.TargetResolution target) =>
        new(
            Pattern: target.Pattern,
            Status: target.Status switch
            {
                EffectsDiffQueryService.TargetStatus.Matched => "matched",
                EffectsDiffQueryService.TargetStatus.NoMatch => "no-match",
                _ => "ambiguous",
            },
            ResolvedId: target.ResolvedId,
            Matches: target.Matches
        );

    private static EffectsDiffResourceDto Resource(EffectsDiffQueryService.ResourceSetItem item) => new(item.ResourceKey, item.Categories);

    private static string? Error(EffectsDiffQueryService.EffectsDiffQueryResult result)
    {
        if (result.Matched)
        {
            return null;
        }

        var failed = new[] { (Side: "a", Target: result.A), (Side: "b", Target: result.B) }
            .Where(x => x.Target.Status != EffectsDiffQueryService.TargetStatus.Matched)
            .Select(x =>
                x.Target.Status == EffectsDiffQueryService.TargetStatus.NoMatch
                    ? $"No symbol matches '{x.Target.Pattern}' ({x.Side})."
                    : $"'{x.Target.Pattern}' ({x.Side}) is ambiguous across {x.Target.Matches.Count} symbols."
            );
        return string.Join(" ", failed);
    }

    private static EffectsDiffResponseDto MissingResponse(string a, string b, string? error = null) =>
        new(
            Label: "",
            Matched: false,
            A: new EffectsDiffTargetDto(a, string.IsNullOrWhiteSpace(a) ? "missing" : "unknown", null, []),
            B: new EffectsDiffTargetDto(b, string.IsNullOrWhiteSpace(b) ? "missing" : "unknown", null, []),
            Common: [],
            AOnly: [],
            BOnly: [],
            Error: error ?? "Provide ?a=<pattern>&b=<pattern>."
        );

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
