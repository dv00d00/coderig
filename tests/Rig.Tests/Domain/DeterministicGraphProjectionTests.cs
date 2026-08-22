using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

public sealed class DeterministicGraphProjectionTests
{
    [Test]
    public void Reversed_symbol_enumeration_produces_the_same_canonical_method_refs()
    {
        const string sharedId = "M:App.Service.Run";
        var canonical = Method(sharedId, "/repo/A.cs", line: 4, name: "Run", containing: "T:App.Service");
        var duplicate = Method(sharedId, "/repo/Z.cs", line: 40, name: "RunDuplicate", containing: "T:App.Other");
        var other = Method("M:App.Other.Go", "/repo/B.cs", line: 8, name: "Go", containing: "T:App.Other");
        var type = new SymbolFact(
            "T:App.Service",
            SymbolKinds.Type,
            "Service",
            "App",
            null,
            "public",
            "class",
            "Service",
            "/repo/A.cs",
            1,
            20,
            "App",
            false
        );
        SymbolFact[] symbols = [duplicate, type, other, canonical];

        var forward = SymbolFactProjections.SelectCanonicalMethodFacts(symbols).Select(SymbolFactProjections.ToMethodRef).ToArray();
        var reversed = SymbolFactProjections
            .SelectCanonicalMethodFacts(symbols.Reverse())
            .Select(SymbolFactProjections.ToMethodRef)
            .ToArray();

        reversed.ShouldBe(forward);
        forward.Select(method => method.SymbolId).ShouldBe([sharedId, other.SymbolId]);
        var selected = forward.Single(method => method.SymbolId == sharedId);
        selected.FilePath.ShouldBe(canonical.FilePath);
        selected.Line.ShouldBe(canonical.Line);
        selected.Name.ShouldBe(canonical.Name);
        selected.ContainingTypeId.ShouldBe(canonical.ContainingSymbolId);
    }

    [Test]
    public void Canonical_selection_uses_later_symbol_fields_only_after_location_and_identity_fields_tie()
    {
        const string symbolId = "M:App.Service.Run";
        var laterSignature = Method(symbolId, "/repo/A.cs", line: 4, name: "Run", containing: "T:App.Service") with
        {
            Signature = "Run(Z value)",
            BodyHash = "aaa",
        };
        var earlierSignature = laterSignature with { Signature = "Run(A value)", BodyHash = "zzz" };

        var selected = SymbolFactProjections.SelectCanonicalMethodFacts([laterSignature, earlierSignature]);

        selected.ShouldBe([earlierSignature]);
    }

    private static SymbolFact Method(string symbolId, string filePath, int line, string name, string? containing) =>
        new(
            symbolId,
            SymbolKinds.Method,
            name,
            "App",
            containing,
            "public",
            "",
            $"{name}()",
            filePath,
            line,
            line + 2,
            "App",
            false,
            BodyHash: $"body-{line}",
            SurfaceHash: $"surface-{line}"
        );
}
