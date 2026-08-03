namespace Rig.Domain.Data;

// Shared encoding for the structural-context facts on ReferenceFact (P1c). The Roslyn
// EffectObservationExtractor reasons over an invocation's ancestor invocations and try/catch
// clauses; the fact layer captures those as flat strings so the stage-2 observation deriver (P2b)
// can reproduce the observations without Roslyn. Encoding is rule-agnostic raw structure — the
// fanout/resilience/concurrency rule data lives in the rules, not here.
public static class FactStructuralContext
{
    // Separates entries in a list (enclosing invocations, caught types). ASCII record separator.
    private const char ListSeparator = '';

    // Separates fields within one enclosing-invocation entry. ASCII unit separator.
    private const char FieldSeparator = '';

    // One enclosing (ancestor) invocation: the receiver's source text (e.g. "Task", "Parallel"),
    // the receiver's resolved static type FQN ("" when unresolved, e.g. a static type access), and
    // the invoked method name. ReceiverText feeds parallel_fanout; ReceiverType feeds
    // resilience_retry.
    // DeclaringType is the resolved TARGET method's containing type — for an extension method in reduced
    // form this is the type declaring the extension (`ids.Select(..)` → "System.Linq.Enumerable"), a
    // single stable FQN across every receiver shape (List, IEnumerable, arrays, IQueryable). That is what
    // makes the enumerating-method gate both precise and high-recall: matching the RECEIVER type would
    // need an open-ended list of sequence types and would still miss custom IEnumerables.
    // LambdaParameter is the comma-joined parameter list of this invocation's lambda ARGUMENT that
    // lexically contains the effect ("p", or "x, i" for an indexed overload) — the identifiers rebound
    // per element when the method enumerates. Both are "" when absent or unresolved.
    public readonly record struct EnclosingInvocation(
        string ReceiverText,
        string ReceiverType,
        string MethodName,
        string DeclaringType = "",
        string LambdaParameter = ""
    );

    public static string? EncodeInvocations(IReadOnlyList<EnclosingInvocation> invocations) =>
        invocations.Count == 0
            ? null
            : string.Join(
                ListSeparator.ToString(),
                invocations.Select(i =>
                    $"{i.ReceiverText}{FieldSeparator}{i.ReceiverType}{FieldSeparator}{i.MethodName}{FieldSeparator}{i.DeclaringType}{FieldSeparator}{i.LambdaParameter}"
                )
            );

    public static IReadOnlyList<EnclosingInvocation> DecodeInvocations(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded))
        {
            return [];
        }

        var result = new List<EnclosingInvocation>();
        // Guarded non-null above; netstandard2.0's string.IsNullOrEmpty lacks [NotNullWhen(false)],
        // so the flow analysis can't see it — assert non-null rather than re-check.
        foreach (var entry in encoded!.Split(ListSeparator))
        {
            var fields = entry.Split(FieldSeparator);
            // 3 fields = a store written before DeclaringType/LambdaParameter existed. Decode it rather
            // than discard it, so the pre-existing fanout/retry observations keep working against an old
            // store; the enumerating-method gate simply finds no lambda parameter until it is re-indexed.
            if (fields.Length is 3 or 5)
            {
                result.Add(
                    new EnclosingInvocation(
                        ReceiverText: fields[0],
                        ReceiverType: fields[1],
                        MethodName: fields[2],
                        DeclaringType: fields.Length == 5 ? fields[3] : "",
                        LambdaParameter: fields.Length == 5 ? fields[4] : ""
                    )
                );
            }
        }

        return result;
    }

    public static string? EncodeList(IReadOnlyList<string> values) =>
        values.Count == 0 ? null : string.Join(ListSeparator.ToString(), values);

    public static IReadOnlyList<string> DecodeList(string? encoded) => string.IsNullOrEmpty(encoded) ? [] : encoded!.Split(ListSeparator);

    // One enclosing held-resource scope (innermost-first): the scope KIND ("using"|"lock") and the
    // resource's static type FQN ("" when unresolved, e.g. a `lock (someField)` whose type didn't
    // resolve). Feeds the resource_span observation (P2b ordering/nesting): a network/IO effect whose
    // scope chain contains a transaction-`using` or a `lock` is held across that effect — the
    // "transaction spans a network call" / "lock held across IO" property.
    public readonly record struct EnclosingScope(string Kind, string Type);

    public static string? EncodeScopes(IReadOnlyList<EnclosingScope> scopes) =>
        scopes.Count == 0 ? null : string.Join(ListSeparator.ToString(), scopes.Select(s => $"{s.Kind}{FieldSeparator}{s.Type}"));

    public static IReadOnlyList<EnclosingScope> DecodeScopes(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded))
        {
            return [];
        }

        var result = new List<EnclosingScope>();
        foreach (var entry in encoded!.Split(ListSeparator))
        {
            var fields = entry.Split(FieldSeparator);
            if (fields.Length == 2)
            {
                result.Add(new EnclosingScope(Kind: fields[0], Type: fields[1]));
            }
        }

        return result;
    }

    // One control-dependence guard on a call-site: the branch predicate's source TEXT (`a == null`, a
    // switch governing expr, `a` for a `?.`) and the POLARITY under which control flows toward the effect
    // (WhenTrue = the effect runs when the predicate holds). Frozen at index from
    // ControlDependence.ComputeGuards; feeds the derive-side spine/guarded partition. Intra-method only.
    public static string? EncodeGuards(IReadOnlyList<(string Predicate, bool WhenTrue)> guards) =>
        guards.Count == 0
            ? null
            : string.Join(ListSeparator.ToString(), guards.Select(g => $"{g.Predicate}{FieldSeparator}{(g.WhenTrue ? "1" : "0")}"));

    public static IReadOnlyList<(string Predicate, bool WhenTrue)> DecodeGuards(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded))
        {
            return [];
        }

        var result = new List<(string, bool)>();
        foreach (var entry in encoded!.Split(ListSeparator))
        {
            var fields = entry.Split(FieldSeparator);
            if (fields.Length == 2)
            {
                result.Add((fields[0], fields[1] == "1"));
            }
        }

        return result;
    }
}
