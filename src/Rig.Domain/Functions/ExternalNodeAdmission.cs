using Rig.Domain.Data;

namespace Rig.Domain.Functions;

// EXTERNAL-NODE ADMISSION — the policy, as DATA, deciding which out-of-source (library/BCL) call targets
// become first-class LEAF nodes in the call graph.
//
// The fact store has always kept EVERY method-call ref, including refs to BCL/library members (see
// Reads.LoadFactGraphAsync's header): it is the call GRAPH that used to drop them, so a call into
// `HttpClient.SendAsync` or `DbTransaction.Commit` was invisible as a node even though the fact was there.
// That made the graph read as if the codebase's outward boundary did not exist. This policy admits the
// targets a reader actually cares about and keeps out the noise (`ToString`, `List<T>.Add`, LINQ) that
// floods the tree with width and no reach. It is a QUERY-side change only — no extraction change, no store
// schema change, no re-index.
//
// A target is ADMITTED when EITHER:
//   (a) its DECLARING TYPE matches a type pattern the loaded EFFECT RULES already mention — so the BCL
//       types the product models as effects (System.Data.Common.DbConnection, System.Net.Http.HttpClient,
//       System.IO.File, …) get in even though the deny-list below would reject their assemblies. The
//       patterns are DERIVED from the rules in hand (declaringTypes / receiverTypes /
//       declaringTypeBaseTypes), never a hand-copied list of BCL names in C#: a ruleset that models a type
//       as an effect is exactly a ruleset that wants that type visible in the graph.
//   (b) its TargetAssembly is NOT matched by the FRAMEWORK deny-list (DefaultFrameworkAssemblies) — so
//       every third-party/product library (Dapper, MediatR, Serilog, the ORM) is in by default.
// Explicit config (`externalNodes` in rig.rules.json -> FactExternalNodeRule) overrides both arms: an
// explicitly DENIED assembly stays out even when non-framework; an explicitly ALLOWED one gets in even
// when framework. Allow wins over deny.
//
// Assembly matching is on NAME SEGMENTS, not a bare StartsWith: `System` matches `System` and `System.*`
// (System.Core, System.Net.Http) but NOT `SystemX` / `SystemTextJsonPatch`.
//
// The two admission POINTS — Reads.LoadFactGraphAsync (SQL) and FactGraphProjection.ProjectCalls (the
// in-memory twin the live `rig watch` host and `rig index` materialization use) — both consult THIS object,
// so they cannot drift: the row filtering is the only thing those two still write twice.
//
// SCOPE LIMIT, deliberate: an admitted external node is a LEAF. It has no outgoing call edges (nothing
// inside the external DLL was indexed) and FactPathFinder suppresses its dispatch entirely, so DISPATCH
// THROUGH AN EXTERNAL INTERFACE OR BASE DECLARATION IS OUT OF SCOPE for this change — admitting
// `IDisposable.Dispose` as a node must not CHA-fan to every first-party `Dispose()`. The pre-existing
// `redirectRules` seam (RedirectClassifier) remains the ONE sanctioned way an external target resolves to
// first-party code, and a row it consumes is never also admitted here (the redirect rewrite stays
// authoritative; one row never yields two edges).
public sealed record ExternalNodeAdmission
{
    // The framework assembly deny-list: the runtime/BCL and WPF/interop facades whose members are called
    // everywhere and carry no domain meaning. Matched on name segments (see SegmentMatch).
    public static readonly IReadOnlyList<string> DefaultFrameworkAssemblies =
    [
        "System",
        "mscorlib",
        "netstandard",
        "WindowsBase",
        "PresentationCore",
        "PresentationFramework",
        "Accessibility",
        "Microsoft.CSharp",
        "Microsoft.VisualBasic",
        "Microsoft.Win32",
    ];

    // Declaring-type patterns mined from the loaded effect rules (arm (a)). Matched with the SAME
    // equality-or-namespace-prefix semantics FactEffectDeriver.TypeNameMatches uses, so a rule gating on a
    // namespace (`System.IO`) admits every type under it exactly as it matches every effect under it.
    public IReadOnlyList<string> RuleTypePatterns { get; init; } = [];

    public IReadOnlyList<string> DeniedAssemblies { get; init; } = DefaultFrameworkAssemblies;

    public IReadOnlyList<string> AllowedAssemblies { get; init; } = [];

    // The policy for a rule set: arm (a)'s patterns from the loaded effect rules, arm (b)'s deny-list from
    // the defaults, and both overridden by the optional `externalNodes` section. This is the ONLY
    // constructor production code should use — it is what makes the feature default-ON with the config
    // section as its knob.
    public static ExternalNodeAdmission FromRules(RuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return Create(rules.Effects, rules.ExternalNodes);
    }

    public static ExternalNodeAdmission Create(IReadOnlyList<FactEffectRule>? effectRules, FactExternalNodeRule? config) =>
        new ExternalNodeAdmission
        {
            RuleTypePatterns = TypePatternsOf(effectRules),
            DeniedAssemblies = config is { DenyAssemblies.Count: > 0 }
                ? [.. DefaultFrameworkAssemblies, .. config.DenyAssemblies]
                : DefaultFrameworkAssemblies,
            AllowedAssemblies = config?.AllowAssemblies ?? [],
        };

    // Every declaring/receiver/base type gate any effect rule mentions, deduped. `treatAsDispatch` rules
    // are INCLUDED: they still name a type the ruleset cares about, and they are the graph-shaping rules —
    // exactly the ones whose targets should be visible as nodes.
    public static IReadOnlyList<string> TypePatternsOf(IReadOnlyList<FactEffectRule>? effectRules)
    {
        if (effectRules is null || effectRules.Count == 0)
        {
            return [];
        }

        var patterns = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in effectRules)
        {
            Add(rule.DeclaringTypes);
            Add(rule.ReceiverTypes);
            Add(rule.DeclaringTypeBaseTypes);
        }

        return [.. patterns];

        void Add(IReadOnlyList<string>? gates)
        {
            foreach (var gate in gates ?? [])
            {
                if (!string.IsNullOrWhiteSpace(gate))
                {
                    patterns.Add(gate);
                }
            }
        }
    }

    // Is this out-of-source call target admitted as a graph node? `targetAssembly` is
    // ReferenceFact.TargetAssembly, `targetDocId` its TargetSymbolId. Only ever asked of rows with
    // TargetInSource == false; a first-party target is admitted by the ordinary graph filter, not here.
    public bool Admits(string? targetAssembly, string targetDocId)
    {
        if (string.IsNullOrEmpty(targetDocId) || !targetDocId.StartsWith("M:", StringComparison.Ordinal))
        {
            // Only METHOD DocIDs can be call-graph nodes (see CLAUDE.md's effect/reachability invariant):
            // an unresolved error-type target (`!:`) has no declaring type to reason about and no body.
            return false;
        }

        var assembly = targetAssembly ?? "";

        // Explicit ALLOW wins over everything, including the framework deny-list.
        if (SegmentMatchAny(assembly, AllowedAssemblies))
        {
            return true;
        }

        // Arm (a): a type the loaded effect rules already model. Checked BEFORE the deny-list, because
        // these are deliberately the BCL types the deny-list would otherwise reject.
        if (DeclaringTypeIsRuleMentioned(targetDocId))
        {
            return true;
        }

        // Arm (b): anything not in a framework (or explicitly denied) assembly.
        return !SegmentMatchAny(assembly, DeniedAssemblies);
    }

    private bool DeclaringTypeIsRuleMentioned(string targetDocId)
    {
        if (RuleTypePatterns.Count == 0)
        {
            return false;
        }

        var declaringType = DeclaringTypeOf(targetDocId);
        if (declaringType is null)
        {
            return false;
        }

        foreach (var pattern in RuleTypePatterns)
        {
            // FactEffectDeriver.TypeNameMatches, verbatim: FQN equality, or the gate is a namespace/base
            // prefix of the actual type.
            if (
                string.Equals(declaringType, pattern, StringComparison.Ordinal)
                || declaringType.StartsWith(pattern + ".", StringComparison.Ordinal)
            )
            {
                return true;
            }
        }

        return false;
    }

    // Assembly-NAME-SEGMENT match: `entry` matches `name` exactly, or `name` starts with `entry + "."`.
    // So `System` admits System / System.Core / System.Net.Http and rejects SystemX — the bare StartsWith
    // this replaces would have eaten the latter.
    public static bool SegmentMatchAny(string name, IReadOnlyList<string> entries)
    {
        foreach (var entry in entries)
        {
            if (
                string.Equals(name, entry, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(entry + ".", StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }
        }

        return false;
    }

    // The LEAF node for an admitted external target. SymbolId is the DocID exactly as the fact stores it
    // (so an edge's Callee and the node's id are the same string — nothing to reconcile); Name and
    // ContainingTypeId are parsed back out of it; FilePath/Line stay null/0 because there is no indexed
    // source. IsExternal is the marker every consumer keys on — dispatch suppression and the renderers'
    // «external» tag — never `FilePath is null`, which synthetic test MethodRefs also satisfy.
    public static MethodRef SynthesizeNode(string targetDocId)
    {
        var parsed = ParseMethodDocId(targetDocId);
        return new MethodRef(
            SymbolId: targetDocId,
            Name: parsed?.Name ?? targetDocId,
            ContainingTypeId: parsed is { } p ? "T:" + p.DeclaringType : null,
            IsOverride: false,
            FilePath: null,
            Line: 0,
            IsExternal: true
        );
    }

    // "M:Ns.Type.Member(args)" -> ("Ns.Type", "Member"). Generic arity markers are kept on the declaring
    // type (`Foo`1`) so the id round-trips; the METHOD name is trimmed at its own ``N marker.
    public static (string DeclaringType, string Name)? ParseMethodDocId(string docId)
    {
        if (string.IsNullOrEmpty(docId) || !docId.StartsWith("M:", StringComparison.Ordinal))
        {
            return null;
        }

        var searchEnd = docId.IndexOf('(', StringComparison.Ordinal);
        if (searchEnd < 0)
        {
            searchEnd = docId.Length;
        }

        var lastDot = docId.LastIndexOf('.', searchEnd - 1);
        if (lastDot < 2)
        {
            return null;
        }

        var methodStart = lastDot + 1;
        var backtick = docId.IndexOf('`', startIndex: methodStart, count: searchEnd - methodStart);
        var methodEnd = backtick >= 0 ? backtick : searchEnd;
        if (methodEnd <= methodStart)
        {
            return null;
        }

        return (docId[2..lastDot], docId[methodStart..methodEnd]);
    }

    // The external DECLARING type of a method DocID, without the `T:` prefix and without arity markers —
    // the shape the effect rules' type gates are authored in.
    private static string? DeclaringTypeOf(string docId)
    {
        var parsed = ParseMethodDocId(docId);
        if (parsed is null)
        {
            return null;
        }

        var declaring = parsed.Value.DeclaringType;
        var backtick = declaring.IndexOf('`', StringComparison.Ordinal);
        return backtick < 0 ? declaring : StripArity(declaring);
    }

    // Removes every `N / ``N arity marker from a dotted type name ("Ns.Foo`2.Bar`1" -> "Ns.Foo.Bar").
    private static string StripArity(string type)
    {
        var sb = new System.Text.StringBuilder(type.Length);
        for (var i = 0; i < type.Length; i++)
        {
            if (type[i] != '`')
            {
                sb.Append(type[i]);
                continue;
            }

            i++;
            while (i < type.Length && (type[i] == '`' || char.IsAsciiDigit(type[i])))
            {
                i++;
            }

            i--;
        }

        return sb.ToString();
    }
}
