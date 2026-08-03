using Rig.Domain.Data;

namespace Rig.Domain.Functions;

// Stage-2-over-facts observation derivation (P2b): reproduces the notes the Roslyn
// EffectObservationExtractor attaches to an effect — looped_effect, parallel_fanout,
// resilience_retry, concurrency_handled — from the structural-context facts captured in P1c
// (EnclosingLoopKind/Detail, EnclosingInvocations, EnclosingCatchTypes) plus the projected
// observation rules. No Roslyn. This is generic infra; the matching data lives in the rules.
//
// read_before_commit is intentionally NOT derived here: it needs cross-invocation ordering (an
// earlier read in the same method body), and it is EF-specific (SaveChanges + DbSet reads) — not
// the LLBLGen/MedDBase target. The facts to add it later already exist (ReceiverType + Line per
// invocation).
public static class FactObservationDeriver
{
    public static IReadOnlyList<EffectObservationInfo> Derive(
        string methodName,
        string? loopKind,
        string? loopDetail,
        IReadOnlyList<FactStructuralContext.EnclosingInvocation> enclosingInvocations,
        IReadOnlyList<string> catchTypes,
        FactObservationRules rules,
        string? provider = null,
        IReadOnlyList<FactStructuralContext.EnclosingScope>? enclosingScopes = null,
        // Call-site generic type arguments (comma-joined display FQNs, FactInvocation.TypeArguments).
        // Feeds unserializable_payload — the payload type at a store/serialize boundary. Null for
        // non-generic calls / field writes (which carry no payload type argument).
        string? typeArguments = null,
        // The matched rule's effect operation (e.g. "GET"/"read"). Feeds n_plus_1, whose read-gate is
        // provider+operation. Null for callers that don't carry it (field writes); n_plus_1 then gates
        // on provider alone.
        string? operation = null,
        // The read's KEY ARGUMENT surface (FactInvocation.FirstArgName / FirstArgTemplate + the all-args
        // JSON ArgumentNames / ArgumentTemplates). Feeds n_plus_1 — the loop identifier "varies" when it
        // appears in any of these. Null when the call has no such argument.
        string? firstArgName = null,
        string? firstArgTemplate = null,
        string? argumentNames = null,
        string? argumentTemplates = null
    )
    {
        var observations = new List<EffectObservationInfo>();

        // The enumerating-lambda iteration context: the innermost enclosing call that the rules declare
        // ENUMERATES its receiver and whose lambda argument contains this effect. `ids.Select(id =>
        // Fetch(id))` amplifies exactly as `foreach (var id in ids) Fetch(id)` does, but has no loop
        // STATEMENT ancestor, so the loop facts alone report no iteration at all. Rule-gated on the
        // resolved declaring type, which is what keeps single-shot lambda takers (Option.Map, Try, Lazy,
        // Task.Run) out. Innermost-first; first match wins, mirroring the other enclosing-invocation scans.
        var enumerating = enclosingInvocations.FirstOrDefault(e =>
            !string.IsNullOrEmpty(e.LambdaParameter)
            && rules.EnumeratingMethods.Any(r =>
                (r.Methods.Count == 0 || r.Methods.Contains(e.MethodName, StringComparer.Ordinal))
                && (r.DeclaringTypes.Count == 0 || r.DeclaringTypes.Contains(e.DeclaringType, StringComparer.Ordinal))
            )
        );

        // EnclosingInvocation is a record STRUCT, so a FirstOrDefault miss yields `default` — where the
        // positional `= ""` defaults do NOT apply and every string field is null. Normalize once here
        // rather than null-guarding each use.
        var enumeratingParameter = enumerating.LambdaParameter ?? "";

        // A loop STATEMENT wins the reported kind when both are present (it is the coarser, outer context
        // and keeps existing output stable), but the identifiers UNION: in `foreach (var a in xs) ys.Select(y
        // => Fetch(y))` both `a` and `y` are genuinely rebound per iteration, so a key built from either is
        // amplified. Taking only the loop's identifier would miss the `y`-keyed read entirely.
        var iterationKind = loopKind ?? (enumeratingParameter.Length > 0 ? "lambda" : null);
        var iterationDetail =
            loopDetail ?? (enumeratingParameter.Length > 0 ? $"{enumeratingParameter} in {enumerating.MethodName}" : null);

        // looped_effect — the effect is lexically inside an iteration context (nearest enclosing loop, or a
        // rule-declared enumerating lambda).
        if (iterationKind is not null)
        {
            observations.Add(
                new EffectObservationInfo(
                    Type: "looped_effect",
                    Context: iterationKind,
                    Detail: iterationDetail ?? iterationKind,
                    Confidence: "high",
                    Basis: "compilation",
                    Reason: "effect_inside_loop"
                )
            );
        }

        // parallel_fanout — a fanout wrapper (Task.WhenAll / Parallel.ForEach…) lexically encloses
        // the effect. Innermost-first; first match wins (mirrors the Roslyn ancestor walk).
        foreach (var enclosing in enclosingInvocations)
        {
            // Match on the FQN of the resolved receiver TYPE (robust to how the call was qualified), not the
            // syntactic receiver text — a fully-qualified `System.Threading.Tasks.Parallel.ForEach` matches
            // exactly as the using-imported `Parallel.ForEach` does.
            var fanout = rules.ParallelFanout.FirstOrDefault(f =>
                string.Equals(enclosing.ReceiverType, f.ReceiverType, StringComparison.Ordinal)
                && f.Methods.Contains(enclosing.MethodName, StringComparer.Ordinal)
            );
            if (fanout is not null)
            {
                var context = $"{fanout.Receiver}.{enclosing.MethodName}";
                observations.Add(
                    new EffectObservationInfo(
                        Type: "parallel_fanout",
                        Context: context,
                        Detail: context,
                        Confidence: "high",
                        Basis: "compilation",
                        Reason: "effect_inside_parallel_fanout"
                    )
                );
                break;
            }
        }

        // resilience_retry — a wrapper invocation (Execute/ExecuteAsync on a ResiliencePipeline /
        // execution strategy) encloses the effect. Matches the wrapper method + a receiver-type
        // pattern (substring), per rule.
        foreach (var rule in rules.ResilienceRetry)
        {
            var match = enclosingInvocations
                .Where(e => rule.WrapperMethods.Contains(e.MethodName, StringComparer.Ordinal))
                .Select(e =>
                    (
                        e.ReceiverType,
                        Pattern: rule.ReceiverTypePatterns.FirstOrDefault(p => e.ReceiverType.IndexOf(p, StringComparison.Ordinal) >= 0)
                    )
                )
                .FirstOrDefault(m => m.Pattern is not null);
            if (match.Pattern is not null)
            {
                observations.Add(
                    new EffectObservationInfo(
                        Type: "resilience_retry",
                        Context: match.Pattern,
                        Detail: match.ReceiverType,
                        Confidence: "high",
                        Basis: "compilation",
                        Reason: "effect_inside_resilience_retry"
                    )
                );
                break;
            }
        }

        // concurrency_handled — the effect is a commit (SaveChanges…) wrapped in a try/catch whose
        // caught type matches a concurrency-exception pattern.
        foreach (var rule in rules.ConcurrencyHandled)
        {
            if (!rule.CommitMethods.Contains(methodName, StringComparer.Ordinal))
            {
                continue;
            }

            foreach (var caught in catchTypes)
            {
                var matched = rule.CatchTypePatterns.FirstOrDefault(p => caught.IndexOf(p, StringComparison.Ordinal) >= 0);
                if (matched is not null)
                {
                    observations.Add(
                        new EffectObservationInfo(
                            Type: "concurrency_handled",
                            Context: matched,
                            Detail: caught,
                            Confidence: "high",
                            Basis: "compilation",
                            Reason: "efcore_optimistic_concurrency_catch"
                        )
                    );
                    break;
                }
            }

            if (observations.Any(o => o.Type == "concurrency_handled"))
            {
                break;
            }
        }

        // resource_span (ordering/nesting) — a span-sensitive effect (this provider) is lexically
        // nested inside a held-resource scope: a transaction-`using` or a `lock`. Proves the resource
        // is held ACROSS the effect ("transaction spans a network call" / "lock held across IO").
        // Innermost-first scope chain; the first scope that satisfies a rule emits the observation.
        if (provider is not null && enclosingScopes is { Count: > 0 })
        {
            foreach (var rule in rules.ResourceSpan)
            {
                // Deny-list: flag every effect except the scope's own expected family.
                if (rule.ExcludeProviders.Contains(provider, StringComparer.Ordinal))
                {
                    continue;
                }

                var scope = enclosingScopes.FirstOrDefault(s =>
                    string.Equals(s.Kind, rule.ScopeKind, StringComparison.Ordinal)
                    && (
                        rule.ScopeTypePatterns.Count == 0
                        || rule.ScopeTypePatterns.Any(p => s.Type.IndexOf(p, StringComparison.Ordinal) >= 0)
                    )
                );
                // EnclosingScope is a struct; a no-match FirstOrDefault yields Kind == null.
                if (scope.Kind is null)
                {
                    continue;
                }

                observations.Add(
                    new EffectObservationInfo(
                        Type: rule.ObservationType,
                        Context: rule.Context,
                        Detail: scope.Type.Length == 0 ? rule.Context : scope.Type,
                        Confidence: "high",
                        Basis: "compilation",
                        Reason: "effect_inside_held_resource_scope"
                    )
                );
            }
        }

        // unserializable_payload (FR-6, RCA #1646) — the effect stores/serializes a payload whose generic
        // TYPE ARGUMENT is a serializer-unsupported type (e.g. LanguageExt.Option / Either, which the store
        // CAN serialize but CANNOT deserialize). Unlike the structural observations above, this keys off the
        // effect's OWN payload type, not the surrounding code. ANNOTATE-only: it adds a note; the effect is
        // never removed. Matched against the call-site type arguments; first matching pattern wins per rule.
        if (provider is not null && !string.IsNullOrEmpty(typeArguments))
        {
            foreach (var rule in rules.SerializationHazard)
            {
                if (rule.Providers.Count > 0 && !rule.Providers.Contains(provider, StringComparer.Ordinal))
                {
                    continue;
                }

                var matched = rule.UnsupportedTypePatterns.FirstOrDefault(p => typeArguments!.IndexOf(p, StringComparison.Ordinal) >= 0);
                if (matched is not null)
                {
                    observations.Add(
                        new EffectObservationInfo(
                            Type: "unserializable_payload",
                            Context: matched,
                            Detail: typeArguments!,
                            Confidence: "high",
                            Basis: "compilation",
                            Reason: "serializer_unsupported_payload_type"
                        )
                    );
                    break;
                }
            }
        }

        // n_plus_1 (FR-3, RCA #2892) — a READ-category effect inside a loop whose KEY ARGUMENT VARIES per
        // iteration: the loop's iteration variable appears in the read's key argument. This refines the
        // structural looped_effect above (which fires for ANY effect in a loop): a read whose key is
        // CONSTANT is hoistable and is NOT an n+1, so it must not fire. The discriminator is the loop
        // identifier (the foreach iteration variable) appearing in any of the call's argument surfaces
        // (first-arg name/template + all positional arg names/templates — an interpolated `$"/var/{id}"`
        // reduces to "/var/{id}", preserving the {id} token). ANNOTATE-only.
        //
        // The identifier set spans every iteration context that BINDS a name: a foreach's iteration
        // variable, a query expression's range variables, and an enumerating lambda's parameters. They are
        // unioned rather than preferred in order, because a lambda nested in a loop is rebound by both and
        // a key from either varies. for/while/do bind nothing, so they carry no candidate and stay covered
        // by looped_effect alone — deliberately, rather than emitting a keyless n_plus_1 guess.
        if (provider is not null && iterationKind is not null && rules.NPlusOne.Count > 0)
        {
            var iterationIdentifiers = IterationIdentifiers(loopKind, loopDetail)
                .Concat(SplitIdentifiers(enumeratingParameter))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (iterationIdentifiers.Count > 0)
            {
                foreach (var rule in rules.NPlusOne)
                {
                    if (rule.Providers.Count > 0 && !rule.Providers.Contains(provider, StringComparer.Ordinal))
                    {
                        continue;
                    }

                    if (rule.Operations.Count > 0 && (operation is null || !rule.Operations.Contains(operation, StringComparer.Ordinal)))
                    {
                        continue;
                    }

                    // ANY per-iteration identifier in the key is enough — a query expression rebinds every
                    // variable it introduces, so a key built from a `let` is just as amplified as one built
                    // from the `from`. The matched identifier becomes the Context so the finding names the
                    // variable that actually varies.
                    var varying = iterationIdentifiers.FirstOrDefault(id =>
                        KeyVariesWith(id, firstArgName, firstArgTemplate, argumentNames, argumentTemplates)
                    );

                    if (varying is not null)
                    {
                        observations.Add(
                            new EffectObservationInfo(
                                Type: "n_plus_1",
                                Context: varying,
                                Detail: iterationDetail ?? iterationKind,
                                Confidence: "high",
                                Basis: "compilation",
                                Reason: "looped_read_with_varying_key"
                            )
                        );
                        break;
                    }
                }
            }
        }

        return observations;
    }

    // The identifiers rebound on every iteration, parsed from a loopDetail of the form
    // "{identifier}[, {identifier}…] in {expression}" — a `foreach` contributes its single iteration
    // variable ("id in ids" -> ["id"]), a `query` contributes every range variable the query binds
    // ("p, profile in profiles" -> ["p", "profile"]). Empty for `for`/`while`/`do`, which carry no
    // identifier: their amplification is real but the varying-key discriminator has nothing to match, so
    // they stay covered by looped_effect alone rather than producing a keyless n_plus_1 guess.
    private static IReadOnlyList<string> IterationIdentifiers(string? loopKind, string? loopDetail)
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
    private static IReadOnlyList<string> SplitIdentifiers(string? identifiers) =>
        string.IsNullOrEmpty(identifiers)
            ? []
            : identifiers!.Split(',').Select(part => part.Trim()).Where(part => part.Length > 0).ToList();

    // True when the loop identifier appears as a whole-word token in any of the read's key-argument
    // surfaces: the first-argument member/identifier path, the first-argument string/interp template, or
    // any element of the all-positional-args JSON name/template arrays. Whole-word so "id" matches in
    // "/var/{id}" and "id" but not as a substring of "invalid"/"width".
    private static bool KeyVariesWith(
        string loopIdentifier,
        string? firstArgName,
        string? firstArgTemplate,
        string? argumentNames,
        string? argumentTemplates
    )
    {
        return ContainsToken(haystack: firstArgName, token: loopIdentifier)
            || ContainsToken(haystack: firstArgTemplate, token: loopIdentifier)
            || ContainsToken(haystack: argumentNames, token: loopIdentifier)
            || ContainsToken(haystack: argumentTemplates, token: loopIdentifier);
    }

    // True when `token` occurs in `haystack` bounded by non-identifier characters on both sides (so it is
    // a distinct identifier reference, not a substring of a longer name). A C# identifier char is a
    // letter, digit, or underscore — any other char (`/`, `{`, `}`, `.`, `"`, `,`, quotes) is a boundary,
    // which is exactly what surrounds a varying key in a member path ("a.id"), an interp template
    // ("/var/{id}"), or a JSON arg array (["id"]).
    private static bool ContainsToken(string? haystack, string token)
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
