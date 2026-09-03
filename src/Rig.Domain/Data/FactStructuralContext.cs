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
    // LambdaParameterType is the resolved type of that lambda's FIRST parameter — the ELEMENT type when the
    // method enumerates (`profiles.Select(p => ..)` -> the profile type). It is the only element-type signal a
    // lambda iteration context has: unlike a foreach/query its detail string degenerates to the method name
    // ("p in Select"), so nothing else says WHAT is being iterated. First parameter only: that is the element for
    // Select/Where/Any/ForEach and the indexed overloads alike; an `Aggregate((acc, x) => ..)` element sits
    // SECOND and is therefore typed as the accumulator here — a known, narrow mis-read, not worth an arity table.
    public readonly record struct EnclosingInvocation(
        string ReceiverText,
        string ReceiverType,
        string MethodName,
        string DeclaringType = "",
        string LambdaParameter = "",
        string LambdaParameterType = ""
    );

    public static string? EncodeInvocations(IReadOnlyList<EnclosingInvocation> invocations) =>
        invocations.Count == 0
            ? null
            : string.Join(
                ListSeparator.ToString(),
                invocations.Select(i =>
                    $"{i.ReceiverText}{FieldSeparator}{i.ReceiverType}{FieldSeparator}{i.MethodName}{FieldSeparator}{i.DeclaringType}{FieldSeparator}{i.LambdaParameter}{FieldSeparator}{i.LambdaParameterType}"
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
            // Field count IS the version: 3 = before DeclaringType/LambdaParameter, 5 = before
            // LambdaParameterType, 6 = current. Decode the short forms rather than discard them, so the
            // pre-existing fanout/retry observations keep working against an older store; the missing fields
            // simply read as "" (no lambda parameter / no element type) until it is re-indexed.
            if (fields.Length is 3 or 5 or 6)
            {
                result.Add(
                    new EnclosingInvocation(
                        ReceiverText: fields[0],
                        ReceiverType: fields[1],
                        MethodName: fields[2],
                        DeclaringType: fields.Length >= 5 ? fields[3] : "",
                        LambdaParameter: fields.Length >= 5 ? fields[4] : "",
                        LambdaParameterType: fields.Length >= 6 ? fields[5] : ""
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

    // The guards MINUS the one a `foreach` contributes about itself. Roslyn surfaces the enumerator's
    // MoveNext as a control dependence whose predicate is the ITERATED COLLECTION, so a call that is
    // unconditional inside `foreach (var row in Rows)` still decodes with a guard whose text is `Rows`.
    // That guard says "this is a foreach", which the loop marker already said; it does NOT make the call
    // conditional.
    //
    // Shared rather than duplicated because two callers ask the same question for different reasons and must
    // not diverge: the tree renderer decides whether to draw the ⎇ glyph, and the tier-3 anchor's evidence
    // grade decides whether a looped call is UNCONDITIONAL per iteration. Measured on MedDBase 2026-09-03:
    // 619 of 1,530 guarded (anchor x witness) rows — 40% — carry nothing but this redundant guard, so a grade
    // that skipped this filter downgraded two fifths of its guarded evidence for a guard that means nothing.
    public static IReadOnlyList<(string Predicate, bool WhenTrue)> DecodeGuardsBeyondLoop(string? encoded, string? loopDetail)
    {
        var guards = DecodeGuards(encoded);
        var collection = ForeachCollection(loopDetail);
        if (guards.Count == 0 || collection is null)
        {
            return guards;
        }

        return guards.Where(g => !string.Equals(CollapseWhitespace(g.Predicate), collection, StringComparison.Ordinal)).ToList();
    }

    // The COLL of a `x in COLL` loop detail; null for while/for, which carry no iterated collection (and
    // empirically do not emit the redundant condition-guard).
    public static string? ForeachCollection(string? loopDetail)
    {
        if (string.IsNullOrEmpty(loopDetail))
        {
            return null;
        }

        var inAt = loopDetail!.IndexOf(" in ", StringComparison.Ordinal);
        return inAt < 0 ? null : CollapseWhitespace(loopDetail.Substring(inAt + 4));
    }

    // Guard predicates are source TEXT, so a multi-line condition arrives with its newlines and indentation.
    public static string CollapseWhitespace(string s) =>
        string.Join(' ', s.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
}
