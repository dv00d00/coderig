using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

public sealed class FactEffectSetDiffGenericTests
{
    [Test]
    public void Canonical_generic_effect_is_owned_by_each_reached_monomorph_execution()
    {
        const string a = "M:N.A.Entry";
        const string b = "M:N.B.Entry";
        const string open = "M:N.Worker.Run``1(System.String)";
        var mono = MonomorphizedNodeId.For(open, [], ["System.Int32"]);
        var graph = new FactGraphData(
            [new CallEdge(a, mono, "invocation", "f.cs", 1)],
            [],
            new[] { a, b, open, mono }.Select(id => new MethodRef(id, id, null)).ToList()
        );
        var effects = new[] { new DerivedEffect("db", "write", "N.WorkEntityCollection", open, "f.cs", 2) };

        var findings = FactEffectSetDiffDeriver.Derive(
            graph,
            effects,
            new EffectSetDiffSpec(
                [new EffectSetDiffPair("", a, b)],
                [],
                new NormalizeSpec(SimpleTypeName: true, StripSuffix: ["EntityCollection"])
            )
        );

        findings.Count.ShouldBe(1);
        findings[0].ResourceKey.ShouldBe("Work");
        findings[0].Direction.ShouldBe(EffectDiffSide.AOnly);
    }

    [Test]
    public void Effect_owned_by_one_concrete_monomorph_does_not_leak_to_a_sibling_binding()
    {
        const string a = "M:N.A.Entry";
        const string b = "M:N.B.Entry";
        const string open = "M:N.Worker.Run``1(System.String)";
        var intMono = MonomorphizedNodeId.For(open, [], ["System.Int32"]);
        var textMono = MonomorphizedNodeId.For(open, [], ["System.String"]);
        var graph = new FactGraphData(
            [new CallEdge(a, intMono, "invocation", "f.cs", 1)],
            [],
            new[] { a, b, open, intMono, textMono }.Select(id => new MethodRef(id, id, null)).ToList()
        );
        var effects = new[] { new DerivedEffect("db", "write", "N.TextOnlyEntityCollection", textMono, "f.cs", 2) };

        var findings = FactEffectSetDiffDeriver.Derive(
            graph,
            effects,
            new EffectSetDiffSpec(
                [new EffectSetDiffPair("", a, b)],
                [],
                new NormalizeSpec(SimpleTypeName: true, StripSuffix: ["EntityCollection"])
            )
        );

        findings.ShouldBeEmpty();
    }
}
