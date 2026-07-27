using Rig.Cli.Impact;
using Shouldly;

namespace Rig.Tests.Cli;

// `impact --only/--exclude` plus the default hiding of language-intrinsic providers (alloc/throw), applied
// to the per-EP behavioral deltas. Motivation (2026-07-27, MedDBase MR !11025): a 33-file MR produced 68,261
// TSV rows of which ~80% were alloc:* churn from generated LLBLGen collection init, and a routine
// `Select-Object -First 300` capture therefore contained ZERO effect-delta rows — the diff read as "no
// behavioural change" while 190 EPs newly wrote a table. See
// docs/backlog/todo/impact-usability-parity-filter-and-alloc-noise.md.
public sealed class ImpactEffectFilterTests
{
    private static EpFootprintDelta Delta(
        string route,
        (string Provider, string Operation, string Resource, string Enclosing)[]? added = null,
        (string Provider, string Operation, string Resource, string Enclosing)[]? removed = null
    ) =>
        new(
            Kind: "http",
            Route: route,
            FilePath: $"/{route}.cs",
            Line: 1,
            BranchEffects: 1,
            BaseEffects: 1,
            Added: added ?? [],
            Removed: removed ?? [],
            Amplified: [],
            SharedMutationOnPath: false,
            HazardsAdded: [],
            HazardsRemoved: []
        );

    private static HashSet<string> Set(params string[] tokens) => new(tokens, StringComparer.OrdinalIgnoreCase);

    [Test]
    public void Alloc_and_throw_deltas_are_hidden_by_default_and_the_count_is_disclosed()
    {
        var perEp = new[]
        {
            Delta("noisy", added: [("alloc", "object", "Dictionary", "M:A.B"), ("throw", "raise", "InvalidOp", "M:A.B")]),
            Delta("real", added: [("llblgen", "write", "DocumentHistoryEntity", "M:C.D")]),
        };

        var (kept, hidden) = ImpactEngine.FilterPerEpEffects(perEp, only: Set(), exclude: Set(), includeIntrinsic: false);

        // The alloc/throw-only EP disappears entirely rather than leaving an `ep_delta … +0 -0 ~0` husk.
        kept.Select(d => d.Route).ShouldBe(["real"]);
        hidden.ShouldBe(2);
    }

    [Test]
    public void The_intrinsic_flag_restores_the_alloc_and_throw_deltas()
    {
        var perEp = new[] { Delta("noisy", added: [("alloc", "object", "Dictionary", "M:A.B")]) };

        var (kept, hidden) = ImpactEngine.FilterPerEpEffects(perEp, only: Set(), exclude: Set(), includeIntrinsic: true);

        kept.Select(d => d.Route).ShouldBe(["noisy"]);
        hidden.ShouldBe(0);
    }

    [Test]
    public void Only_keeps_just_the_named_providers_across_added_and_removed()
    {
        var perEp = new[]
        {
            Delta(
                "mixed",
                added: [("llblgen", "write", "Doc", "M:A.B"), ("audit", "write", "AuditLog", "M:A.B")],
                removed: [("audit", "write", "AuditLog", "M:C.D"), ("cache", "read", "Redis", "M:C.D")]
            ),
        };

        var (kept, hidden) = ImpactEngine.FilterPerEpEffects(perEp, only: Set("audit"), exclude: Set(), includeIntrinsic: false);

        kept.Single().Added.Select(x => x.Provider).ShouldBe(["audit"]);
        kept.Single().Removed.Select(x => x.Provider).ShouldBe(["audit"]);
        // Dropped by an EXPLICIT --only, so not reported as an intrinsic suppression the user can undo.
        hidden.ShouldBe(0);
    }

    [Test]
    public void The_review_parity_token_set_selects_exactly_the_write_and_permission_deltas()
    {
        // The token list a review actually wants. Deliberately NOT a built-in `@parity` alias: which effects
        // count as behaviour-relevant is repo DOMAIN policy, so it lives in the rig skill docs, not in the CLI.
        var perEp = new[]
        {
            Delta(
                "ep",
                added:
                [
                    ("llblgen", "write", "DocumentHistoryEntity", "M:A.B"),
                    ("permission", "assert", "rightsMask", "M:A.B"),
                    ("audit", "write", "AuditLog", "M:A.B"),
                    ("alloc", "boxing", "int", "M:A.B"),
                    ("llblgen", "read", "ProfileEntity", "M:A.B"),
                ]
            ),
        };

        var (kept, _) = ImpactEngine.FilterPerEpEffects(
            perEp,
            only: Set("permission", "llblgen:write", "llblgen:bulk_write", "llblgen:delete", "audit"),
            exclude: Set(),
            includeIntrinsic: false
        );

        kept.Single().Added.Select(x => $"{x.Provider}:{x.Operation}").ShouldBe(["llblgen:write", "permission:assert", "audit:write"]);
    }

    [Test]
    public void Naming_an_intrinsic_provider_in_only_implies_inclusion()
    {
        // `impact --only alloc` is a deliberate request for allocation churn (a perf lens), so it must not be
        // defeated by the default hiding.
        var perEp = new[] { Delta("ep", added: [("alloc", "object", "Dictionary", "M:A.B"), ("audit", "write", "AuditLog", "M:A.B")]) };

        var (kept, hidden) = ImpactEngine.FilterPerEpEffects(perEp, only: Set("alloc"), exclude: Set(), includeIntrinsic: false);

        kept.Single().Added.Select(x => x.Provider).ShouldBe(["alloc"]);
        hidden.ShouldBe(0);
    }

    [Test]
    public void The_gate_count_follows_the_filter_so_output_and_ci_verdict_cannot_disagree()
    {
        // An alloc-ONLY MR: with the default filter nothing behavioural survives, so the diff the reader sees
        // and the diff the gate counts are the same one. This IS a deliberate loosening of
        // --expect-no-effect-change; impact_summary's intrinsic_hidden column is the audit trail for it.
        var perEp = new[] { Delta("ep", added: [("alloc", "object", "Dictionary", "M:A.B")]) };

        var (kept, hidden) = ImpactEngine.FilterPerEpEffects(perEp, only: Set(), exclude: Set(), includeIntrinsic: false);
        var filtered = new ImpactDiff(Ep: null, AffectedEps: [], PerEp: kept);

        ImpactEngine.EffectChangedEpCount(filtered).ShouldBe(0);
        hidden.ShouldBe(1); // ...but never silently: the withheld count is disclosed.

        // With --intrinsic the same MR trips the gate again.
        var (keptAll, _) = ImpactEngine.FilterPerEpEffects(perEp, only: Set(), exclude: Set(), includeIntrinsic: true);
        ImpactEngine.EffectChangedEpCount(new ImpactDiff(Ep: null, AffectedEps: [], PerEp: keptAll)).ShouldBe(1);
    }

    [Test]
    public void An_unfiltered_default_run_still_returns_the_original_list_instance()
    {
        // Fast path: --intrinsic with no --only/--exclude must not copy the per-EP list at all.
        var perEp = new[] { Delta("ep", added: [("audit", "write", "AuditLog", "M:A.B")]) };

        var (kept, hidden) = ImpactEngine.FilterPerEpEffects(perEp, only: Set(), exclude: Set(), includeIntrinsic: true);

        kept.ShouldBeSameAs(perEp);
        hidden.ShouldBe(0);
    }
}
