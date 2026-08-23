namespace Rig.Analysis.Inventory;

// What the design-time-build cache holds for a project (the sidecar payload): the input fingerprint the
// build output was produced under, plus that output. Admitted defaults false and CandidateId defaults null,
// so sidecars written before exact Roslyn compilation-health admission fail closed after deserialization. Kept
// separate from "does it still match" so the match is a pure decision, not a side effect of loading.
internal sealed record StoredBuild(string Fingerprint, ProjectBuildInfo Info, bool Admitted = false, string? CandidateId = null);

// PURE CORE of the build cache: given the freshly-computed input fingerprint and whatever the sidecar holds
// (if anything), decide HIT (replay the cached build output, skip the design-time build) or MISS (rebuild,
// then store under the new fingerprint). No IO, no clock — the correctness-bearing choice, isolated so it is
// exhaustively unit-testable and reused verbatim by --verify-build-cache. The imperative shell (Gather/Load/
// build/Store in SolutionSourceLoader) only feeds it inputs and acts on its verdict.
internal abstract record BuildCacheDecision
{
    private BuildCacheDecision() { }

    // An admitted sidecar's fingerprint matched — replay Info without building.
    internal sealed record Hit(ProjectBuildInfo Info) : BuildCacheDecision;

    // No admitted matching sidecar — build and stage under Fingerprint for Roslyn health admission.
    internal sealed record Miss(string Fingerprint) : BuildCacheDecision;

    // A sidecar HITS only when Roslyn admitted an identified candidate after a zero-error project
    // compilation AND its stored fingerprint equals the current one. Legacy/candidate, absent, or stale
    // sidecars are misses carrying the current fingerprint for a newly staged candidate.
    public static BuildCacheDecision Decide(string currentFingerprint, StoredBuild? stored) =>
        stored is { Admitted: true, CandidateId: not null }
        && string.Equals(stored.Fingerprint, currentFingerprint, StringComparison.Ordinal)
            ? new Hit(stored.Info)
            : new Miss(currentFingerprint);
}
