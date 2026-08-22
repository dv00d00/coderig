using Rig.Domain.Data;

namespace Rig.Domain.Functions;

// The SINGLE source of truth for the four `symbol_facts` -> domain-record mappings: SymbolFact rows in,
// MethodRef (the call-graph descriptor) / MethodSymbol (the entry-point deriver's) / TypeSymbol /
// DeadCodeFinder.MethodMeta out. Every read path funnels through these, so a field can no longer be present
// on one path and missing on another:
//
//   MethodRef  — Reads.LoadFactGraphAsync (EF, whole store), FactGraphProjection.FromAnalysis (in-memory),
//                SqlReachability.LoadReachInputsAsync (raw ADO, BOUNDED to the reach set). THREE copies,
//                including one with hand-written reader ORDINALS — exactly the shape that drifted for
//                FactInvocation (see FactInvocationProjection), so it gets the same treatment: the
//                `MethodRefColumn` enum below IS the ADO ordinal set, and the bounded loader's SELECT list is
//                generated from it (SymbolFactRows.MethodRefSelectList).
//   MethodSymbol / TypeSymbol — Reads.LoadFactEntryPointDataAsync (EF) + LiveReads.FactEntryPointData
//                (in-memory twin).
//   MethodMeta — Reads.LoadDeadCodeMethodsAsync (EF) + LiveReads.DeadCodeMethods (in-memory twin), and with
//                it the generated-file heuristic, which LiveReads had to duplicate VERBATIM because the
//                storage-layer copy was private and Rig.Domain cannot reference Rig.Storage. It lives here
//                now, so there is one copy and Rig.Storage calls it.
//
// The precedent is FactInvocationProjection / DeliverySiteProjection: the pure projection core in the domain,
// the row SUPPLY left to each caller (EF projection / raw-ADO reader / in-memory AnalysisResult) — see
// SymbolFactRows for the store-side halves. Each mapping reads only the fields listed with it, so a loader
// that supplies placeholders for the rest of the SymbolFact is still correct; that is what keeps
// the whole-store scans as narrow as they were before this was single-sourced.
//
// Most dedups remain the caller's business because their identities differ (by SymbolId for TypeSymbol/
// MethodMeta, by (FilePath, Line) for MethodSymbol). The demand-shaped graph's canonical MethodRef
// selection lives here because resident enumeration order is deliberately not semantic. It remains dormant
// until that view is wired; the existing full/store projections retain their current first-row parity.
public static class SymbolFactProjections
{
    private static readonly IComparer<SymbolFact> CanonicalSymbolComparer = new CanonicalSymbolFactComparer();

    // The symbol_facts column set MethodRef is a function of, in SELECT order. Declaration order is
    // load-bearing (it is the ADO ordinal set on the bounded path) — APPEND new members, never insert or
    // reorder. Member names are simultaneously the SymbolFact property names, the symbol_facts column names,
    // and `(int)MethodRefColumn.X` == the reader ordinal.
    public enum MethodRefColumn
    {
        SymbolId,
        Name,
        ContainingSymbolId,
        IsOverride,
        FilePath,
        Line,
    }

    // The column names in ordinal order (Enum.GetNames orders by value = declaration order for a
    // default-valued enum). Drives the bounded path's SELECT list — see SymbolFactRows.MethodRefSelectList.
    public static readonly IReadOnlyList<string> MethodRefColumns = Enum.GetNames<MethodRefColumn>();

    // symbol_facts (Kind="method") -> the call-graph method descriptor. Reads exactly MethodRefColumn.
    public static MethodRef ToMethodRef(SymbolFact s) =>
        new MethodRef(
            SymbolId: s.SymbolId,
            Name: s.Name,
            ContainingTypeId: s.ContainingSymbolId,
            IsOverride: s.IsOverride,
            FilePath: s.FilePath,
            Line: s.Line
        );

    // Selects one deterministic declaration row per method id, then returns those rows in the same
    // deterministic order. Duplicate symbol facts can legitimately come from several emitters; neither
    // extraction arrival order nor live overlay replacement chronology is a semantic tie-break.
    public static IReadOnlyList<SymbolFact> SelectCanonicalMethodFacts(IEnumerable<SymbolFact> symbols) =>
        SelectCanonicalFacts(symbols, SymbolKinds.Method);

    // Deterministic raw-catalog selector used by demand-shaped lookup paths for method and type symbols.
    public static IReadOnlyList<SymbolFact> SelectCanonicalFacts(IEnumerable<SymbolFact> symbols, string? kind = null)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        var selected = new Dictionary<string, SymbolFact>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            if (kind is not null && symbol.Kind != kind)
            {
                continue;
            }

            if (!selected.TryGetValue(symbol.SymbolId, out var current) || CanonicalSymbolComparer.Compare(symbol, current) < 0)
            {
                selected[symbol.SymbolId] = symbol;
            }
        }

        return selected.Values.Order(CanonicalSymbolComparer).ToArray();
    }

    // symbol_facts (Kind="method") -> the entry-point deriver's method record. Same source as ToMethodRef
    // plus Signature (parameter-type matching); distinct record because the two derivations need different
    // fields. Reads: SymbolId, Name, ContainingSymbolId, Signature, FilePath, Line, IsOverride.
    public static MethodSymbol ToMethodSymbol(SymbolFact s) =>
        new MethodSymbol(
            SymbolId: s.SymbolId,
            Name: s.Name,
            ContainingSymbolId: s.ContainingSymbolId,
            Signature: s.Signature,
            FilePath: s.FilePath,
            Line: s.Line,
            IsOverride: s.IsOverride
        );

    // symbol_facts (Kind="type") -> the entry-point deriver's type record. Reads: SymbolId, Namespace,
    // FilePath, Line, Modifiers. IsAbstract is a token test over the space-joined Modifiers — String.Split
    // has no SQL translation, so this runs in memory on EVERY path (the store loader projects the raw
    // Modifiers column and calls this client-side, exactly as the in-memory twin does).
    public static TypeSymbol ToTypeSymbol(SymbolFact s) =>
        new TypeSymbol(
            SymbolId: s.SymbolId,
            Namespace: s.Namespace,
            FilePath: s.FilePath,
            Line: s.Line,
            IsAbstract: s.Modifiers.Split(' ').Contains("abstract")
        );

    // symbol_facts (Kind="method") -> the dead-code finder's per-method metadata. Reads: SymbolId, Name,
    // Modifiers, FilePath, Line, IsOverride — plus the generated-file heuristic below (a function of FilePath).
    public static DeadCodeFinder.MethodMeta ToMethodMeta(SymbolFact s) =>
        new DeadCodeFinder.MethodMeta(
            SymbolId: s.SymbolId,
            Name: s.Name,
            Modifiers: s.Modifiers,
            FilePath: s.FilePath,
            Line: s.Line,
            IsOverride: s.IsOverride,
            IsGenerated: IsGeneratedPath(s.FilePath)
        );

    // Heuristic: a file is generated when it carries the conventional generated-source markers or the
    // synthetic source-generator path the loader assigns. Such members are reached via the generator /
    // build, not first-party calls, so the dead-code finder must not flag them. Public because BOTH the
    // storage loader and the in-memory twin need it and it used to exist twice (private in Rig.Storage,
    // copied verbatim into LiveReads).
    public static bool IsGeneratedPath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return false;
        }

        var p = filePath.Replace(oldChar: '\\', newChar: '/');
        return p.Contains("<generated>", StringComparison.Ordinal)
            || p.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CanonicalSymbolFactComparer : IComparer<SymbolFact>
    {
        public int Compare(SymbolFact? x, SymbolFact? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }
            if (x is null)
            {
                return -1;
            }
            if (y is null)
            {
                return 1;
            }

            var comparison = StringComparer.Ordinal.Compare(x.FilePath, y.FilePath);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = x.Line.CompareTo(y.Line);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = x.EndLine.CompareTo(y.EndLine);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = StringComparer.Ordinal.Compare(x.DefiningAssembly, y.DefiningAssembly);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = StringComparer.Ordinal.Compare(x.ContainingSymbolId, y.ContainingSymbolId);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = StringComparer.Ordinal.Compare(x.Name, y.Name);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = StringComparer.Ordinal.Compare(x.Signature, y.Signature);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = StringComparer.Ordinal.Compare(x.SymbolId, y.SymbolId);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = StringComparer.Ordinal.Compare(x.Kind, y.Kind);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = StringComparer.Ordinal.Compare(x.Namespace, y.Namespace);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = StringComparer.Ordinal.Compare(x.Modifiers, y.Modifiers);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = StringComparer.Ordinal.Compare(x.TypeKind, y.TypeKind);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = x.IsOverride.CompareTo(y.IsOverride);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = StringComparer.Ordinal.Compare(x.BodyHash, y.BodyHash);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = StringComparer.Ordinal.Compare(x.SurfaceHash, y.SurfaceHash);
            return comparison != 0 ? comparison : x.IsIterator.CompareTo(y.IsIterator);
        }
    }
}
