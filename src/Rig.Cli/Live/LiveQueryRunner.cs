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
        "supported live queries: `reaches <pattern>`, `path <from> <to>`, `callers <to>`, `tree <pattern>`; " + "`quit` (or EOF) exits.";

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
                return await ServeAsync<CallersCommand.Options>(
                    request,
                    Normalize,
                    o => o.ExtraRules,
                    IsValidCallersOptions,
                    o => RunCallersAsync(o, facts, workingDirectory)
                );

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
        if (string.Equals(verb, LiveQueryVerbs.Path, StringComparison.OrdinalIgnoreCase))
        {
            return TrySplitArguments(remainder, out var endpoints) && endpoints.Count == 2
                ? BuildPathDemand(DefaultPathOptions(endpoints[0], endpoints[1]), rules, deploymentsConfigured)
                : null;
        }

        var pattern = remainder.Trim('"');
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }

        if (string.Equals(verb, LiveQueryVerbs.Reaches, StringComparison.OrdinalIgnoreCase))
        {
            return BuildReachesDemand(DefaultReachesOptions(pattern), rules, deploymentsConfigured);
        }

        if (string.Equals(verb, LiveQueryVerbs.Callers, StringComparison.OrdinalIgnoreCase))
        {
            return BuildCallersDemand(DefaultCallersOptions(pattern), rules, deploymentsConfigured);
        }

        return string.Equals(verb, LiveQueryVerbs.Tree, StringComparison.OrdinalIgnoreCase)
            ? BuildTreeDemand(DefaultTreeOptions(pattern), rules, deploymentsConfigured)
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

    internal static LiveAnswer ExactUnavailable(string verb, long revision, string reason) =>
        new(
            Exit: 2,
            Out: "",
            Err: $"live: exact {verb} unavailable at resident revision {revision}: {reason}; restart the watcher and retry{Environment.NewLine}"
        );

    private static ExactForwardDemand BuildPathDemand(
        PathCommand.Options options,
        Rig.Domain.Data.RuleSet rules,
        bool deploymentsConfigured
    )
    {
        var shaped = options.Raw ? rules with { Factory = [], Cut = [], Context = [] } : rules;
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
        var shaped = options.Raw ? rules with { Factory = [], Cut = [], Context = [] } : rules;
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
                shaped.Context
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
                shaped.Context
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

    private static bool IsValidPathOptions(PathCommand.Options options) =>
        !string.IsNullOrWhiteSpace(options.FromPattern)
        && !string.IsNullOrWhiteSpace(options.ToPattern)
        && IsValidExactForwardMode(options.Async, options.IncludeDelivery)
        && IsValidDepth(options.Depth);

    private static bool IsValidReachesOptions(ReachesCommand.Options options) =>
        !string.IsNullOrWhiteSpace(options.FromPattern)
        && IsValidExactForwardMode(options.Async, options.IncludeDelivery)
        && IsValidDepth(options.Depth)
        && (options.Limit is null or > 0);

    private static bool IsValidTreeOptions(TreeCommand.Options options) =>
        !string.IsNullOrWhiteSpace(options.FromPattern)
        && IsValidTreeView(options.View)
        && IsValidExactForwardMode(options.Async, options.IncludeDelivery)
        && IsValidDepth(options.Depth)
        && (options.Limit is null or > 0);

    private static bool IsValidCallersOptions(CallersCommand.Options options) =>
        !string.IsNullOrWhiteSpace(options.ToPattern)
        && !(options.RootsOnly && options.EntrypointsOnly)
        && IsValidExactForwardMode(options.Async, options.IncludeDelivery)
        && IsValidDepth(options.Depth)
        && (options.Limit is null or > 0);

    private static bool IsValidExactForwardMode(bool asyncMode, bool includeDelivery) => !asyncMode && !includeDelivery;

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
            return new LiveAnswer(Exit: 2, Out: $"live: empty query — {Usage}{Environment.NewLine}", Err: "");
        }

        var split = trimmed.IndexOf(' ', StringComparison.Ordinal);
        var verb = split < 0 ? trimmed : trimmed[..split];
        // SINGLE-argument verbs take the WHOLE remainder as the pattern (only the surrounding quotes are
        // stripped), because a pattern may legitimately contain spaces — a full DocID signature does
        // (`M:Ns.T.M(System.Int32, System.String)`). Only `path`, which needs two, is tokenized; see PathAsync.
        var remainder = split < 0 ? "" : trimmed[(split + 1)..].Trim();
        var argument = remainder.Trim('"');

        if (string.Equals(verb, "reaches", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(argument)
                ? Rejected("`reaches` needs an entry-point pattern")
                : await ReachesAsync(argument, facts, workingDirectory);
        }

        if (string.Equals(verb, "callers", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(argument)
                ? Rejected("`callers` needs a target pattern")
                : await CallersAsync(argument, facts, workingDirectory);
        }

        if (string.Equals(verb, "tree", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(argument)
                ? Rejected("`tree` needs an entry-point pattern")
                : await TreeAsync(argument, facts, workingDirectory);
        }

        if (string.Equals(verb, "path", StringComparison.OrdinalIgnoreCase))
        {
            // The RAW remainder, not the quote-trimmed `argument`: `path` tokenizes it itself and needs the
            // quotes intact to group a pattern that contains spaces.
            return await PathAsync(remainder, facts, workingDirectory);
        }

        return new LiveAnswer(Exit: 2, Out: $"live: unsupported query '{verb}' — {Usage}{Environment.NewLine}", Err: "");
    }

    // A usage rejection: exit 2, the reason, and what IS supported. Exit 2 (not 1) keeps "you asked wrong"
    // distinguishable from "the answer is no results", the same way the CLI's parse errors are.
    private static LiveAnswer Rejected(string reason) =>
        new LiveAnswer(Exit: 2, Out: $"live: {reason} — {Usage}{Environment.NewLine}", Err: "");

    // `reaches <pattern>`, answered off the resident facts. The Options record is the DEFAULT one the CLI
    // builds when only the positional pattern is given — no --async/--raw/--depth/--limit/--format on the live
    // surface yet, so the output is the default human rendering and is directly comparable to
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
        var output = new StringWriter();
        var error = new StringWriter();
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
    // DEFAULT options record (no --orphans/--entrypoints/--async/--raw/--limit/--format on the live surface
    // yet), so the rendering is directly comparable to `rig callers <to>` against a store of the same tree.
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
    private static async Task<LiveAnswer> PathAsync(string remainder, LiveFactSource facts, string workingDirectory)
    {
        if (!TrySplitArguments(remainder, out var endpoints) || endpoints.Count != 2)
        {
            return Rejected("`path` needs exactly two patterns, a from and a to (quote a pattern containing spaces)");
        }

        return await RunPathAsync(DefaultPathOptions(endpoints[0], endpoints[1]), facts, workingDirectory);
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
}
