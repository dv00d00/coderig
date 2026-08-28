using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rig.Analysis;
using Rig.Analysis.Extraction;
using Rig.Analysis.Rules;
using Rig.Domain.Data;
using Rig.Domain.Functions;

namespace Rig.Tests.Fixtures;

// Executable RCA corpus harness. Each production fix from meddbase-analysis/docs/rca-corpus-meddbase.md is reproduced as a
// self-contained bug/fix snippet; this compiles it IN MEMORY (full framework references) and runs the REAL
// extract -> derive pipeline with the SHIPPED builtin rules, returning the derived effects so a corpus test
// can assert what the detectors fire on the BUG vs the FIX. No store, no playground restore — the snippet IS
// the fixture. The point is to replace prose claims ("rig would catch X") with a test that proves whether it
// does, and to pin the known GAPS (a bug the current detectors miss) as explicit, named expectations.
public static class ProductionFixCorpus
{
    // Reference every assembly the test runtime trusts (System.Collections.Concurrent, Immutable, etc.) so a
    // snippet can use real BCL concurrency types — only third-party idioms (LanguageExt.Atom) need a stub.
    private static readonly MetadataReference[] FrameworkReferences = ((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
        .ToArray();

    // A minimal LanguageExt.Atom<A> stub with the FQN the shared_state(Atom) rule gates on. Faithful to the
    // !10706 fix surface: Swap(func) is the atomic read-modify-write. Prepended to any snippet that uses it.
    // Option<A> is the FR-6 (RCA #1646) hazard payload: the object-store serializer can write it but cannot
    // read it back (None must be null), so a stored Option<T> is a latent serialization-contract defect.
    public const string LanguageExtStub = """
        namespace LanguageExt
        {
            public sealed class Atom<A>
            {
                private A _value;
                public Atom(A value) => _value = value;
                public A Value => _value;
                public A Swap(System.Func<A, A> f) { _value = f(_value); return _value; }
            }
            public static class Atom
            {
                public static Atom<A> Create<A>(A value) => new Atom<A>(value);
            }
            public struct Option<A>
            {
                public bool IsSome => false;
                // Single-shot: Map applies f to AT MOST ONE value, so an effect in the lambda is NOT
                // amplified. The enumerating-method gate must exclude it — a LanguageExt-heavy codebase
                // calls Map/Match/Bind everywhere, so admitting them would swamp n_plus_1 with noise.
                public Option<B> Map<B>(System.Func<A, B> f) => default;
            }
        }
        """;

    public sealed record CorpusResult(IReadOnlyList<DerivedEffect> Effects)
    {
        // Every effect whose enclosing method DocID contains the marker (a method name distinguishes the bug
        // variant from the fix variant in the same snippet).
        public IReadOnlyList<DerivedEffect> EffectsIn(string enclosingMarker) =>
            Effects.Where(e => (e.EnclosingSymbolId ?? "").Contains(enclosingMarker, StringComparison.Ordinal)).ToList();

        public IReadOnlyList<DerivedEffect> SharedStateMutationsIn(string enclosingMarker) =>
            EffectsIn(enclosingMarker).Where(e => e.Provider == "shared_state" && e.Operation == "mutate").ToList();

        // The FR-1 read-arm counterpart of SharedStateMutationsIn: every shared_state:read effect enclosed by
        // the marker method (a read of a STATIC field/auto-property — the "check" of a shared cell).
        public IReadOnlyList<DerivedEffect> SharedStateReadsIn(string enclosingMarker) =>
            EffectsIn(enclosingMarker).Where(e => e.Provider == "shared_state" && e.Operation == "read").ToList();

        public bool HasGuardEffectIn(string enclosingMarker) => EffectsIn(enclosingMarker).Any(e => e.Provider is "lock" or "async_lock");

        // Every unserializable_payload observation attached to an effect enclosed by the marker method.
        public IReadOnlyList<EffectObservationInfo> SerializationHazardsIn(string enclosingMarker) =>
            EffectsIn(enclosingMarker).SelectMany(e => e.Observations ?? []).Where(o => o.Type == "unserializable_payload").ToList();

        // Every observation of the given type attached to an effect enclosed by the marker method (e.g.
        // "n_plus_1" / "looped_effect"). The general form behind SerializationHazardsIn.
        public IReadOnlyList<EffectObservationInfo> ObservationsIn(string enclosingMarker, string observationType) =>
            EffectsIn(enclosingMarker).SelectMany(e => e.Observations ?? []).Where(o => o.Type == observationType).ToList();

        // Every race_window hazard observation attached to a (mutate) effect enclosed by the marker method —
        // the read-before-write / TOCTOU finding. Sugar over ObservationsIn for the race_window corpus tests.
        public IReadOnlyList<EffectObservationInfo> RaceWindowsIn(string enclosingMarker) =>
            ObservationsIn(enclosingMarker, FactHazardDeriver.RaceWindowType);

        // The lazy-init / do-once split of race_window: every lazy_init_race observation (low confidence,
        // heuristic) on a mutate effect enclosed by the marker method. Sugar over ObservationsIn.
        public IReadOnlyList<EffectObservationInfo> LazyInitRacesIn(string enclosingMarker) =>
            ObservationsIn(enclosingMarker, FactHazardDeriver.LazyInitRaceType);

        // FR-8: every dual_write hazard observation on an effect enclosed by the marker method — the
        // ≥2-distinct-durable-systems-in-one-method finding. Sugar over ObservationsIn.
        public IReadOnlyList<EffectObservationInfo> DualWritesIn(string enclosingMarker) =>
            ObservationsIn(enclosingMarker, FactHazardDeriver.DualWriteType);

        // FR-2: every thread_local_context observation on an effect enclosed by the marker method — the
        // [ThreadStatic] reroute of an RMW (thread-confined ⇒ not a race, but a context-propagation candidate).
        public IReadOnlyList<EffectObservationInfo> ThreadLocalContextsIn(string enclosingMarker) =>
            ObservationsIn(enclosingMarker, FactHazardDeriver.ThreadLocalContextType);
    }

    // `projectRulesJson` (optional) is a rig.rules.json body layered OVER the shipped builtin rules, exactly
    // as an analyzed codebase's own ruleset is. Needed for detectors whose vocabulary is legitimately
    // project-side — e.g. which payload types a serializer cannot round-trip (core-purity F5: the builtin
    // serializationHazard section ships EMPTY, because the answer is a property of the analyzed stack).
    public static CorpusResult Analyze(string source, string? projectRulesJson = null)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "Corpus.cs");
        var compilation = CSharpCompilation.Create(
            assemblyName: "Corpus",
            syntaxTrees: [tree],
            references: FrameworkReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        var model = compilation.GetSemanticModel(tree);
        var extraction = FactExtractor.Extract(
            new SourceModel(ProjectName: "Corpus", FilePath: "Corpus.cs", Tree: tree, Root: tree.GetRoot(), SemanticModel: model),
            new SymbolStringCache()
        );

        var result = new AnalysisResult(
            SolutionPath: "Corpus",
            SourceFiles: [],
            DiRegistrations: [],
            Symbols: extraction.Symbols,
            References: extraction.References,
            TypeRelations: extraction.TypeRelations,
            DispatchFacts: extraction.Dispatch
        );

        var rules = LoadRules(projectRulesJson);
        var epData = FactProjection.EntryPointData(result);
        var effects = FactEffectDeriver.Derive(
            FactProjection.Invocations(result),
            rules.Effects,
            providerFilter: null,
            baseEdges: epData.BaseEdges,
            ctorRefs: epData.CtorRefs,
            observationRules: rules.Observations,
            throwRefs: FactProjection.ThrowRefs(result),
            staticFieldWriteRefs: StaticFieldAccessRefs(result: result, refKind: RefKinds.Write),
            staticFieldReadRefs: StaticFieldAccessRefs(result: result, refKind: RefKinds.Read)
        );
        // Hazard post-pass: the race_window read-before-write matcher (same enclosing method, same cell).
        // The whole-store `derive` path runs this too (EffectDerivation.DeriveEffects deriveHazards:true);
        // the harness mirrors it so the corpus measures the SHIPPED behavior end to end — including the
        // [ThreadStatic] reroute (thread-confined RMW → thread_local_context), fed the same way the shipped
        // path is (Reads.LoadThreadStaticFieldIdsAsync): the field DocIDs decorated with [ThreadStatic].
        effects = FactHazardDeriver.DeriveRaceWindows(effects, ThreadStaticCells(result));
        // FR-8 dual_write post-pass: ≥2 distinct durable systems written in one method. Mirrors the shipped
        // derive path (EffectDerivation.DeriveEffects runs both hazard passes when deriveHazards:true), map
        // included — the system-class table is rule data (`dualWrite.systemClassMap`), so the corpus reads it
        // from the SHIPPED builtin rules exactly as production does.
        effects = FactHazardDeriver.DeriveDualWrites(effects, rules.DualWrite?.SystemClassMap);
        return new CorpusResult(effects);
    }

    // The shipped builtin rules, plus an OPTIONAL project ruleset layered on top: load rooted at a temp dir so
    // a dev's own colocated/global rules can never leak in — the corpus measures what we SHIP (plus exactly the
    // project rules a test declares), not a machine's local state.
    private static Rig.Domain.Data.RuleSet LoadRules(string? projectRulesJson)
    {
        var tempDir = Directory.CreateTempSubdirectory("rig-corpus-rules-").FullName;
        try
        {
            if (projectRulesJson is not null)
            {
                File.WriteAllText(Path.Combine(tempDir, "rig.rules.json"), projectRulesJson);
            }

            return RuleSetLoader.Load(tempDir);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch (IOException) { }
        }
    }

    // Mirror of Reads.LoadThreadStaticFieldIdsAsync over the in-memory facts: the field DocIDs carrying a
    // [ThreadStatic] attribute, recovered from the ctor reference the attribute application emits (enclosing =
    // the field, target = the attribute ctor). Feeds the race_window [ThreadStatic] reroute.
    private static IReadOnlySet<string> ThreadStaticCells(AnalysisResult result) =>
        (result.References ?? [])
            .Where(r =>
                r.RefKind == RefKinds.Ctor && r.EnclosingSymbolId is not null && r.TargetSymbolId == "M:System.ThreadStaticAttribute.#ctor"
            )
            .Select(r => r.EnclosingSymbolId!)
            .ToHashSet(StringComparer.Ordinal);

    // Mirror of Reads.LoadStaticFieldAccessRefsAsync over the in-memory facts: access refs (read OR write,
    // selected by refKind) whose target is a STATIC field/auto-property slot (gated via the symbol's
    // modifiers), deduped by site. This is the FR-1(b) write / FR-1 read input population — and it only works
    // because the field-emission fix now emits class field symbols.
    //
    // The READ arm additionally excludes `readonly` static targets (immutable cell ⇒ cannot be a TOCTOU
    // "check" ⇒ pure noise), mirroring the shipped path's `excludeReadonly` gate. The WRITE arm keeps them.
    private static IReadOnlyList<FactFieldAccess> StaticFieldAccessRefs(AnalysisResult result, string refKind)
    {
        var excludeReadonly = refKind == RefKinds.Read;
        var staticSlots = (result.Symbols ?? [])
            .Where(s =>
                s.Modifiers.Contains("static", StringComparison.Ordinal)
                && (!excludeReadonly || !s.Modifiers.Contains("readonly", StringComparison.Ordinal))
            )
            .Select(s => s.SymbolId)
            .ToHashSet(StringComparer.Ordinal);

        return (result.References ?? [])
            .Where(r => r.RefKind == refKind && r.TargetInSource && r.EnclosingSymbolId != null && staticSlots.Contains(r.TargetSymbolId))
            .GroupBy(r => (r.FilePath, r.Line, r.TargetSymbolId))
            .Select(g => g.First())
            .Select(r => new FactFieldAccess(
                Target: r.TargetSymbolId,
                Enclosing: r.EnclosingSymbolId,
                FilePath: r.FilePath,
                Line: r.Line,
                LoopKind: r.EnclosingLoopKind,
                LoopDetail: r.EnclosingLoopDetail,
                EnclosingInvocations: r.EnclosingInvocations,
                CatchTypes: r.EnclosingCatchTypes,
                EnclosingScopes: r.EnclosingScopes
            ))
            .ToList();
    }
}
