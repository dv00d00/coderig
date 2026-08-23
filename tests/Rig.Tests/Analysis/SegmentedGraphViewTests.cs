using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Rig.Analysis.Inventory;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Analysis;

public sealed class SegmentedGraphViewTests
{
    [Test]
    public void Keyed_base_and_overlay_lookups_equal_the_composite_raw_fact_multisets()
    {
        const string emitterA = "/repo/A.cs";
        const string emitterB = "/repo/B.cs";
        const string caller = "M:App.Caller";
        const string target = "M:App.Target";
        const string containing = "T:App.Service";
        const string implementation = "T:App.Implementation";
        const string contract = "T:App.IContract";
        const string contractMember = "M:App.IContract.Run";
        const string implementationMember = "M:App.Implementation.Run";

        var baseFacts = Result(
            symbols: [Method(caller, containing, emitterA, 1), Method(implementationMember, implementation, emitterB, 2)],
            references: [Reference(caller, target, emitterA, 10), Reference(caller, target, emitterB, 11)],
            relations:
            [
                new TypeRelationFact(implementation, contract, RelationKinds.Interface, emitterA),
                new TypeRelationFact(implementation, contract, RelationKinds.Interface, emitterB),
            ],
            dispatch:
            [
                new DispatchFact(contractMember, implementationMember, DispatchKinds.Impl, emitterA),
                new DispatchFact(contractMember, implementationMember, DispatchKinds.Impl, emitterB),
            ]
        );
        var replacement = Slice(
            symbols: [Method(implementationMember, implementation, emitterB, 20)],
            references: [Reference(caller, target, emitterB, 21)],
            relations: [new TypeRelationFact(implementation, contract, RelationKinds.Interface, emitterB)],
            dispatch: [new DispatchFact(contractMember, implementationMember, DispatchKinds.Impl, emitterB)]
        );
        using var workspace = new AdhocWorkspace();
        var snapshot = Snapshot(workspace.CurrentSolution, baseFacts, (emitterB, replacement));
        IFactSnapshotView composite = snapshot;
        (composite is IIndexedFactSnapshotView).ShouldBeTrue("the keyed graph must be consumable through a public Domain capability");
        var indexed = (IIndexedFactSnapshotView)composite;
        var graph = indexed.GraphView;
        ((object)baseFacts is IIndexedFactSnapshotView).ShouldBeFalse("cold AnalysisResult must not claim the resident capability");

        AssertRows(graph.ReferencesFrom(caller), snapshot.EnumerateReferences().Where(r => r.EnclosingSymbolId == caller));
        AssertRows(graph.ReferencesTo(target), snapshot.EnumerateReferences().Where(r => r.TargetSymbolId == target));
        AssertRows(graph.MethodsById(implementationMember), snapshot.EnumerateSymbols().Where(s => s.SymbolId == implementationMember));
        AssertRows(
            graph.MethodsByContainingSymbol(implementation),
            snapshot.EnumerateSymbols().Where(s => s.Kind == SymbolKinds.Method && s.ContainingSymbolId == implementation)
        );
        AssertRows(graph.TypeRelationsFrom(implementation), snapshot.EnumerateTypeRelations().Where(r => r.TypeSymbolId == implementation));
        AssertRows(graph.TypeRelationsTo(contract), snapshot.EnumerateTypeRelations().Where(r => r.RelatedSymbolId == contract));
        AssertRows(graph.DispatchFrom(contractMember), snapshot.EnumerateDispatchFacts().Where(d => d.SourceMember == contractMember));
        AssertRows(
            graph.DispatchTo(implementationMember),
            snapshot.EnumerateDispatchFacts().Where(d => d.TargetMember == implementationMember)
        );

        graph
            .MethodSymbolIds.OrderBy(x => x, StringComparer.Ordinal)
            .ShouldBe(
                snapshot
                    .EnumerateSymbols()
                    .Where(s => s.Kind == SymbolKinds.Method)
                    .Select(s => s.SymbolId)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(x => x, StringComparer.Ordinal)
            );
        graph.TypeRelationsFrom(implementation).Select(r => r.FilePath).ShouldBe([emitterA, emitterB]);
        graph.DispatchFrom(contractMember).Select(d => d.FilePath).ShouldBe([emitterA, emitterB]);
        ((SegmentedFactGraphView)graph)
            .LookupReferencesFrom(caller)
            .Diagnostics.ShouldBe(new GraphLookupDiagnostics(KeyPartitionsExamined: 2, EmitterShardsExamined: 3, RowsExamined: 3));
    }

    [Test]
    public void Dispatch_relation_indexes_cover_generic_and_error_families_across_replacement_and_tombstone()
    {
        const string emitterA = "/repo/A.cs";
        const string emitterB = "/repo/B.cs";
        var baseCat = new TypeRelationFact("T:App.CatSub", "T:App.Base`1{T:App.Cat}", RelationKinds.Base, emitterA);
        var baseDog = new TypeRelationFact("T:App.DogSub", "T:App.Base`1{T:App.Dog}", RelationKinds.Base, emitterB);
        var errorA = new TypeRelationFact("T:App.PartialA", "!:IFoo", RelationKinds.Interface, emitterA);
        var errorB = new TypeRelationFact("T:App.PartialB", "!:IFoo", RelationKinds.Interface, emitterB);
        var unrelated = new TypeRelationFact("T:App.Other", "!:IBar", RelationKinds.Interface, emitterA);
        var baseLayer = SegmentedFactGraphBase.Build(Result(relations: [baseCat, baseDog, errorA, errorB, unrelated]));
        var captured = new SegmentedFactGraphView(baseLayer, SegmentedFactGraphOverlay.Empty);

        AssertRows(captured.DispatchRelationsTo("T:App.Base`1"), [baseCat, baseDog]);
        AssertRows(captured.DispatchRelationsTo("T:App.IFoo"), [errorA, errorB]);
        captured.DispatchRelationsTo("T:App.IFoo").ShouldNotContain(unrelated);
        captured
            .LookupDispatchRelationsTo("T:App.Base`1")
            .Diagnostics.ShouldBe(new GraphLookupDiagnostics(KeyPartitionsExamined: 1, EmitterShardsExamined: 2, RowsExamined: 2));
        captured
            .LookupDispatchRelationsTo("T:App.IFoo")
            .Diagnostics.ShouldBe(new GraphLookupDiagnostics(KeyPartitionsExamined: 1, EmitterShardsExamined: 2, RowsExamined: 2));

        var replacementBase = new TypeRelationFact("T:App.HorseSub", "T:App.Base`1{T:App.Horse}", RelationKinds.Base, emitterB);
        var replacementError = new TypeRelationFact("T:App.PartialBar", "!:IBar", RelationKinds.Interface, emitterB);
        var replacedOverlay = SegmentedFactGraphOverlay.Empty.Replace(
            new Dictionary<string, FileFacts> { [emitterB] = Slice(relations: [replacementBase, replacementError]) }
        );
        var replaced = new SegmentedFactGraphView(baseLayer, replacedOverlay);

        AssertRows(replaced.DispatchRelationsTo("T:App.Base`1"), [baseCat, replacementBase]);
        AssertRows(replaced.DispatchRelationsTo("T:App.IFoo"), [errorA]);
        AssertRows(replaced.DispatchRelationsTo("T:App.IBar"), [unrelated, replacementError]);

        var tombstoned = new SegmentedFactGraphView(
            baseLayer,
            replacedOverlay.Replace(new Dictionary<string, FileFacts> { [emitterB] = Slice() })
        );
        AssertRows(tombstoned.DispatchRelationsTo("T:App.Base`1"), [baseCat]);
        AssertRows(tombstoned.DispatchRelationsTo("T:App.IFoo"), [errorA]);
        AssertRows(tombstoned.DispatchRelationsTo("T:App.IBar"), [unrelated]);

        // Captured immutable views retain their original base rows after later overlay roots replace them.
        AssertRows(captured.DispatchRelationsTo("T:App.Base`1"), [baseCat, baseDog]);
        AssertRows(captured.DispatchRelationsTo("T:App.IFoo"), [errorA, errorB]);
    }

    [Test]
    public void Normalized_reference_target_lookup_covers_overloads_and_honors_replacement_and_tombstones()
    {
        const string emitterA = "/repo/A.cs";
        const string emitterB = "/repo/B.cs";
        const string saveDocId = "M:External.Entity.Save";
        const string saveKey = "External.Entity.Save";
        const string factoryKey = "N.Entity.New";
        var saveNoArgs = Reference("M:App.CallerA", saveDocId, emitterA, 1);
        var saveBool = Reference("M:App.CallerB", $"{saveDocId}(System.Boolean)", emitterB, 2);
        var factoryInt = Reference("M:App.FactoryA", "M:N.Entity.New``3(System.Int32)", emitterA, 3);
        var factoryGuid = Reference("M:App.FactoryB", "M:N.Entity.New``3(System.Guid)", emitterB, 4);
        var unrelated = Reference("M:App.CallerC", "M:External.Entity.Delete()", emitterA, 3);
        var baseLayer = SegmentedFactGraphBase.Build(Result(references: [saveNoArgs, saveBool, factoryInt, factoryGuid, unrelated]));
        var captured = new SegmentedFactGraphView(baseLayer, SegmentedFactGraphOverlay.Empty);

        ReferenceTargetMethodKey.Normalize("M:N.Entity.New``3(System.Int32)").ShouldBe(factoryKey);
        ReferenceTargetMethodKey.Normalize(factoryKey).ShouldBe(factoryKey);
        AssertRows(captured.ReferencesToMethodKey(saveKey), [saveNoArgs, saveBool]);
        AssertRows(captured.ReferencesToMethodKey(factoryKey), [factoryInt, factoryGuid]);
        captured.ReferencesToMethodKey(saveKey).ShouldNotContain(unrelated);
        captured
            .LookupReferencesToMethodKey(saveKey)
            .Diagnostics.ShouldBe(new GraphLookupDiagnostics(KeyPartitionsExamined: 1, EmitterShardsExamined: 2, RowsExamined: 2));

        var savePredicate = Reference("M:App.CallerB2", $"{saveDocId}(External.IPredicate)", emitterB, 5);
        var factoryLong = Reference("M:App.FactoryB2", "M:N.Entity.New``3(System.Int64)", emitterB, 6);
        var replacedOverlay = SegmentedFactGraphOverlay.Empty.Replace(
            new Dictionary<string, FileFacts> { [emitterB] = Slice(references: [savePredicate, factoryLong]) }
        );
        var replaced = new SegmentedFactGraphView(baseLayer, replacedOverlay);

        AssertRows(replaced.ReferencesToMethodKey(saveKey), [saveNoArgs, savePredicate]);
        AssertRows(replaced.ReferencesToMethodKey(factoryKey), [factoryInt, factoryLong]);
        replaced.ReferencesToMethodKey(saveKey).ShouldNotContain(saveBool);
        replaced.ReferencesToMethodKey(factoryKey).ShouldNotContain(factoryGuid);

        var tombstonedOverlay = replacedOverlay
            .Replace(new Dictionary<string, FileFacts> { [emitterB] = Slice() })
            .Replace(new Dictionary<string, FileFacts> { [emitterA] = Slice() });
        var tombstoned = new SegmentedFactGraphView(baseLayer, tombstonedOverlay);

        tombstoned.ReferencesToMethodKey(saveKey).ShouldBeEmpty();
        tombstoned.ReferencesToMethodKey(factoryKey).ShouldBeEmpty();
        AssertRows(captured.ReferencesToMethodKey(saveKey), [saveNoArgs, saveBool]);
        AssertRows(captured.ReferencesToMethodKey(factoryKey), [factoryInt, factoryGuid]);
    }

    [Test]
    public void Replacement_removes_both_reference_directions_empty_tombstones_and_preserves_captured_view()
    {
        const string emitter = "/repo/Changed.cs";
        const string oldCaller = "M:App.OldCaller";
        const string oldTarget = "M:App.OldTarget";
        const string newCaller = "M:App.NewCaller";
        const string newTarget = "M:App.NewTarget";
        const string oldMethod = "M:App.OldMethod";
        const string newMethod = "M:App.NewMethod";
        const string oldContaining = "T:App.OldContaining";
        const string newContaining = "T:App.NewContaining";
        const string oldType = "T:App.OldImplementation";
        const string newType = "T:App.NewImplementation";
        const string oldRelated = "T:App.IOldContract";
        const string newRelated = "T:App.INewContract";
        const string oldDispatchSource = "M:App.IOldContract.Run";
        const string newDispatchSource = "M:App.INewContract.Run";
        const string oldDispatchTarget = "M:App.OldImplementation.Run";
        const string newDispatchTarget = "M:App.NewImplementation.Run";
        var oldMethodFact = Method(oldMethod, oldContaining, emitter, 1);
        var oldRelation = new TypeRelationFact(oldType, oldRelated, RelationKinds.Interface, emitter);
        var oldDispatch = new DispatchFact(oldDispatchSource, oldDispatchTarget, DispatchKinds.Impl, emitter);
        var baseFacts = Result(
            symbols: [oldMethodFact],
            references: [Reference(oldCaller, oldTarget, emitter, 1)],
            relations: [oldRelation],
            dispatch: [oldDispatch]
        );
        var baseLayer = SegmentedFactGraphBase.Build(baseFacts);
        var oldOverlay = SegmentedFactGraphOverlay.Empty;
        using var workspace = new AdhocWorkspace();
        var oldSnapshot = new FactSnapshot(
            new FactRevision(0),
            workspace.CurrentSolution,
            baseFacts,
            ImmutableDictionary.Create<string, FileFacts>(StringComparer.OrdinalIgnoreCase),
            DirtySet.Empty,
            SnapshotDelta.Empty,
            graphBase: baseLayer,
            graphOverlay: oldOverlay
        );
        IIndexedFactSnapshotView oldIndexed = oldSnapshot;
        var oldView = oldIndexed.GraphView;

        var replacementEmitter = emitter.ToUpperInvariant();
        var newMethodFact = Method(newMethod, newContaining, replacementEmitter, 2);
        var newRelation = new TypeRelationFact(newType, newRelated, RelationKinds.Interface, replacementEmitter);
        var newDispatch = new DispatchFact(newDispatchSource, newDispatchTarget, DispatchKinds.Impl, replacementEmitter);
        var replacement = Slice(
            symbols: [newMethodFact],
            references: [Reference(newCaller, newTarget, replacementEmitter, 2)],
            relations: [newRelation],
            dispatch: [newDispatch]
        );
        var replacedEntries = ImmutableDictionary
            .Create<string, FileFacts>(StringComparer.OrdinalIgnoreCase)
            .Add(replacementEmitter, replacement);
        var replacedOverlay = oldOverlay.Replace(replacedEntries);
        var replacedSnapshot = new FactSnapshot(
            new FactRevision(1),
            workspace.CurrentSolution,
            baseFacts,
            replacedEntries,
            DirtySet.Empty,
            SnapshotDelta.Empty,
            graphBase: baseLayer,
            graphOverlay: replacedOverlay
        );
        IIndexedFactSnapshotView replacedIndexed = replacedSnapshot;
        var replacedView = replacedIndexed.GraphView;

        oldView.ReferencesFrom(oldCaller).ShouldBe([Reference(oldCaller, oldTarget, emitter, 1)]);
        oldView.ReferencesTo(oldTarget).ShouldBe([Reference(oldCaller, oldTarget, emitter, 1)]);
        replacedView.ReferencesFrom(oldCaller).ShouldBeEmpty();
        replacedView.ReferencesTo(oldTarget).ShouldBeEmpty();
        replacedView.ReferencesFrom(newCaller).ShouldBe([Reference(newCaller, newTarget, replacementEmitter, 2)]);
        replacedView.ReferencesTo(newTarget).ShouldBe([Reference(newCaller, newTarget, replacementEmitter, 2)]);
        oldView.MethodsById(oldMethod).ShouldBe([oldMethodFact]);
        oldView.MethodsByContainingSymbol(oldContaining).ShouldBe([oldMethodFact]);
        replacedView.MethodsById(oldMethod).ShouldBeEmpty();
        replacedView.MethodsByContainingSymbol(oldContaining).ShouldBeEmpty();
        replacedView.MethodsById(newMethod).ShouldBe([newMethodFact]);
        replacedView.MethodsByContainingSymbol(newContaining).ShouldBe([newMethodFact]);
        oldView.TypeRelationsFrom(oldType).ShouldBe([oldRelation]);
        oldView.TypeRelationsTo(oldRelated).ShouldBe([oldRelation]);
        replacedView.TypeRelationsFrom(oldType).ShouldBeEmpty();
        replacedView.TypeRelationsTo(oldRelated).ShouldBeEmpty();
        replacedView.TypeRelationsFrom(newType).ShouldBe([newRelation]);
        replacedView.TypeRelationsTo(newRelated).ShouldBe([newRelation]);
        oldView.DispatchFrom(oldDispatchSource).ShouldBe([oldDispatch]);
        oldView.DispatchTo(oldDispatchTarget).ShouldBe([oldDispatch]);
        replacedView.DispatchFrom(oldDispatchSource).ShouldBeEmpty();
        replacedView.DispatchTo(oldDispatchTarget).ShouldBeEmpty();
        replacedView.DispatchFrom(newDispatchSource).ShouldBe([newDispatch]);
        replacedView.DispatchTo(newDispatchTarget).ShouldBe([newDispatch]);

        var tombstoneEntries = replacedEntries.SetItem(emitter, Slice());
        var tombstoneSnapshot = new FactSnapshot(
            new FactRevision(2),
            workspace.CurrentSolution,
            baseFacts,
            tombstoneEntries,
            DirtySet.Empty,
            SnapshotDelta.Empty,
            graphBase: baseLayer,
            graphOverlay: replacedOverlay.Replace(new Dictionary<string, FileFacts> { [emitter] = Slice() })
        );
        IIndexedFactSnapshotView tombstonedIndexed = tombstoneSnapshot;
        var tombstoned = tombstonedIndexed.GraphView;
        tombstoned.ReferencesFrom(oldCaller).ShouldBeEmpty();
        tombstoned.ReferencesTo(oldTarget).ShouldBeEmpty();
        tombstoned.ReferencesFrom(newCaller).ShouldBeEmpty();
        tombstoned.ReferencesTo(newTarget).ShouldBeEmpty();
        tombstoned.MethodsById(newMethod).ShouldBeEmpty();
        tombstoned.MethodsByContainingSymbol(newContaining).ShouldBeEmpty();
        tombstoned.TypeRelationsFrom(newType).ShouldBeEmpty();
        tombstoned.TypeRelationsTo(newRelated).ShouldBeEmpty();
        tombstoned.DispatchFrom(newDispatchSource).ShouldBeEmpty();
        tombstoned.DispatchTo(newDispatchTarget).ShouldBeEmpty();

        oldView.ReferencesFrom(oldCaller).ShouldBe([Reference(oldCaller, oldTarget, emitter, 1)]);
        oldView.MethodsById(oldMethod).ShouldBe([oldMethodFact]);
        oldView.TypeRelationsFrom(oldType).ShouldBe([oldRelation]);
        oldView.DispatchFrom(oldDispatchSource).ShouldBe([oldDispatch]);
        replacedView.ReferencesFrom(newCaller).ShouldBe([Reference(newCaller, newTarget, replacementEmitter, 2)]);
        replacedView.MethodsById(newMethod).ShouldBe([newMethodFact]);
        replacedView.TypeRelationsFrom(newType).ShouldBe([newRelation]);
        replacedView.DispatchFrom(newDispatchSource).ShouldBe([newDispatch]);
    }

    [Test]
    public void Lookup_and_replacement_touch_only_the_keyed_or_replaced_partitions_and_share_unrelated_roots()
    {
        const int unrelatedCount = 3000;
        const string tinyEmitter = "/repo/Tiny.cs";
        const string tinyCaller = "M:App.TinyCaller";
        const string tinyTarget = "M:App.TinyTarget";
        const string changedEmitter = "/repo/Changed.cs";
        const string sharedEmitter = "/repo/Shared.cs";
        const string sharedCaller = "M:App.SharedCaller";

        var unrelated = Enumerable
            .Range(0, unrelatedCount)
            .Select(i => Reference($"M:App.Caller{i}", $"M:App.Target{i}", $"/repo/F{i}.cs", i))
            .Append(Reference(tinyCaller, tinyTarget, tinyEmitter, unrelatedCount))
            .ToArray();
        var methods = Enumerable
            .Range(0, unrelatedCount)
            .Select(i => Method($"M:App.Caller{i}", $"T:App.Type{i}", $"/repo/F{i}.cs", i))
            .Append(Method(tinyCaller, "T:App.Tiny", tinyEmitter, unrelatedCount))
            .ToArray();
        var baseFacts = Result(symbols: methods, references: unrelated);
        var baseLayer = SegmentedFactGraphBase.Build(baseFacts);
        var overlay = SegmentedFactGraphOverlay.Empty.Replace(
            new Dictionary<string, FileFacts>
            {
                [changedEmitter] = Slice(references: [Reference("M:App.ChangedCaller", "M:App.ChangedTarget", changedEmitter, 1)]),
                [sharedEmitter] = Slice(references: [Reference(sharedCaller, "M:App.SharedTarget", sharedEmitter, 2)]),
            }
        );
        var before = new SegmentedFactGraphView(baseLayer, overlay);
        var unrelatedPartition = before.ReferenceForwardPartitionIdentity(sharedCaller);

        var lookup = before.LookupReferencesFrom(tinyCaller);
        lookup.Rows.ShouldBe([Reference(tinyCaller, tinyTarget, tinyEmitter, unrelatedCount)]);
        lookup.Diagnostics.ShouldBe(new GraphLookupDiagnostics(KeyPartitionsExamined: 1, EmitterShardsExamined: 1, RowsExamined: 1));

        var afterOverlay = overlay.Replace(
            new Dictionary<string, FileFacts>
            {
                [changedEmitter] = Slice(references: [Reference("M:App.ChangedCaller2", "M:App.ChangedTarget2", changedEmitter, 3)]),
            }
        );
        var after = new SegmentedFactGraphView(baseLayer, afterOverlay);

        afterOverlay.Diagnostics.ShouldBe(new GraphPartitionUpdateDiagnostics(EmitterCount: 1, RowCount: 1, PriorShardsRemoved: 3));
        after.BaseLayer.ShouldBeSameAs(before.BaseLayer);
        after.BaseLayer.Diagnostics.ShouldBe(new GraphPartitionBuildDiagnostics(1, unrelatedCount + 1, 2 * (unrelatedCount + 1)));
        after.ReferenceForwardPartitionIdentity(sharedCaller).ShouldBeSameAs(unrelatedPartition);
        after.ReferencesFrom("M:App.ChangedCaller").ShouldBeEmpty();
        after
            .ReferencesFrom("M:App.ChangedCaller2")
            .ShouldBe([Reference("M:App.ChangedCaller2", "M:App.ChangedTarget2", changedEmitter, 3)]);

        var nodeLookup = after.LookupMethodSymbolIds();
        nodeLookup
            .Rows.OrderBy(x => x, StringComparer.Ordinal)
            .ShouldBe(methods.Select(m => m.SymbolId).OrderBy(x => x, StringComparer.Ordinal));
        nodeLookup.Diagnostics.KeyPartitionsExamined.ShouldBe(unrelatedCount + 1);
        nodeLookup.Diagnostics.EmitterShardsExamined.ShouldBe(unrelatedCount + 1);
        nodeLookup.Diagnostics.RowsExamined.ShouldBe(unrelatedCount + 1);
        nodeLookup.Diagnostics.NodeKeysExamined.ShouldBe(unrelatedCount + 1);
    }

    [Test]
    public void Tombstoned_same_key_rows_are_counted_as_work_even_when_the_lookup_returns_nothing()
    {
        const int emitterCount = 100;
        const string methodId = "M:App.Shared";
        const string caller = "M:App.Caller";
        const string target = "M:App.Target";
        var emitters = Enumerable.Range(0, emitterCount).Select(i => $"/repo/F{i}.cs").ToArray();
        var baseFacts = Result(
            symbols: emitters.Select((emitter, line) => Method(methodId, "T:App.Shared", emitter, line)).ToArray(),
            references: emitters.Select((emitter, line) => Reference(caller, target, emitter, line)).ToArray()
        );
        var baseLayer = SegmentedFactGraphBase.Build(baseFacts);
        var tombstones = emitters.ToDictionary(emitter => emitter, _ => Slice(), StringComparer.OrdinalIgnoreCase);
        var view = new SegmentedFactGraphView(baseLayer, SegmentedFactGraphOverlay.Empty.Replace(tombstones));

        var references = view.LookupReferencesFrom(caller);
        references.Rows.ShouldBeEmpty();
        references.Diagnostics.ShouldBe(
            new GraphLookupDiagnostics(KeyPartitionsExamined: 1, EmitterShardsExamined: emitterCount, RowsExamined: emitterCount)
        );

        var methods = view.LookupMethodSymbolIds();
        methods.Rows.ShouldBeEmpty();
        methods.Diagnostics.ShouldBe(
            new GraphLookupDiagnostics(
                KeyPartitionsExamined: 1,
                EmitterShardsExamined: emitterCount,
                RowsExamined: emitterCount,
                NodeKeysExamined: 1
            )
        );
    }

    [Test]
    public void Duplicate_method_projection_is_stable_by_emitter_not_replacement_chronology()
    {
        const string emitterA = "/repo/A.cs";
        const string emitterB = "/repo/B.cs";
        const string methodId = "M:App.Shared";
        var baseFacts = Result();
        var baseLayer = SegmentedFactGraphBase.Build(baseFacts);
        var firstA = Method(methodId, "T:App.A", emitterA, 1);
        var methodB = Method(methodId, "T:App.B", emitterB, 2);
        var replacementA = Method(methodId, "T:App.A2", emitterA, 3);
        var entries = ImmutableDictionary
            .Create<string, FileFacts>(StringComparer.OrdinalIgnoreCase)
            .Add(emitterA, Slice(symbols: [firstA]))
            .Add(emitterB, Slice(symbols: [methodB]));
        var graphOverlay = SegmentedFactGraphOverlay
            .Empty.Replace(new Dictionary<string, FileFacts> { [emitterA] = entries[emitterA] })
            .Replace(new Dictionary<string, FileFacts> { [emitterB] = entries[emitterB] })
            .Replace(new Dictionary<string, FileFacts> { [emitterA] = Slice(symbols: [replacementA]) });
        entries = entries.SetItem(emitterA, Slice(symbols: [replacementA]));
        using var workspace = new AdhocWorkspace();
        var snapshot = new FactSnapshot(
            new FactRevision(1),
            workspace.CurrentSolution,
            baseFacts,
            entries,
            DirtySet.Empty,
            SnapshotDelta.Empty,
            graphBase: baseLayer,
            graphOverlay: graphOverlay
        );
        var graphRows = ((IIndexedFactSnapshotView)snapshot).GraphView.MethodsById(methodId);
        var compositeRows = snapshot.EnumerateSymbols().Where(s => s.SymbolId == methodId).ToArray();

        graphRows.ShouldBe(compositeRows);
        graphRows.Select(SymbolFactProjections.ToMethodRef).First().ShouldBe(SymbolFactProjections.ToMethodRef(replacementA));
    }

    private static FactSnapshot Snapshot(Solution solution, AnalysisResult baseFacts, params (string Path, FileFacts Slice)[] overlay)
    {
        var entries = overlay.ToImmutableDictionary(p => p.Path, p => p.Slice, StringComparer.OrdinalIgnoreCase);
        var graphBase = SegmentedFactGraphBase.Build(baseFacts);
        var graphOverlay = SegmentedFactGraphOverlay.Empty.Replace(entries);
        return new FactSnapshot(
            new FactRevision(0),
            solution,
            baseFacts,
            entries,
            DirtySet.Empty,
            SnapshotDelta.Empty,
            graphBase: graphBase,
            graphOverlay: graphOverlay
        );
    }

    private static AnalysisResult Result(
        IReadOnlyList<SymbolFact>? symbols = null,
        IReadOnlyList<ReferenceFact>? references = null,
        IReadOnlyList<TypeRelationFact>? relations = null,
        IReadOnlyList<DispatchFact>? dispatch = null
    ) =>
        new(
            "/repo/App.sln",
            [],
            [],
            Symbols: symbols ?? [],
            References: references ?? [],
            TypeRelations: relations ?? [],
            DispatchFacts: dispatch ?? []
        );

    private static FileFacts Slice(
        IReadOnlyList<SymbolFact>? symbols = null,
        IReadOnlyList<ReferenceFact>? references = null,
        IReadOnlyList<TypeRelationFact>? relations = null,
        IReadOnlyList<DispatchFact>? dispatch = null
    ) =>
        new(
            [],
            [],
            (symbols ?? []).ToImmutableArray(),
            (references ?? []).ToImmutableArray(),
            (relations ?? []).ToImmutableArray(),
            (dispatch ?? []).ToImmutableArray(),
            [],
            []
        );

    private static SymbolFact Method(string symbolId, string containing, string emitter, int line) =>
        new(symbolId, SymbolKinds.Method, symbolId, "App", containing, "public", "", symbolId, emitter, line, line, "App", false);

    private static ReferenceFact Reference(string caller, string target, string emitter, int line) =>
        new(target, RefKinds.Invocation, caller, "App", true, emitter, line);

    private static void AssertRows<T>(IEnumerable<T> actual, IEnumerable<T> expected)
        where T : notnull =>
        actual.OrderBy(row => RowKey(row), StringComparer.Ordinal).ShouldBe(expected.OrderBy(row => RowKey(row), StringComparer.Ordinal));

    private static string RowKey<T>(T row)
        where T : notnull => row.ToString() ?? "";
}
