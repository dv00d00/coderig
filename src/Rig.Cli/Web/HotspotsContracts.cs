namespace Rig.Cli.Web;

// Stable transparent /api/hotspots contract. Every metric is carried independently; there is no blended
// score whose weighting a client would have to reverse-engineer.
internal sealed record HotspotRowDto(
    string Id,
    string Name,
    string File,
    int Line,
    int Lines,
    int CallerMethods,
    int IncomingCallSites,
    int CalleeMethods,
    int OutgoingCallSites,
    int EffectSites,
    int EffectKinds,
    double EffectSitesPer100Lines,
    int HazardSites,
    int HazardKinds,
    int AmplificationSites,
    int ResidualDispatchFan,
    int DispatchIncomingEdges,
    long DispatchRank,
    bool IsGenerated,
    bool IsLambda
);

internal sealed record HotspotsResponseDto(
    string Sort,
    int Top,
    bool NoLambdas,
    bool Intrinsic,
    int HiddenIntrinsic,
    IReadOnlyList<HotspotRowDto> Rows
);

internal sealed record HotspotsErrorDto(string Error, IReadOnlyList<string> AllowedSorts, int MinTop, int MaxTop);
