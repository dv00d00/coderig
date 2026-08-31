using Rig.Analysis.Rules;
using Rig.Cli.Rendering;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Shouldly;

namespace Rig.Tests.Domain;

// EXTERNAL-NODE ADMISSION (ExternalNodeAdmission): out-of-source call targets become first-class LEAF
// nodes in the call graph. These pin the POLICY (which targets get in, and how config overrides it), the
// SYNTHESIZED node's shape, the leaf/dispatch invariants, the mutual exclusion with the redirect rewrite,
// and the SQL-loader/in-memory-twin parity on one corpus.
//
// The corpus is a hand-built AnalysisResult, so the same facts can be fed to BOTH admission points: the
// in-memory twin (FactGraphProjection.FromAnalysis) directly, and Reads.LoadFactGraphAsync after a
// Writes.SaveAsync round-trip into a temp store.
public sealed class ExternalNodeAdmissionTests
{
    private const string Caller = "M:App.Service.Run";
    private const string FirstPartyCallee = "M:App.Repo.Load";

    // A modelled BCL type: `System.Data.Common.DbTransaction` is named by a real effect rule's
    // declaringTypes, which is what admits it despite living in a DENIED (framework) assembly.
    private const string RuleMentionedBcl = "M:System.Data.Common.DbTransaction.CommitAsync(System.Threading.CancellationToken)";

    // BCL noise: no effect rule mentions either type, and both are in framework assemblies.
    private const string BclNoiseToString = "M:System.String.ToString";
    private const string BclNoiseListAdd = "M:System.Collections.Generic.List`1.Add(`0)";

    // A non-framework library: in by the assembly arm alone, with no rule mentioning it.
    private const string LibraryTarget = "M:Dapper.SqlMapper.QueryAsync``1(System.Data.IDbConnection,System.String)";

    // The redirect seam: a convenience overload that a `redirectRules` entry rewrites to the virtual hatch.
    private const string RedirectedOverload = "M:External.EntityBase.Save(System.Boolean)";
    private const string RedirectHatch = "M:External.EntityBase.Save(External.IPredicate,System.Boolean)";

    private static readonly FactRedirectRule[] RedirectRules = [new FactRedirectRule("M:External.EntityBase.Save", RedirectHatch)];

    // The effect rules stand in for a loaded ruleset: the DbTransaction gate is the only reason a
    // framework-assembly target is admitted, and `TypePatternsOf` must mine it rather than a hand-copied
    // BCL list living in C#.
    private static readonly FactEffectRule[] EffectRules =
    [
        new FactEffectRule(
            Provider: "db",
            Operation: "commit",
            Methods: ["CommitAsync"],
            DeclaringTypes: ["System.Data.Common.DbTransaction"],
            ReceiverTypes: []
        ),
        new FactEffectRule(
            Provider: "http",
            Operation: "send",
            Methods: ["SendAsync"],
            DeclaringTypes: [],
            ReceiverTypes: ["System.Net.Http.HttpClient"]
        ),
    ];

    private static ExternalNodeAdmission Policy(FactExternalNodeRule? config = null) => ExternalNodeAdmission.Create(EffectRules, config);

    [Test]
    public void Type_patterns_come_from_the_loaded_effect_rules_not_a_hardcoded_list()
    {
        // Derived, not curated: declaringTypes AND receiverTypes both contribute, and nothing else does.
        ExternalNodeAdmission
            .TypePatternsOf(EffectRules)
            .ShouldBe(["System.Data.Common.DbTransaction", "System.Net.Http.HttpClient"], ignoreOrder: true);

        // No rules => no arm (a); only the assembly arm can then admit anything.
        ExternalNodeAdmission.TypePatternsOf([]).ShouldBeEmpty();
        ExternalNodeAdmission.Create(effectRules: [], config: null).Admits("mscorlib", RuleMentionedBcl).ShouldBeFalse();
    }

    [Test]
    public void A_rule_mentioned_bcl_target_is_admitted_and_a_non_mentioned_one_is_not()
    {
        var policy = Policy();

        // Case 1: rule-mentioned, framework assembly -> IN (arm (a) beats the deny-list).
        policy.Admits("System.Data", RuleMentionedBcl).ShouldBeTrue();
        policy.Admits("mscorlib", RuleMentionedBcl).ShouldBeTrue();

        // Case 2: BCL noise -> OUT.
        policy.Admits("mscorlib", BclNoiseToString).ShouldBeFalse();
        policy.Admits("System.Private.CoreLib", BclNoiseToString).ShouldBeFalse();
        policy.Admits("mscorlib", BclNoiseListAdd).ShouldBeFalse();
        policy.Admits("netstandard", BclNoiseListAdd).ShouldBeFalse();

        // Case 3: a non-framework library -> IN by the assembly arm, with no rule naming it.
        policy.Admits("Dapper", LibraryTarget).ShouldBeTrue();
        policy.Admits("MediatR", "M:MediatR.IMediator.Send``1(MediatR.IRequest{``0})").ShouldBeTrue();
    }

    [Test]
    public void The_framework_deny_list_matches_assembly_name_segments_not_a_bare_prefix()
    {
        var policy = Policy();

        // `System` denies System and System.* ...
        policy.Admits("System", LibraryTarget).ShouldBeFalse();
        policy.Admits("System.Core", LibraryTarget).ShouldBeFalse();
        policy.Admits("System.Net.Http", LibraryTarget).ShouldBeFalse();

        // ... but NOT a differently-named assembly that merely starts with the same letters. A bare
        // StartsWith would have eaten both of these.
        policy.Admits("SystemX", LibraryTarget).ShouldBeTrue();
        policy.Admits("SystemTextJsonPatch", LibraryTarget).ShouldBeTrue();
        policy.Admits("Microsoft.Win32Extras", LibraryTarget).ShouldBeTrue();
        policy.Admits("Microsoft.Win32.Registry", LibraryTarget).ShouldBeFalse();
    }

    [Test]
    public void Config_overrides_win_over_both_default_arms()
    {
        // An explicitly DENIED assembly stays out even though it is not a framework one.
        var denied = Policy(new FactExternalNodeRule(AllowAssemblies: [], DenyAssemblies: ["Dapper"]));
        denied.Admits("Dapper", LibraryTarget).ShouldBeFalse();
        denied.Admits("Dapper.Contrib", LibraryTarget).ShouldBeFalse(); // segment match
        denied.Admits("MediatR", LibraryTarget).ShouldBeTrue(); // the rest of the default policy is intact

        // An explicitly ALLOWED assembly gets in even though it IS a framework one, for a target no rule
        // mentions — allow beats the deny-list.
        var allowed = Policy(new FactExternalNodeRule(AllowAssemblies: ["System.Net.Http"], DenyAssemblies: []));
        allowed.Admits("System.Net.Http", BclNoiseToString).ShouldBeTrue();
        allowed.Admits("mscorlib", BclNoiseToString).ShouldBeFalse();

        // Allow wins over an explicit deny of the same name too (stated precedence, not an accident).
        var both = Policy(new FactExternalNodeRule(AllowAssemblies: ["Dapper"], DenyAssemblies: ["Dapper"]));
        both.Admits("Dapper", LibraryTarget).ShouldBeTrue();
    }

    [Test]
    public void Only_method_doc_ids_can_be_admitted()
    {
        var policy = Policy();

        // Types/fields/properties are never call-graph nodes (CLAUDE.md's effect/reachability invariant),
        // and an unresolved error-type target has no declaring type to reason about.
        policy.Admits("Dapper", "T:Dapper.SqlMapper").ShouldBeFalse();
        policy.Admits("Dapper", "F:Dapper.SqlMapper.Cache").ShouldBeFalse();
        policy.Admits("Dapper", "!:SqlMapper.QueryAsync").ShouldBeFalse();
        policy.Admits("Dapper", "").ShouldBeFalse();
    }

    [Test]
    public void A_synthesized_external_node_is_a_source_less_leaf_carrying_the_marker()
    {
        var node = ExternalNodeAdmission.SynthesizeNode(RuleMentionedBcl);

        node.SymbolId.ShouldBe(RuleMentionedBcl); // the DocID exactly as stored — nothing to reconcile
        node.Name.ShouldBe("CommitAsync");
        node.ContainingTypeId.ShouldBe("T:System.Data.Common.DbTransaction");
        node.FilePath.ShouldBeNull();
        node.Line.ShouldBe(0);
        node.IsOverride.ShouldBeFalse();
        node.IsExternal.ShouldBeTrue();

        // A generic declaring type / generic method keeps its arity markers on the id but parses cleanly.
        var generic = ExternalNodeAdmission.SynthesizeNode(LibraryTarget);
        generic.Name.ShouldBe("QueryAsync");
        generic.ContainingTypeId.ShouldBe("T:Dapper.SqlMapper");
    }

    [Test]
    public void The_twin_admits_the_policy_set_and_synthesizes_a_leaf_node_for_each()
    {
        var graph = FactGraphProjection.FromAnalysis(Corpus(), handoffRules: null, redirectRules: RedirectRules, externalNodes: Policy());

        var callees = graph.CallEdges.Where(e => e.Caller == Caller).Select(e => e.Callee).ToHashSet(StringComparer.Ordinal);
        callees.ShouldContain(FirstPartyCallee);
        callees.ShouldContain(RuleMentionedBcl);
        callees.ShouldContain(LibraryTarget);
        callees.ShouldNotContain(BclNoiseToString);
        callees.ShouldNotContain(BclNoiseListAdd);

        // Case 1 (continued): the admitted node exists, is marked, and has NO source location.
        var admitted = graph.Methods.Single(m => m.SymbolId == RuleMentionedBcl);
        admitted.IsExternal.ShouldBeTrue();
        admitted.FilePath.ShouldBeNull();
        admitted.Line.ShouldBe(0);

        // Case 3 (continued): the library target too.
        graph.Methods.Single(m => m.SymbolId == LibraryTarget).IsExternal.ShouldBeTrue();

        // The first-party method keeps its real, source-located node — nothing was re-synthesized over it.
        var firstParty = graph.Methods.Single(m => m.SymbolId == FirstPartyCallee);
        firstParty.IsExternal.ShouldBeFalse();
        firstParty.FilePath.ShouldBe("/repo/Repo.cs");

        // Nothing that was rejected became a node.
        graph.Methods.ShouldNotContain(m => m.SymbolId == BclNoiseToString);
        graph.Methods.ShouldNotContain(m => m.SymbolId == BclNoiseListAdd);
    }

    [Test]
    public void Admission_is_off_when_no_policy_is_supplied()
    {
        // The pre-change shape, which the synthetic/test projections that pass no rules still get.
        var graph = FactGraphProjection.FromAnalysis(Corpus(), handoffRules: null, redirectRules: RedirectRules);

        graph.CallEdges.Select(e => e.Callee).ShouldNotContain(RuleMentionedBcl);
        graph.CallEdges.Select(e => e.Callee).ShouldNotContain(LibraryTarget);
        graph.Methods.ShouldNotContain(m => m.IsExternal);
        // The redirect arm is independent of admission and still fires.
        graph.CallEdges.Select(e => e.Callee).ShouldContain(RedirectHatch);
    }

    [Test]
    public void A_redirect_matched_row_produces_only_the_redirect_edge()
    {
        var graph = FactGraphProjection.FromAnalysis(Corpus(), handoffRules: null, redirectRules: RedirectRules, externalNodes: Policy());

        // ONE edge from that one row, and it points at the HATCH, not at the convenience overload.
        var fromRedirectSite = graph.CallEdges.Where(e => e.Caller == Caller && e.Line == 40).ToList();
        fromRedirectSite.Count.ShouldBe(1);
        fromRedirectSite[0].Callee.ShouldBe(RedirectHatch);
        graph.CallEdges.ShouldNotContain(e => e.Callee == RedirectedOverload);

        // And the hatch is NOT an external leaf: it must keep its dispatch, which is the whole point of the
        // external-virtual-override-orphan fix.
        graph.Methods.ShouldNotContain(m => m.SymbolId == RedirectHatch && m.IsExternal);
    }

    [Test]
    public void An_admitted_external_node_has_no_successors_and_is_not_a_dispatch_or_cha_root()
    {
        // `App.Repo` implements the EXTERNAL interface, and a first-party `CommitAsync` exists on it — so
        // without the leaf gate, admitting the external declaration would CHA-fan straight into it.
        var graph = FactGraphProjection.FromAnalysis(Corpus(), handoffRules: null, redirectRules: RedirectRules, externalNodes: Policy());

        // Forward: reach from the admitted node is the node itself. Nothing beyond it.
        FactPathFinder.Reaches(graph, RuleMentionedBcl).Keys.ShouldBe([RuleMentionedBcl], ignoreOrder: true);
        FactPathFinder.Reaches(graph, LibraryTarget).Keys.ShouldBe([LibraryTarget], ignoreOrder: true);

        // And the fan-out oracle agrees: the node dispatches nowhere, so it is never a CHA fan-out root.
        FactPathFinder.AllDispatchEdges(graph).ShouldNotContain(e => e.From == RuleMentionedBcl);

        // The first-party impl is still reachable the honest way (from its own caller), so the assertions
        // above are not vacuous — the impl exists, is in the graph, and shares CommitAsync's name/arity.
        FactPathFinder.Reaches(graph, Caller).Keys.ShouldContain(FirstPartyCallee);

        // ANTI-VACUITY: strip the IsExternal marker off the same graph and the CHA fan DOES fire — the
        // external DbTransaction declaration reaches App.Repo.CommitAsync through the implements edge. So
        // the two assertions above are the gate working, not an absent hierarchy.
        var unmarked = graph with
        {
            Methods = [.. graph.Methods.Select(m => m with { IsExternal = false })],
        };
        FactPathFinder.Reaches(unmarked, RuleMentionedBcl).Keys.ShouldContain("M:App.Repo.CommitAsync(System.Threading.CancellationToken)");
    }

    [Test]
    public async Task The_sql_loader_and_the_in_memory_twin_agree_on_the_same_corpus()
    {
        var corpus = Corpus();
        var twin = FactGraphProjection.FromAnalysis(corpus, handoffRules: null, redirectRules: RedirectRules, externalNodes: Policy());

        var directory = Path.Combine(Path.GetTempPath(), "rig-extnode-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "rig.db");
        try
        {
            await using (var write = new RigDbContext(databasePath, pooling: false))
            {
                await Writes.SaveAsync(write, corpus);
            }

            await using var read = new RigDbContext(databasePath, pooling: false);
            var stored = await Reads.LoadFactGraphAsync(read, handoffRules: null, redirectRules: RedirectRules, externalNodes: Policy());

            // Edge SETS (order differs by construction: the store loader appends its redirect + external
            // scans after the first-party scan, the twin interleaves them per reference).
            stored.CallEdges.ShouldBe(twin.CallEdges, ignoreOrder: true);

            // The external LEAVES agree exactly — the point of the parity: one policy, two admission points.
            External(stored).ShouldBe(External(twin), ignoreOrder: true);
            External(stored).ShouldBe([RuleMentionedBcl, LibraryTarget], ignoreOrder: true);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException) { }
        }

        static string[] External(FactGraphData graph) => graph.Methods.Where(m => m.IsExternal).Select(m => m.SymbolId).ToArray();
    }

    // The CONFIG SURFACE end to end: a colocated rig.rules.json `externalNodes` section reaches the policy
    // through the whole cascade (the trap RuleSetLoader's dualWrite comment warns about — a section that
    // isn't folded in Merge() is silently ignored), and the ABSENT section means "the defaults", which is
    // what makes the feature default-ON.
    [Test]
    public void The_external_nodes_section_reaches_the_policy_through_the_rule_cascade()
    {
        using var configured = TempRulesWorkspace.Create(
            // lang=json
            """
            {
              "externalNodes": {
                "allowAssemblies": ["System.Net.Http"],
                "denyAssemblies": ["Dapper"]
              }
            }
            """
        );

        var rules = RuleSetLoader.Load(configured.DirectoryPath);
        rules.ExternalNodes.ShouldNotBeNull();
        rules.ExternalNodes!.AllowAssemblies.ShouldBe(["System.Net.Http"]);
        rules.ExternalNodes.DenyAssemblies.ShouldBe(["Dapper"]);

        var policy = ExternalNodeAdmission.FromRules(rules);
        policy.Admits("Dapper", LibraryTarget).ShouldBeFalse();
        policy.Admits("System.Net.Http", BclNoiseToString).ShouldBeTrue();
        // Arm (a) is live off the SHIPPED builtin effect rules, with nothing authored here.
        policy.RuleTypePatterns.ShouldContain("System.Data.Common.DbTransaction");

        using var bare = TempRulesWorkspace.Create("{}");
        var defaults = RuleSetLoader.Load(bare.DirectoryPath);
        defaults.ExternalNodes.ShouldBeNull(); // absent section => the defaults, not an empty policy
        var defaultPolicy = ExternalNodeAdmission.FromRules(defaults);
        defaultPolicy.Admits("Dapper", LibraryTarget).ShouldBeTrue();
        defaultPolicy.Admits("mscorlib", BclNoiseToString).ShouldBeFalse();
        defaultPolicy.Admits("System.Data", RuleMentionedBcl).ShouldBeTrue();
    }

    [Test]
    public void The_tree_renderer_marks_an_admitted_external_leaf_and_leaves_first_party_nodes_alone()
    {
        var root = new TraceNode(
            Caller,
            "entry",
            null,
            null,
            [
                new TraceNode(FirstPartyCallee, "invocation", null, null, []),
                new TraceNode(RuleMentionedBcl, "invocation", null, null, [], IsExternal: true),
            ]
        );

        var output = new StringWriter();
        var noEffects = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        TreeRenderer.RenderTreeNode(root, new TreeRenderContext(output, noEffects, FactRenderRules.Empty, noEffects) { Prune = false });

        // Asserted against the renderer's ACTUAL output (captured 2026-08-31), not an imagined shape.
        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
        lines.ShouldBe(["Service.Run", "├─ Repo.Load", "└─ DbTransaction.CommitAsync «external»"]);
    }

    // The corpus: one first-party caller whose body calls a first-party method, a modelled BCL member, two
    // BCL-noise members, a library member, and a redirect-matched external convenience overload. `App.Repo`
    // declares its own CommitAsync and implements the EXTERNAL DbTransaction type, which is the CHA bait the
    // leaf gate must refuse.
    private static AnalysisResult Corpus() =>
        new AnalysisResult(
            "/repo/App.sln",
            [],
            [],
            Symbols:
            [
                Method(Caller, "Run", "T:App.Service", "/repo/Service.cs", 5),
                Method(FirstPartyCallee, "Load", "T:App.Repo", "/repo/Repo.cs", 11),
                Method("M:App.Repo.CommitAsync(System.Threading.CancellationToken)", "CommitAsync", "T:App.Repo", "/repo/Repo.cs", 21),
            ],
            References:
            [
                Reference(FirstPartyCallee, RefKinds.Invocation, line: 10),
                Reference(RuleMentionedBcl, RefKinds.Invocation, line: 20, assembly: "System.Data", inSource: false),
                Reference(BclNoiseToString, RefKinds.Invocation, line: 21, assembly: "mscorlib", inSource: false),
                Reference(BclNoiseListAdd, RefKinds.Invocation, line: 22, assembly: "mscorlib", inSource: false),
                Reference(LibraryTarget, RefKinds.Invocation, line: 30, assembly: "Dapper", inSource: false),
                Reference(RedirectedOverload, RefKinds.Invocation, line: 40, assembly: "External", inSource: false),
            ],
            TypeRelations:
            [
                new TypeRelationFact(
                    TypeSymbolId: "T:App.Repo",
                    RelatedSymbolId: "T:System.Data.Common.DbTransaction",
                    RelationKind: RelationKinds.Interface,
                    FilePath: "/repo/Repo.cs"
                ),
            ]
        );

    private static SymbolFact Method(string id, string name, string containingType, string file, int line) =>
        new SymbolFact(
            SymbolId: id,
            Kind: SymbolKinds.Method,
            Name: name,
            Namespace: "App",
            ContainingSymbolId: containingType,
            Modifiers: "public",
            TypeKind: "class",
            Signature: $"public void {name}()",
            FilePath: file,
            Line: line,
            EndLine: line + 3,
            DefiningAssembly: "App",
            IsOverride: false,
            BodyHash: "0"
        );

    private static ReferenceFact Reference(string target, string kind, int line, string assembly = "App", bool inSource = true) =>
        new ReferenceFact(target, kind, Caller, assembly, inSource, "/repo/Service.cs", line);

    // A throwaway rules workspace: a colocated rig.rules.json on top of the shipped builtin cascade. (A
    // private copy rather than a shared fixture, so a concurrent agent editing RuleSetLoaderTests cannot
    // clobber this file.)
    private sealed class TempRulesWorkspace : IDisposable
    {
        private TempRulesWorkspace(string directory) => DirectoryPath = directory;

        public string DirectoryPath { get; }

        public static TempRulesWorkspace Create(string rulesJson)
        {
            var directory = Directory.CreateTempSubdirectory("rig-extnode-rules-").FullName;
            File.WriteAllText(Path.Combine(directory, "Sample.slnx"), "<Solution />");
            File.WriteAllText(Path.Combine(directory, "rig.rules.json"), rulesJson);
            return new TempRulesWorkspace(directory);
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
