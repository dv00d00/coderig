using System.Collections.Immutable;
using Rig.Analysis.Inventory;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Analysis;

public sealed class GraphSymbolCatalogTests
{
    [Test]
    public void All_symbol_keys_preserve_raw_rows_while_method_catalogs_remain_method_only()
    {
        const string emitter = "/repo/Service.cs";
        const string typeId = "T:App.Service";
        const string methodId = "M:App.Service.Run";
        const string lambdaId = "M:App.Service.Run~lambda1";
        var type = Symbol(typeId, SymbolKinds.Type, containing: null, emitter, line: 1);
        var method = Symbol(methodId, SymbolKinds.Method, typeId, emitter, line: 4);
        var duplicateMethod = method with { BodyHash = "duplicate-emission" };
        var lambda = Symbol(lambdaId, SymbolKinds.Method, methodId, emitter, line: 6);
        var baseLayer = SegmentedFactGraphBase.Build(Result([type, method, duplicateMethod, lambda]));
        var graph = new SegmentedFactGraphView(baseLayer, SegmentedFactGraphOverlay.Empty);

        graph.SymbolsById(typeId).ShouldBe([type]);
        graph.SymbolsById(methodId).ShouldBe([method, duplicateMethod]);
        graph.SymbolsByContainingSymbol(typeId).ShouldBe([method, duplicateMethod]);
        graph.SymbolsByContainingSymbol(methodId).ShouldBe([lambda]);
        graph.MethodsById(typeId).ShouldBeEmpty();
        graph.MethodsById(methodId).ShouldBe([method, duplicateMethod]);
        graph.MethodsByContainingSymbol(methodId).ShouldBe([lambda]);
        graph.MethodSymbolIds.OrderBy(id => id, StringComparer.Ordinal).ShouldBe([methodId, lambdaId]);
        baseLayer.Diagnostics.ShouldBe(new GraphPartitionBuildDiagnostics(ConstructionCount: 1, EmitterCount: 1, RowCount: 4));
    }

    [Test]
    public void Replacement_and_empty_tombstone_remove_non_method_symbol_shards()
    {
        const string emitter = "/repo/Changed.cs";
        var oldType = Symbol("T:App.Old", SymbolKinds.Type, containing: null, emitter, line: 1);
        var newType = Symbol("T:App.New", SymbolKinds.Type, containing: null, emitter.ToUpperInvariant(), line: 2);
        var baseLayer = SegmentedFactGraphBase.Build(Result([oldType]));
        var replacement = SegmentedFactGraphOverlay.Empty.Replace(
            new Dictionary<string, FileFacts>(StringComparer.OrdinalIgnoreCase) { [emitter.ToUpperInvariant()] = Slice([newType]) }
        );
        var replaced = new SegmentedFactGraphView(baseLayer, replacement);

        replaced.SymbolsById(oldType.SymbolId).ShouldBeEmpty();
        replaced.SymbolsById(newType.SymbolId).ShouldBe([newType]);
        replaced.MethodSymbolIds.ShouldBeEmpty();
        replacement.Diagnostics.ShouldBe(new GraphPartitionUpdateDiagnostics(EmitterCount: 1, RowCount: 1, PriorShardsRemoved: 0));

        var tombstone = replacement.Replace(new Dictionary<string, FileFacts>(StringComparer.OrdinalIgnoreCase) { [emitter] = Slice([]) });
        var tombstoned = new SegmentedFactGraphView(baseLayer, tombstone);

        tombstoned.SymbolsById(oldType.SymbolId).ShouldBeEmpty();
        tombstoned.SymbolsById(newType.SymbolId).ShouldBeEmpty();
        tombstoned.SymbolsByContainingSymbol("T:App.Parent").ShouldBeEmpty();
        tombstone.Diagnostics.RowCount.ShouldBe(0);
        replaced.SymbolsById(newType.SymbolId).ShouldBe([newType]);
    }

    [Test]
    public void Reversed_overlay_chronology_preserves_raw_duplicates_and_canonical_method_location()
    {
        const string methodId = "M:App.Shared.Run";
        const string emitterA = "/repo/A.cs";
        const string emitterB = "/repo/B.cs";
        var rowA = Symbol(methodId, SymbolKinds.Method, "T:App.Shared", emitterA, line: 3);
        var duplicateA = rowA with { BodyHash = "second-emission" };
        var rowB = Symbol(methodId, SymbolKinds.Method, "T:App.Other", emitterB, line: 30);
        var sliceA = Slice([rowA, duplicateA]);
        var sliceB = Slice([rowB]);
        var forward = SegmentedFactGraphOverlay
            .Empty.Replace(new Dictionary<string, FileFacts> { [emitterA] = sliceA })
            .Replace(new Dictionary<string, FileFacts> { [emitterB] = sliceB });
        var reversed = SegmentedFactGraphOverlay
            .Empty.Replace(new Dictionary<string, FileFacts> { [emitterB] = sliceB })
            .Replace(new Dictionary<string, FileFacts> { [emitterA] = sliceA });
        var baseLayer = SegmentedFactGraphBase.Build(Result([]));
        var forwardRows = new SegmentedFactGraphView(baseLayer, forward).SymbolsById(methodId);
        var reversedRows = new SegmentedFactGraphView(baseLayer, reversed).SymbolsById(methodId);

        forwardRows.ShouldBe([rowA, duplicateA, rowB]);
        reversedRows.ShouldBe(forwardRows);
        var forwardCanonical = SymbolFactProjections
            .SelectCanonicalMethodFacts(forwardRows)
            .Select(SymbolFactProjections.ToMethodRef)
            .ToArray();
        var reversedCanonical = SymbolFactProjections
            .SelectCanonicalMethodFacts(reversedRows)
            .Select(SymbolFactProjections.ToMethodRef)
            .ToArray();
        reversedCanonical.ShouldBe(forwardCanonical);
        forwardCanonical.ShouldBe([SymbolFactProjections.ToMethodRef(rowA)]);
    }

    [Test]
    public void Base_overlay_replacement_and_tombstone_never_leave_a_stale_canonical_method()
    {
        const string methodId = "M:App.Shared.Run";
        const string emitterA = "/repo/A.cs";
        const string emitterB = "/repo/B.cs";
        var staleA = Symbol(methodId, SymbolKinds.Method, "T:App.Stale", emitterA, line: 90);
        var survivingB = Symbol(methodId, SymbolKinds.Method, "T:App.B", emitterB, line: 20);
        var canonicalA = Symbol(methodId, SymbolKinds.Method, "T:App.A", emitterA, line: 2);
        var baseLayer = SegmentedFactGraphBase.Build(Result([staleA, survivingB]));
        var replacement = SegmentedFactGraphOverlay.Empty.Replace(new Dictionary<string, FileFacts> { [emitterA] = Slice([canonicalA]) });
        var replacedRows = new SegmentedFactGraphView(baseLayer, replacement).SymbolsById(methodId);

        replacedRows.ShouldBe([survivingB, canonicalA]);
        SymbolFactProjections.SelectCanonicalMethodFacts(replacedRows).ShouldBe([canonicalA]);

        var tombstone = replacement.Replace(new Dictionary<string, FileFacts> { [emitterA] = Slice([]) });
        var tombstonedRows = new SegmentedFactGraphView(baseLayer, tombstone).SymbolsById(methodId);

        tombstonedRows.ShouldBe([survivingB]);
        SymbolFactProjections.SelectCanonicalMethodFacts(tombstonedRows).ShouldBe([survivingB]);
        replacedRows.ShouldBe([survivingB, canonicalA]);
    }

    [Test]
    public void Method_catalog_diagnostics_include_non_method_overlay_scan_work()
    {
        const string emitter = "/repo/Overlay.cs";
        const string methodId = "M:App.Service.Run";
        var nonMethods = Enumerable
            .Range(0, 12)
            .Select(line => Symbol("T:App.Duplicate", SymbolKinds.Type, containing: null, emitter, line))
            .ToArray();
        var method = Symbol(methodId, SymbolKinds.Method, "T:App.Service", emitter, line: 20);
        var overlay = SegmentedFactGraphOverlay.Empty.Replace(
            new Dictionary<string, FileFacts> { [emitter] = Slice([.. nonMethods, method]) }
        );
        var graph = new SegmentedFactGraphView(SegmentedFactGraphBase.Build(Result([])), overlay);

        var catalog = graph.LookupMethodSymbolIds();

        catalog.Rows.ShouldBe([methodId]);
        catalog.Diagnostics.ShouldBe(
            new GraphLookupDiagnostics(KeyPartitionsExamined: 2, EmitterShardsExamined: 2, RowsExamined: 13, NodeKeysExamined: 2)
        );
    }

    private static AnalysisResult Result(IReadOnlyList<SymbolFact> symbols) => new("/repo/App.sln", [], [], Symbols: symbols);

    private static FileFacts Slice(IReadOnlyList<SymbolFact> symbols) => new([], [], symbols.ToImmutableArray(), [], [], [], [], []);

    private static SymbolFact Symbol(string symbolId, string kind, string? containing, string emitter, int line) =>
        new(
            symbolId,
            kind,
            symbolId,
            "App",
            containing,
            "public",
            kind == SymbolKinds.Type ? "class" : "",
            symbolId,
            emitter,
            line,
            line,
            "App",
            false,
            BodyHash: $"body-{line}"
        );
}
