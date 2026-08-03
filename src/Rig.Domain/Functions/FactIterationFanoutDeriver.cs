using System.Text.Json;
using Rig.Domain.Data;

namespace Rig.Domain.Functions;

// Stage-2 pure derivation of the ITERATION-FANOUT pseudo-event: an in-source call site that sits in an
// iteration context, i.e. a call issued once per element. It is the ANCHOR half of cross-method read
// amplification — the shipped lexical n_plus_1 asks "does a READ sit in an iteration context", this asks the
// same question of a CALL, so that a later reachability join can ask whether a read lives beneath it.
//
// The event is emitted for BOTH keyed and keyless sites. C# statements are eager, so a read reachable inside
// an iteration context executes per element REGARDLESS of the key — presence is the finding, and the key only
// ever spoke to whether a CACHE would absorb the repetition. A null key token is therefore data, not a
// disqualifier; `for`/`while`/`do` bind no identifier and amplify all the same.
//
// EnclosingSymbolId = THE CALLEE, deliberately and load-bearingly: the correlation operator seeds its forward
// reach at each anchor's EnclosingSymbolId and the reach set INCLUDES the seed, so "companion reachable from
// the anchor's enclosing method" becomes "read reachable AT OR BENEATH the per-iteration call" — the intended
// semantics, with zero changes to the reach step, and a read in the callee's own body (depth 0) is found. The
// price is that the finding's HUMAN site is not the pseudo-event's enclosing symbol: it is Caller, carried
// beside the event.
//
// Pure, no I/O, input not mutated.
public static class FactIterationFanoutDeriver
{
    public const string Provider = "iteration";
    public const string Operation = "fanout";

    // One looped call site. Event is what the correlation operator consumes; every other field is EVIDENCE for
    // the analysis pass that has to derive the amortization rules from real shapes — nothing here is a filter.
    public sealed record IterationFanout(
        DerivedEffect Event,
        // The anchor's enclosing method — the CALLER, i.e. where a human would fix it. Not Event's enclosing
        // symbol (that is the callee, see above).
        string Caller,
        string IterationKind,
        string IterationDetail,
        // The ITERATED SOURCE expression ("index in indexes" -> "indexes"). A read keyed on the identity of the
        // very collection being iterated has key cardinality N by construction — the self-keyed shape — which
        // is only detectable with the source in hand.
        string IteratedSource,
        // The RESOLVED element type of that source (open-generic FQN). The semantic counterpart of IteratedSource
        // and of KeyPath: iterate X and read X, and the key cardinality is N whatever the argument is named. It
        // is also the ONLY iteration evidence a `lambda` anchor has — its IteratedSource degenerates to the
        // enumerating method name ("Select"). "" for for/while/do, for an unresolved source, and for an anonymous
        // projection, which is not a mis-extraction: that element genuinely is not an entity.
        string ElementType,
        // The per-element identifier found in argument ArgumentIndex, or "" with index -1 when the site is
        // keyless (for/while/do, or an argument surface that captured nothing).
        string KeyToken,
        // The FULL captured surface of that argument — `p.PkProfile`, not the bare `p` that KeyToken carries.
        // KeyToken answers "does a per-element value cross this boundary"; only the PATH says WHICH value, and
        // in MedDBase the self-keyed-vs-foreign-keyed distinction lives entirely in the member NAME
        // (`p.PkProfile` = this element's own identity, cardinality N; `row.FkDepartmentCode` = a foreign
        // reference into a bounded domain). Keeps the leading '~' of a reduced composite surface, which is
        // load-bearing elsewhere (FactEffectDeriver rejects a marked value as an identity).
        string KeyPath,
        int ArgumentIndex
    )
    {
        // The callee is the caller: a tree walk, not a fan-out. Kept as evidence rather than suppressed —
        // per-node reads in a recursive descent are sometimes exactly the hotspot.
        public bool Recursive => string.Equals(Caller, Event.EnclosingSymbolId, StringComparison.Ordinal);
    }

    // Every in-source-or-not call site in an iteration context, one event each. Deterministic order: file, then
    // line, then callee — a stable dataset across runs, which the downstream cross-tab depends on.
    public static IReadOnlyList<IterationFanout> Derive(IReadOnlyList<FactInvocation> invocations, FactObservationRules rules)
    {
        var fanouts = new List<IterationFanout>();
        foreach (var inv in invocations)
        {
            if (inv.Enclosing is null)
            {
                continue;
            }

            var iteration = IterationContext.Of(
                loopKind: inv.LoopKind,
                loopDetail: inv.LoopDetail,
                enclosingInvocations: FactStructuralContext.DecodeInvocations(inv.EnclosingInvocations),
                rules: rules,
                loopElementType: inv.LoopElementType
            );
            if (iteration.Kind is null)
            {
                continue;
            }

            var (keyToken, keyPath, argumentIndex) = KeyOf(iteration.Identifiers, inv);
            fanouts.Add(
                new IterationFanout(
                    Event: new DerivedEffect(
                        Provider: Provider,
                        Operation: Operation,
                        // The key token IS the resource identity of this event — what the operator would join
                        // on once the callee's parameter names exist. "" when keyless.
                        ResourceType: keyToken,
                        EnclosingSymbolId: inv.Target,
                        FilePath: inv.FilePath,
                        Line: inv.Line,
                        // Guards are the suspected real precision lever (a read behind a rarely-true `if`
                        // inside the loop body executes ~never whatever its key), so they ride the event.
                        EnclosingGuards: inv.EnclosingGuards
                    ),
                    Caller: inv.Enclosing,
                    IterationKind: iteration.Kind,
                    IterationDetail: iteration.Detail ?? iteration.Kind,
                    IteratedSource: iteration.Source,
                    ElementType: iteration.ElementType,
                    KeyToken: keyToken,
                    KeyPath: keyPath,
                    ArgumentIndex: argumentIndex
                )
            );
        }

        fanouts.Sort(
            (a, b) =>
            {
                var byFile = string.CompareOrdinal(a.Event.FilePath, b.Event.FilePath);
                if (byFile != 0)
                {
                    return byFile;
                }

                var byLine = a.Event.Line.CompareTo(b.Event.Line);
                return byLine != 0 ? byLine : string.CompareOrdinal(a.Event.EnclosingSymbolId, b.Event.EnclosingSymbolId);
            }
        );
        return fanouts;
    }

    // The FIRST argument position whose captured surface names a per-iteration identifier: that identifier, the
    // whole surface it was found in, and the position. First-match (not all matches) keeps the event grain
    // one-per-call-site, which is what makes (file, line, callee) an identity the dataset can join on; a site
    // that passes the element at two positions is a shape we have never needed to distinguish.
    // ("", "", -1) when nothing matches — a keyless anchor, still emitted.
    //
    // The PATH is the matching surface verbatim (member path, reduced composite surface, or string template),
    // NOT a reconstruction: `ArgumentNames[k]` is already `expression.ToString()` for a member access, so
    // `Cache.New(p.PkProfile)` yields the path "p.PkProfile" while the token stays "p". Names are preferred over
    // templates because a name is the member path the key discriminator needs; a template only wins where the
    // element travels inside a string ("/vars/{id}") and there is no name at all.
    private static (string KeyToken, string KeyPath, int ArgumentIndex) KeyOf(IReadOnlyList<string> identifiers, FactInvocation inv)
    {
        if (identifiers.Count == 0)
        {
            return ("", "", -1);
        }

        var names = Elements(inv.ArgumentNames);
        var templates = Elements(inv.ArgumentTemplates);
        var positions = Math.Max(names.Length, templates.Length);
        for (var k = 0; k < positions; k++)
        {
            var name = k < names.Length ? names[k] : null;
            var template = k < templates.Length ? templates[k] : null;
            foreach (var identifier in identifiers)
            {
                if (IterationContext.ContainsToken(name, identifier))
                {
                    return (identifier, name!, k);
                }

                if (IterationContext.ContainsToken(template, identifier))
                {
                    return (identifier, template!, k);
                }
            }
        }

        // The unindexed first-argument fast path: present on call sites whose positional lists were skipped.
        // Index 0 by construction.
        foreach (var identifier in identifiers)
        {
            if (IterationContext.ContainsToken(inv.FirstArgName, identifier))
            {
                return (identifier, inv.FirstArgName!, 0);
            }

            if (IterationContext.ContainsToken(inv.FirstArgTemplate, identifier))
            {
                return (identifier, inv.FirstArgTemplate!, 0);
            }
        }

        return ("", "", -1);
    }

    // A stored JSON string?[] (ArgumentNames / ArgumentTemplates) to its elements; empty for null/blank or a
    // malformed payload (same tolerance the nth-argument resource resolution has: an undecodable surface is
    // simply no surface).
    private static string?[] Elements(string? jsonArray)
    {
        if (string.IsNullOrEmpty(jsonArray))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string?[]>(jsonArray!) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
