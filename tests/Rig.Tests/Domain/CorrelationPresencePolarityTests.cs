using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

// Polarity=Presence over the same generic correlation operator FR-7 uses at Polarity=Absence: flag anchors that
// HAVE a companion on their forward closure, and carry the WITNESS that made them fire. The instance under test
// is cross_method_amplification — anchor = the iteration-fanout pseudo-event (whose EnclosingSymbolId is the CALLEE,
// so the reach means "at or beneath the per-iteration call"), companion = any read.
//
// The Absence path must not move: its findings carry NULL in every witness field, which is what keeps the FR-7
// golden output byte-identical now that CorrelationFinding has them.
public sealed class CorrelationPresencePolarityTests
{
    private static MethodRef M(string id) => new(id, id, null);

    private static FactGraphData Graph(params CallEdge[] edges)
    {
        var nodes = edges.SelectMany(e => new[] { e.Caller, e.Callee }).Distinct(StringComparer.Ordinal).Select(M).ToArray();
        return new FactGraphData(edges, Array.Empty<ImplementsEdge>(), nodes);
    }

    private static CallEdge Edge(string caller, string callee) =>
        new(Caller: caller, Callee: callee, Kind: "invocation", FilePath: "f.cs", Line: 1);

    // The pseudo-event: enclosing = the CALLEE, location = the call site.
    private static DerivedEffect Fanout(string callee, string key = "id", int line = 12) =>
        new(
            Provider: FactIterationFanoutDeriver.Provider,
            Operation: FactIterationFanoutDeriver.Operation,
            ResourceType: key,
            EnclosingSymbolId: callee,
            FilePath: "Page.cs",
            Line: line
        );

    private static DerivedEffect Read(string enclosing, string resource = "N.AccountEntity", int line = 77) =>
        new(Provider: "llblgen", Operation: "read", ResourceType: resource, EnclosingSymbolId: enclosing, FilePath: "Dao.cs", Line: line);

    private static CorrelationSpec Spec(int maxDepth = 6, int maxWitnesses = 0) =>
        new(
            Anchor: new EffectPredicate(FactIterationFanoutDeriver.Provider, FactIterationFanoutDeriver.Operation),
            Companion: new EffectPredicate(FactIterationFanoutDeriver.Provider),
            AnchorNormalize: new NormalizeSpec(),
            CompanionNormalize: new NormalizeSpec(),
            Polarity: CorrelationPolarity.Presence,
            KeyMatch: CorrelationKeyMatch.PropagatedKeyToken,
            Companions: [new EffectPredicate("llblgen", "read")],
            MaxDepth: maxDepth,
            MaxWitnessesPerAnchor: maxWitnesses
        );

    // Depth 0: the read is in the callee's own body — the common `foreach (x in xs) Helper.Load(x)` shape, and
    // the case that only works because a seed's reach set includes the seed itself.
    [Test]
    public void A_read_in_the_callees_own_body_is_a_depth_zero_witness()
    {
        var callee = "M:N.Helper.Load(System.Int64)";
        var graph = Graph(Edge("M:N.Page.Render", callee));

        var finding = FactCorrelationDeriver.Derive(graph, [Fanout(callee), Read(callee)], Spec()).ShouldHaveSingleItem();

        finding.Method.ShouldBe(callee);
        finding.FilePath.ShouldBe("Page.cs");
        finding.Line.ShouldBe(12);
        finding.ResourceKey.ShouldBe("id");
        finding.WitnessMethod.ShouldBe(callee);
        finding.WitnessProvider.ShouldBe("llblgen");
        finding.WitnessOperation.ShouldBe("read");
        finding.WitnessResourceKey.ShouldBe("N.AccountEntity");
        finding.WitnessLine.ShouldBe(77);
        finding.WitnessDepth.ShouldBe(0);
    }

    [Test]
    public void A_read_two_frames_beneath_the_looped_call_is_found_with_its_depth()
    {
        var callee = "M:N.Helper.Load(System.Int64)";
        var graph = Graph(Edge("M:N.Page.Render", callee), Edge(callee, "M:N.Mid.Step"), Edge("M:N.Mid.Step", "M:N.Dao.Fetch"));

        var finding = FactCorrelationDeriver.Derive(graph, [Fanout(callee), Read("M:N.Dao.Fetch")], Spec()).ShouldHaveSingleItem();

        finding.WitnessMethod.ShouldBe("M:N.Dao.Fetch");
        finding.WitnessDepth.ShouldBe(2);
    }

    // A KEYLESS anchor is a finding: presence is the finding, so an empty key token is data, not a gate. Without
    // this the whole for/while/do population — and every site whose argument surface was never captured —
    // silently vanishes from the dataset.
    [Test]
    public void A_keyless_anchor_still_fires()
    {
        var callee = "M:N.Helper.Load()";
        var graph = Graph(Edge("M:N.Page.Render", callee));

        var finding = FactCorrelationDeriver.Derive(graph, [Fanout(callee, key: ""), Read(callee)], Spec()).ShouldHaveSingleItem();

        finding.ResourceKey.ShouldBe("");
        finding.WitnessDepth.ShouldBe(0);
    }

    [Test]
    public void An_anchor_with_no_read_beneath_it_does_not_fire()
    {
        var callee = "M:N.Helper.Format(System.Int64)";
        var graph = Graph(Edge("M:N.Page.Render", callee));

        FactCorrelationDeriver.Derive(graph, [Fanout(callee)], Spec()).ShouldBeEmpty();
    }

    // MaxWitnessesPerAnchor = 0 is the DATASET grain: every (anchor, witness) pair, so a cross-tab can see the
    // whole witness population. The finding grain (1) keeps only the nearest.
    [Test]
    public void Zero_max_witnesses_emits_the_full_cross_product_one_keeps_the_nearest()
    {
        var callee = "M:N.Helper.Load(System.Int64)";
        var graph = Graph(Edge("M:N.Page.Render", callee), Edge(callee, "M:N.Mid.Step"), Edge("M:N.Mid.Step", "M:N.Dao.Fetch"));
        var effects = new List<DerivedEffect> { Fanout(callee), Read("M:N.Mid.Step"), Read("M:N.Dao.Fetch") };

        FactCorrelationDeriver.Derive(graph, effects, Spec()).Count.ShouldBe(2);

        var nearest = FactCorrelationDeriver.Derive(graph, effects, Spec(maxWitnesses: 1)).ShouldHaveSingleItem();
        nearest.WitnessMethod.ShouldBe("M:N.Mid.Step");
        nearest.WitnessDepth.ShouldBe(1);
    }

    // The depth bound is a RESOURCE bound, not a semantic one — but it is still a bound, and the dataset must
    // report depth rather than have it silently truncate at a tighter default.
    [Test]
    public void The_depth_bound_excludes_a_read_beyond_it()
    {
        var callee = "M:N.Helper.Load(System.Int64)";
        var graph = Graph(Edge("M:N.Page.Render", callee), Edge(callee, "M:N.Mid.Step"), Edge("M:N.Mid.Step", "M:N.Dao.Fetch"));
        var effects = new List<DerivedEffect> { Fanout(callee), Read("M:N.Dao.Fetch") };

        FactCorrelationDeriver.Derive(graph, effects, Spec(maxDepth: 2)).ShouldNotBeEmpty();
        FactCorrelationDeriver.Derive(graph, effects, Spec(maxDepth: 1)).ShouldBeEmpty();
    }

    // The read gate is a SET of provider:operation predicates (a read is any of ~7 pairs), and one reach must
    // serve all of them — a companion outside the set is not a witness.
    [Test]
    public void Only_effects_in_the_read_gate_are_witnesses()
    {
        var callee = "M:N.Helper.Save(System.Int64)";
        var graph = Graph(Edge("M:N.Page.Render", callee));
        var write = new DerivedEffect(
            Provider: "llblgen",
            Operation: "bulk_write",
            ResourceType: "N.AccountEntity",
            EnclosingSymbolId: callee,
            FilePath: "Dao.cs",
            Line: 5
        );

        FactCorrelationDeriver.Derive(graph, [Fanout(callee), write], Spec()).ShouldBeEmpty();
    }

    // The FR-7 guard: an Absence finding carries NULL in every witness field, so adding them cannot have moved
    // the cache_coherence output.
    [Test]
    public void An_absence_finding_carries_no_witness()
    {
        var graph = Graph(Edge("M:N.Importer.Run", "M:N.AccountEntityCollection.UpdateMulti(System.Object)"));
        var anchor = new DerivedEffect(
            Provider: "llblgen",
            Operation: "bulk_write",
            ResourceType: "N.AccountEntityCollection",
            EnclosingSymbolId: "M:N.Importer.Run",
            FilePath: "f.cs",
            Line: 42
        );

        var finding = FactCorrelationDeriver
            .Derive(
                graph,
                [anchor],
                new CorrelationSpec(
                    Anchor: new EffectPredicate("llblgen", "bulk_write"),
                    Companion: new EffectPredicate("cache", "invalidate"),
                    AnchorNormalize: new NormalizeSpec(SimpleTypeName: true, StripSuffix: ["EntityCollection"]),
                    CompanionNormalize: new NormalizeSpec(SimpleTypeName: true, StripSuffix: ["Cache"])
                )
            )
            .ShouldHaveSingleItem();

        finding.ResourceKey.ShouldBe("Account");
        finding.WitnessMethod.ShouldBeNull();
        finding.WitnessDepth.ShouldBeNull();
        finding.WitnessProvider.ShouldBeNull();
        finding.WitnessDispatchBasis.ShouldBeNull();
    }
}
