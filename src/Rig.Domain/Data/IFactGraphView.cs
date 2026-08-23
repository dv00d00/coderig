namespace Rig.Domain.Data;

// Optional capability carried by resident snapshots. Cold AnalysisResult values deliberately expose
// only IFactSnapshotView and therefore do not pretend to own a keyed resident graph index.
public interface IIndexedFactSnapshotView : IFactSnapshotView
{
    IFactGraphView GraphView { get; }
}

// Roslyn- and storage-free keyed access to the raw fact families needed to build and traverse a
// call graph. Implementations preserve emitted row multiplicity, but raw-row order is not a semantic
// contract; deterministic projection/deduplication remains the caller's responsibility.
public interface IFactGraphView
{
    IReadOnlyList<ReferenceFact> ReferencesFrom(string enclosingSymbolId);
    IReadOnlyList<ReferenceFact> ReferencesTo(string targetSymbolId);

    // Redirect and generic-factory rules match a method family rather than one exact overload.
    // Resident implementations provide this canonical keyed inverse so callers do not scan the corpus.
    IReadOnlyList<ReferenceFact> ReferencesToMethodKey(string methodKey) =>
        throw new NotSupportedException("This fact graph does not expose normalized reference-target lookup.");

    // Raw symbol-fact catalogs preserve every emitted row and cover every symbol kind.
    IReadOnlyList<SymbolFact> SymbolsById(string symbolId);
    IReadOnlyList<SymbolFact> SymbolsByContainingSymbol(string containingSymbolId);

    IReadOnlyCollection<string> MethodSymbolIds { get; }
    IReadOnlyList<SymbolFact> MethodsById(string symbolId);

    // The containing symbol is normally a type; synthetic lambda methods use their parent member.
    IReadOnlyList<SymbolFact> MethodsByContainingSymbol(string containingSymbolId);

    IReadOnlyList<TypeRelationFact> TypeRelationsFrom(string typeSymbolId);
    IReadOnlyList<TypeRelationFact> TypeRelationsTo(string relatedSymbolId);

    // Reverse hierarchy neighborhood used specifically by dispatch: the generic-normalized related
    // family plus unresolved-interface rows with the declaring type's simple name.
    IReadOnlyList<TypeRelationFact> DispatchRelationsTo(string declaringTypeId);

    IReadOnlyList<DispatchFact> DispatchFrom(string sourceMember);
    IReadOnlyList<DispatchFact> DispatchTo(string targetMember);
}
