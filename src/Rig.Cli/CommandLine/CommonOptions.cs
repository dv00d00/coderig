using System.CommandLine;
using Rig.Domain.Functions;

namespace Rig.Cli.CommandLine;

// Shared option/argument factories so every command declares the same flag the same way — one home for
// --rules, --async, --depth, --only/--exclude, --format, --limit, etc. Each factory returns a FRESH
// instance (System.CommandLine binds a value per symbol), so a command's Build() keeps the references it
// reads back in its action. This is where the hand-rolled GetOption/MaxDepthOf/ParseList loops used to
// live, now expressed once, declaratively.
internal static class CommonOptions
{
    internal static Argument<string> Pattern(string name, string description) => new(name) { Description = description };

    // --rules <path>... (repeatable): each value is resolved to a full path, matching the old loop.
    internal static Option<string[]> Rules() =>
        new("--rules")
        {
            Description = "Extra analysis-rule JSON file(s) to layer on (repeatable).",
            CustomParser = r => r.Tokens.Select(t => Path.GetFullPath(t.Value)).ToArray(),
        };

    internal static Option<bool> Async() =>
        new("--async") { Description = "Also walk async handoff edges (scheduled/cross-thread), tagged ⤳." };

    // --include-delivery: with --async, ALSO cross the imprecise publish→consumer delivery FAN-OUT edges
    // (an event raise / actor tell joined to EVERY same-symbol subscriber, no instance identity). Off by
    // default because that join over-approximates — it links unrelated callers to unrelated handlers
    // (see docs/FIX-event-raise-overapproximation.md). No effect without --async.
    internal static Option<bool> IncludeDelivery() =>
        new("--include-delivery")
        {
            Description =
                "With --async, also cross imprecise delivery fan-out edges (event_raise/actor_tell to all subscribers). Over-approximate.",
        };

    internal static Option<bool> Raw() => new("--raw") { Description = "Bypass graph shaping (factory/cut/context rules)." };

    // --max-depth / --maxdepth / --depth (aliases): unbounded when absent (the action substitutes int.MaxValue).
    // Keep both historical spellings; --max-depth is the conventional long-option form agents expect.
    internal static Option<int?> Depth() =>
        new("--max-depth", "--maxdepth", "--depth") { Description = "Max traversal depth (default: unbounded)." };

    internal static Option<string[]> Only() =>
        FilterList(name: "--only", description: "Keep only these effects (provider or provider:operation).");

    internal static Option<string[]> Exclude() => FilterList(name: "--exclude", description: "Drop these effects (e.g. --exclude throw).");

    // --intrinsic: restore the language-intrinsic providers (alloc, throw) that are hidden by default.
    // They scale with code VOLUME rather than with behaviour and are ~91% of all effects on a large
    // monorepo, so they are noise for review and signal only for low-level perf/robustness work.
    // See EffectDerivation.IntrinsicProviders for why the set is closed at two.
    internal static Option<bool> Intrinsic() =>
        new("--intrinsic")
        {
            Description =
                "Include language-intrinsic effects (alloc, throw) — every `new`/`throw`, proportional to code volume "
                + "rather than behaviour, and hidden by default. Naming one in --only (e.g. --only alloc) implies this.",
        };

    // --exclude-namespace <prefix>... (repeatable): drop hazard findings whose enclosing DocID namespace
    // starts with the given prefix (case-insensitive). Filters HAZARD output only — effects are unaffected.
    // Useful to suppress framework/vendored noise (e.g. --exclude-namespace Echo.Process --exclude-namespace System.).
    internal static Option<string[]> ExcludeNamespace() =>
        new("--exclude-namespace")
        {
            Description =
                "Drop hazard findings whose enclosing method namespace starts with this prefix (repeatable; case-insensitive). Filters hazards only — effects are unaffected. Example: --exclude-namespace Echo.Process --exclude-namespace System.",
            CustomParser = r => r.Tokens.Select(t => t.Value).ToArray(),
        };

    // A repeatable list option whose value is split on commas OR whitespace (also ';' / tab) with empties
    // trimmed — so `--exclude throw`, `--exclude throw,llblgen:read`, `--exclude "throw cache"`, and
    // repeated flags all parse identically. The case-insensitive set is built by FilterSet at read time.
    private static Option<string[]> FilterList(string name, string description) =>
        new(name)
        {
            Description = description,
            CustomParser = r =>
                r.Tokens.SelectMany(t =>
                        t.Value.Split([',', ' ', '\t', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    )
                    .ToArray(),
        };

    internal static Option<string?> Format(string? description = null, string[]? allowedValues = null)
    {
        var opt = new Option<string?>("--format") { Description = description ?? "Output format; `tsv` for machine-readable rows." };
        if (allowedValues is not null)
        {
            opt.AcceptOnlyFromAmong(allowedValues);
        }

        return opt;
    }

    // --store <ref> (aliases --commit/--at): read from a specific per-commit store instead of the latest
    // index. The ref is a store-id or a commit sha (full or short) — resolved by StoreLayout.DbPathForRef.
    internal static Option<string?> Store() =>
        new("--store", "--commit", "--at")
        {
            Description = "Read from a specific indexed store (commit sha/short-sha or store-id); default is the latest index.",
        };

    internal static Option<string?> Kind() => new("--kind") { Description = "Filter by symbol/reference kind." };

    internal static Option<int> Limit(int defaultValue) =>
        new("--limit") { Description = $"Max rows to show (default {defaultValue}).", DefaultValueFactory = _ => defaultValue };

    // Tier-1 `--limit` with NO fixed default — absent means UNBOUNDED (the action substitutes int.MaxValue).
    // Distinct from Limit(n), which symbols/refs use for their sensible fixed cap; the flood-prone traversal
    // listings (reaches/callers) default to showing everything and truncate only when a limit is given.
    // `tree` overrides the description: there absent means the internal 50k node-budget safety cap (not
    // unbounded) and the unit is tree NODES, not listing rows.
    internal static Option<int?> Limit(string? description = null) =>
        new("--limit") { Description = description ?? "Max rows in flood-prone listings (default: unbounded)." };

    // THE DEMAND BUDGETS, made visible. A live traversal builds a keyed working graph whose size is an
    // order of magnitude larger than the answer it produces (a 2k-method `reaches` on a 227-project monorepo
    // admits well past 20k nodes), and generic projection has a separate work budget. Both used to be
    // hard-coded, so on a large codebase every query failed with a number the user could neither see the
    // meaning of nor change. Defaults are calibrated against a real 227-project solution; 0 means uncapped,
    // for the case where the honest answer is "this codebase is bigger than any default I can pick".
    internal static Option<int?> MaxNodes() =>
        new("--max-nodes") { Description = "Max nodes in the demand traversal graph (0 = uncapped; default 250000)." };

    internal static Option<int?> MaxGenericWork() =>
        new("--max-generic-work") { Description = "Max generic-monomorphization work units (0 = uncapped; default 5000000)." };

    // 0 => uncapped, absent => the calibrated default the request record already carries.
    internal static int? ResolveBudget(int? value) =>
        value is null ? null
        : value == 0 ? int.MaxValue
        : value;

    internal static Option<bool> NoCache() => new("--no-cache") { Description = "Bypass the query cache." };

    // --no-gate: disable the shared_state:read write-pairing gate. By default a static-field read effect is
    // emitted only when its cell is ALSO written somewhere (so it can pair with a write for the race_window
    // TOCTOU hazard); --no-gate emits every static-field read, including never-written const/enum cells.
    internal static Option<bool> NoGate() =>
        new("--no-gate")
        {
            Description =
                "Disable the shared_state:read write-pairing gate — emit every static-field read, including never-written const/enum cells (default: gate on).",
        };

    // --no-amplification: disable the AMPLIFICATION finding tier (looped_effect — see HazardKinds). By default
    // a looped effect gets its own displayed section (derive) / inline 🔁 mark (tree --view hazards), broken
    // down by provider:operation; --no-amplification collapses it back to an anonymous count line in the
    // generic "Observations on effects" block, i.e. reproduces the pre-2026-08 output exactly. Mirrors --no-gate
    // (an inverted default-on flag) rather than an --amplification opt-in: a looped effect is a structural FACT,
    // and facts ship as on-by-default inventory (only JUDGMENTS like n_plus_1 need FP calibration first).
    internal static Option<bool> NoAmplification() =>
        new("--no-amplification")
        {
            Description =
                "Disable the amplification finding tier (looped_effect) — collapse it back into the generic observation counts instead of its own provider:operation section (default: amplification on).",
        };

    internal static Option<bool> Time() => new("--time") { Description = "Print per-phase timings to stderr." };

    // --no-live: read the .rig STORE even when a `rig watch` resident index is serving this directory.
    //
    // The default is the other way round on purpose (see LiveRoute): while a host is running, the store is
    // the STALE answer — pinned to whatever commit was indexed — and the live index is the tree as it is
    // now. So this flag is for the cases where the indexed SNAPSHOT is what you actually want: reproducing a
    // report, comparing against a commit, or checking whether a difference is your edit or the index.
    // RIG_NO_LIVE=1 is the same choice made once for a whole shell.
    internal static Option<bool> NoLive() =>
        new("--no-live")
        {
            Description =
                "Answer from the .rig store even when a `rig watch` resident index is serving this directory (default: use the "
                + "resident index when one is running, since the store is pinned to the last indexed commit). Env: RIG_NO_LIVE=1.",
        };

    internal static Option<bool> Files() => new("--files") { Description = "Append each node's source location." };

    internal static Option<bool> Signatures() => new("--signatures", "--sig") { Description = "Show compact parameter signatures." };

    // --- value readers (the invariant translations every command shares) ---

    // Traversal DEFAULTS to SYNC-CUT: handoff edges aren't crossed unless --async opts in. Under --async the
    // default is AsyncExact (cross sound handoffs but NOT imprecise delivery fan-out); --include-delivery
    // escalates to AsyncInclude (cross the fan-out too — the over-approximate superset). --include-delivery
    // without --async is a no-op (stays SyncCut).
    internal static FactPathFinder.TraversalMode Mode(bool async, bool includeDelivery = false) =>
        async
            ? (includeDelivery ? FactPathFinder.TraversalMode.AsyncInclude : FactPathFinder.TraversalMode.AsyncExact)
            : FactPathFinder.TraversalMode.SyncCut;

    // --max-depth/--maxdepth/--depth absent => unbounded (int.MaxValue); the closure + node cap + cycle dedup still terminate.
    internal static int DepthOrUnbounded(int? depth) => depth ?? int.MaxValue;

    // --format token readers: matched case-insensitively. These replace the hand-rolled
    // `string.Equals(format, "<fmt>", StringComparison.OrdinalIgnoreCase)` that was repeated at every
    // command's read site (and in `tree`'s cross-flag validator). `llm`/`llm-ids` are only meaningful for
    // `tree`; the helpers live here so the one spelling serves all callers.
    internal static bool IsTsv(string? format) => string.Equals(format, "tsv", StringComparison.OrdinalIgnoreCase);

    internal static bool IsLlm(string? format) => string.Equals(format, "llm", StringComparison.OrdinalIgnoreCase);

    internal static bool IsLlmIds(string? format) => string.Equals(format, "llm-ids", StringComparison.OrdinalIgnoreCase);

    // The case-insensitive effect-filter set from a parsed --only/--exclude value (null when the flag was absent).
    internal static HashSet<string> FilterSet(string[]? tokens) => new(tokens ?? [], StringComparer.OrdinalIgnoreCase);

    // Returns the parsed --exclude-namespace prefixes as a list (empty when the flag was absent).
    internal static IReadOnlyList<string> NamespacePrefixes(string[]? tokens) => tokens ?? [];

    // Returns true when the enclosing DocID matches any of the given namespace prefixes. Matching strips the
    // leading "M:" kind prefix (and any other single-char kind prefix) and compares the namespace portion of
    // the remainder against each prefix, case-insensitively. An empty prefix list never matches (pass-through).
    internal static bool MatchesExcludedNamespace(string enclosing, IReadOnlyList<string> excludedPrefixes)
    {
        if (excludedPrefixes.Count == 0)
        {
            return false;
        }

        // Strip the "M:" (or other) kind prefix.
        var id = enclosing.Length > 2 && enclosing[1] == ':' ? enclosing[2..] : enclosing;
        foreach (var prefix in excludedPrefixes)
        {
            if (id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // --rules is null when absent; callers want an empty list.
    internal static IReadOnlyList<string> RulesOf(string[]? rules) => rules ?? [];
}
