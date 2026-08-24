using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

// THE SCALE GATE for the demand traversal budgets.
//
// The live surface shipped with a hard-coded 20,000-node cap on demand graph materialization. Every test
// corpus in this suite is far smaller than that, so nothing failed — while on a real 227-project monorepo
// EVERY live query died, because the intermediate working graph is roughly an order of magnitude larger than
// the answer it produces (a ~2,000-method `reaches` admitted well past 20,000 nodes). "Green on the demo
// playground" therefore meant nothing about whether the feature worked where it was aimed.
//
// These tests pin the two properties that failure had:
//   1. the DEFAULT budget survives a graph an order of magnitude past the old cap, and
//   2. when a budget IS exceeded, the message reports the count it reached and names the knob that changes
//      it — rather than a bare number and a remedy ("restart the watcher") that cannot possibly work.
//
// A chain is used rather than the `stress` corpus on purpose: this must stay a millisecond-scale test that
// runs on every build. The generated-corpus equivalent (LiveScaleGeneratorTests / LiveSnapshotScaleTrial)
// covers realistic shape at a cost that cannot sit in the inner loop.
public sealed class DemandBudgetTests
{
    private const int PastOldCap = 25_000; // the retired hard-coded cap was 20_000

    // WIDE AND SHALLOW, NOT A CHAIN. A real call graph at this size fans out — a 227-project solution is
    // ~11k files of breadth, a few hops from any seed to most of what it reaches. A 25,000-long chain is a
    // shape this tool never sees.
    //
    // The view is INDEXED rather than the shared DemandPathTestView. That fixture answers every keyed lookup
    // with a linear scan over its row lists, which is free at the dozen-node scale the other demand tests
    // use and quadratic here: a 25k-node corpus took this test from milliseconds to minutes, and every one
    // of those minutes was the FIXTURE, not the builder under test (the real builder answers the same shape
    // on a 227-project solution in ~12s). A scale gate that is itself a blind wait would not survive.
    private sealed class IndexedFanView : IFactGraphView
    {
        private readonly Dictionary<string, List<ReferenceFact>> from = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ReferenceFact>> to = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<SymbolFact>> byId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<SymbolFact>> byContaining = new(StringComparer.Ordinal);
        private readonly List<string> methodIds = [];

        internal IndexedFanView(int nodes)
        {
            const int branches = 250;
            var perBranch = Math.Max(1, (nodes - 1 - branches) / branches);
            Method("M:N.T0.Run", "T:N.T0");
            for (var b = 1; b <= branches; b++)
            {
                Method($"M:N.B{b}.Run", $"T:N.B{b}");
                Call("M:N.T0.Run", $"M:N.B{b}.Run");
                for (var leaf = 0; leaf < perBranch; leaf++)
                {
                    Method($"M:N.B{b}L{leaf}.Run", $"T:N.B{b}L{leaf}");
                    Call($"M:N.B{b}.Run", $"M:N.B{b}L{leaf}.Run");
                }
            }
        }

        private void Method(string id, string containing)
        {
            var name = id[(id.LastIndexOf('.') + 1)..];
            var fact = new SymbolFact(id, SymbolKinds.Method, name, "N", containing, "public", "", "Run()", "/f.cs", 1, 1, "App", false);
            Add(byId, id, fact);
            Add(byContaining, containing, fact);
            methodIds.Add(id);
        }

        private void Call(string caller, string callee)
        {
            var reference = new ReferenceFact(
                TargetSymbolId: callee,
                RefKind: RefKinds.Invocation,
                EnclosingSymbolId: caller,
                TargetAssembly: "App",
                TargetInSource: true,
                FilePath: "/f.cs",
                Line: 1
            );
            Add(from, caller, reference);
            Add(to, callee, reference);
        }

        private static void Add<T>(Dictionary<string, List<T>> map, string key, T value)
        {
            if (!map.TryGetValue(key, out var list))
            {
                map[key] = list = [];
            }
            list.Add(value);
        }

        private static IReadOnlyList<T> Get<T>(Dictionary<string, List<T>> map, string key) =>
            map.TryGetValue(key, out var list) ? list : [];

        public IReadOnlyList<ReferenceFact> ReferencesFrom(string enclosingSymbolId) => Get(from, enclosingSymbolId);

        public IReadOnlyList<ReferenceFact> ReferencesTo(string targetSymbolId) => Get(to, targetSymbolId);

        public IReadOnlyList<ReferenceFact> ReferencesToMethodKey(string methodKey) => Get(to, methodKey);

        public IReadOnlyList<SymbolFact> SymbolsById(string symbolId) => Get(byId, symbolId);

        public IReadOnlyList<SymbolFact> SymbolsByContainingSymbol(string containingSymbolId) => Get(byContaining, containingSymbolId);

        public IReadOnlyCollection<string> MethodSymbolIds => methodIds;

        public IReadOnlyList<SymbolFact> MethodsById(string symbolId) => Get(byId, symbolId);

        public IReadOnlyList<SymbolFact> MethodsByContainingSymbol(string containingSymbolId) => Get(byContaining, containingSymbolId);

        public IReadOnlyList<TypeRelationFact> TypeRelationsFrom(string typeSymbolId) => [];

        public IReadOnlyList<TypeRelationFact> TypeRelationsTo(string relatedSymbolId) => [];

        public IReadOnlyList<TypeRelationFact> DispatchRelationsTo(string declaringTypeId) => [];

        public IReadOnlyList<DispatchFact> DispatchFrom(string sourceMember) => [];

        public IReadOnlyList<DispatchFact> DispatchTo(string targetMember) => [];
    }

    private static DemandForwardGraphResult BuildForward(IFactGraphView view, int? maxNodes = null) =>
        DemandForwardPathGraph.Build(
            view,
            new DemandForwardGraphRules(new ForwardCallProjectionRules(), [], []),
            maxNodes is null
                ? new DemandForwardGraphRequest("M:N.T0.Run", int.MaxValue, FactPathFinder.TraversalMode.SyncCut)
                : new DemandForwardGraphRequest("M:N.T0.Run", int.MaxValue, FactPathFinder.TraversalMode.SyncCut, MaxNodes: maxNodes.Value)
        );

    [Test]
    public void Default_forward_budget_admits_a_graph_an_order_of_magnitude_past_the_retired_cap()
    {
        var result = BuildForward(new IndexedFanView(PastOldCap));

        // Anti-vacuity: a builder that silently truncated would also "not throw".
        result.Graph.Methods.Count.ShouldBeGreaterThan(20_000);
    }

    [Test]
    public void An_exceeded_forward_budget_reports_the_count_it_reached_and_the_knob_that_changes_it()
    {
        var error = Should.Throw<DemandForwardGraphUnavailableException>(() => BuildForward(new IndexedFanView(PastOldCap), maxNodes: 100));

        error.Message.ShouldContain("100"); // the budget that was in force
        error.Message.ShouldContain("--max-nodes"); // a knob that actually exists
        // The retired advice: identical work, multi-minute cold boot, guaranteed identical failure.
        error.Message.ShouldNotContain("restart the watcher");
    }
}
