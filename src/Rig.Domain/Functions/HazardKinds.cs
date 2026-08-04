namespace Rig.Domain.Functions;

// The catalog of DISPLAYED FINDING types, in TWO TIERS. This is the single place that answers "is this a
// finding, and which kind?" so the derive Hazards/Amplification views, the generic-observations exclusion,
// the tsv split, and the impact deltas don't each hard-code a list.
//
// TIER 1 — HAZARDS (`All` / IsHazard): higher-order findings that match PATTERNS over effects (a
// read-modify-write window, an N+1 read in a loop, …). Most are EffectObservationInfo notes on effects; the
// graph-tier ones (event_cycle / cache_coherence / static_init_capture) are NOT effect-attached — they are
// properties of the call-graph topology or the static-field universe, derived over the graph and folded into
// the same Hazards view as extra sources.
//
// TIER 2 — AMPLIFICATION (`Amplification` / IsAmplification): looped_effect. Promoted (2026-08) out of the
// anonymous "Observations on effects" count line into its own displayed, provider-agnostic section — a looped
// `http:POST` is as visible as a looped `llblgen:read`. On by DEFAULT with an opt-out (`--no-amplification`).
//
// Why the tiers are separate — FACT vs JUDGMENT. This deliberately REVERSES the display half of the earlier
// "looped_effect is a context fact, not a hazard" call that this header used to record: looped_effect stays
// NOT a hazard, but it becomes a FINDING.
//   * `looped_effect` is a structural FACT: the effect is lexically inside an iteration context, soundly,
//     with no guess. A fact can ship on-by-default as INVENTORY — "here is every effect that repeats" — and
//     needs no false-positive calibration, because there is nothing to be wrong about.
//   * `n_plus_1` is a JUDGMENT layered on that fact: does the key VARY per iteration, and does it matter?
//     Judgments are wrong sometimes, so they must be FP-calibrated before going on by default.
// Keeping them in separate sets is what lets amplification ship now without disturbing the hazard surface:
// `All` and `IsHazard` are semantically UNCHANGED, so every existing hazard count, golden file, tsv `hazard`
// row, `rig impact` delta, and `--expect-no-effect-change` gate stays byte-identical. IsFinding is the union,
// for the ONE consumer that needs "don't also show this as an anonymous observation".
//
// The type strings are owned by their derivers (race_window / lazy_init_race by FactHazardDeriver; n_plus_1 /
// unserializable_payload / looped_effect by FactObservationDeriver) and re-stated here as the closed sets —
// this catalog enumerates, it does not detect.
public static class HazardKinds
{
    // race_window / lazy_init_race come from FactHazardDeriver; reuse its constants so the catalog can never
    // drift from the emitter.
    public const string NPlusOne = "n_plus_1";
    public const string UnserializablePayload = "unserializable_payload";

    // sync_over_async comes from FactHazardDeriver; reuse its constant so the catalog can never drift from
    // the emitter (same convention as race_window/lazy_init_race/thread_local_context/dual_write above).
    public const string SyncOverAsync = FactHazardDeriver.SyncOverAsyncType;

    // cache_coherence is the cache-specific INSTANCE of the generic effect-correlation deriver
    // (FactCorrelationDeriver): a bulk_write anchor whose forward closure lacks a same-key cache:invalidate
    // companion. It has no single owning deriver class anymore (it is wired in DeriveCommand), so the type
    // string lives HERE — referenced by both the All set below and the DeriveCommand finding mapping.
    public const string CacheCoherence = "cache_coherence";

    // static_init_capture: a config / mutable source (Settings.* / feature flag) READ into a STATIC FIELD
    // INITIALIZER, frozen at CLR type-init and never re-evaluated ("wrong until app restart"). Derived by
    // FactStaticInitCaptureDeriver and wired in DeriveCommand (like cache_coherence, it has no effect-attached
    // observation), so the type string lives HERE — referenced by both the All set below and the finding mapping.
    public const string StaticInitCapture = "static_init_capture";

    // The closed set of hazard finding types. Membership test for "promote this observation into the Hazards
    // view (and drop it from the generic Observations block)".
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        FactHazardDeriver.RaceWindowType,
        FactHazardDeriver.LazyInitRaceType,
        FactHazardDeriver.ThreadLocalContextType,
        FactHazardDeriver.DualWriteType,
        SyncOverAsync,
        FactCycleDeriver.EventCycleType,
        CacheCoherence,
        StaticInitCapture,
        NPlusOne,
        UnserializablePayload,
    };

    // True when an observation TYPE is a hazard finding (tier 1). Deliberately does NOT include the
    // amplification tier: every hazard-keyed surface (the Hazards view, the tsv `hazard` rows, the `rig impact`
    // per-EP hazard deltas) must keep its exact pre-amplification membership.
    public static bool IsHazard(string type) => All.Contains(type);

    // looped_effect — an effect lexically inside an iteration context. Emitted by FactObservationDeriver;
    // reuse its constant so this catalog can never drift from the emitter.
    public const string LoopedEffect = FactObservationDeriver.LoopedEffectType;

    // The closed set of AMPLIFICATION finding types (tier 2): structural facts that repeat an effect. A set of
    // one today, but a set on purpose — parallel_fanout is the obvious next member (a fanout wrapper amplifies
    // the same way a loop does), and the display machinery is already generic over the set.
    public static readonly IReadOnlySet<string> Amplification = new HashSet<string>(StringComparer.Ordinal) { LoopedEffect };

    // True when an observation TYPE is an amplification finding (tier 2).
    public static bool IsAmplification(string type) => Amplification.Contains(type);

    // TIER 3 — CROSS-METHOD N+1 (`CrossMethodNPlusOne` / IsCrossMethod): a read reachable at or beneath a call
    // issued once per loop element — the amplification the lexical tiers structurally cannot see (loop and read
    // live in different frames). Promoted (2026-08) from a machine-only dataset to a DISPLAYED finding at the
    // ANCHOR grain (one row per looped call site, nearest witness as evidence) after a stratified hand audit of
    // the post-v5 surface measured 93% TP+TP-weak precision. Kept OUT of `All`/IsHazard for the same reason
    // amplification is: every pre-existing hazard-keyed surface (tsv `hazard` rows, `rig impact` hazard deltas)
    // keeps its exact membership; this tier has its own section, marks, and opt-out.
    public const string CrossMethodNPlusOne = "n_plus_1_cross_method";

    // True when an observation TYPE is the cross-method N+1 finding (tier 3).
    public static bool IsCrossMethod(string type) => string.Equals(type, CrossMethodNPlusOne, StringComparison.Ordinal);

    // True when an observation TYPE is DISPLAYED as a finding in any tier. The one question the generic
    // "Observations on effects" block asks: a type that has its own section must not ALSO appear there as an
    // anonymous count (double-counting).
    public static bool IsFinding(string type) => IsHazard(type) || IsAmplification(type) || IsCrossMethod(type);
}
