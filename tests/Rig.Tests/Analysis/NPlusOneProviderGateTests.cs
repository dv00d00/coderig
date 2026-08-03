using Rig.Analysis.Rules;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Analysis;

// The n_plus_1 READ GATE is rules DATA (`observations.nPlusOne`), and two entries were simply missing from the
// shipped list: `object_store` (33 looped object_store:read sites fired looped_effect and none could reach
// n_plus_1) and the `execute` operation (db_command's ONLY tagged operation, so the provider was in the gate
// with no operation that could match it). Unlike the composite-key capture defect these calls take the key as a
// DIRECT first argument — nothing was wrong with the extraction, only with the list — so the gate is what this
// pins, at the deriver, without needing an effect rule for the provider in the BUILTIN set (the MedDBase
// object_store:read rules live in that project's own rules file).
public sealed class NPlusOneProviderGateTests
{
    [Test]
    public void The_shipped_read_gate_admits_object_store_read()
    {
        Observations(provider: "object_store", operation: "read", key: "fkObjectHolder").ShouldContain(o => o.Type == HazardKinds.NPlusOne);
    }

    [Test]
    public void The_shipped_read_gate_admits_db_command_execute()
    {
        Observations(provider: "db_command", operation: "execute", key: "id").ShouldContain(o => o.Type == HazardKinds.NPlusOne);
    }

    // The gate is a gate: an operation outside the read set still gets looped_effect and never n_plus_1 — a
    // WRITE in a loop is not read amplification. Guards against "fix the gate by opening it".
    [Test]
    public void The_shipped_read_gate_still_rejects_a_looped_object_store_write()
    {
        var observations = Observations(provider: "object_store", operation: "write", key: "fkObjectHolder");
        observations.ShouldContain(o => o.Type == "looped_effect");
        observations.ShouldNotContain(o => o.Type == HazardKinds.NPlusOne);
    }

    // One looped effect whose key argument is the foreach iteration variable, run through the SHIPPED
    // observation rules — the same call FactEffectDeriver makes per effect.
    private static IReadOnlyList<EffectObservationInfo> Observations(string provider, string operation, string key) =>
        FactObservationDeriver.Derive(
            methodName: "Get",
            loopKind: "foreach",
            loopDetail: $"{key} in {key}s",
            enclosingInvocations: [],
            catchTypes: [],
            rules: BuiltinRules().Observations,
            provider: provider,
            operation: operation,
            firstArgName: key
        );

    // Builtin-only rule set (no colocated/global overlay), loaded rooted at an empty temp dir — the same trick
    // ProductionFixCorpus uses, so this measures what we SHIP and not a dev's local rules.
    private static RuleSet BuiltinRules()
    {
        var tempDir = Directory.CreateTempSubdirectory("rig-gate-rules-").FullName;
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
