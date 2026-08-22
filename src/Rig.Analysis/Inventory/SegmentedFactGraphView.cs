using System.Collections.Immutable;
using Rig.Domain.Data;

namespace Rig.Analysis.Inventory;

internal readonly record struct GraphPartitionBuildDiagnostics(int ConstructionCount, int EmitterCount, int RowCount);

internal readonly record struct GraphPartitionUpdateDiagnostics(int EmitterCount, int RowCount, int PriorShardsRemoved);

internal readonly record struct GraphLookupDiagnostics(
    int KeyPartitionsExamined,
    int EmitterShardsExamined,
    int RowsExamined,
    int NodeKeysExamined = 0
);

internal readonly record struct GraphLookupResult<T>(IReadOnlyList<T> Rows, GraphLookupDiagnostics Diagnostics);

// The cold graph-fact layer. ResidentIndex constructs exactly one of these for its lifetime; every
// snapshot shares this reference and combines it with a persistent overlay root.
internal sealed class SegmentedFactGraphBase
{
    private SegmentedFactGraphBase(AnalysisResult facts)
    {
        ReferencesByEnclosing = BaseEmitterKeyIndex<ReferenceFact>.Build(
            facts.References ?? [],
            r => r.EnclosingSymbolId,
            r => r.FilePath,
            collectEmitters: true
        );
        ReferencesByTarget = BaseEmitterKeyIndex<ReferenceFact>.Build(facts.References ?? [], r => r.TargetSymbolId, r => r.FilePath);
        var methods = (facts.Symbols ?? []).Where(s => s.Kind == SymbolKinds.Method).ToArray();
        MethodsById = BaseEmitterKeyIndex<SymbolFact>.Build(methods, s => s.SymbolId, s => s.FilePath, collectEmitters: true);
        MethodsByContaining = BaseEmitterKeyIndex<SymbolFact>.Build(methods, s => s.ContainingSymbolId, s => s.FilePath);
        TypeRelationsByType = BaseEmitterKeyIndex<TypeRelationFact>.Build(
            facts.TypeRelations ?? [],
            r => r.TypeSymbolId,
            r => r.FilePath,
            collectEmitters: true
        );
        TypeRelationsByRelated = BaseEmitterKeyIndex<TypeRelationFact>.Build(
            facts.TypeRelations ?? [],
            r => r.RelatedSymbolId,
            r => r.FilePath
        );
        DispatchBySource = BaseEmitterKeyIndex<DispatchFact>.Build(
            facts.DispatchFacts ?? [],
            d => d.SourceMember,
            d => d.FilePath,
            collectEmitters: true
        );
        DispatchByTarget = BaseEmitterKeyIndex<DispatchFact>.Build(facts.DispatchFacts ?? [], d => d.TargetMember, d => d.FilePath);

        var emitters = ReferencesByEnclosing
            .Emitters.Concat(MethodsById.Emitters)
            .Concat(TypeRelationsByType.Emitters)
            .Concat(DispatchBySource.Emitters)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Diagnostics = new GraphPartitionBuildDiagnostics(
            ConstructionCount: 1,
            EmitterCount: emitters.Count,
            RowCount: methods.Length
                + (facts.References?.Count ?? 0)
                + (facts.TypeRelations?.Count ?? 0)
                + (facts.DispatchFacts?.Count ?? 0)
        );
    }

    internal BaseEmitterKeyIndex<ReferenceFact> ReferencesByEnclosing { get; }
    internal BaseEmitterKeyIndex<ReferenceFact> ReferencesByTarget { get; }
    internal BaseEmitterKeyIndex<SymbolFact> MethodsById { get; }
    internal BaseEmitterKeyIndex<SymbolFact> MethodsByContaining { get; }
    internal BaseEmitterKeyIndex<TypeRelationFact> TypeRelationsByType { get; }
    internal BaseEmitterKeyIndex<TypeRelationFact> TypeRelationsByRelated { get; }
    internal BaseEmitterKeyIndex<DispatchFact> DispatchBySource { get; }
    internal BaseEmitterKeyIndex<DispatchFact> DispatchByTarget { get; }
    internal GraphPartitionBuildDiagnostics Diagnostics { get; }

    internal static SegmentedFactGraphBase Build(AnalysisResult facts) => new(facts);
}

// One immutable overlay-index root. Replacement touches only keys formerly/currently owned by each
// replaced emitter; unrelated KeyPartition objects remain reference-identical across generations.
internal sealed class SegmentedFactGraphOverlay
{
    private SegmentedFactGraphOverlay(
        ImmutableHashSet<string> replacedEmitters,
        OverlayEmitterKeyIndex<ReferenceFact> referencesByEnclosing,
        OverlayEmitterKeyIndex<ReferenceFact> referencesByTarget,
        OverlayEmitterKeyIndex<SymbolFact> methodsById,
        OverlayEmitterKeyIndex<SymbolFact> methodsByContaining,
        OverlayEmitterKeyIndex<TypeRelationFact> typeRelationsByType,
        OverlayEmitterKeyIndex<TypeRelationFact> typeRelationsByRelated,
        OverlayEmitterKeyIndex<DispatchFact> dispatchBySource,
        OverlayEmitterKeyIndex<DispatchFact> dispatchByTarget,
        GraphPartitionUpdateDiagnostics diagnostics
    )
    {
        ReplacedEmitters = replacedEmitters;
        ReferencesByEnclosing = referencesByEnclosing;
        ReferencesByTarget = referencesByTarget;
        MethodsById = methodsById;
        MethodsByContaining = methodsByContaining;
        TypeRelationsByType = typeRelationsByType;
        TypeRelationsByRelated = typeRelationsByRelated;
        DispatchBySource = dispatchBySource;
        DispatchByTarget = dispatchByTarget;
        Diagnostics = diagnostics;
    }

    internal static SegmentedFactGraphOverlay Empty { get; } =
        new(
            ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase),
            OverlayEmitterKeyIndex<ReferenceFact>.Empty,
            OverlayEmitterKeyIndex<ReferenceFact>.Empty,
            OverlayEmitterKeyIndex<SymbolFact>.Empty,
            OverlayEmitterKeyIndex<SymbolFact>.Empty,
            OverlayEmitterKeyIndex<TypeRelationFact>.Empty,
            OverlayEmitterKeyIndex<TypeRelationFact>.Empty,
            OverlayEmitterKeyIndex<DispatchFact>.Empty,
            OverlayEmitterKeyIndex<DispatchFact>.Empty,
            new GraphPartitionUpdateDiagnostics(0, 0, 0)
        );

    internal ImmutableHashSet<string> ReplacedEmitters { get; }
    internal OverlayEmitterKeyIndex<ReferenceFact> ReferencesByEnclosing { get; }
    internal OverlayEmitterKeyIndex<ReferenceFact> ReferencesByTarget { get; }
    internal OverlayEmitterKeyIndex<SymbolFact> MethodsById { get; }
    internal OverlayEmitterKeyIndex<SymbolFact> MethodsByContaining { get; }
    internal OverlayEmitterKeyIndex<TypeRelationFact> TypeRelationsByType { get; }
    internal OverlayEmitterKeyIndex<TypeRelationFact> TypeRelationsByRelated { get; }
    internal OverlayEmitterKeyIndex<DispatchFact> DispatchBySource { get; }
    internal OverlayEmitterKeyIndex<DispatchFact> DispatchByTarget { get; }
    internal GraphPartitionUpdateDiagnostics Diagnostics { get; }

    internal SegmentedFactGraphOverlay Replace(IReadOnlyDictionary<string, FileFacts> slices)
    {
        var replaced = ReplacedEmitters;
        var referencesByEnclosing = ReferencesByEnclosing;
        var referencesByTarget = ReferencesByTarget;
        var methodsById = MethodsById;
        var methodsByContaining = MethodsByContaining;
        var typeRelationsByType = TypeRelationsByType;
        var typeRelationsByRelated = TypeRelationsByRelated;
        var dispatchBySource = DispatchBySource;
        var dispatchByTarget = DispatchByTarget;
        var rowCount = 0;
        var priorShardsRemoved = 0;

        foreach (var (emitter, slice) in slices)
        {
            replaced = replaced.Add(emitter);
            priorShardsRemoved += referencesByEnclosing.OwnedShardCount(emitter);
            priorShardsRemoved += referencesByTarget.OwnedShardCount(emitter);
            priorShardsRemoved += methodsById.OwnedShardCount(emitter);
            priorShardsRemoved += methodsByContaining.OwnedShardCount(emitter);
            priorShardsRemoved += typeRelationsByType.OwnedShardCount(emitter);
            priorShardsRemoved += typeRelationsByRelated.OwnedShardCount(emitter);
            priorShardsRemoved += dispatchBySource.OwnedShardCount(emitter);
            priorShardsRemoved += dispatchByTarget.OwnedShardCount(emitter);
            referencesByEnclosing = referencesByEnclosing.ReplaceEmitter(emitter, slice.References, r => r.EnclosingSymbolId);
            referencesByTarget = referencesByTarget.ReplaceEmitter(emitter, slice.References, r => r.TargetSymbolId);
            var methods = slice.Symbols.Where(s => s.Kind == SymbolKinds.Method).ToArray();
            methodsById = methodsById.ReplaceEmitter(emitter, methods, s => s.SymbolId);
            methodsByContaining = methodsByContaining.ReplaceEmitter(emitter, methods, s => s.ContainingSymbolId);
            typeRelationsByType = typeRelationsByType.ReplaceEmitter(emitter, slice.TypeRelations, r => r.TypeSymbolId);
            typeRelationsByRelated = typeRelationsByRelated.ReplaceEmitter(emitter, slice.TypeRelations, r => r.RelatedSymbolId);
            dispatchBySource = dispatchBySource.ReplaceEmitter(emitter, slice.Dispatch, d => d.SourceMember);
            dispatchByTarget = dispatchByTarget.ReplaceEmitter(emitter, slice.Dispatch, d => d.TargetMember);
            rowCount += methods.Length + slice.References.Length + slice.TypeRelations.Length + slice.Dispatch.Length;
        }

        return new SegmentedFactGraphOverlay(
            replaced,
            referencesByEnclosing,
            referencesByTarget,
            methodsById,
            methodsByContaining,
            typeRelationsByType,
            typeRelationsByRelated,
            dispatchBySource,
            dispatchByTarget,
            new GraphPartitionUpdateDiagnostics(slices.Count, rowCount, priorShardsRemoved)
        );
    }
}

internal sealed class SegmentedFactGraphView(SegmentedFactGraphBase baseLayer, SegmentedFactGraphOverlay overlay) : IFactGraphView
{
    public IReadOnlyList<ReferenceFact> ReferencesFrom(string enclosingSymbolId) => LookupReferencesFrom(enclosingSymbolId).Rows;

    public IReadOnlyList<ReferenceFact> ReferencesTo(string targetSymbolId) => LookupReferencesTo(targetSymbolId).Rows;

    public IReadOnlyCollection<string> MethodSymbolIds => LookupMethodSymbolIds().Rows;

    public IReadOnlyList<SymbolFact> MethodsById(string symbolId) => Lookup(baseLayer.MethodsById, overlay.MethodsById, symbolId).Rows;

    public IReadOnlyList<SymbolFact> MethodsByContainingSymbol(string containingSymbolId) =>
        Lookup(baseLayer.MethodsByContaining, overlay.MethodsByContaining, containingSymbolId).Rows;

    public IReadOnlyList<TypeRelationFact> TypeRelationsFrom(string typeSymbolId) =>
        Lookup(baseLayer.TypeRelationsByType, overlay.TypeRelationsByType, typeSymbolId).Rows;

    public IReadOnlyList<TypeRelationFact> TypeRelationsTo(string relatedSymbolId) =>
        Lookup(baseLayer.TypeRelationsByRelated, overlay.TypeRelationsByRelated, relatedSymbolId).Rows;

    public IReadOnlyList<DispatchFact> DispatchFrom(string sourceMember) =>
        Lookup(baseLayer.DispatchBySource, overlay.DispatchBySource, sourceMember).Rows;

    public IReadOnlyList<DispatchFact> DispatchTo(string targetMember) =>
        Lookup(baseLayer.DispatchByTarget, overlay.DispatchByTarget, targetMember).Rows;

    internal SegmentedFactGraphBase BaseLayer => baseLayer;
    internal SegmentedFactGraphOverlay Overlay => overlay;

    internal GraphLookupResult<ReferenceFact> LookupReferencesFrom(string key) =>
        Lookup(baseLayer.ReferencesByEnclosing, overlay.ReferencesByEnclosing, key);

    internal GraphLookupResult<ReferenceFact> LookupReferencesTo(string key) =>
        Lookup(baseLayer.ReferencesByTarget, overlay.ReferencesByTarget, key);

    internal GraphLookupResult<string> LookupMethodSymbolIds()
    {
        var rows = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var keysExamined = 0;
        var keyPartitionsExamined = 0;
        var emitterShardsExamined = 0;
        var rowsExamined = 0;
        foreach (var key in baseLayer.MethodsById.Keys)
        {
            keysExamined++;
            var probe = baseLayer.MethodsById.ProbeActiveRows(key, overlay.ReplacedEmitters);
            keyPartitionsExamined++;
            emitterShardsExamined += probe.EmitterShardsExamined;
            rowsExamined += probe.RowsExamined;
            if (probe.HasActiveRows && seen.Add(key))
            {
                rows.Add(key);
            }
        }
        foreach (var key in overlay.MethodsById.Keys)
        {
            keysExamined++;
            if (overlay.MethodsById.HasRows(key) && seen.Add(key))
            {
                rows.Add(key);
            }
        }

        return new GraphLookupResult<string>(
            rows.ToImmutableArray(),
            new GraphLookupDiagnostics(keyPartitionsExamined, emitterShardsExamined, rowsExamined, keysExamined)
        );
    }

    internal object? ReferenceForwardPartitionIdentity(string key) => overlay.ReferencesByEnclosing.PartitionIdentity(key);

    private GraphLookupResult<T> Lookup<T>(BaseEmitterKeyIndex<T> baseIndex, OverlayEmitterKeyIndex<T> overlayIndex, string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var baseResult = baseIndex.Lookup(key, overlay.ReplacedEmitters);
        var overlayResult = overlayIndex.Lookup(key, replacedEmitters: null);
        return new GraphLookupResult<T>(
            baseResult.Rows.Count == 0 ? overlayResult.Rows
                : overlayResult.Rows.Count == 0 ? baseResult.Rows
                : baseResult.Rows.Concat(overlayResult.Rows).ToImmutableArray(),
            new GraphLookupDiagnostics(
                baseResult.Diagnostics.KeyPartitionsExamined + overlayResult.Diagnostics.KeyPartitionsExamined,
                baseResult.Diagnostics.EmitterShardsExamined + overlayResult.Diagnostics.EmitterShardsExamined,
                baseResult.Diagnostics.RowsExamined + overlayResult.Diagnostics.RowsExamined
            )
        );
    }
}

// Compact cold index: one semantic-key dictionary and one ordered flat array per key. The 2.4M-row
// reference corpus is indexed in both directions without paying for a persistent emitter dictionary
// or a duplicate emitter field at every base key; replacement filtering reads ownership from each
// queried fact row.
internal sealed class BaseEmitterKeyIndex<T>
{
    private readonly ImmutableDictionary<string, ImmutableArray<T>> _byKey;
    private readonly Func<T, string> _emitterSelector;
    private readonly ImmutableHashSet<string> _emitters;

    private BaseEmitterKeyIndex(
        ImmutableDictionary<string, ImmutableArray<T>> byKey,
        Func<T, string> emitterSelector,
        ImmutableHashSet<string> emitters
    )
    {
        _byKey = byKey;
        _emitterSelector = emitterSelector;
        _emitters = emitters;
    }

    internal IEnumerable<string> Keys => _byKey.Keys;
    internal IEnumerable<string> Emitters => _emitters;

    internal static BaseEmitterKeyIndex<T> Build(
        IEnumerable<T> rows,
        Func<T, string?> keySelector,
        Func<T, string> emitterSelector,
        bool collectEmitters = false
    )
    {
        var byKey = new Dictionary<string, List<T>>(StringComparer.Ordinal);
        var emitters = collectEmitters ? ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase) : null;
        foreach (var row in rows)
        {
            emitters?.Add(emitterSelector(row));
            var key = keySelector(row);
            if (key is null)
            {
                continue;
            }
            if (!byKey.TryGetValue(key, out var keyedRows))
            {
                byKey[key] = keyedRows = [];
            }
            keyedRows.Add(row);
        }

        return new BaseEmitterKeyIndex<T>(
            byKey.ToImmutableDictionary(p => p.Key, p => p.Value.ToImmutableArray(), StringComparer.Ordinal),
            emitterSelector,
            emitters?.ToImmutable() ?? ImmutableHashSet<string>.Empty
        );
    }

    internal GraphLookupResult<T> Lookup(string key, ImmutableHashSet<string> replacedEmitters)
    {
        if (!_byKey.TryGetValue(key, out var partition))
        {
            return new GraphLookupResult<T>(ImmutableArray<T>.Empty, new GraphLookupDiagnostics(0, 0, 0));
        }

        var rows = ImmutableArray.CreateBuilder<T>();
        var examinedEmitters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in partition)
        {
            var emitter = _emitterSelector(row);
            examinedEmitters.Add(emitter);
            if (replacedEmitters.Contains(emitter))
            {
                continue;
            }
            rows.Add(row);
        }
        return new GraphLookupResult<T>(rows.ToImmutable(), new GraphLookupDiagnostics(1, examinedEmitters.Count, partition.Length));
    }

    internal ActiveRowProbe ProbeActiveRows(string key, ImmutableHashSet<string> replacedEmitters)
    {
        if (!_byKey.TryGetValue(key, out var partition))
        {
            return default;
        }

        var examinedEmitters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rowsExamined = 0;
        foreach (var row in partition)
        {
            rowsExamined++;
            var emitter = _emitterSelector(row);
            examinedEmitters.Add(emitter);
            if (!replacedEmitters.Contains(emitter))
            {
                return new ActiveRowProbe(true, examinedEmitters.Count, rowsExamined);
            }
        }
        return new ActiveRowProbe(false, examinedEmitters.Count, rowsExamined);
    }
}

internal readonly record struct ActiveRowProbe(bool HasActiveRows, int EmitterShardsExamined, int RowsExamined);

internal sealed class OverlayEmitterKeyIndex<T>
{
    private readonly ImmutableDictionary<string, KeyPartition<T>> _byKey;
    private readonly ImmutableDictionary<string, ImmutableHashSet<string>> _keysByEmitter;

    private OverlayEmitterKeyIndex(
        ImmutableDictionary<string, KeyPartition<T>> byKey,
        ImmutableDictionary<string, ImmutableHashSet<string>> keysByEmitter
    )
    {
        _byKey = byKey;
        _keysByEmitter = keysByEmitter;
    }

    internal static OverlayEmitterKeyIndex<T> Empty { get; } =
        new(
            ImmutableDictionary.Create<string, KeyPartition<T>>(StringComparer.Ordinal),
            ImmutableDictionary.Create<string, ImmutableHashSet<string>>(StringComparer.OrdinalIgnoreCase)
        );

    internal IEnumerable<string> Keys => _byKey.Keys;

    internal OverlayEmitterKeyIndex<T> ReplaceEmitter(string emitter, IEnumerable<T> rows, Func<T, string?> keySelector)
    {
        var byKey = _byKey;
        if (_keysByEmitter.TryGetValue(emitter, out var oldKeys))
        {
            foreach (var key in oldKeys)
            {
                var reduced = byKey[key].WithoutEmitter(emitter);
                byKey = reduced.IsEmpty ? byKey.Remove(key) : byKey.SetItem(key, reduced);
            }
        }

        var grouped = new Dictionary<string, List<T>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = keySelector(row);
            if (key is null)
            {
                continue;
            }
            if (!grouped.TryGetValue(key, out var keyedRows))
            {
                grouped[key] = keyedRows = [];
            }
            keyedRows.Add(row);
        }
        foreach (var (key, keyedRows) in grouped)
        {
            var partition = byKey.TryGetValue(key, out var existing) ? existing : KeyPartition<T>.Empty;
            byKey = byKey.SetItem(key, partition.Append(emitter, keyedRows));
        }

        var keysByEmitter = _keysByEmitter.SetItem(emitter, grouped.Keys.ToImmutableHashSet(StringComparer.Ordinal));
        return new OverlayEmitterKeyIndex<T>(byKey, keysByEmitter);
    }

    internal GraphLookupResult<T> Lookup(string key, ImmutableHashSet<string>? replacedEmitters)
    {
        if (!_byKey.TryGetValue(key, out var partition))
        {
            return new GraphLookupResult<T>(ImmutableArray<T>.Empty, new GraphLookupDiagnostics(0, 0, 0));
        }
        return partition.Lookup(replacedEmitters);
    }

    internal bool HasRows(string key) => _byKey.TryGetValue(key, out var partition) && !partition.IsEmpty;

    internal bool HasActiveRows(string key, ImmutableHashSet<string> replacedEmitters) =>
        _byKey.TryGetValue(key, out var partition) && partition.HasActiveRows(replacedEmitters);

    internal object? PartitionIdentity(string key) => _byKey.TryGetValue(key, out var partition) ? partition : null;

    internal int OwnedShardCount(string emitter)
    {
        if (!_keysByEmitter.TryGetValue(emitter, out var keys))
        {
            return 0;
        }

        var count = 0;
        foreach (var key in keys)
        {
            count += _byKey[key].OwnedShardCount(emitter);
        }
        return count;
    }
}

internal sealed class KeyPartition<T>
{
    private readonly ImmutableDictionary<string, ImmutableArray<T>> _rowsByEmitter;

    private KeyPartition(ImmutableDictionary<string, ImmutableArray<T>> rowsByEmitter) => _rowsByEmitter = rowsByEmitter;

    internal static KeyPartition<T> Empty { get; } =
        new(ImmutableDictionary.Create<string, ImmutableArray<T>>(StringComparer.OrdinalIgnoreCase));
    internal bool IsEmpty => _rowsByEmitter.IsEmpty;

    internal KeyPartition<T> WithoutEmitter(string emitter)
    {
        if (!_rowsByEmitter.ContainsKey(emitter))
        {
            return this;
        }
        return new KeyPartition<T>(_rowsByEmitter.Remove(emitter));
    }

    internal KeyPartition<T> Append(string emitter, IEnumerable<T> rows) => new(_rowsByEmitter.SetItem(emitter, rows.ToImmutableArray()));

    internal bool HasActiveRows(ImmutableHashSet<string> replacedEmitters) =>
        _rowsByEmitter.Any(p => !replacedEmitters.Contains(p.Key) && !p.Value.IsEmpty);

    internal int OwnedShardCount(string emitter) => _rowsByEmitter.ContainsKey(emitter) ? 1 : 0;

    internal GraphLookupResult<T> Lookup(ImmutableHashSet<string>? replacedEmitters)
    {
        var rows = new List<T>();
        var shardsExamined = 0;
        var rowsExamined = 0;
        foreach (var (emitter, emitterRows) in _rowsByEmitter.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (replacedEmitters?.Contains(emitter) == true)
            {
                continue;
            }
            shardsExamined++;
            rowsExamined += emitterRows.Length;
            rows.AddRange(emitterRows);
        }
        return new GraphLookupResult<T>(rows.ToImmutableArray(), new GraphLookupDiagnostics(1, shardsExamined, rowsExamined));
    }
}
