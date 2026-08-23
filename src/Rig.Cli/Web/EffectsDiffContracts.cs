namespace Rig.Cli.Web;

// Stable JSON contract for /api/effects-diff. Target status is a string (matched | no-match | ambiguous)
// rather than an enum ordinal so agents can branch on it without coupling to CLR declaration order.
internal sealed record EffectsDiffTargetDto(
    string Pattern,
    string Status,
    string? ResolvedId,
    IReadOnlyList<string> Matches
);

internal sealed record EffectsDiffResourceDto(string ResourceKey, IReadOnlyList<string> Categories);

internal sealed record EffectsDiffResponseDto(
    string Label,
    bool Matched,
    EffectsDiffTargetDto A,
    EffectsDiffTargetDto B,
    IReadOnlyList<EffectsDiffResourceDto> Common,
    IReadOnlyList<EffectsDiffResourceDto> AOnly,
    IReadOnlyList<EffectsDiffResourceDto> BOnly,
    string? Error
);
