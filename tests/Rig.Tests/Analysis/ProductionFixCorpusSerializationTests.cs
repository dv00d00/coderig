using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Analysis;

// Executable RCA corpus, FR-6 (RCA #1646 / !9288): a change stored a LanguageExt.Option<T> into the
// object store. The store's serializer CAN serialize Option but CANNOT deserialize it (None must be
// modeled as null), so the stored payload threw on read — a latent serialization-contract defect,
// invisible until the object is read back. The detector flags a store/serialize effect whose payload
// TYPE ARGUMENT matches a serializer-unsupported pattern — data-driven, and the patterns are PROJECT data
// (core-purity F5): which types a serializer cannot round-trip is a property of the analyzed stack's
// serializer + FP library, so the builtin `serializationHazard` section ships EMPTY and the ruleset below
// declares the rule, exactly as the MedDBase ruleset does. ANNOTATE-only: the effect is untouched, a
// unserializable_payload observation is ADDED. Kept in its own file (the SHIPPED object_store effect rule +
// generic observation machinery must fire this from nothing but rule data).
public sealed class ProductionFixCorpusSerializationTests
{
    // The project-side rule the analyzed codebase declares: this object store's serializer cannot round-trip
    // LanguageExt Option/Either. Core ships no such pattern — it cannot know another codebase's serializer.
    private const string ProjectRules = """
        {
          "observations": {
            "serializationHazard": [
              {
                "providers": ["object_store"],
                "unsupportedTypePatterns": ["LanguageExt.Option", "LanguageExt.Either"]
              }
            ]
          }
        }
        """;

    // BUG vs FIX in one snippet: Store_Bug serializes Option<int> (hazard); Store_Fix serializes a plain
    // serializable type (no hazard). Both hit the same object-store write boundary, so the ONLY difference
    // the assertions can latch onto is the payload type — exactly the FR-6 signal.
    [Test]
    public void _1646_option_payload_into_object_store_is_a_unserializable_payload_plain_payload_is_not()
    {
        var result = ProductionFixCorpus.Analyze(
            projectRulesJson: ProjectRules,
            source: ProductionFixCorpus.LanguageExtStub
                + """
                namespace Storage
                {
                    public sealed class PatientRecord
                    {
                        public int Id { get; set; }
                    }

                    // An object-store write surface (matched by the builtin declaringTypeNameEndsWith:["ObjectStore"]
                    // gate). Generic Store<T>(value) captures the payload type as the call's type argument.
                    public interface IObjectStore
                    {
                        void Store<T>(T value);
                    }

                    public sealed class RecordWriter
                    {
                        private readonly IObjectStore _store;
                        public RecordWriter(IObjectStore store) => _store = store;

                        // BUG: storing an Option<T> — the serializer cannot read it back (None != null on deser).
                        public void Persist_Bug(LanguageExt.Option<int> maybe) => _store.Store(maybe);

                        // FIX: storing a plain, round-trippable type.
                        public void Persist_Fix(PatientRecord record) => _store.Store(record);
                    }
                }
                """
        );

        // The buggy store of Option<int> carries a unserializable_payload observation, naming the matched
        // unsupported-type pattern (LanguageExt.Option) and the full payload type-arg in its detail.
        var hazards = result.SerializationHazardsIn("Persist_Bug");
        var hazard = hazards.ShouldHaveSingleItem();
        hazard.Context.ShouldBe("LanguageExt.Option");
        hazard.Detail.ShouldContain("LanguageExt.Option");
        hazard.Reason.ShouldBe("serializer_unsupported_payload_type");

        // The effect itself is NOT removed — annotate-only. The buggy store is still a derived object_store
        // write; only the observation distinguishes it.
        var bugEffect = result.EffectsIn("Persist_Bug").ShouldHaveSingleItem(); // exactly one object_store write effect on the buggy path
        bugEffect.Provider.ShouldBe("object_store");

        // The FIX (plain serializable payload) fires the SAME object_store write effect but carries NO
        // unserializable_payload — the signal keys off the payload type, not the boundary.
        result.SerializationHazardsIn("Persist_Fix").ShouldBeEmpty();
        var fixEffect = result.EffectsIn("Persist_Fix").ShouldHaveSingleItem();
        fixEffect.Provider.ShouldBe("object_store");
    }
}
