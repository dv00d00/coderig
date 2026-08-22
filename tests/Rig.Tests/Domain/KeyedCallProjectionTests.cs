using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

public sealed class KeyedCallProjectionTests
{
    [Test]
    public void Keyed_projection_matches_full_projection_and_reads_only_the_requested_caller()
    {
        const string caller = "M:App.Caller.Run";
        const string dispatcher = "M:Infra.BackgroundProcessSchedule.#ctor(System.Action)";
        const string redirectedTarget = "M:External.EntityBase.Save(System.Boolean)";
        const string redirectHatch = "M:External.EntityBase.Save(External.IPredicate,System.Boolean)";
        const string safeSlot = "F:App.Callbacks.Safe";
        const string safeCallable = "M:App.Callbacks.SafeTarget";
        const string escapedSlot = "F:App.Callbacks.Escaped";
        const string escapedCallable = "M:App.Callbacks.EscapedTarget";
        var handoffRules = new[] { new FactHandoffRule("background.schedule", "background", [".BackgroundProcessSchedule.#ctor"]) };
        var redirectRules = new[] { new FactRedirectRule("M:External.EntityBase.Save", redirectHatch) };
        var callerReferences = new[]
        {
            Reference(caller, "M:App.Direct", RefKinds.Invocation, line: 10),
            Reference(caller, "M:App.Direct", RefKinds.Invocation, line: 10), // raw duplicate
            Reference(caller, dispatcher, RefKinds.Ctor, line: 20),
            Reference(caller, "M:App.PrimaryCallback", RefKinds.MethodGroup, line: 21, delegateConsumer: dispatcher),
            Reference(caller, "M:App.FallbackCallback", RefKinds.MethodGroup, line: 20),
            Reference(caller, "F:App.State.Value", RefKinds.Read, line: 30),
            Reference(caller, "M:External.Unrelated.Call", RefKinds.Invocation, line: 31, targetInSource: false),
            Reference(caller, redirectedTarget, RefKinds.Invocation, line: 32, targetInSource: false),
        };
        var unrelated = Enumerable
            .Range(0, 3000)
            .ToDictionary(
                i => $"M:App.Unrelated{i}.Run",
                i => (IReadOnlyList<ReferenceFact>)[Reference($"M:App.Unrelated{i}.Run", $"M:App.Target{i}", RefKinds.Invocation, i)]
            );
        unrelated.Add(caller, callerReferences);
        var dispatchFacts = new[]
        {
            new DispatchFact(safeSlot, safeCallable, DispatchKinds.DelegateFieldBind, "/repo/A.cs"),
            new DispatchFact(safeSlot, safeCallable, DispatchKinds.DelegateFieldBind, "/repo/B.cs"),
            new DispatchFact(safeSlot, caller, DispatchKinds.DelegateFieldInvoke, "/repo/A.cs"),
            new DispatchFact(safeSlot, caller, DispatchKinds.DelegateFieldInvoke, "/repo/B.cs"),
            new DispatchFact(escapedSlot, escapedCallable, DispatchKinds.DelegateFieldBind, "/repo/A.cs"),
            new DispatchFact(escapedSlot, caller, DispatchKinds.DelegateFieldInvoke, "/repo/A.cs"),
            new DispatchFact(escapedSlot, escapedSlot, DispatchKinds.DelegateFieldEscape, "/repo/B.cs"),
            new DispatchFact("M:App.IContract.Run", caller, DispatchKinds.Impl, "/repo/Contract.cs"),
        };
        var graph = new CountingFactGraphView(unrelated, dispatchFacts, caller);
        var allReferences = unrelated.Values.SelectMany(rows => rows).ToArray();
        var full = FactGraphProjection.FromAnalysis(
            new AnalysisResult("/repo/App.sln", [], [], References: allReferences, DispatchFacts: dispatchFacts),
            handoffRules,
            redirectRules
        );

        var keyed = FactGraphProjection.CallsFrom(graph, caller, handoffRules, redirectRules);
        var correspondingFullEdges = full.CallEdges.Where(edge => edge.Caller == caller).ToArray();

        keyed.ShouldBe(correspondingFullEdges);
        keyed.Count.ShouldBe(6);
        keyed.Count(edge => edge.Callee == "M:App.Direct").ShouldBe(1);
        keyed.Single(edge => edge.Callee == dispatcher).Kind.ShouldBe(EdgeKinds.Ctor);
        var primary = keyed.Single(edge => edge.Callee == "M:App.PrimaryCallback");
        primary.Kind.ShouldBe(EdgeKinds.Handoff);
        primary.HandoffDispatcher.ShouldBe("background.schedule");
        var fallback = keyed.Single(edge => edge.Callee == "M:App.FallbackCallback");
        fallback.Kind.ShouldBe(EdgeKinds.Handoff);
        fallback.HandoffDispatcher.ShouldBe("background.schedule");
        keyed.ShouldNotContain(edge => edge.Callee == "F:App.State.Value");
        keyed.ShouldNotContain(edge => edge.Callee == "M:External.Unrelated.Call");
        keyed.Single(edge => edge.Callee == redirectHatch).Kind.ShouldBe(EdgeKinds.Invocation);
        keyed.Single(edge => edge.Callee == safeCallable).Kind.ShouldBe(EdgeKinds.DelegateField);
        keyed.ShouldNotContain(edge => edge.Callee == escapedCallable);
        graph.RequestedCallers.ShouldBe([caller]);
        graph.DispatchTargets.ShouldBe([caller]);
        graph.DispatchSources.ShouldBe([escapedSlot, safeSlot]);
    }

    private static ReferenceFact Reference(
        string caller,
        string target,
        string kind,
        int line,
        bool targetInSource = true,
        string? delegateConsumer = null
    ) => new(target, kind, caller, "App", targetInSource, "/repo/App.cs", line, DelegateConsumer: delegateConsumer);

    private sealed class CountingFactGraphView : IFactGraphView
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<ReferenceFact>> references;
        private readonly IReadOnlyList<DispatchFact> dispatchFacts;
        private readonly string allowedCaller;
        private readonly HashSet<string> allowedSlots;

        public CountingFactGraphView(
            IReadOnlyDictionary<string, IReadOnlyList<ReferenceFact>> references,
            IReadOnlyList<DispatchFact> dispatchFacts,
            string allowedCaller
        )
        {
            this.references = references;
            this.dispatchFacts = dispatchFacts;
            this.allowedCaller = allowedCaller;
            allowedSlots = dispatchFacts
                .Where(fact => fact.Kind == DispatchKinds.DelegateFieldInvoke && fact.TargetMember == allowedCaller)
                .Select(fact => fact.SourceMember)
                .ToHashSet(StringComparer.Ordinal);
        }

        public List<string> RequestedCallers { get; } = [];
        public List<string> DispatchTargets { get; } = [];
        public List<string> DispatchSources { get; } = [];

        public IReadOnlyList<ReferenceFact> ReferencesFrom(string enclosingSymbolId)
        {
            RequestedCallers.Add(enclosingSymbolId);
            return references.TryGetValue(enclosingSymbolId, out var rows) ? rows : [];
        }

        public IReadOnlyList<ReferenceFact> ReferencesTo(string targetSymbolId) => throw Unexpected();

        public IReadOnlyCollection<string> MethodSymbolIds => throw Unexpected();

        public IReadOnlyList<SymbolFact> MethodsById(string symbolId) => throw Unexpected();

        public IReadOnlyList<SymbolFact> MethodsByContainingSymbol(string containingSymbolId) => throw Unexpected();

        public IReadOnlyList<TypeRelationFact> TypeRelationsFrom(string typeSymbolId) => throw Unexpected();

        public IReadOnlyList<TypeRelationFact> TypeRelationsTo(string relatedSymbolId) => throw Unexpected();

        public IReadOnlyList<DispatchFact> DispatchFrom(string sourceMember)
        {
            if (!allowedSlots.Contains(sourceMember))
            {
                throw Unexpected();
            }

            DispatchSources.Add(sourceMember);
            return dispatchFacts.Where(fact => fact.SourceMember == sourceMember).ToArray();
        }

        public IReadOnlyList<DispatchFact> DispatchTo(string targetMember)
        {
            if (targetMember != allowedCaller)
            {
                throw Unexpected();
            }

            DispatchTargets.Add(targetMember);
            return dispatchFacts.Where(fact => fact.TargetMember == targetMember).ToArray();
        }

        private static InvalidOperationException Unexpected() => new("The keyed call projection accessed an unrelated graph family.");
    }
}
