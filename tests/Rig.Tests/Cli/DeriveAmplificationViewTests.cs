using Rig.Analysis.Rules;
using Rig.Cli.Commands;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Cli;

// The AMPLIFICATION finding tier: looped_effect promoted from an anonymous count in the generic "Observations on
// effects" block to a first-class DISPLAYED finding with its own section, its own tsv row type, and its own
// provider:operation breakdown — on by default, `--no-amplification` off.
//
// What these pin:
//   1. The TIER INVARIANT — looped_effect is NOT a hazard (every hazard-keyed surface must be unchanged) but IS a
//      finding. This is the load-bearing distinction: looped_effect is a structural FACT (the effect is lexically
//      inside an iteration context, soundly), whereas n_plus_1 is a JUDGMENT about the key varying. Facts ship
//      on-by-default as inventory; judgments need FP calibration first.
//   2. PROVIDER-AGNOSTIC visibility — a looped NON-READ effect (http:POST, an outbound write) surfaces, not just
//      the reads n_plus_1 gates on. This is the headline requirement.
//   3. The rules-declared DISPLAY SCOPE (`observations.amplification`) — network-crossing providers are in by
//      default, local ones (shared_state / entity_cache / lock) are out and stay counted in the generic block, so
//      narrowing the scope is lossless.
//   4. --no-amplification restores the pre-tier behaviour exactly.
//   5. No DOUBLE-COUNTING across the Hazards and Amplification sections.
public sealed class DeriveAmplificationViewTests
{
    // --- 1. the tier invariant -------------------------------------------------------------------------------

    [Test]
    public void looped_effect_is_not_a_hazard_but_is_a_finding()
    {
        // NOT a hazard: the Hazards view, the tsv `hazard` rows and the impact hazard deltas are all keyed on
        // IsHazard, and promoting the display tier must not move any of them.
        HazardKinds.IsHazard(HazardKinds.LoopedEffect).ShouldBeFalse();
        HazardKinds.All.ShouldNotContain(HazardKinds.LoopedEffect);
        // IS an amplification finding, and therefore a finding.
        HazardKinds.IsAmplification(HazardKinds.LoopedEffect).ShouldBeTrue();
        HazardKinds.IsFinding(HazardKinds.LoopedEffect).ShouldBeTrue();
        // The tiers are disjoint: a hazard is never an amplification.
        foreach (var hazard in HazardKinds.All)
        {
            HazardKinds.IsAmplification(hazard).ShouldBeFalse();
            HazardKinds.IsFinding(hazard).ShouldBeTrue();
        }
    }

    [Test]
    public void The_catalog_reuses_the_emitters_type_string()
    {
        // The catalog enumerates, the deriver detects — so the constant must be the EMITTER's, not a copy.
        HazardKinds.LoopedEffect.ShouldBe(FactObservationDeriver.LoopedEffectType);
        HazardKinds.LoopedEffect.ShouldBe("looped_effect");
    }

    // --- 2. provider-agnostic: a looped NON-READ effect is a finding ----------------------------------------

    // The headline requirement, measured end-to-end through the REAL extract→derive pipeline with the SHIPPED
    // rules: an `http:POST` inside a foreach. n_plus_1 cannot fire here (POST is not in the read gate), so
    // before this tier the site was invisible outside a bare "looped_effect: N" count.
    [Test]
    public void A_looped_http_POST_surfaces_as_an_amplification_finding()
    {
        var result = ProductionFixCorpus.Analyze(
            """
            using System.Net.Http;
            using System.Collections.Generic;
            using System.Threading.Tasks;

            public sealed class Notifier
            {
                private readonly HttpClient _http = new HttpClient();

                // BUG SHAPE: one outbound POST per element — N round trips. Not a read, so n_plus_1 is silent.
                public async Task Notify_Looped(List<string> ids)
                {
                    foreach (var id in ids)
                    {
                        await _http.PostAsync("https://hook.example/notify", new StringContent(id));
                    }
                }

                // CONTROL: the same POST, once.
                public async Task Notify_Once(string id)
                {
                    await _http.PostAsync("https://hook.example/notify", new StringContent(id));
                }
            }
            """
        );

        var looped = result.EffectsIn("Notify_Looped").Where(e => e.Provider == "http" && e.Operation == "POST").ToList();
        looped.ShouldNotBeEmpty("expected an http:POST effect in the looped method");
        looped.ShouldContain(e => e.Observations != null && e.Observations.Any(o => o.Type == HazardKinds.LoopedEffect));
        // A WRITE, so the n_plus_1 read gate must stay silent — this is precisely the gap the tier fills.
        result.ObservationsIn("Notify_Looped", HazardKinds.NPlusOne).ShouldBeEmpty();

        var scope = BuiltinRules().Observations.AmplificationOrEmpty;
        var findings = DeriveCommand.AmplificationFindings(result.Effects, scope);

        findings.ShouldContain(f => f.Provider == "http" && f.Operation == "POST" && f.Enclosing.Contains("Notify_Looped"));
        // The unlooped control must NOT be a finding — the tier reports iteration context, not the provider.
        findings.ShouldNotContain(f => f.Enclosing.Contains("Notify_Once"));

        // And it renders under its provider:operation, which is the whole point of the section.
        var sw = new StringWriter();
        DeriveCommand.WriteAmplification(sw, findings, limit: 40);
        var text = sw.ToString();
        text.ShouldContain("Amplification (looped effects");
        text.ShouldContain("http:POST: 1 site(s)");
        text.ShouldNotContain("Hazards"); // never rendered as a hazard
    }

    // --- 3. the rules-declared display scope ----------------------------------------------------------------

    [Test]
    public void The_shipped_scope_admits_network_crossing_providers_and_excludes_local_ones()
    {
        var scope = BuiltinRules().Observations.AmplificationOrEmpty;
        scope.ShouldNotBeEmpty("builtin-rules.json must ship an observations.amplification section");

        // In: the effects where x N means N round trips — and ONLY providers this file itself declares.
        AmplificationScope.Includes(scope, "http", "POST").ShouldBeTrue();
        AmplificationScope.Includes(scope, "http", "GET").ShouldBeTrue();
        AmplificationScope.Includes(scope, "db_command", "execute").ShouldBeTrue();
        AmplificationScope.Includes(scope, "efcore", "commit").ShouldBeTrue();
        AmplificationScope.Includes(scope, "object_store", "read").ShouldBeTrue();
        AmplificationScope.Includes(scope, "queue", "read").ShouldBeTrue();

        // Out (staged, deliberately — x N is CPU/contention, not round trips). These are also the two
        // HIGHEST-volume looped providers on the real store, so admitting them would dominate the section.
        AmplificationScope.Includes(scope, "shared_state", "read").ShouldBeFalse();
        AmplificationScope.Includes(scope, "entity_cache", "read").ShouldBeFalse();
        AmplificationScope.Includes(scope, "lock", "acquire").ShouldBeFalse();
        AmplificationScope.Includes(scope, "alloc", "object").ShouldBeFalse();
        AmplificationScope.Includes(scope, "throw", "throw").ShouldBeFalse();

        // Out because they are ONE PROJECT'S vocabulary (core-purity F5): the shipped scope must not name a
        // codebase-specific ORM or actor framework — those arrive from that project's own ruleset, which
        // APPENDS to this list. A regression here means the MedDBase overlay leaked back into the tool.
        AmplificationScope.Includes(scope, "llblgen", "write").ShouldBeFalse();
        AmplificationScope.Includes(scope, "actor", "tell").ShouldBeFalse();
    }

    [Test]
    public void An_empty_scope_yields_no_findings_so_the_scope_is_declared_never_implied()
    {
        var effects = new[] { Looped("llblgen", "write", "M:App.Svc.Save") };
        DeriveCommand.AmplificationFindings(effects, []).ShouldBeEmpty();
        AmplificationScope.Includes([], "llblgen", "write").ShouldBeFalse();
    }

    [Test]
    public void An_out_of_scope_looped_effect_stays_counted_in_the_generic_observations_block()
    {
        var effects = new[]
        {
            Looped("db_command", "execute", "M:App.Svc.Save"), // in scope   -> Amplification section
            Looped("entity_cache", "read", "M:App.Svc.Get"), // out of scope -> generic block
        };
        var scope = BuiltinRules().Observations.AmplificationOrEmpty;

        // Only the in-scope one becomes a finding...
        var findings = DeriveCommand.AmplificationFindings(effects, scope);
        findings.Count.ShouldBe(1);
        findings[0].Provider.ShouldBe("db_command");

        // ...and the out-of-scope one is still reported, as a count, so narrowing the scope loses nothing.
        var groups = DeriveCommand.GenericObservationGroups(effects, scope, amplification: true);
        groups.ShouldContain(g => g.Type == HazardKinds.LoopedEffect && g.Count == 1);
    }

    // --- 4. --no-amplification reproduces the pre-tier behaviour --------------------------------------------

    [Test]
    public void No_amplification_suppresses_the_section_and_restores_the_generic_count()
    {
        var effects = new[] { Looped("db_command", "execute", "M:App.Svc.Save"), Looped("http", "POST", "M:App.Svc.Push") };
        var scope = BuiltinRules().Observations.AmplificationOrEmpty;

        // ON (default): both are findings, and NEITHER is left in the generic block.
        DeriveCommand.AmplificationFindings(effects, scope).Count.ShouldBe(2);
        DeriveCommand
            .GenericObservationGroups(effects, scope, amplification: true)
            .ShouldNotContain(g => g.Type == HazardKinds.LoopedEffect);

        // OFF: no section (WriteAmplification is a no-op on an empty list) and the count is back where it was.
        var sw = new StringWriter();
        DeriveCommand.WriteAmplification(sw, [], limit: 40);
        sw.ToString().ShouldBeEmpty();
        var groups = DeriveCommand.GenericObservationGroups(effects, scope, amplification: false);
        groups.ShouldContain(g => g.Type == HazardKinds.LoopedEffect && g.Count == 2);
    }

    // --- 5. no double-counting across the two sections -----------------------------------------------------

    [Test]
    public void A_hazard_is_not_double_counted_across_the_hazards_and_amplification_sections()
    {
        // One effect carrying BOTH a hazard (n_plus_1) and the structural looped_effect — the common case for an
        // in-scope looped READ. It must appear ONCE in each section, under its own type, and nowhere twice.
        var effect = new DerivedEffect(
            Provider: "db_command",
            Operation: "execute",
            ResourceType: "AccountEntity",
            EnclosingSymbolId: "M:App.Svc.Load",
            FilePath: "C:/repo/App/Svc.cs",
            Line: 12,
            Observations:
            [
                Obs(HazardKinds.NPlusOne, "high", "looped_read_with_varying_key"),
                Obs(HazardKinds.LoopedEffect, "high", "effect_inside_loop", context: "foreach"),
            ]
        );
        var scope = BuiltinRules().Observations.AmplificationOrEmpty;

        var hazards = DeriveCommand.HazardFindings([effect]);
        var amplification = DeriveCommand.AmplificationFindings([effect], scope);

        // Each finding lands in exactly ONE section.
        hazards.Select(f => f.Type).ShouldBe([HazardKinds.NPlusOne]);
        amplification.Select(f => f.Type).ShouldBe([HazardKinds.LoopedEffect]);
        hazards.ShouldNotContain(f => HazardKinds.IsAmplification(f.Type));
        amplification.ShouldNotContain(f => HazardKinds.IsHazard(f.Type));

        // ...and the rendered sections agree: 1 hazard site, 1 amplification site, no cross-mention.
        var sw = new StringWriter();
        DeriveCommand.WriteHazards(sw, hazards, limit: 40);
        DeriveCommand.WriteAmplification(sw, amplification, limit: 40);
        var text = sw.ToString();
        text.ShouldContain("Hazards (pattern findings): 1");
        text.ShouldContain("Amplification (looped effects — structural inventory): 1");
        text.ShouldContain("n_plus_1: 1 site(s)");
        text.ShouldContain("db_command:execute: 1 site(s)");
        // The generic block sees NEITHER (both have their own section).
        DeriveCommand.GenericObservationGroups([effect], scope, amplification: true).ShouldBeEmpty();
    }

    // --- the tsv contract ---------------------------------------------------------------------------------

    [Test]
    public void AmplificationTsvRow_is_its_own_row_type_and_carries_provider_and_operation()
    {
        var row = DeriveCommand.AmplificationTsvRow(
            new DeriveCommand.HazardFinding(
                Type: HazardKinds.LoopedEffect,
                Confidence: "high",
                Reason: "effect_inside_loop",
                Context: "foreach",
                Detail: "id in ids",
                Enclosing: "M:App.Svc.Push",
                FilePath: "C:/repo/App/Svc.cs",
                Line: 7,
                Provider: "http",
                Operation: "POST"
            )
        );

        var cols = row.Split('\t');
        cols[0].ShouldBe("amplification"); // NOT "hazard" — downstream consumers filter on column 1
        cols[1].ShouldBe("looped_effect");
        cols[2].ShouldBe("high");
        cols[3].ShouldBe("effect_inside_loop");
        cols[4].ShouldBe("foreach"); // the iteration context
        cols[5].ShouldBe("M:App.Svc.Push");
        cols[6].ShouldBe("C:/repo/App/Svc.cs");
        cols[7].ShouldBe("7");
        cols[8].ShouldBe("id in ids");
        cols[9].ShouldBe("http"); // the two columns amplification exists for
        cols[10].ShouldBe("POST");
    }

    [Test]
    public void A_hazard_tsv_row_is_unaffected_by_the_provider_columns()
    {
        // The provider/operation fields default empty and are ABSENT from HazardTsvRow, so the `hazard` row
        // contract is byte-identical to pre-tier. This is the regression gate for every downstream consumer.
        var row = DeriveCommand.HazardTsvRow(
            new DeriveCommand.HazardFinding(
                Type: FactHazardDeriver.RaceWindowType,
                Confidence: "high",
                Reason: "rmw_no_isolation_on_path",
                Context: "F:App.Cache._x",
                Detail: "C:/repo/App/Cache.cs:7",
                Enclosing: "M:App.Cache.Bump",
                FilePath: "C:/repo/App/Cache.cs",
                Line: 42
            )
        );
        row.Split('\t').Length.ShouldBe(9);
        row.ShouldBe(
            "hazard\trace_window\thigh\trmw_no_isolation_on_path\tF:App.Cache._x\tM:App.Cache.Bump\tC:/repo/App/Cache.cs\t42\tC:/repo/App/Cache.cs:7"
        );
    }

    [Test]
    public void The_section_groups_by_provider_operation_busiest_first()
    {
        // A flat "looped_effect: N" is useless; the breakdown is the deliverable. Ordering mirrors Hazards:
        // group site count desc, ties by key.
        var findings = new List<DeriveCommand.HazardFinding>();
        for (var i = 0; i < 3; i++)
        {
            findings.Add(Finding("llblgen", "write", $"M:App.Svc.Save{i}"));
        }

        findings.Add(Finding("http", "POST", "M:App.Svc.Push"));

        var sw = new StringWriter();
        DeriveCommand.WriteAmplification(sw, findings, limit: 40);
        var text = sw.ToString();

        text.ShouldContain("Amplification (looped effects — structural inventory): 4");
        text.ShouldContain("llblgen:write: 3 site(s) across 3 method(s)");
        text.ShouldContain("http:POST: 1 site(s)");
        text.IndexOf("llblgen:write", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("http:POST", StringComparison.Ordinal));
    }

    // --- helpers ------------------------------------------------------------------------------------------

    private static EffectObservationInfo Obs(string type, string confidence, string reason, string context = "foreach") =>
        new(Type: type, Context: context, Detail: context, Confidence: confidence, Basis: "fact_derived", Reason: reason);

    private static DerivedEffect Looped(string provider, string operation, string enclosing) =>
        new(
            Provider: provider,
            Operation: operation,
            ResourceType: "Account",
            EnclosingSymbolId: enclosing,
            FilePath: "C:/repo/App/Svc.cs",
            Line: 7,
            Observations: [Obs(HazardKinds.LoopedEffect, "high", "effect_inside_loop")]
        );

    private static DeriveCommand.HazardFinding Finding(string provider, string operation, string enclosing) =>
        new(
            Type: HazardKinds.LoopedEffect,
            Confidence: "high",
            Reason: "effect_inside_loop",
            Context: "foreach",
            Detail: "x in xs",
            Enclosing: enclosing,
            FilePath: "C:/repo/App/Svc.cs",
            Line: 7,
            Provider: provider,
            Operation: operation
        );

    // Builtin-only rule set (no colocated/global overlay), loaded rooted at an empty temp dir — the same trick
    // ProductionFixCorpus/NPlusOneProviderGateTests use, so this measures the scope we SHIP, not a dev's rules.
    private static RuleSet BuiltinRules()
    {
        var tempDir = Directory.CreateTempSubdirectory("rig-amp-rules-").FullName;
        try
        {
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
}
