using System.Text.Json;
using Rig.Analysis.Inventory;
using Rig.Cli.CommandLine;
using Rig.Cli.Commands;
using Rig.Domain.Functions;

namespace Rig.Cli.Live;

// Answers ONE query against ONE generation of live facts, returning the rendered text. Two ways in, one body:
//
//   * a LINE OF TEXT (`AnswerAsync`) — the stdin loop `rig watch` exposes to a human at a terminal. Every
//     verb here is the DEFAULT invocation of its command, no flags, which is exactly what makes each answer
//     directly comparable to `rig <verb> …` against a store of the same tree (LiveReachesTests,
//     LivePathCallersTests and LiveTreeTests all compare precisely that).
//   * a TRANSPORT REQUEST (`RunRequestAsync`) — an allowlisted verb plus that verb's own strongly-typed
//     options record, arriving over the named pipe from a one-shot `rig` invocation. This is where the
//     rendering flags come from: `--format tsv`, `--limit`, `--view`, `--depth` and the rest are already in
//     the options record the CLI parsed, so carrying it gets them all with no per-flag work.
//
// THE ALLOWLIST IS THE SWITCH IN RunRequestAsync, and it is the only place a verb becomes a command. There is
// no argv, no command lookup and no reflection anywhere on this path: a verb outside the four arms has no
// code to reach, which is a stronger statement than "is rejected". LiveQueryVerbs.Routable names the same
// four for the client's benefit, and LiveTransportRoutingTests asserts the two agree.
//
// Nothing is written to Console: the caller owns presentation (and TUnit does not surface console output, a
// lesson this program has already paid for). Unrecognized input answers with what IS supported — an obscure
// failure on a mistyped query would make the whole live surface feel broken.
internal static class LiveQueryRunner
{
    // NOTE ON WORD ORDER: verbs are APPENDED to the list, before the `quit` clause, and WatchCommandTests pins
    // this sentence verbatim — so adding one means updating that one string literal too (the assertion's
    // SUBJECT is unchanged: the banner enumerates exactly the verbs that route). Keep doing it that way rather
    // than loosening the assertion to a substring: an inaccurate banner tells a user a feature doesn't exist.
    internal const string Usage =
        "supported live queries: `reaches <pattern>`, `path <from> <to>`, `callers <to>`, `tree <pattern>` "
        + "(optionally `--async [--include-delivery]`); `quit` (or EOF) exits.";

    // One answer, with the command's two streams kept SEPARATE. Splitting them is not fussiness: the CLI puts
    // the answer on stdout and disclosures (ambiguity, seed notes) on stderr, and a test that claims live
    // output equals store output has to compare like with like — a concatenated blob would let a disclosure
    // moving between streams pass unnoticed. `Text` is the single-string view a text transport wants.
    internal sealed record LiveAnswer(int Exit, string Out, string Err)
    {
        public string Text => Err.Length == 0 ? Out : Out + Err;
    }

    // The outcome of a TRANSPORT request: an answer, or a DECLINE. A decline carries no rendered output on
    // purpose — the client must fall back to the store, and an empty result would read as "no matches".
    internal sealed record RequestResult(LiveAnswer? Answer, string? DeclineReason)
    {
        internal static RequestResult Declined(string reason) => new(null, reason);
    }

    // THE VERB ALLOWLIST. Four arms, one per live-servable command, each decoding the options record its own
    // command declared. This is the entire attack surface of the transport: there is no path from request text
    // to a command name, a file path, or a process — only from one of four constants to one of four bodies.
    //
    // Decoding failures decline rather than throw. A client from a different build whose options record gained
    // a required member should get a store answer with a stated reason, not a stack trace and no answer.
    internal static async Task<RequestResult> RunRequestAsync(LiveQueryRequest request, LiveFactSource facts, string workingDirectory)
    {
        try
        {
            switch (request.Verb)
            {
                case LiveQueryVerbs.Reaches:
                    return await ServeAsync<ReachesCommand.Options>(
                        request,
                        Normalize,
                        o => o.ExtraRules,
                        IsValidReachesOptions,
                        o => RunReachesAsync(o, facts, workingDirectory)
                    );

                case LiveQueryVerbs.Path:
                    return await ServePathAsync(request, facts, workingDirectory);

                case LiveQueryVerbs.Callers:
                    return await ServeCallersAsync(request, facts, workingDirectory);

                case LiveQueryVerbs.Tree:
                    return await ServeAsync<TreeCommand.Options>(
                        request,
                        Normalize,
                        o => o.ExtraRules,
                        IsValidTreeOptions,
                        o => RunTreeAsync(o, facts, workingDirectory)
                    );

                default:
                    return RequestResult.Declined($"`{request.Verb}` is not served from the resident index — {Usage}");
            }
        }
        catch (Exception exception)
            when (exception is DemandForwardGraphUnavailableException or DemandReverseCallersGraphUnavailableException)
        {
            return RequestResult.Declined($"resident `{request.Verb}` demand projection is unavailable: {exception.Message}");
        }
    }

    // WHY `--rules` IS DECLINED RATHER THAN OBEYED, and why this guard is not optional.
    //
    // The live artifacts are memoized under the rules the FACTS were extracted with, and the two identity
    // checks that protect them — LiveQueryFactSource.SameShapingAsMemo (three gated slice SIZES) and
    // DeriveEffectsAsync's reference-identity test — were both written when the live surface had no `--rules`
    // flag, and both carry a comment saying they must be WIDENED if it ever gained one. This transport gives
    // it one: a routed request carries the command's whole options record, `ExtraRules` included.
    //
    // Obeying it is not achievable here, and not only because of those memos. Extraction-time rules (file
    // include/exclude, DI descriptors) shaped the facts the host is holding; no query-time rule set can
    // retroactively change what was extracted. So the choices are a silently-wrong answer or a decline, and
    // the store — which applies `--rules` correctly, from scratch — is one fallback away.
    //
    // Note what is NOT declined: a query with NO `--rules`, which is the ordinary case and the agent case.
    // Its rules come from the same cascade, anchored at the same working directory, that the host booted with
    // (RuleSetLoader.Load auto-discovers the local rig.rules.json), so the two rule sets are equal by
    // construction — which is exactly the argument those two memo checks already rest on.
    private const string RulesNotHonoured =
        "--rules is not honoured by the resident index: its facts were extracted, and its derived layer memoized, "
        + "under the rules `rig watch` booted with. Re-boot the host with those rules, or drop --rules";

    // Preparation is intentionally separate from execution: WatchHost calls this before it captures a
    // snapshot or pays any refinement cost. Null means the normal runner must reject/decline the request
    // without touching resident debt.
    internal static IExactQueryDemand? PrepareTextExactDemand(string query, Rig.Domain.Data.RuleSet rules, bool deploymentsConfigured)
    {
        var trimmed = (query ?? "").Trim();
        var split = trimmed.IndexOf(' ', StringComparison.Ordinal);
        var verb = split < 0 ? trimmed : trimmed[..split];
        if (split < 0)
        {
            return null;
        }

        var remainder = trimmed[(split + 1)..].Trim();
        if (!TryExtractTraversalFlags(remainder, out var queryText, out var asyncMode, out var includeDelivery))
        {
            return null;
        }
        if (string.Equals(verb, LiveQueryVerbs.Path, StringComparison.OrdinalIgnoreCase))
        {
            return TrySplitArguments(queryText, out var endpoints) && endpoints.Count == 2
                ? BuildPathDemand(
                    DefaultPathOptions(endpoints[0], endpoints[1]) with
                    {
                        Async = asyncMode,
                        IncludeDelivery = includeDelivery,
                    },
                    rules,
                    deploymentsConfigured
                )
                : null;
        }

        var pattern = queryText.Trim('"');
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }

        if (string.Equals(verb, LiveQueryVerbs.Reaches, StringComparison.OrdinalIgnoreCase))
        {
            return BuildReachesDemand(
                DefaultReachesOptions(pattern) with
                {
                    Async = asyncMode,
                    IncludeDelivery = includeDelivery,
                },
                rules,
                deploymentsConfigured
            );
        }

        if (string.Equals(verb, LiveQueryVerbs.Callers, StringComparison.OrdinalIgnoreCase))
        {
            return BuildCallersDemand(
                DefaultCallersOptions(pattern) with
                {
                    Async = asyncMode,
                    IncludeDelivery = includeDelivery,
                },
                rules,
                deploymentsConfigured
            );
        }

        return string.Equals(verb, LiveQueryVerbs.Tree, StringComparison.OrdinalIgnoreCase)
            ? BuildTreeDemand(
                DefaultTreeOptions(pattern) with
                {
                    Async = asyncMode,
                    IncludeDelivery = includeDelivery,
                },
                rules,
                deploymentsConfigured
            )
            : null;
    }

    // Kept as the forward-only compatibility seam for focused planner tests and callers that intentionally
    // do not opt into reverse refinement.
    internal static ExactForwardDemand? PrepareTextForwardDemand(string query, Rig.Domain.Data.RuleSet rules, bool deploymentsConfigured) =>
        PrepareTextExactDemand(query, rules, deploymentsConfigured) as ExactForwardDemand;

    internal static IExactQueryDemand? PrepareTransportExactDemand(
        LiveQueryRequest request,
        Rig.Domain.Data.RuleSet rules,
        bool deploymentsConfigured
    )
    {
        if (string.Equals(request.Verb, LiveQueryVerbs.Callers, StringComparison.Ordinal))
        {
            var options = Decode<CallersCommand.Options>(request.Options) is { } decoded ? Normalize(decoded) : null;
            return options is not null && IsValidCallersOptions(options) && options.ExtraRules is not { Count: > 0 }
                ? BuildCallersDemand(options, rules, deploymentsConfigured)
                : null;
        }

        return PrepareTransportForwardDemand(request, rules, deploymentsConfigured);
    }

    internal static ExactForwardDemand? PrepareTransportForwardDemand(
        LiveQueryRequest request,
        Rig.Domain.Data.RuleSet rules,
        bool deploymentsConfigured
    )
    {
        if (string.Equals(request.Verb, LiveQueryVerbs.Path, StringComparison.Ordinal))
        {
            var options = Decode<PathCommand.Options>(request.Options);
            return options is not null && IsValidPathOptions(options) && options.ExtraRules is not { Count: > 0 }
                ? BuildPathDemand(options, rules, deploymentsConfigured)
                : null;
        }

        if (string.Equals(request.Verb, LiveQueryVerbs.Reaches, StringComparison.Ordinal))
        {
            var options = Decode<ReachesCommand.Options>(request.Options) is { } decoded ? Normalize(decoded) : null;
            return options is not null && IsValidReachesOptions(options) && options.ExtraRules is not { Count: > 0 }
                ? BuildReachesDemand(options, rules, deploymentsConfigured)
                : null;
        }

        if (string.Equals(request.Verb, LiveQueryVerbs.Tree, StringComparison.Ordinal))
        {
            var options = Decode<TreeCommand.Options>(request.Options) is { } decoded ? Normalize(decoded) : null;
            return options is not null && IsValidTreeOptions(options) && options.ExtraRules is not { Count: > 0 }
                ? BuildTreeDemand(options, rules, deploymentsConfigured)
                : null;
        }

        return null;
    }

    // WHY THE REMEDY IS CONDITIONAL. "restart the watcher and retry" is sound advice for a STALE-STATE
    // failure — the resident revision moved under the query — and useless for a BUDGET failure: a graph that
    // did not fit a node/monomorphization cap is exactly the same size after a restart, so the suggestion
    // sends the user off to pay a multi-minute cold boot for a guaranteed identical failure. On a large
    // monorepo that is every query, which is how a hard-coded 20k cap read as "rig is broken here" rather
    // than "rig needs a bigger budget here". A budget reason already names its own knob, so pass it through.
    internal static LiveAnswer ExactUnavailable(string verb, long revision, string reason) =>
        new(Exit: 2, Out: "", Err: $"live: {ExactUnavailableDecline(verb, revision, reason)}\n");

    // The SAME sentence, as a DECLINE reason rather than a rendered answer — one string so the two channels
    // cannot drift.
    //
    // WHY THE TRANSPORT DECLINES INSTEAD OF ANSWERING EXIT 2. A live query can fail in two places: while the
    // host PREPARES exactness (demand refinement, a watcher overflow, a topology change, a snapshot that kept
    // moving) and while it EXECUTES (a demand projection that will not load). Execution failure has always
    // declined — RunRequestAsync catches the two Demand*Unavailable exceptions — so the client falls back and
    // the user gets a store answer with the reason stated. Preparation failure used to be rendered as an
    // exit-2 ANSWER, which the client faithfully printed: same class of failure, opposite outcome, and on a
    // large solution (where the sticky flags latch and never clear) that killed every routed query for the
    // life of the host while a perfectly good store answer sat one fallback away. Both channels now decline.
    //
    // The fallback is never silent: LiveRoute prints this reason on stderr and the store path then prints its
    // own `store:` provenance line, so the answer names both why live declined and which snapshot answered.
    internal static string ExactUnavailableDecline(string verb, long revision, string reason) =>
        $"exact {verb} unavailable at resident revision {revision}: {reason}{(IsBudgetReason(reason) ? "" : "; restart the watcher and retry")}";

    private static bool IsBudgetReason(string reason) =>
        reason.Contains("node cap", StringComparison.Ordinal) || reason.Contains("monomorphization", StringComparison.Ordinal);

    private static ExactForwardDemand BuildPathDemand(
        PathCommand.Options options,
        Rig.Domain.Data.RuleSet rules,
        bool deploymentsConfigured
    )
    {
        var shaped = options.Raw ? rules with { Factory = [], Cut = [], Context = [], MaterializedGraphCompatible = false } : rules;
        return BuildDemand(
            ExactForwardQueryKind.Path,
            options.FromPattern,
            options.ToPattern,
            options.Raw,
            options.Depth,
            options.Async,
            options.IncludeDelivery,
            shaped,
            deploymentsConfigured ? ExactForwardDebtScope.WholeResident : ExactForwardDebtScope.DemandBoundary
        );
    }

    private static ExactForwardDemand BuildReachesDemand(
        ReachesCommand.Options options,
        Rig.Domain.Data.RuleSet rules,
        bool deploymentsConfigured
    )
    {
        // Reaches raw retains generic-factory projection; only cut/context and event classification are off.
        var shaped = options.Raw ? rules with { Cut = [], Context = [] } : rules;
        return BuildDemand(
            ExactForwardQueryKind.Reaches,
            options.FromPattern,
            null,
            options.Raw,
            options.Depth,
            options.Async,
            options.IncludeDelivery,
            shaped,
            deploymentsConfigured ? ExactForwardDebtScope.WholeResident : ExactForwardDebtScope.DemandBoundary
        );
    }

    private static ExactForwardDemand BuildTreeDemand(
        TreeCommand.Options options,
        Rig.Domain.Data.RuleSet rules,
        bool deploymentsConfigured
    )
    {
        var shaped = options.Raw ? rules with { Factory = [], Cut = [], Context = [], MaterializedGraphCompatible = false } : rules;
        var wholeDebt = deploymentsConfigured || string.Equals(options.View, "hazards", StringComparison.OrdinalIgnoreCase);
        return BuildDemand(
            ExactForwardQueryKind.Tree,
            options.FromPattern,
            null,
            options.Raw,
            options.Depth,
            options.Async,
            options.IncludeDelivery,
            shaped,
            wholeDebt ? ExactForwardDebtScope.WholeResident : ExactForwardDebtScope.DemandBoundary
        );
    }

    private static ExactCallersDemand BuildCallersDemand(
        CallersCommand.Options options,
        Rig.Domain.Data.RuleSet rules,
        bool deploymentsConfigured
    )
    {
        var shaped = CallersCommand.ShapeRules(options, rules);
        var executionMode = CommonOptions.Mode(options.Async, options.IncludeDelivery);
        var discoveryMode = CallersCommand.DiscoveryMode(options, CommonOptions.IsTsv(options.Format));
        var wholeDebt = options.EntrypointsOnly || deploymentsConfigured;
        return new ExactCallersDemand(
            options.ToPattern,
            new DemandForwardGraphRules(
                new ForwardCallProjectionRules(shaped.Handoff, shaped.Redirect, shaped.Factory, ClassifyEventSubscriptions: !options.Raw),
                shaped.Cut,
                shaped.Context,
                shaped.Delivery
            ),
            CommonOptions.DepthOrUnbounded(options.Depth),
            executionMode,
            discoveryMode,
            wholeDebt ? ExactForwardDebtScope.WholeResident : ExactForwardDebtScope.DemandBoundary
        );
    }

    private static ExactForwardDemand BuildDemand(
        ExactForwardQueryKind queryKind,
        string fromPattern,
        string? toPattern,
        bool raw,
        int? depth,
        bool asyncMode,
        bool includeDelivery,
        Rig.Domain.Data.RuleSet shaped,
        ExactForwardDebtScope debtScope
    ) =>
        new(
            queryKind,
            fromPattern,
            toPattern,
            new DemandForwardGraphRules(
                new ForwardCallProjectionRules(shaped.Handoff, shaped.Redirect, shaped.Factory, ClassifyEventSubscriptions: !raw),
                shaped.Cut,
                shaped.Context,
                shaped.Delivery
            ),
            CommonOptions.DepthOrUnbounded(depth),
            CommonOptions.Mode(asyncMode, includeDelivery),
            debtScope
        );

    private static PathCommand.Options DefaultPathOptions(string from, string to) =>
        new(
            FromPattern: from,
            ToPattern: to,
            Async: false,
            IncludeDelivery: false,
            Raw: false,
            ExtraRules: [],
            Depth: null,
            Format: null,
            Time: false
        );

    private static async Task<RequestResult> ServePathAsync(LiveQueryRequest request, LiveFactSource facts, string workingDirectory)
    {
        if (Decode<PathCommand.Options>(request.Options) is not { } options || !IsValidPathOptions(options))
        {
            return RequestResult.Declined($"unreadable options for `{request.Verb}`");
        }

        return options.ExtraRules is { Count: > 0 }
            ? RequestResult.Declined(RulesNotHonoured)
            : new RequestResult(await RunPathAsync(options, facts, workingDirectory), null);
    }

    private static async Task<RequestResult> ServeCallersAsync(LiveQueryRequest request, LiveFactSource facts, string workingDirectory)
    {
        if (Decode<CallersCommand.Options>(request.Options) is not { } decoded)
        {
            return RequestResult.Declined($"unreadable options for `{request.Verb}`");
        }

        var options = Normalize(decoded);
        if (!IsStructurallyValidCallersOptions(options))
        {
            return RequestResult.Declined($"unreadable options for `{request.Verb}`");
        }

        if (options.ExtraRules is { Count: > 0 })
        {
            return RequestResult.Declined(RulesNotHonoured);
        }

        return new RequestResult(await RunCallersAsync(options, facts, workingDirectory), null);
    }

    private static bool IsValidPathOptions(PathCommand.Options options) =>
        !string.IsNullOrWhiteSpace(options.FromPattern) && !string.IsNullOrWhiteSpace(options.ToPattern) && IsValidDepth(options.Depth);

    private static bool IsValidReachesOptions(ReachesCommand.Options options) =>
        !string.IsNullOrWhiteSpace(options.FromPattern) && IsValidDepth(options.Depth) && (options.Limit is null or > 0);

    private static bool IsValidTreeOptions(TreeCommand.Options options) =>
        !string.IsNullOrWhiteSpace(options.FromPattern)
        && IsValidTreeView(options.View)
        && IsValidDepth(options.Depth)
        && (options.Limit is null or > 0);

    private static bool IsValidCallersOptions(CallersCommand.Options options) => IsStructurallyValidCallersOptions(options);

    private static bool IsStructurallyValidCallersOptions(CallersCommand.Options options) =>
        !string.IsNullOrWhiteSpace(options.ToPattern)
        && !(options.RootsOnly && options.EntrypointsOnly)
        && IsValidDepth(options.Depth)
        && (options.Limit is null or > 0);

    private static bool IsValidDepth(int? depth) => depth is null or >= 0;

    private static bool IsValidTreeView(string? view) =>
        view is not null && new[] { "paths", "full", "effects", "summary", "hazards" }.Contains(view, StringComparer.OrdinalIgnoreCase);

    private static async Task<RequestResult> ServeAsync<TOptions>(
        LiveQueryRequest request,
        Func<TOptions, TOptions> normalize,
        Func<TOptions, IReadOnlyList<string>?> extraRulesOf,
        Func<TOptions, bool> isValid,
        Func<TOptions, Task<LiveAnswer>> run
    )
        where TOptions : class
    {
        if (Decode<TOptions>(request.Options) is not { } decoded)
        {
            return RequestResult.Declined($"unreadable options for `{request.Verb}`");
        }
        var options = normalize(decoded);
        if (!isValid(options))
        {
            return RequestResult.Declined($"unreadable options for `{request.Verb}`");
        }

        return extraRulesOf(options) is { Count: > 0 }
            ? RequestResult.Declined(RulesNotHonoured)
            : new RequestResult(await run(options), null);
    }

    private static T? Decode<T>(string json)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, LiveQueryTransport.Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The one thing a JSON round-trip LOSES, restored explicitly. `--only`/`--exclude` are built by
    // CommonOptions.FilterSet as CASE-INSENSITIVE sets; System.Text.Json reconstructs a HashSet<string> with
    // the DEFAULT (ordinal) comparer, so `--only EFCORE` would silently match nothing on the routed path while
    // matching everything on the store path. Rebuilding through the same factory the CLI used is the fix, and
    // it is the reason the transport carries an options RECORD rather than pretending a record is just data.
    // Pinned by Routed_case_insensitive_effect_filters_behave_exactly_as_they_do_in_process.
    private static ReachesCommand.Options Normalize(ReachesCommand.Options options) =>
        options with
        {
            ExtraRules = options.ExtraRules ?? [],
            Only = CommonOptions.FilterSet([.. options.Only ?? []]),
            Exclude = CommonOptions.FilterSet([.. options.Exclude ?? []]),
        };

    private static TreeCommand.Options Normalize(TreeCommand.Options options) =>
        options with
        {
            ExtraRules = options.ExtraRules ?? [],
            Only = CommonOptions.FilterSet([.. options.Only ?? []]),
            Exclude = CommonOptions.FilterSet([.. options.Exclude ?? []]),
            ExcludeNamespaces = options.ExcludeNamespaces ?? [],
        };

    private static CallersCommand.Options Normalize(CallersCommand.Options options) =>
        options with
        {
            ExtraRules = options.ExtraRules ?? [],
        };

    internal static async Task<LiveAnswer> AnswerAsync(string query, LiveFactSource facts, string workingDirectory)
    {
        var trimmed = (query ?? "").Trim();
        if (trimmed.Length == 0)
        {
            return new LiveAnswer(Exit: 2, Out: $"live: empty query — {Usage}\n", Err: "");
        }

        var split = trimmed.IndexOf(' ', StringComparison.Ordinal);
        var verb = split < 0 ? trimmed : trimmed[..split];
        // SINGLE-argument verbs take the WHOLE remainder as the pattern (only the surrounding quotes are
        // stripped), because a pattern may legitimately contain spaces — a full DocID signature does
        // (`M:Ns.T.M(System.Int32, System.String)`). Only `path`, which needs two, is tokenized; see PathAsync.
        var remainder = split < 0 ? "" : trimmed[(split + 1)..].Trim();
        if (!TryExtractTraversalFlags(remainder, out var queryText, out var asyncMode, out var includeDelivery))
        {
            return Rejected("traversal flags are malformed (each flag may appear once and only as a trailing option)");
        }
        var argument = queryText.Trim('"');

        try
        {
            if (string.Equals(verb, "reaches", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(argument)
                    ? Rejected("`reaches` needs an entry-point pattern")
                    : await RunReachesAsync(
                        DefaultReachesOptions(argument) with
                        {
                            Async = asyncMode,
                            IncludeDelivery = includeDelivery,
                        },
                        facts,
                        workingDirectory
                    );
            }

            if (string.Equals(verb, "callers", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(argument)
                    ? Rejected("`callers` needs a target pattern")
                    : await RunCallersAsync(
                        DefaultCallersOptions(argument) with
                        {
                            Async = asyncMode,
                            IncludeDelivery = includeDelivery,
                        },
                        facts,
                        workingDirectory
                    );
            }

            if (string.Equals(verb, "tree", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(argument)
                    ? Rejected("`tree` needs an entry-point pattern")
                    : await RunTreeAsync(
                        DefaultTreeOptions(argument) with
                        {
                            Async = asyncMode,
                            IncludeDelivery = includeDelivery,
                        },
                        facts,
                        workingDirectory
                    );
            }

            if (string.Equals(verb, "path", StringComparison.OrdinalIgnoreCase))
            {
                // The RAW remainder, not the quote-trimmed `argument`: `path` tokenizes it itself and needs the
                // quotes intact to group a pattern that contains spaces.
                return await PathAsync(queryText, facts, workingDirectory, asyncMode, includeDelivery);
            }

            return new LiveAnswer(Exit: 2, Out: $"live: unsupported query '{verb}' — {Usage}\n", Err: "");
        }
        catch (Exception exception)
            when (exception is DemandForwardGraphUnavailableException or DemandReverseCallersGraphUnavailableException)
        {
            return Rejected($"resident `{verb}` demand projection is unavailable: {exception.Message}");
        }
    }

    // A usage rejection: exit 2, the reason, and what IS supported. Exit 2 (not 1) keeps "you asked wrong"
    // distinguishable from "the answer is no results", the same way the CLI's parse errors are.
    private static LiveAnswer Rejected(string reason) => new LiveAnswer(Exit: 2, Out: $"live: {reason} — {Usage}\n", Err: "");

    // `reaches <pattern>`, answered off the resident facts. The Options record is the DEFAULT one the CLI
    // builds when only the positional pattern is given. The text surface additionally accepts the two
    // traversal-mode flags; rendering flags remain transport-only, so default output is comparable to
    // `rig reaches <pattern>` against a store of the same tree (which is exactly what LiveReachesTests does).
    private static Task<LiveAnswer> ReachesAsync(string pattern, LiveFactSource facts, string workingDirectory) =>
        RunReachesAsync(DefaultReachesOptions(pattern), facts, workingDirectory);

    private static ReachesCommand.Options DefaultReachesOptions(string pattern) =>
        new(
            FromPattern: pattern,
            Async: false,
            IncludeDelivery: false,
            Raw: false,
            ExtraRules: [],
            Depth: null,
            Format: null,
            Only: CommonOptions.FilterSet(null),
            Exclude: CommonOptions.FilterSet(null),
            Intrinsic: false,
            Limit: null,
            Time: false
        );

    private static Task<LiveAnswer> RunReachesAsync(ReachesCommand.Options options, LiveFactSource facts, string workingDirectory) =>
        RunCommandAsync(options, facts, workingDirectory, ReachesCommand.RunAsync);

    // The ONE place a live query is actually executed, shared by all four verbs: build the writers, wrap the
    // fact generation in the IQueryFactSource adapter, run the command's own body, hand back the two streams.
    // Parameterised on the command's RunAsync so a verb arm is a single line and cannot diverge in how it
    // wires IO — the divergence that would make one verb's routed answer subtly unlike its in-process one.
    private static async Task<LiveAnswer> RunCommandAsync<TOptions>(
        TOptions options,
        LiveFactSource facts,
        string workingDirectory,
        Func<TOptions, CommandIo, Func<Task<IQueryFactSource>>, Task<int>> run
    )
    {
        // Same LF contract as CliApplication.RunAsync — a live answer must be byte-comparable with the
        // store-backed answer for the same tree, on any host.
        var output = new StringWriter { NewLine = "\n" };
        var error = new StringWriter { NewLine = "\n" };
        var source = new LiveQueryFactSource(facts);
        var exit = await run(
            options,
            new CommandIo(new TextOutput(Output: output, Error: error), new WorkspaceLocation(WorkingDirectory: workingDirectory)),
            // The live source is owned by the HOST (it is one fact generation, shared by every query against
            // it), so the adapter's DisposeAsync is a no-op and the command's `await using` cannot close
            // anything out from under the next query.
            () => Task.FromResult<IQueryFactSource>(source)
        );

        return new LiveAnswer(Exit: exit, Out: output.ToString(), Err: error.ToString());
    }

    // `callers <to>` — the REVERSE traversal, answered off the resident facts. Same shape as ReachesAsync: the
    // DEFAULT options record (plus the two text traversal-mode flags), so the default rendering is directly
    // comparable to `rig callers <to>` against a store of the same tree.
    private static Task<LiveAnswer> CallersAsync(string pattern, LiveFactSource facts, string workingDirectory) =>
        RunCallersAsync(DefaultCallersOptions(pattern), facts, workingDirectory);

    private static CallersCommand.Options DefaultCallersOptions(string pattern) =>
        new(
            ToPattern: pattern,
            RootsOnly: false,
            EntrypointsOnly: false,
            IncludeReverseOnly: false,
            Async: false,
            IncludeDelivery: false,
            Raw: false,
            ExtraRules: [],
            Depth: null,
            Format: null,
            Limit: null,
            Time: false
        );

    private static Task<LiveAnswer> RunCallersAsync(CallersCommand.Options options, LiveFactSource facts, string workingDirectory) =>
        RunCommandAsync(options, facts, workingDirectory, CallersCommand.RunAsync);

    // `tree <pattern>` — the call TREE, answered off the resident facts. Same shape as ReachesAsync: the DEFAULT
    // options record, so this is `--view paths` with no filters and no --format, directly comparable to
    // `rig tree <pattern>` against a store of the same tree (LiveTreeTests compares exactly that, and drives the
    // command directly for the view/format matrix the live surface doesn't expose yet).
    //
    // NoCache is FALSE here, i.e. caching ON — which on this path means the fact generation's in-memory memo
    // (LiveQueryFactSource.OpenArtifactCache), never `.rig/cache.db`. Passing NoCache: true would be the wrong
    // "safe" choice: it would recompute the forest for every repeat question, which is precisely the cost a
    // resident host exists to avoid.
    private static Task<LiveAnswer> TreeAsync(string pattern, LiveFactSource facts, string workingDirectory) =>
        RunTreeAsync(DefaultTreeOptions(pattern), facts, workingDirectory);

    private static TreeCommand.Options DefaultTreeOptions(string pattern) =>
        new(
            FromPattern: pattern,
            View: "paths",
            Async: false,
            IncludeDelivery: false,
            Raw: false,
            Files: false,
            Signatures: false,
            Plain: false,
            Guards: false,
            ExtraRules: [],
            Depth: null,
            Limit: null,
            Only: CommonOptions.FilterSet(null),
            Exclude: CommonOptions.FilterSet(null),
            Intrinsic: false,
            ExcludeNamespaces: CommonOptions.NamespacePrefixes(null),
            NoCache: false,
            Gate: true,
            Amplification: true,
            Time: false,
            Format: null,
            Suppress: null
        );

    private static Task<LiveAnswer> RunTreeAsync(TreeCommand.Options options, LiveFactSource facts, string workingDirectory) =>
        RunCommandAsync(options, facts, workingDirectory, TreeCommand.RunAsync);

    // `path <from> <to>` — the only two-argument live verb, so the only one that tokenizes its remainder.
    // Whitespace separates the two patterns; double quotes group, so a DocID-shaped pattern whose signature
    // contains a space survives as ONE argument (`path "M:A.B(System.Int32, System.String)" C.D`). Anything
    // other than exactly two arguments is rejected rather than guessed at — silently pairing the wrong
    // endpoints would answer a question nobody asked.
    private static async Task<LiveAnswer> PathAsync(
        string remainder,
        LiveFactSource facts,
        string workingDirectory,
        bool asyncMode = false,
        bool includeDelivery = false
    )
    {
        if (!TrySplitArguments(remainder, out var endpoints) || endpoints.Count != 2)
        {
            return Rejected("`path` needs exactly two patterns, a from and a to (quote a pattern containing spaces)");
        }

        return await RunPathAsync(
            DefaultPathOptions(endpoints[0], endpoints[1]) with
            {
                Async = asyncMode,
                IncludeDelivery = includeDelivery,
            },
            facts,
            workingDirectory
        );
    }

    private static Task<LiveAnswer> RunPathAsync(PathCommand.Options options, LiveFactSource facts, string workingDirectory) =>
        RunCommandAsync(options, facts, workingDirectory, PathCommand.RunAsync);

    // Whitespace-separated positional arguments with double-quote grouping. Deliberately minimal (no escapes,
    // no single quotes): this is a query line typed at a terminal, not a shell.
    private static List<string> SplitArguments(string remainder)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var ch in remainder)
        {
            if (ch == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (!quoted && char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            args.Add(current.ToString());
        }

        return args;
    }

    private static bool TrySplitArguments(string remainder, out List<string> arguments)
    {
        arguments = SplitArguments(remainder);
        return remainder.Count(ch => ch == '"') % 2 == 0;
    }

    private static bool TryExtractTraversalFlags(string remainder, out string queryText, out bool asyncMode, out bool includeDelivery)
    {
        queryText = remainder.Trim();
        asyncMode = false;
        includeDelivery = false;
        if (!HasBalancedQuotes(queryText))
        {
            return false;
        }
        var seenAsync = false;
        var seenDelivery = false;
        while (true)
        {
            if (TryRemoveTrailingFlag(ref queryText, "--async"))
            {
                if (seenAsync)
                {
                    return false;
                }
                seenAsync = asyncMode = true;
                continue;
            }
            if (TryRemoveTrailingFlag(ref queryText, "--include-delivery"))
            {
                if (seenDelivery)
                {
                    return false;
                }
                seenDelivery = includeDelivery = true;
                continue;
            }
            break;
        }

        var misplacedTraversalFlag = ContainsUnquotedTraversalFlag(queryText);
        // Preserve CommonOptions.Mode exactly: include-delivery without async is a sync no-op, not a
        // resident-only validation error. Execution and preparation both flow through that same normalizer.
        return !misplacedTraversalFlag;
    }

    private static bool TryRemoveTrailingFlag(ref string text, string flag)
    {
        if (!text.EndsWith(flag, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var start = text.Length - flag.Length;
        if (start > 0 && !char.IsWhiteSpace(text[start - 1]))
        {
            return false;
        }
        if (IsInsideQuotes(text, start))
        {
            return false;
        }
        text = text[..start].TrimEnd();
        return true;
    }

    private static bool HasBalancedQuotes(string text) => text.Count(ch => ch == '"') % 2 == 0;

    private static bool IsInsideQuotes(string text, int offset)
    {
        var quoted = false;
        for (var index = 0; index < offset; index++)
        {
            if (text[index] == '"')
            {
                quoted = !quoted;
            }
        }
        return quoted;
    }

    private static bool ContainsUnquotedTraversalFlag(string text)
    {
        var quoted = false;
        var tokenStart = -1;
        for (var index = 0; index <= text.Length; index++)
        {
            var atEnd = index == text.Length;
            var ch = atEnd ? ' ' : text[index];
            if (!atEnd && ch == '"')
            {
                quoted = !quoted;
            }
            if (!quoted && !atEnd && !char.IsWhiteSpace(ch) && tokenStart < 0)
            {
                tokenStart = index;
            }
            if (!quoted && (atEnd || char.IsWhiteSpace(ch)) && tokenStart >= 0)
            {
                var token = text[tokenStart..index];
                if (
                    string.Equals(token, "--async", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(token, "--include-delivery", StringComparison.OrdinalIgnoreCase)
                )
                {
                    return true;
                }
                tokenStart = -1;
            }
        }
        return false;
    }
}
