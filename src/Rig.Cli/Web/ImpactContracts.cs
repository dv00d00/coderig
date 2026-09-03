namespace Rig.Cli.Web;

// JSON contracts for /api/impact — a flat projection of ImpactCommand's internal diff. The MVP headline is
// the per-EP behavioral delta (PerEp): which entry points gained/lost which effects (and hazards) between two
// commits. Entry-point add/remove and the structural affected-EP COUNT ride along; the (large) structural
// affected-EP list is summarized as a count for now.

internal sealed record ImpactProvenanceDto(string? Branch, string? Commit, string Label);

internal sealed record ImpactEffectDto(string Provider, string Operation, string Resource, string Enclosing, string? File, int Line);

internal sealed record ImpactHazardDto(string Type, string Cell, string Enclosing, string Confidence, string? File, int Line);

// The AMPLIFICATION tier's per-EP delta entry (from Impact.EpAmplification): a provider:operation whose effect is
// now reached INSIDE an iteration context (or no longer is), with the reachable site count. A SEPARATE dto and
// separate lists rather than extra ImpactHazardDto entries: looped_effect is NOT a hazard (HazardKinds keeps the
// tiers disjoint), it has no cell/confidence dimension worth showing, and the client must label and count the two
// independently.
internal sealed record ImpactAmplificationDto(string Provider, string Operation, int Sites);

internal sealed record ImpactEpDeltaDto(
    string Kind,
    string Route,
    string Fqn, // queryable dotted name — round-trips into the tree view
    string? File,
    int Line,
    int BaseEffects,
    int BranchEffects,
    IReadOnlyList<ImpactEffectDto> Added,
    IReadOnlyList<ImpactEffectDto> Removed,
    IReadOnlyList<ImpactHazardDto> HazardsAdded,
    IReadOnlyList<ImpactHazardDto> HazardsRemoved,
    bool SharedMutationOnPath,
    // Amplification delta — defaulted empty so an existing client (and any test constructing this dto) is
    // unaffected; a client that knows about the tier renders them as a labelled "looped" group.
    IReadOnlyList<ImpactAmplificationDto>? AmplificationsAdded = null,
    IReadOnlyList<ImpactAmplificationDto>? AmplificationsRemoved = null
);

internal sealed record ImpactKindRouteDto(string Kind, string Route);

// Per-EP STRUCTURAL reach delta (from Impact.EpReachDelta): the methods newly reachable in head
// (Added — DocIDs, matched to head-tree node ids to tint newly-reached nodes) and no longer reachable
// (Removed — base-only, shown as a list since they're absent from the head tree).
internal sealed record ImpactReachNodeDto(string Id, string Name);

internal sealed record ImpactReachDto(IReadOnlyList<ImpactReachNodeDto> Added, IReadOnlyList<ImpactReachNodeDto> Removed);

internal sealed record ImpactResponseDto(
    ImpactProvenanceDto Base,
    ImpactProvenanceDto Head,
    IReadOnlyList<ImpactKindRouteDto> AddedEps,
    IReadOnlyList<ImpactKindRouteDto> RemovedEps,
    int AffectedEpCount, // structural: EPs whose reachable tree changed (behavioral subset is PerEp)
    // The EPs whose reachable EFFECT set changed, over the SELECTED set — the same number `rig impact` prints
    // in its header and in impact_summary's behavioral_eps. NOT PerEp.Count: PerEp also carries the EPs kept
    // for a hazard / amplification-tier / guard delta alone, which are shown but are not an effect change.
    int BehavioralEpCount,
    IReadOnlyList<ImpactEpDeltaDto> PerEp,
    // MANDATORY DISCLOSURE, same contract as TreeResponseDto.IntrinsicHidden: selection now runs SERVER-side,
    // so the default response withholds alloc/throw and the client cannot know that from the payload alone.
    // The count is the withheld effect ENTRIES across every EP (11,745 of 14,059 on the MedDBase pair) — a
    // view that hid 83% of its rows without saying so would read as "barely any change".
    int HiddenIntrinsic = 0
);
