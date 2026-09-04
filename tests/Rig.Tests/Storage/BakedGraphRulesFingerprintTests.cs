using Microsoft.EntityFrameworkCore;
using Rig.Analysis.Rules;
using Rig.Cli.Graph;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Shouldly;

namespace Rig.Tests.Storage;

[NotInParallel]
public sealed class BakedGraphRulesFingerprintTests
{
    private const string Caller = "M:Demo.Caller.Start";
    private const string Callback = "M:Demo.Caller.Callback";
    private const string RulesAHash = "rules-a";
    private const string RulesBHash = "rules-b";

    private static readonly RuleSet RulesA = new()
    {
        EffectiveFingerprint = RulesAHash,
        Handoff = [new FactHandoffRule("scheduler-a", "background", [".Scheduler.RunNow"])],
    };

    private static readonly RuleSet RulesB = new() { EffectiveFingerprint = RulesBHash };

    [Test]
    public async Task Graph_stamp_requires_the_same_rules_and_legacy_missing_stamp_fails_closed()
    {
        await WithMaterializedStoreAsync(async databasePath =>
        {
            await using var context = new RigDbContext(databasePath, pooling: false);
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync();

            (await SchemaGate.GraphAvailableAsync(connection, RulesAHash)).ShouldBeTrue();
            (await SchemaGate.GraphAvailableAsync(connection, RulesBHash)).ShouldBeFalse();
            (await SchemaGate.GraphAvailableAsync(connection)).ShouldBeTrue(
                "schema-only graph consumers remain available when they do not consume baked rule decisions"
            );

            var previousTrust = Environment.GetEnvironmentVariable("RIG_TRUST_GRAPH");
            try
            {
                Environment.SetEnvironmentVariable("RIG_TRUST_GRAPH", "1");
                (await SchemaGate.GraphAvailableAsync(connection, RulesBHash)).ShouldBeFalse(
                    "RIG_TRUST_GRAPH may bypass a graph schema mismatch, never a rules mismatch"
                );
            }
            finally
            {
                Environment.SetEnvironmentVariable("RIG_TRUST_GRAPH", previousTrust);
            }

            await using (var removeStamp = connection.CreateCommand())
            {
                removeStamp.CommandText = "DROP TABLE graph_build_meta;";
                await removeStamp.ExecuteNonQueryAsync();
            }

            (await SchemaGate.GraphAvailableAsync(connection, RulesAHash)).ShouldBeFalse(
                "a legacy graph with no rules stamp cannot be trusted by a rules-sensitive query"
            );
            (await SchemaGate.GraphAvailableAsync(connection)).ShouldBeTrue(
                "removing the rules stamp does not disable graph-independent FTS consumers"
            );
        });
    }

    [Test]
    public async Task Failed_rebuild_withdraws_the_old_graph_before_any_partial_tables_can_be_trusted()
    {
        await WithMaterializedStoreAsync(async databasePath =>
        {
            await using (var rebuild = new RigDbContext(databasePath, pooling: false))
            {
                await Should.ThrowAsync<InvalidOperationException>(async () =>
                    await GraphMaterializer.BuildAsync(
                        rebuild,
                        handoffRules: RulesA.Handoff,
                        progress: message =>
                        {
                            if (message == "Rebuilding derived edge tables")
                            {
                                throw new InvalidOperationException("stop after invalidation");
                            }
                        },
                        rulesFingerprint: RulesAHash
                    )
                );
            }

            await using var read = new RigDbContext(databasePath, pooling: false, readOnly: true);
            var connection = read.Database.GetDbConnection();
            await connection.OpenAsync();

            (await SchemaGate.GraphAvailableAsync(connection)).ShouldBeFalse();
            (await SchemaGate.GraphAvailableAsync(connection, RulesAHash)).ShouldBeFalse();
            (await SchemaGate.GraphAvailableAsync(connection, RulesBHash)).ShouldBeFalse();
        });
    }

    [Test]
    public async Task Bounded_loader_falls_back_to_the_current_rule_graph_after_a_rules_edit()
    {
        await WithMaterializedStoreAsync(async databasePath =>
        {
            await using var context = new RigDbContext(databasePath, pooling: false, readOnly: true);

            var bounded = await TraversalGraphLoader.LoadShapedTraversalGraphAsync(
                context,
                "Caller.Start",
                SqlReachability.Direction.Forward,
                RulesB
            );
            var currentRulesOracle = await Reads.LoadFactGraphAsync(context, RulesB.Handoff, RulesB.Redirect);

            EdgeShape(bounded).ShouldBe(EdgeShape(currentRulesOracle));
            bounded.CallEdges.ShouldContain(
                edge => edge.Caller == Caller && edge.Callee == Callback && edge.Kind == EdgeKinds.MethodGroup,
                "rules B no longer classify RunNow as a handoff, so the stale rules-A call_edges row must not be read"
            );
        });
    }

    [Test]
    public async Task Handoff_entry_points_do_not_read_stale_baked_classification_after_a_rules_edit()
    {
        await WithMaterializedStoreAsync(async databasePath =>
        {
            await using var context = new RigDbContext(databasePath, pooling: false, readOnly: true);

            var bakedA = await Reads.DeriveHandoffEntryPointsAsync(
                context,
                int.MaxValue,
                RulesA.Handoff,
                expectedRulesFingerprint: RulesAHash
            );
            bakedA.Single(entry => entry.Target == Callback).Dispatcher.ShouldBe("scheduler-a");

            var currentRulesGraph = await Reads.LoadFactGraphAsync(context, RulesB.Handoff, RulesB.Redirect);
            var derivedB = await Reads.DeriveHandoffEntryPointsAsync(
                context,
                int.MaxValue,
                RulesB.Handoff,
                graph: currentRulesGraph,
                expectedRulesFingerprint: RulesBHash
            );

            var callback = derivedB.Single(entry => entry.Target == Callback);
            callback.Dispatcher.ShouldBeNull();
            callback.Kind.ShouldBeNull();
        });
    }

    [Test]
    public async Task Raw_projection_bypasses_a_same_fingerprint_graph_with_baked_factory_rewrites()
    {
        var materializedRules = new RuleSet
        {
            EffectiveFingerprint = RulesAHash,
            Factory = [new FactGenericFactoryRule("N.Entity.New", ConstructArgIndex: 0, TargetMethod: "New")],
        };

        await WithMaterializedStoreAsync(
            materializedRules,
            FactoryResult,
            async databasePath =>
            {
                await using var context = new RigDbContext(databasePath, pooling: false, readOnly: true);

                var baked = await SqlReachability.LoadBoundedGraphAsync(context, "Caller.Start", SqlReachability.Direction.Forward);
                baked.CallEdges.ShouldContain(edge => edge.Callee == "M:N.Cat.New(System.Int32)");

                var rawRules = materializedRules with { Factory = [], MaterializedGraphCompatible = false };
                var raw = await TraversalGraphLoader.LoadShapedTraversalGraphAsync(
                    context,
                    "Caller.Start",
                    SqlReachability.Direction.Forward,
                    rawRules
                );
                var rawOracle = await Reads.LoadFactGraphAsync(context);

                EdgeShape(raw).ShouldBe(EdgeShape(rawOracle));
                raw.CallEdges.ShouldContain(
                    edge => edge.Caller == Caller && edge.Callee == "M:N.Entity.New``2(``1)",
                    "--raw must retain the original factory call rather than consume rewritten call_edges"
                );
                raw.CallEdges.ShouldNotContain(edge => edge.Callee == "M:N.Cat.New(System.Int32)");
            }
        );
    }

    [Test]
    public void Loaded_rule_set_carries_the_fingerprint_of_the_resolved_cascade()
    {
        var directory = Directory.CreateTempSubdirectory("rig-rule-fingerprint-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "rig.rules.json"), """{"effectEmoji":{"snapshot-test":"!"}}""");
            var rules = RuleSetLoader.Load(directory, extraRules: [], loadedPaths: out var loadedPaths);

            rules.EffectiveFingerprint.ShouldBe(RulesFingerprint.ComputeFromPaths(loadedPaths));
            rules.EffectiveFingerprint.ShouldNotBeNullOrEmpty();
            rules.EffectEmoji["snapshot-test"].ShouldBe("!");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static IReadOnlyList<(string Caller, string Callee, string Kind, string? Dispatcher)> EdgeShape(FactGraphData graph) =>
        graph
            .CallEdges.Select(edge => (edge.Caller, edge.Callee, edge.Kind, edge.HandoffDispatcher))
            .OrderBy(edge => edge.Caller, StringComparer.Ordinal)
            .ThenBy(edge => edge.Callee, StringComparer.Ordinal)
            .ThenBy(edge => edge.Kind, StringComparer.Ordinal)
            .ToList();

    private static async Task WithMaterializedStoreAsync(Func<string, Task> body)
    {
        await WithMaterializedStoreAsync(RulesA, HandoffResult, body);
    }

    private static async Task WithMaterializedStoreAsync(
        RuleSet materializedRules,
        Func<string, AnalysisResult> resultFactory,
        Func<string, Task> body
    )
    {
        var directory = Directory.CreateTempSubdirectory("rig-baked-rules-").FullName;
        var databasePath = Path.Combine(directory, "rig.db");
        try
        {
            var result = resultFactory(directory);

            await using (var write = new RigDbContext(databasePath, pooling: false))
            {
                await Writes.SaveAsync(write, result);
            }

            await using (var graph = new RigDbContext(databasePath, pooling: false))
            {
                await GraphMaterializer.BuildAsync(
                    graph,
                    handoffRules: materializedRules.Handoff,
                    factoryRules: materializedRules.Factory,
                    rulesFingerprint: materializedRules.EffectiveFingerprint
                );
            }

            await body(databasePath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static AnalysisResult HandoffResult(string directory) =>
        new(
            SolutionPath: Path.Combine(directory, "Demo.slnx"),
            SourceFiles: [],
            DiRegistrations: [],
            Symbols: [Method(Caller, "Start", line: 3), Method(Callback, "Callback", line: 8)],
            References:
            [
                new ReferenceFact(
                    TargetSymbolId: Callback,
                    RefKind: RefKinds.MethodGroup,
                    EnclosingSymbolId: Caller,
                    TargetAssembly: "Demo",
                    TargetInSource: true,
                    FilePath: Path.Combine(directory, "Caller.cs"),
                    Line: 5,
                    DelegateConsumer: "M:Demo.Scheduler.RunNow(System.Action)"
                ),
            ]
        );

    private static AnalysisResult FactoryResult(string directory) =>
        new(
            SolutionPath: Path.Combine(directory, "Demo.slnx"),
            SourceFiles: [],
            DiRegistrations: [],
            Symbols:
            [
                Method(Caller, "Start", line: 3),
                Method("M:N.Entity.New``2(``1)", "New", line: 5, containingTypeId: "T:N.Entity"),
                Method("M:N.Cat.New(System.Int32)", "New", line: 9, containingTypeId: "T:N.Cat"),
            ],
            References:
            [
                new ReferenceFact(
                    TargetSymbolId: "M:N.Entity.New``2(``1)",
                    RefKind: RefKinds.Invocation,
                    EnclosingSymbolId: Caller,
                    TargetAssembly: "Demo",
                    TargetInSource: true,
                    FilePath: Path.Combine(directory, "Caller.cs"),
                    Line: 5,
                    TypeArguments: "N.Cat,System.Int32"
                ),
            ]
        );

    private static SymbolFact Method(string id, string name, int line, string containingTypeId = "T:Demo.Caller") =>
        new(
            SymbolId: id,
            Kind: SymbolKinds.Method,
            Name: name,
            Namespace: "Demo",
            ContainingSymbolId: containingTypeId,
            Modifiers: "public",
            TypeKind: "",
            Signature: $"void {name}()",
            FilePath: "Caller.cs",
            Line: line,
            EndLine: line,
            DefiningAssembly: "Demo",
            IsOverride: false
        );
}
