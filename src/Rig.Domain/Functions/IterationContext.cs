using Rig.Domain.Data;

namespace Rig.Domain.Functions;

// The ITERATION CONTEXT of a call site, and the whole-word key-token test over an argument surface — the two
// pieces the intra-method n_plus_1 detector (FactObservationDeriver) and the cross-method iteration-fanout
// deriver (FactIterationFanoutDeriver) must agree on EXACTLY. They ask the same question of two different
// subjects: "does a READ sit in an iteration context with a per-element key" vs "does a CALL". Two copies of
// the union of iteration contexts would drift on the next context we learn about (`query` and the enumerating
// lambda were both added after the first cut), and the drift would be silent — one detector would simply see
// fewer loops than the other. Extracted verbatim, no behaviour change.
public static class IterationContext
{
    // The iteration context around a call site. Kind is null when there is none. Identifiers are the names
    // REBOUND on each iteration (a foreach's variable, a query's range variables, an enumerating lambda's
    // parameters) — empty for for/while/do, which iterate while binding nothing. Source is the ITERATED
    // EXPRESSION (the tail of "x in <expr>"), which is what a self-keyed read is self-keyed on. ElementType is
    // the RESOLVED type of the element — the semantic counterpart of Source, which is only source text, and the
    // only such signal a `lambda` context has (its detail degenerates to "x in Select"). "" when unresolved, for
    // for/while/do, and for an anonymous projection, which genuinely has no nameable element type.
    public readonly record struct Context(
        string? Kind,
        string? Detail,
        IReadOnlyList<string> Identifiers,
        string Source,
        string ElementType
    );

    public static readonly Context None = new(Kind: null, Detail: null, Identifiers: [], Source: "", ElementType: "");

    // The iteration context of a call site, from the loop facts (EnclosingLoopKind/Detail) plus the
    // rule-declared enumerating-lambda contexts among its ancestor invocations.
    //
    // A loop STATEMENT wins the reported kind when both are present (it is the coarser, outer context), but
    // the identifiers UNION: in `foreach (var a in xs) ys.Select(y => f(y))` both `a` and `y` are genuinely
    // rebound per iteration, so a key built from either is amplified.
    public static Context Of(
        string? loopKind,
        string? loopDetail,
        IReadOnlyList<FactStructuralContext.EnclosingInvocation> enclosingInvocations,
        FactObservationRules rules,
        // The loop STATEMENT's resolved element type (foreach/query). Optional so the intra-method caller, which
        // has no use for it, needs no change; an enumerating lambda's element type rides the invocation chain.
        string? loopElementType = null,
        // The declaring type of a `query` context's bind method (ReferenceFact.EnclosingLoopBindType). Gates
        // whether the query is ITERATION at all — see the enumerating gate below. Null = unresolved/absent,
        // which fails OPEN (the query stays a loop), so old stores and unresolvable binds keep prior behavior.
        string? loopBindType = null
    )
    {
        // The enumerating gate for QUERY syntax — the same discipline the lambda path applies below via
        // rules.EnumeratingMethods, aimed at the same failure: query comprehensions over a single-value monad
        // (Validation / Either / Option / a first-party state monad) bind AT MOST ONCE and are not loops, yet
        // their syntax is identical to a real query over a collection. The discriminator is the DECLARING TYPE
        // of the bind method the compiler chose (System.Linq.Enumerable for a collection, the monad's own
        // extension class otherwise), matched against the SAME allow-list that keeps Option.Map out of lambda
        // contexts — one list governs both syntaxes, and a monad nobody has named yet falls out by default.
        // A rule with EMPTY DeclaringTypes means "any declaring type" and keeps the gate vacuously open, same
        // as its meaning on the lambda path.
        if (
            loopKind == "query"
            && !string.IsNullOrEmpty(loopBindType)
            && !rules.EnumeratingMethods.Any(r =>
                r.DeclaringTypes.Count == 0 || r.DeclaringTypes.Contains(loopBindType!, StringComparer.Ordinal)
            )
        )
        {
            loopKind = null;
            loopDetail = null;
            loopElementType = null;
        }

        // The enumerating-lambda context: the innermost enclosing call that the rules declare ENUMERATES its
        // receiver and whose lambda argument contains this site. Rule-gated on the resolved declaring type,
        // which is what keeps single-shot lambda takers (Option.Map, Try, Lazy, Task.Run) out.
        var enumerating = enclosingInvocations.FirstOrDefault(e =>
            !string.IsNullOrEmpty(e.LambdaParameter)
            && rules.EnumeratingMethods.Any(r =>
                (r.Methods.Count == 0 || r.Methods.Contains(e.MethodName, StringComparer.Ordinal))
                && (r.DeclaringTypes.Count == 0 || r.DeclaringTypes.Contains(e.DeclaringType, StringComparer.Ordinal))
            )
        );

        // EnclosingInvocation is a record STRUCT, so a FirstOrDefault miss yields `default` — where the
        // positional `= ""` defaults do NOT apply and every string field is null. Normalize once here.
        var enumeratingParameter = enumerating.LambdaParameter ?? "";
        var enumeratingMethod = enumerating.MethodName ?? "";
        var enumeratingElementType = enumerating.LambdaParameterType ?? "";

        var kind = loopKind ?? (enumeratingParameter.Length > 0 ? "lambda" : null);
        var detail = loopDetail ?? (enumeratingParameter.Length > 0 ? $"{enumeratingParameter} in {enumeratingMethod}" : null);
        if (kind is null)
        {
            return None;
        }

        var identifiers = LoopIdentifiers(loopKind, loopDetail)
            .Concat(SplitIdentifiers(enumeratingParameter))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        // A loop STATEMENT wins the element type exactly as it wins the reported kind, so the two always describe
        // the same context; the lambda's parameter type stands in only when there is no enclosing loop at all.
        return new Context(
            Kind: kind,
            Detail: detail,
            Identifiers: identifiers,
            Source: SourceOf(detail),
            ElementType: loopKind is not null ? loopElementType ?? "" : enumeratingElementType
        );
    }

    // The identifiers rebound on every iteration, parsed from a loopDetail of the form
    // "{identifier}[, {identifier}…] in {expression}" — a `foreach` contributes its single iteration
    // variable ("id in ids" -> ["id"]), a `query` contributes every range variable the query binds
    // ("p, profile in profiles" -> ["p", "profile"]). Empty for `for`/`while`/`do`, which carry no
    // identifier: their amplification is real but a key discriminator has nothing to match.
    public static IReadOnlyList<string> LoopIdentifiers(string? loopKind, string? loopDetail)
    {
        if (loopKind is not ("foreach" or "query") || string.IsNullOrEmpty(loopDetail))
        {
            return [];
        }

        var inMarker = loopDetail!.IndexOf(" in ", StringComparison.Ordinal);
        if (inMarker <= 0)
        {
            return [];
        }

        return SplitIdentifiers(loopDetail.Substring(startIndex: 0, length: inMarker));
    }

    // A comma-separated identifier list ("p, profile", a lambda's "x, i") to its trimmed, non-empty parts.
    public static IReadOnlyList<string> SplitIdentifiers(string? identifiers) =>
        string.IsNullOrEmpty(identifiers)
            ? []
            : identifiers!.Split(',').Select(part => part.Trim()).Where(part => part.Length > 0).ToList();

    // The ITERATED SOURCE expression: everything after the " in " of an iteration detail ("index in indexes"
    // -> "indexes"). "" when the detail carries no source (for/while/do). Evidence, not a discriminator: a
    // read keyed on the identity of the very collection being iterated has key cardinality N by construction.
    public static string SourceOf(string? detail)
    {
        if (string.IsNullOrEmpty(detail))
        {
            return "";
        }

        var inMarker = detail!.IndexOf(" in ", StringComparison.Ordinal);
        return inMarker < 0 ? "" : detail.Substring(inMarker + 4).Trim();
    }

    // True when `token` occurs in `haystack` bounded by non-identifier characters on both sides (so it is
    // a distinct identifier reference, not a substring of a longer name). A C# identifier char is a
    // letter, digit, or underscore — any other char (`/`, `{`, `}`, `.`, `"`, `,`, `|`, quotes) is a
    // boundary, which is exactly what surrounds a key in a member path ("a.id"), an interp template
    // ("/var/{id}"), a JSON arg array (["id"]) or a reduced composite surface ("Fields.Name|s.Trim").
    public static bool ContainsToken(string? haystack, string token)
    {
        if (string.IsNullOrEmpty(haystack))
        {
            return false;
        }

        var from = 0;
        while (true)
        {
            var at = haystack!.IndexOf(token, from, StringComparison.Ordinal);
            if (at < 0)
            {
                return false;
            }

            var before = at == 0 ? '\0' : haystack[at - 1];
            var afterIndex = at + token.Length;
            var after = afterIndex >= haystack.Length ? '\0' : haystack[afterIndex];
            if (!IsIdentifierChar(before) && !IsIdentifierChar(after))
            {
                return true;
            }

            from = at + 1;
        }
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
