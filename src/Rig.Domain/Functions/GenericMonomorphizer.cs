using Rig.Domain.Data;

namespace Rig.Domain.Functions;

// Static monomorphization (docs/design-dispatch-precision.md): MATERIALIZE the monomorphized subgraph as a
// PURE FUNCTION over an in-memory FactGraphData. Each reachable generic-method instantiation (from the
// inventory fixpoint) becomes a DISTINCT node whose CLOSURE edges (the method's body AND its lambda
// sub-nodes', which close over its type-params) have their type-param receiver SUBSTITUTED with the concrete
// type bound — so the EXISTING dispatch-narrowing (FactPathFinder.ResolveNarrowRoot / NarrowByReceiver,
// driven by CallEdge.ReceiverType) resolves the concrete override instead of CHA-fanning.
//
// TRANSITIVE + LAMBDA-CLOSURE (session-2 rework): a cloned edge's CALLEE is also redirected —
//   - a methodGroup edge into one of the method's OWN lambdas (`{M}~λN`) re-points to that lambda's
//     instantiation node (mono-lambda), so the lambda body is walked under the instantiation; and
//   - an edge into a nested generic whose binding FORWARDS this instantiation's type-args ("M:0"/"T:0") is
//     resolved against this instantiation and re-pointed to the nested instantiation node (if inventoried).
// So a concrete type flows from the root, through generic callers and into lambdas, to the concrete dispatch
// — as close to runtime reification as the static facts allow.
//
// NO loader wiring, NO Roslyn, NO traversal-engine change. Returns a NEW FactGraphData (immutable records).
// Base generic methods + their bodies are KEPT unchanged (soundness: forwarded / unresolved / capped /
// un-instantiated callers still reach base `M` with full CHA).
public static class GenericMonomorphizer
{
    public static FactGraphData Materialize(
        FactGraphData graph,
        GenericInstantiationInventory.InstantiationInventoryResult inventory,
        // Ordered type-parameter names for a symbol id (a METHOD id OR a declaring-TYPE id). MethodRef
        // carries no Signature, so the names cannot be read off the graph — the caller supplies them (in real
        // wiring, mined from symbol_facts.Signature via GenericSubstitution.ParseTypeParameterNames).
        Func<string, IReadOnlyList<string>> typeParamNamesFor
    )
    {
        if (inventory.Instantiations.Count == 0)
        {
            return graph;
        }

        var containingTypeOf = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var m in graph.Methods)
        {
            containingTypeOf[m.SymbolId] = m.ContainingTypeId;
        }

        // Body edges by caller, plus the lambda sub-nodes (`{method}~λN`) of each method — the closure we clone.
        var edgesByCaller = new Dictionary<string, List<CallEdge>>(StringComparer.Ordinal);
        var lambdaCallersByMethod = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var edge in graph.CallEdges)
        {
            if (!edgesByCaller.TryGetValue(edge.Caller, out var list))
            {
                edgesByCaller[edge.Caller] = list = new List<CallEdge>();
            }

            list.Add(edge);
        }

        foreach (var caller in edgesByCaller.Keys)
        {
            var lambda = caller.IndexOf("~λ", StringComparison.Ordinal);
            if (lambda > 0)
            {
                var baseMethod = caller[..lambda];
                if (!lambdaCallersByMethod.TryGetValue(baseMethod, out var lambdas))
                {
                    lambdaCallersByMethod[baseMethod] = lambdas = new List<string>();
                }

                lambdas.Add(caller);
            }
        }

        // The set of materialized instantiation node ids — the membership test for redirecting a cloned or
        // incoming edge to a nested instantiation. Identity is MonomorphizedNodeId.For (deterministic).
        var instIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var inst in inventory.Instantiations)
        {
            instIds.Add(MonomorphizedNodeId.For(inst.MethodId, declaringBinding: inst.DeclaringBinding, methodBinding: inst.MethodBinding));
        }

        var clonedEdges = new List<CallEdge>();
        foreach (var inst in inventory.Instantiations)
        {
            var instId = MonomorphizedNodeId.For(inst.MethodId, declaringBinding: inst.DeclaringBinding, methodBinding: inst.MethodBinding);
            var map = BuildNameMap(inst, typeParamNamesFor, containingTypeOf);

            // Clone the method's own body edges onto the instantiation node.
            CloneClosure(caller: inst.MethodId, newCaller: instId);

            // Clone each lambda sub-node's edges onto its mono-lambda node (same binding as the method —
            // lambdas close over the method's type-params).
            if (lambdaCallersByMethod.TryGetValue(inst.MethodId, out var lambdas))
            {
                foreach (var lambdaCaller in lambdas)
                {
                    CloneClosure(
                        caller: lambdaCaller,
                        newCaller: MonomorphizedNodeId.For(
                            lambdaCaller,
                            declaringBinding: inst.DeclaringBinding,
                            methodBinding: inst.MethodBinding
                        )
                    );
                }
            }

            void CloneClosure(string caller, string newCaller)
            {
                if (!edgesByCaller.TryGetValue(caller, out var bodyEdges))
                {
                    return;
                }

                clonedEdges.AddRange(
                    CloneCallerEdges(
                        bodyEdges,
                        newCaller,
                        inst,
                        map,
                        candidate =>
                            instIds.Contains(
                                MonomorphizedNodeId.For(candidate.MethodId, candidate.DeclaringBinding, candidate.MethodBinding)
                            )
                    )
                );
            }
        }

        // REDIRECT incoming edges whose OWN binding is fully concrete and matches an inventory instantiation,
        // REPLACING the callee with the instantiation node (keeping both would still reach base `M` and
        // re-explode the fan). Forwarded incoming edges (from a generic caller) are left on base `M` — they
        // are redirected by the CALLER's clone when that caller is materialized.
        var outEdges = new List<CallEdge>(graph.CallEdges.Count + clonedEdges.Count);
        foreach (var e in graph.CallEdges)
        {
            var redirect = IncomingRedirect(e, instIds);
            outEdges.Add(redirect is null ? e : e with { Callee = redirect });
        }

        clonedEdges.Sort((a, b) => string.Compare(strA: a.Caller, strB: b.Caller, comparisonType: StringComparison.Ordinal));
        outEdges.AddRange(clonedEdges);

        return graph with
        {
            CallEdges = outEdges,
        };
    }

    // The callee of a cloned closure edge, redirected for transitivity / lambda-closure:
    //   - a methodGroup into one of the instantiation method's OWN lambdas -> that lambda's mono node;
    //   - an edge whose binding tokens RESOLVE (against this instantiation) to an inventoried instantiation
    //     -> that nested instantiation node;
    //   - otherwise the original callee (non-generic, unresolved, or un-inventoried -> sound CHA fallback).
    public static IReadOnlyList<CallEdge> CloneCallerEdges(
        IReadOnlyList<CallEdge> baseEdges,
        string newCaller,
        GenericInstantiationInventory.GenericInstantiation bindingContext,
        IReadOnlyDictionary<string, string> typeParameterMap,
        Func<GenericInstantiationInventory.GenericInstantiation, bool> admitNested
    )
    {
        var cloned = new List<CallEdge>(baseEdges.Count);
        foreach (var edge in baseEdges)
        {
            var newReceiver = edge.ReceiverType is null
                ? null
                : GenericSubstitution.Substitute(edge.ReceiverType, typeParameterMap.Keys.ToArray(), typeParameterMap.Values.ToArray());
            cloned.Add(
                edge with
                {
                    Caller = newCaller,
                    Callee = RedirectClonedCallee(edge, bindingContext, admitNested),
                    ReceiverType = newReceiver,
                }
            );
        }

        return cloned;
    }

    private static string RedirectClonedCallee(
        CallEdge edge,
        GenericInstantiationInventory.GenericInstantiation inst,
        Func<GenericInstantiationInventory.GenericInstantiation, bool> admitNested
    )
    {
        // A call into the method's own lambda closure: re-point to the lambda's instantiation (same binding).
        if (edge.Callee.StartsWith(inst.MethodId + "~λ", StringComparison.Ordinal))
        {
            return MonomorphizedNodeId.For(edge.Callee, declaringBinding: inst.DeclaringBinding, methodBinding: inst.MethodBinding);
        }

        var candidate = ResolveInstantiation(
            edge,
            enclosingDeclaringBinding: inst.DeclaringBinding,
            enclosingMethodBinding: inst.MethodBinding
        );
        if (candidate is null)
        {
            return edge.Callee;
        }

        return admitNested(candidate)
            ? MonomorphizedNodeId.For(candidate.MethodId, candidate.DeclaringBinding, candidate.MethodBinding)
            : edge.Callee;
    }

    // The instantiation node an ORIGINAL edge should be redirected to, or null to leave it at base. Only an
    // edge whose own binding is FULLY CONCRETE (resolves against an empty enclosing context) and matches an
    // inventory instantiation is redirected.
    private static string? IncomingRedirect(CallEdge edge, HashSet<string> instIds)
    {
        var instantiation = ResolveInstantiation(
            edge,
            enclosingDeclaringBinding: Array.Empty<string>(),
            enclosingMethodBinding: Array.Empty<string>()
        );
        if (instantiation is null)
        {
            return null; // forwarded/unresolved -> the caller's clone handles it
        }

        var candidate = MonomorphizedNodeId.For(
            instantiation.MethodId,
            declaringBinding: instantiation.DeclaringBinding,
            methodBinding: instantiation.MethodBinding
        );
        return instIds.Contains(candidate) ? candidate : null;
    }

    // The one binding resolver shared by whole-graph materialization and demand-shaped adjacency.
    // Null means non-generic or unresolved and therefore leaves the original base edge intact.
    public static GenericInstantiationInventory.GenericInstantiation? ResolveInstantiation(
        CallEdge edge,
        IReadOnlyList<string> enclosingDeclaringBinding,
        IReadOnlyList<string> enclosingMethodBinding
    )
    {
        var declTokens = GenericSubstitution.ParseBindingTokens(edge.DeclaringTypeArgBinding);
        var methTokens = GenericSubstitution.ParseBindingTokens(edge.MethodTypeArgBinding);
        if (declTokens.Count == 0 && methTokens.Count == 0)
        {
            return null;
        }

        var declaring = GenericSubstitution.ResolveTokens(declTokens, enclosingDeclaringBinding, enclosingMethodBinding);
        var method = GenericSubstitution.ResolveTokens(methTokens, enclosingDeclaringBinding, enclosingMethodBinding);
        return declaring is null || method is null
            ? null
            : new GenericInstantiationInventory.GenericInstantiation(edge.Callee, declaring, method);
    }

    public static IReadOnlyDictionary<string, string> BuildTypeParameterMap(
        GenericInstantiationInventory.GenericInstantiation inst,
        Func<string, IReadOnlyList<string>> typeParamNamesFor,
        string? containingType
    )
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (containingType is not null)
        {
            ZipInto(map, names: typeParamNamesFor(containingType), binding: inst.DeclaringBinding, methodWins: false);
        }

        ZipInto(map, names: typeParamNamesFor(inst.MethodId), binding: inst.MethodBinding, methodWins: true);
        return map;
    }

    // Merged type-param NAME -> concrete map for an instantiation: declaring-type params (zipped to the
    // declaring binding) first, then method params (zipped to the method binding) — method wins on a name
    // collision. Out-of-range positions are skipped (arity mismatch tolerated). The same map substitutes the
    // method's body AND its lambda bodies (lambdas close over the method's type-params).
    private static Dictionary<string, string> BuildNameMap(
        GenericInstantiationInventory.GenericInstantiation inst,
        Func<string, IReadOnlyList<string>> typeParamNamesFor,
        Dictionary<string, string?> containingTypeOf
    )
    {
        containingTypeOf.TryGetValue(inst.MethodId, out var containingType);
        return new Dictionary<string, string>(BuildTypeParameterMap(inst, typeParamNamesFor, containingType), StringComparer.Ordinal);
    }

    private static void ZipInto(Dictionary<string, string> map, IReadOnlyList<string> names, IReadOnlyList<string> binding, bool methodWins)
    {
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            if (string.IsNullOrEmpty(name) || i >= binding.Count)
            {
                continue;
            }

            if (methodWins)
            {
                map[name] = binding[i];
            }
            else
            {
                map.TryAdd(name, binding[i]);
            }
        }
    }
}
