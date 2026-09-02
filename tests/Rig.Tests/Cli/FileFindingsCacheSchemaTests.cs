using System.Security.Cryptography;
using System.Text;
using Rig.Cli.Caching;
using Rig.Cli.Commands;
using Rig.Cli.Effects;
using Rig.Cli.Services;
using Shouldly;

namespace Rig.Tests.Cli;

// The /api/file-findings disk cache: its key material, its invalidation axes, and its codec round-trip.
//
// The key is the interesting part. This artifact is derived from THREE separately-gated inputs — the
// hazard-augmented effect set (HazardEffectsSchema), the graph-tier findings (GraphHazSchema) and the
// classification into displayed findings (FindingViewSchema) — plus the tier-3 cross-method anchor
// derivation, which has no constant of its own. So the version slot is the WHOLE DerivationSchemaToken:
// any one of those bumping must miss here, else the per-file blob keeps serving pre-bump findings off
// freshly-recomputed inputs. These tests are what stops that slot being "simplified" to one constant.
public sealed class FileFindingsCacheSchemaTests
{
    private const string Path = "/repo/A.cs";

    private static string Sha256Hex(string material) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));

    [Test]
    public void The_file_findings_key_material_is_pinned_component_by_component()
    {
        // The material FORM, recomputed here: catches a reordered axis, a dropped axis or a changed delimiter
        // without breaking on an unrelated schema bump.
        QueryCacheKeys
            .FileFindingsCacheKey("store", "rules", Path)
            .ShouldBe(Sha256Hex($"filefindings|v{QueryCacheKeys.DerivationSchemaToken()}|store|rules|{Path}"));

        // …and the literal for TODAY's schema token, mirroring FileEffectsCacheSchemaTests. A `*Schema` bump
        // ANYWHERE moves both lines below, and that is the point: the bump IS the signal that flushes every
        // warm findings blob, on disk and (via /api/meta's derivationVersion) in the browser. Updating these
        // two lines is the deliberate edit that records it.
        QueryCacheKeys.DerivationSchemaToken().ShouldBe("2.8.4.5.8.1.1.2.3.3.6");
        QueryCacheKeys
            .FileFindingsCacheKey("store", "rules", Path)
            .ShouldBe("53F64C7E887CB36E6D034745B4EF608F1B81EF9C7BB8952693F97C400B9406AA");
    }

    [Test]
    public void The_version_slot_is_the_composite_token_not_a_single_schema_constant()
    {
        // The failure this pins: someone "tidies" the slot to `v{FindingViewSchema}`, and thereafter a
        // HazardEffectsSchema or GraphHazSchema bump recomputes the INPUTS while this cache keeps serving
        // findings derived from the pre-bump ones. Asserted as inequality against every single-constant
        // spelling, because that is the shape the mistake takes.
        var actual = QueryCacheKeys.FileFindingsCacheKey("store", "rules", Path);
        int[] singles = [QueryCacheKeys.FindingViewSchema, QueryCacheKeys.HazardEffectsSchema, QueryCacheKeys.GraphHazSchema];
        foreach (var single in singles)
        {
            actual.ShouldNotBe(Sha256Hex($"filefindings|v{single}|store|rules|{Path}"));
        }
    }

    [Test]
    public void Every_file_findings_key_input_is_load_bearing()
    {
        var baseline = QueryCacheKeys.FileFindingsCacheKey("store", "rules", Path);

        // Same inputs -> the same slot (the HIT that makes the endpoint fast).
        QueryCacheKeys.FileFindingsCacheKey("store", "rules", Path).ShouldBe(baseline);

        // Each input changed INDIVIDUALLY -> a MISS. A reindex moves the store identity, a rule edit moves the
        // fingerprint, and a second file must never be served this file's findings.
        QueryCacheKeys.FileFindingsCacheKey("store-2", "rules", Path).ShouldNotBe(baseline);
        QueryCacheKeys.FileFindingsCacheKey("store", "rules-2", Path).ShouldNotBe(baseline);
        QueryCacheKeys.FileFindingsCacheKey("store", "rules", "/repo/B.cs").ShouldNotBe(baseline);
    }

    // Both keys are (store, rules, file) over the same store's cache.db, so only the namespace prefix keeps
    // the badge projection and the findings from decoding each other's blob.
    [Test]
    public void The_findings_namespace_is_distinct_from_the_file_effects_namespace()
    {
        QueryCacheKeys.FileFindingsCacheKey("store", "rules", Path).ShouldNotBe(QueryCacheKeys.FileEffectsCacheKey("store", "rules", Path));
    }

    // Provider/Operation are the amplification tier's GROUPING cell, and the sibling graph-tier codec drops
    // them (its findings never carry any). Dropping them here would decode every looped_effect row with an
    // empty cell — invisible in review, wrong in the overlay.
    [Test]
    public void The_codec_round_trips_every_tier_including_the_amplification_provider()
    {
        var findings = new FileFindingsQueryService.Findings(
            [
                new DeriveCommand.HazardFinding(
                    Type: "n_plus_1",
                    Confidence: "high",
                    Reason: "looped_read_with_varying_key",
                    Context: "reviewer",
                    Detail: "reviewer in newReviewers",
                    Enclosing: "M:Demo.Orders.Load(System.Int32)",
                    FilePath: Path,
                    Line: 671
                ),
            ],
            [
                new DeriveCommand.HazardFinding(
                    Type: "looped_effect",
                    Confidence: "high",
                    Reason: "effect_inside_loop",
                    Context: "foreach",
                    Detail: "reviewer in newReviewers",
                    Enclosing: "M:Demo.Orders.Load(System.Int32)",
                    FilePath: Path,
                    Line: 671,
                    Provider: "entity_cache",
                    Operation: "read"
                ),
            ],
            [
                new CrossMethodAmplificationDataset.AnchorFinding(
                    Caller: "M:Demo.Orders.Load(System.Int32)",
                    FilePath: Path,
                    Line: 232,
                    IterationKind: "query",
                    WitnessProvider: "entity_cache",
                    WitnessOperation: "read",
                    WitnessResource: "Profile",
                    WitnessDepth: 6
                ),
            ],
            CrossMethodDerived: true
        );

        var decoded = FileFindingsCodec.Decode(FileFindingsCodec.Encode(findings)).ShouldNotBeNull();

        decoded.Hazards.ShouldBe(findings.Hazards);
        decoded.Amplifications.ShouldBe(findings.Amplifications);
        decoded.Anchors.ShouldBe(findings.Anchors);
        decoded.CrossMethodDerived.ShouldBeTrue();
        // The tier-3 confidence is DERIVED from the witness depth, so it must survive without being stored.
        decoded.Anchors.ShouldHaveSingleItem().Confidence.ShouldBe("low");
    }

    // A corrupt or foreign blob must be a MISS (recompute), never a 400 on the endpoint.
    [Test]
    public void A_corrupt_blob_decodes_as_a_miss()
    {
        FileFindingsCodec.Decode([1, 2, 3, 4]).ShouldBeNull();
    }
}
