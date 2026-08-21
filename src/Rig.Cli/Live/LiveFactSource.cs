using Rig.Cli.Effects;
using Rig.Domain.Data;
using Rig.Domain.Functions;

namespace Rig.Cli.Live;

// The query-ready bundle over ONE generation of live facts — the in-memory counterpart of what a query
// command loads out of a .rig store before it can answer anything. Given the AnalysisResult a resident index
// (`rig watch` / ResidentIndex) currently holds plus the effective RuleSet, it exposes the three expensive
// derived artifacts every query path needs: the fully shaped call graph, the entry-point fact bundle, and the
// whole-store hazard-augmented effect set.
//
// Every artifact is projected through LiveReads, the in-memory twin of the query-side `Reads` surface, so a
// live-served answer is by construction the same answer a cold index would give. LiveFactSourceParityTests
// asserts that against a real saved store, artifact by artifact.
//
// It is a plain IMMUTABLE value object over one fact generation: no threads, no timers, no file watching, no
// invalidation. A new generation of facts means a NEW LiveFactSource — that is the whole invalidation model,
// and it is what makes each `Lazy<T>` below safe to memoize forever. Wiring this into `rig watch` (swapping
// the current generation in on each rebuild, and routing the query commands at it) is a separate slice.
//
// Thread-safety: the Lazy<T>s use the default ExecutionAndPublication mode, so a single-writer/multi-reader
// host can hand the same instance to concurrent readers and each artifact is still computed at most once.
internal sealed class LiveFactSource
{
    private readonly Lazy<FactGraphData> _shapedGraph;
    private readonly Lazy<FactEntryPointDeriver.FactEntryPointData> _epData;
    private readonly Lazy<IReadOnlyList<DerivedEffect>> _hazardEffects;

    public LiveFactSource(AnalysisResult facts, RuleSet rules)
    {
        Facts = facts;
        Rules = rules;
        _shapedGraph = new Lazy<FactGraphData>(() => LiveReads.ShapedGraph(facts, rules));
        _epData = new Lazy<FactEntryPointDeriver.FactEntryPointData>(() => LiveReads.FactEntryPointData(facts));
        _hazardEffects = new Lazy<IReadOnlyList<DerivedEffect>>(() => DeriveHazardEffects(facts, rules, EpData));
    }

    // The fact generation this source serves. Immutable — a rebuild produces a new LiveFactSource.
    public AnalysisResult Facts { get; }

    public RuleSet Rules { get; }

    // Mirrors Reads.LoadShapedGraphAsync.
    public FactGraphData ShapedGraph => _shapedGraph.Value;

    // Mirrors Reads.LoadFactEntryPointDataAsync.
    public FactEntryPointDeriver.FactEntryPointData EpData => _epData.Value;

    // Mirrors EffectDerivation.DeriveHazardEffectsAsync — the whole-store hazard-augmented effect set, i.e.
    // exactly what `derive` computes.
    public IReadOnlyList<DerivedEffect> HazardEffects => _hazardEffects.Value;

    // The in-memory equivalent of EffectDerivation.DeriveHazardEffectsAsync: the SAME EffectDerivation.
    // DeriveEffects call with the SAME arguments, each feed sourced from LiveReads instead of Reads. Kept
    // argument-for-argument aligned with that method — including `deriveHazards: true` and the `async`-modifier
    // filter that feeds sync_over_async — so the live effect set cannot drift from the store one. The `gate`
    // stays at its default (the FR-1 read-arm write-pairing gate ON), matching `derive`'s default; a
    // `--no-gate` live path would thread it through here.
    private static IReadOnlyList<DerivedEffect> DeriveHazardEffects(
        AnalysisResult facts,
        RuleSet rules,
        FactEntryPointDeriver.FactEntryPointData epData
    )
    {
        var (staticFieldWriteRefs, staticFieldReadRefs) = LiveReads.StaticFieldAccessRefsByKind(facts);
        var asyncMethodIds = LiveReads
            .DeadCodeMethods(facts)
            .Where(m => m.Modifiers.Split(' ').Contains("async"))
            .Select(m => m.SymbolId)
            .ToHashSet(StringComparer.Ordinal);
        return EffectDerivation.DeriveEffects(
            effectRules: rules.Effects,
            observationRules: rules.Observations,
            invocations: LiveReads.InvocationRefs(facts),
            baseEdges: epData.BaseEdges,
            ctorRefs: epData.CtorRefs,
            throwRefs: LiveReads.ThrowRefs(facts),
            staticFieldWriteRefs: staticFieldWriteRefs,
            staticFieldReadRefs: staticFieldReadRefs,
            deriveHazards: true,
            threadStaticCells: LiveReads.ThreadStaticFieldIds(facts),
            volatileCells: LiveReads.VolatileFieldIds(facts),
            asyncMethodIds: asyncMethodIds,
            // AllocationFacts needs no LiveReads twin: Reads.LoadAllocationFactsAsync (whole-store) applies no
            // filter and no dedup, so the extracted list already IS its return value.
            allocationFacts: facts.AllocationFacts ?? []
        );
    }
}
