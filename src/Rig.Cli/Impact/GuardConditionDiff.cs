using System.Text;
using Rig.Domain.Data;
using Rig.Domain.Functions;

namespace Rig.Cli.Impact;

// How a call edge's control-dependence CONDITION moved between two stores. This is the predicate-only
// change class that the effect-set diff is structurally blind to: tightening `if (!IsPersonMerge)` into
// `if (!IsPersonMerge && !StopAudits)` adds no call and no effect, so `ep_effect_*` is empty and
// `--expect-no-effect-change` PASSES — while an audit silently stops firing for a subset of inputs.
// (MedDBase MR !11025; see docs/backlog/todo/impact-guard-delta-for-predicate-only-changes.md.)
internal enum GuardVerdict
{
    // The effect now fires on strictly FEWER paths (base conjuncts ⊂ head conjuncts). The review headline:
    // this is the shape of audit suppression, permission narrowing, and feature-flag gating.
    Narrowed,

    // Strictly MORE paths (head ⊂ base) — a guard relaxed or removed. Also review-worthy: work that used to
    // be conditional now runs unconditionally.
    Widened,

    // Incomparable — the conjunct sets differ in both directions. Deliberately NOT sub-classified: without a
    // solver we cannot say which way the truth set moved, and guessing would be worse than disclosing.
    Changed,
}

// One call edge whose gating condition changed base→head, with what it gates. Keyed on (Caller, Callee) —
// param-bearing DocIDs, since that is the edge identity; the LINE is deliberately excluded so a pure line
// shift is not a change.
//
// Effects are the non-intrinsic effect keys reachable FROM the callee (i.e. what this condition now gates
// differently), as `provider:operation` labels. EpCount/SampleRoutes attribute the edge to entry points
// whose reach contains the caller — a COUNT plus a few samples rather than a row per EP, because one changed
// edge in a shared utility is reachable from hundreds of EPs and would otherwise flood the stream.
internal sealed record GuardConditionDelta(
    string Caller,
    string Callee,
    string BaseCondition,
    string HeadCondition,
    GuardVerdict Verdict,
    IReadOnlyList<string> Effects,
    int EpCount,
    IReadOnlyList<string> SampleRoutes
);

// Diffing control-dependence conditions across two stores, and classifying each change.
//
// WHY CONJUNCT SETS rather than string containment: the stored condition is RAW SOURCE — newlines, original
// indentation, and any interleaved comment. MR !11025's guard is 230 chars and contains
// `// no auditing for documents anymore, …` sitting between its two conjuncts. Substring/prefix containment
// on that text is fragile, and a comment-only edit would register as a condition change. Splitting the
// top-level `&&` and normalising each conjunct (strip comments, collapse whitespace) is robust to formatting
// and gives the wanted verdict on the motivating case:
//
//   base {!IsPersonMerge} ⊂ head {!IsPersonMerge, (!FkDocument.HasValue || !…StopPersonEventAudits)} → NARROWED
//
// This is a SYNTACTIC over-approximation of logical implication, disclosed as such: it recognises the
// common "AND another clause onto the existing guard" shape and falls back to `Changed` for everything
// else. It never claims Narrowed/Widened for conditions it cannot relate by containment.
internal static class GuardConditionDiff
{
    // Effects whose presence does NOT justify reporting a guard change: `alloc`/`throw` are proportional to
    // code volume rather than behaviour (the same reason they are hidden by default in derive/reaches/tree),
    // so a condition moved around pure computation is noise. Which of the REMAINING providers count as
    // review-relevant is repo DOMAIN policy, so it is NOT hardcoded here — scope with --only/--exclude, which
    // filter these rows through the same token grammar as the effect rows.
    private static IReadOnlyList<string> ReportableEffects(IEnumerable<string> labels) =>
        labels
            .Where(l => !Effects.EffectDerivation.IntrinsicProviders.Contains(l.Split(':')[0]))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    // The guarded call edges of a graph: (caller, callee) -> the encoded guard set of the edge. Only edges
    // that actually carry a guard; an unguarded (must-run) edge is represented by ABSENCE, which the diff
    // reads as the empty conjunct set — so "guard appeared" and "guard vanished" fall out of the same subset
    // comparison as a guard that merely moved.
    //
    // Multiple call SITES from one caller to one callee under DIFFERENT conditions collapse here. That is
    // deliberate and disclosed: the pair is the reviewable identity, and per-site keying would make a moved
    // call read as remove+add. When the sites disagree the conditions are unioned, which biases toward
    // `Changed` (the honest fallback) rather than inventing a direction.
    internal static Dictionary<(string Caller, string Callee), SortedSet<string>> GuardedEdges(FactGraphData graph)
    {
        var map = new Dictionary<(string, string), SortedSet<string>>();
        foreach (var e in graph.CallEdges)
        {
            if (string.IsNullOrEmpty(e.EnclosingGuards))
            {
                continue;
            }

            var key = (e.Caller, e.Callee);
            var conjuncts = Conjuncts(e.EnclosingGuards);
            if (map.TryGetValue(key, out var existing))
            {
                existing.UnionWith(conjuncts);
                continue;
            }

            map[key] = conjuncts;
        }

        return map;
    }

    // How many guarded edges target a LAMBDA (`~λN` synthetic id). A store indexed before 2026-07-27 has
    // exactly zero, because ProcessLambda never set EnclosingGuards — so this is the fingerprint that tells a
    // version-skewed diff apart from a real change. See GuardCoverage.
    internal static int LambdaGuardCount(Dictionary<(string Caller, string Callee), SortedSet<string>> guarded) =>
        guarded.Keys.Count(k => k.Callee.Contains("~λ", StringComparison.Ordinal));

    // Which of `candidates` exist as a call edge in this graph. Needed so a DELETED call site (or a deleted
    // method) is not misreported as a guard change: an edge present on only one side is an add/remove that
    // the effect-set and reach diffs already own, not a predicate-only change. Restricted to candidates so
    // this stays bounded by the guarded-edge count rather than the full ~626k-edge set.
    internal static HashSet<(string Caller, string Callee)> PairsPresent(
        FactGraphData graph,
        IReadOnlyCollection<(string Caller, string Callee)> candidates
    )
    {
        var wanted = candidates as HashSet<(string, string)> ?? [.. candidates];
        var present = new HashSet<(string, string)>();
        foreach (var e in graph.CallEdges)
        {
            var key = (e.Caller, e.Callee);
            if (wanted.Contains(key))
            {
                present.Add(key);
            }
        }

        return present;
    }

    // The normalized top-level AND-conjuncts of an encoded guard set. The guard set is already a list of
    // DISTINCT DECISIONS that AND-join (EncodedGuardsFor dedups the lowered sub-branches of one condition
    // back into a single widened entry), so:
    //
    // - WhenTrue entry: the effect runs when the predicate holds, so its own top-level `&&` clauses are
    //   conjuncts of the whole condition and split further.
    // - WhenTrue=false entry: the effective condition is `!(P)`, which by De Morgan is a DISJUNCTION — its
    //   parts are NOT conjuncts. Kept as one opaque `!(P)` clause so no false containment can be derived.
    internal static SortedSet<string> Conjuncts(string? encodedGuards)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (predicate, whenTrue) in FactStructuralContext.DecodeGuards(encodedGuards))
        {
            if (!whenTrue)
            {
                set.Add($"!({NormalizeConjunct(predicate)})");
                continue;
            }

            foreach (var clause in SplitTopLevelAnd(predicate))
            {
                var normalized = NormalizeConjunct(clause);
                if (normalized.Length > 0)
                {
                    set.Add(normalized);
                }
            }
        }

        return set;
    }

    // A human-readable rendering of an encoded guard set: the same clauses the classifier compares, joined
    // with ` && `. Comment- and whitespace-normalized, so the row is greppable and a 230-char multi-line
    // source condition does not span lines in TSV output. Empty string = must-run (no guard).
    internal static string Render(string? encodedGuards) => string.Join(" && ", Conjuncts(encodedGuards));

    // Classify a condition move. Null when nothing changed (equal conjunct sets) — the overwhelmingly common
    // case, filtered out before any row is produced.
    internal static GuardVerdict? Classify(SortedSet<string> baseConjuncts, SortedSet<string> headConjuncts)
    {
        if (baseConjuncts.SetEquals(headConjuncts))
        {
            return null;
        }

        // Strictly more clauses required => fires on strictly fewer paths. `IsSubsetOf` on equal sets is
        // true, but equality was already excluded above, so these are proper subsets.
        if (baseConjuncts.IsSubsetOf(headConjuncts))
        {
            return GuardVerdict.Narrowed;
        }

        if (headConjuncts.IsSubsetOf(baseConjuncts))
        {
            return GuardVerdict.Widened;
        }

        return GuardVerdict.Changed;
    }

    // Strip comments and collapse whitespace in one pass, tracking string/char-literal state so a `//` or
    // `/*` INSIDE a literal is preserved rather than truncating the condition. Verbatim (`@"..."`) strings
    // are handled by the same doubled-quote escape rule; interpolation braces need no special casing because
    // we only care about not mistaking literal content for a comment.
    internal static string NormalizeConjunct(string text)
    {
        var sb = new StringBuilder(text.Length);
        var inString = false;
        var inChar = false;
        var lastWasSpace = true; // leading whitespace is dropped

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inString || inChar)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < text.Length)
                {
                    sb.Append(text[++i]); // escaped char: copy verbatim, never a terminator
                }
                else if ((inString && c == '"') || (inChar && c == '\''))
                {
                    inString = false;
                    inChar = false;
                }

                lastWasSpace = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    sb.Append(c);
                    lastWasSpace = false;
                    continue;
                case '\'':
                    inChar = true;
                    sb.Append(c);
                    lastWasSpace = false;
                    continue;
            }

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }

                // The newline that ended the comment is whitespace between clauses.
                if (!lastWasSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/'))
                {
                    i++;
                }

                i++; // land on '/', the loop's i++ steps past it
                if (!lastWasSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            sb.Append(c);
            lastWasSpace = false;
        }

        return sb.ToString().Trim();
    }

    // Split on TOP-LEVEL `&&` only — inside parens, string/char literals, or a comment, an `&&` belongs to a
    // sub-expression and must not split. `a && (b || c)` yields two clauses; `a || b` yields one (a
    // disjunction is a single conjunct). A single `&` is a bitwise op, not a split point.
    internal static IReadOnlyList<string> SplitTopLevelAnd(string condition)
    {
        var clauses = new List<string>();
        var depth = 0;
        var start = 0;
        var inString = false;
        var inChar = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var i = 0; i < condition.Length; i++)
        {
            var c = condition[i];

            if (inLineComment)
            {
                if (c == '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                if (c == '*' && i + 1 < condition.Length && condition[i + 1] == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (inString || inChar)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if ((inString && c == '"') || (inChar && c == '\''))
                {
                    inString = false;
                    inChar = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    continue;
                case '\'':
                    inChar = true;
                    continue;
                case '(' or '[':
                    depth++;
                    continue;
                case ')' or ']':
                    depth--;
                    continue;
            }

            if (c == '/' && i + 1 < condition.Length && condition[i + 1] == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }

            if (c == '/' && i + 1 < condition.Length && condition[i + 1] == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            if (depth == 0 && c == '&' && i + 1 < condition.Length && condition[i + 1] == '&')
            {
                clauses.Add(condition[start..i]);
                i++;
                start = i + 1;
            }
        }

        clauses.Add(condition[start..]);
        return clauses;
    }

    // The full diff: every (caller, callee) edge present on BOTH stores whose gating condition changed,
    // paired with what it gates and which entry points reach it. `effectsFromCallee` supplies the effect
    // labels reachable from a callee (computed by the caller, which owns the graph + derived effects);
    // `epsReaching` supplies the (count, samples) attribution for a caller.
    //
    // Ordering is the review order: Narrowed first (an effect that silently stopped firing), then Widened,
    // then Changed; within a verdict, the edges gating the most effects first.
    internal static IReadOnlyList<GuardConditionDelta> Diff(
        Dictionary<(string Caller, string Callee), SortedSet<string>> baseGuarded,
        Dictionary<(string Caller, string Callee), SortedSet<string>> headGuarded,
        IReadOnlySet<(string Caller, string Callee)> basePresent,
        IReadOnlySet<(string Caller, string Callee)> headPresent,
        Func<string, IReadOnlyList<string>> effectsFromCallee,
        Func<string, (int Count, IReadOnlyList<string> Samples)> epsReaching
    )
    {
        var candidates = new HashSet<(string Caller, string Callee)>(baseGuarded.Keys);
        candidates.UnionWith(headGuarded.Keys);

        var results = new List<GuardConditionDelta>();
        foreach (var key in candidates)
        {
            // An edge that exists on only ONE side is an added/removed call, not a predicate change — owned
            // by the effect-set and reach diffs. Skipping it here is what keeps this signal specific.
            if (!basePresent.Contains(key) || !headPresent.Contains(key))
            {
                continue;
            }

            // A missing key means the edge is unguarded (must-run) on that side, i.e. the empty conjunct set —
            // so "guard appeared" and "guard vanished" classify through the same subset comparison.
            var baseConjuncts = baseGuarded.TryGetValue(key, out var b) ? b : [];
            var headConjuncts = headGuarded.TryGetValue(key, out var h) ? h : [];
            if (Classify(baseConjuncts, headConjuncts) is not { } verdict)
            {
                continue;
            }

            // A condition moved around work with no behavioural effect is noise, not review material.
            var effects = ReportableEffects(effectsFromCallee(key.Callee));
            if (effects.Count == 0)
            {
                continue;
            }

            var (epCount, samples) = epsReaching(key.Caller);
            results.Add(
                new GuardConditionDelta(
                    Caller: key.Caller,
                    Callee: key.Callee,
                    BaseCondition: string.Join(" && ", baseConjuncts),
                    HeadCondition: string.Join(" && ", headConjuncts),
                    Verdict: verdict,
                    Effects: effects,
                    EpCount: epCount,
                    SampleRoutes: samples
                )
            );
        }

        return
        [
            .. results
                .OrderBy(r =>
                    r.Verdict switch
                    {
                        GuardVerdict.Narrowed => 0,
                        GuardVerdict.Widened => 1,
                        _ => 2,
                    }
                )
                .ThenByDescending(r => r.Effects.Count)
                .ThenBy(r => r.Caller, StringComparer.Ordinal)
                .ThenBy(r => r.Callee, StringComparer.Ordinal),
        ];
    }
}
