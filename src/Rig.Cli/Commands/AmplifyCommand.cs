using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using Rig.Analysis.Rules;
using Rig.Cli.CommandLine;
using Rig.Cli.EntryPoints;
using Rig.Cli.Rendering;
using Rig.Cli.Telemetry;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;
using static Rig.Cli.Caching.QueryCacheKeys;
using static Rig.Cli.Effects.EffectDerivation;
using static Rig.Cli.EntryPoints.EntryPointContext;
using static Rig.Cli.Graph.TraversalGraphLoader;
using static Rig.Cli.Rendering.SymbolNameFormatter;

namespace Rig.Cli.Commands;

// `rig amplify` — estate-wide NON-LINEAR effect discovery. Ranks effects by AMPLIFICATION DEGREE: how many
// independent iteration contexts are stacked between a caller and the effect site. Degree 1 is linear (the
// shipped n_plus_1 / cross_method_amplification tiers already cover it); degree >= 2 is a quadratic-or-worse
// candidate, and a chain that enters a call cycle is reported separately as unbounded.
//
// The engine is FactAmplificationDegreeDeriver (pure, in Rig.Domain). This file is options, loading, ranking
// and rendering only.
internal static class AmplifyCommand
{
    // DISPLAY GROUPING/RANKING/EXCLUSION is entirely rules data — `observations.amplificationCategories`,
    // projected to FactAmplificationCategoryRule and applied through AmplificationCategories. NOTHING here
    // names an effect provider or operation.
    //
    // Why this may not be a C# table, even a presentation-only one: provider and operation tokens are the
    // vocabulary of a particular codebase's RULESET, not of rig. Whether an effect is a blocking round trip,
    // fire-and-forget queueing, or lock contention is a property of that project's providers — Echo actors
    // exist in exactly one repo — so a ranking array or a default exclusion list in core would bake one
    // project's domain into the tool. Core implements only "rank / group / exclude BY CONFIGURED CATEGORY".
    //
    // With no categories configured the output is NEUTRAL, not wrong: one implicit group, ordered by degree
    // then site. A project ruleset adds opinion — which categories cost most per iteration (`weight`), which
    // are fire-and-forget and belong in their own section rather than beside synchronous IO (`separate` +
    // `label`), and which are historically noisy enough to hide (`excluded`).

    internal static Command Build(TextWriter output, TextWriter error, string workingDirectory)
    {
        var minDegree = new Option<int>("--min-degree")
        {
            Description = "Minimum amplification degree to report (default 2 — degree 1 is linear and already covered by derive).",
            DefaultValueFactory = _ => 2,
        };
        var top = new Option<int>("--top") { Description = "Maximum findings per section (default 50).", DefaultValueFactory = _ => 50 };
        var maxDepth = CommonOptions.Depth();
        var maxNodes = CommonOptions.MaxNodes();
        var only = CommonOptions.Only();
        var exclude = CommonOptions.Exclude();
        var rules = CommonOptions.Rules();
        var store = CommonOptions.Store();
        var format = CommonOptions.Format(allowedValues: ["tsv"]);
        var time = CommonOptions.Time();
        var cmd = new Command(
            name: "amplify",
            description: "Rank effects by amplification degree — stacked loop contexts (degree >= 2 = super-linear)."
        )
        {
            minDegree,
            top,
            maxDepth,
            maxNodes,
            only,
            exclude,
            rules,
            store,
            format,
            time,
        };
        cmd.SetAction(pr =>
            CommandGuard.RunGuardedAsync(
                workingDirectory,
                error,
                () =>
                    RunAsync(
                        new Options(
                            MinDegree: pr.GetValue(minDegree),
                            Top: pr.GetValue(top),
                            MaxDepth: pr.GetValue(maxDepth) ?? FactAmplificationDegreeDeriver.DefaultMaxDepth,
                            MaxNodes: CommonOptions.ResolveBudget(pr.GetValue(maxNodes)) ?? FactAmplificationDegreeDeriver.DefaultMaxNodes,
                            Only: CommonOptions.FilterSet(pr.GetValue(only)),
                            Exclude: CommonOptions.FilterSet(pr.GetValue(exclude)),
                            ExtraRules: CommonOptions.RulesOf(pr.GetValue(rules)),
                            Format: pr.GetValue(format),
                            Time: pr.GetValue(time)
                        ),
                        new CommandIo(new TextOutput(output, error), new WorkspaceLocation(workingDirectory, pr.GetValue(store)))
                    )
            )
        );
        return cmd;
    }

    internal sealed record Options(
        int MinDegree,
        int Top,
        int MaxDepth,
        int MaxNodes,
        HashSet<string> Only,
        HashSet<string> Exclude,
        IReadOnlyList<string> ExtraRules,
        string? Format,
        bool Time
    );

    // A finding plus its entry-point attribution — computed only for the findings that survive selection,
    // because that pass is the expensive one (a forward reach per entry point).
    internal sealed record Attributed(
        FactAmplificationDegreeDeriver.Finding Finding,
        int EntryPointCount,
        IReadOnlyList<string> ExampleEntryPoints
    );

    private static async Task<int> RunAsync(Options opts, CommandIo io)
    {
        using var timing = QueryTiming.Start(opts.Time, io.TextOutput.Error);

        var rules = RuleSetLoader.Load(
            workingDirectory: io.WorkspaceLocation.WorkingDirectory,
            extraRules: opts.ExtraRules,
            loadedPaths: out var loadedRulePaths
        );
        WarnUnknownFilterTokens(only: opts.Only, exclude: opts.Exclude, rules: rules, errorWriter: io.TextOutput.Error);

        var (context, rigDir) = await OpenReadContextGatedAsync(io.WorkspaceLocation, withStoreDir: true);
        await using var contextScope = context;

        var storeKey = StoreKey(Path.Combine(rigDir, StoreLayout.DbFileName));
        var rulesHash = RulesFingerprint.ComputeFromPaths(loadedRulePaths);

        var loadWatch = Stopwatch.StartNew();
        var graph = await Caching.WarmStore.GraphAsync(context: context, rules: rules, storeDir: rigDir, rulesHash: rulesHash);
        var epData = await Reads.LoadFactEntryPointDataAsync(context);
        // The SAME store+rules-keyed effect artifact `derive` and `tree --hazards` share, so a warm run pays
        // nothing for it here.
        var effects = await LoadOrDeriveHazardEffectsAsync(
            context: context,
            rigDirectory: rigDir,
            storeKey: storeKey,
            rulesHash: rulesHash,
            rules: rules,
            useCache: true,
            epData: epData
        );
        var invocations = await Caching.WarmStore.InvocationsAsync(context: context, storeDir: rigDir);
        loadWatch.Stop();
        timing.Record("load", loadWatch.Elapsed);

        // --only/--exclude narrow the WITNESS set (which effects can terminate a chain), which is how
        // `--exclude lock` / `--exclude reflection` drop the contention and assembly-load amplifiers a project
        // has widened its amplification scope to include. includeIntrinsic is true because the rules-declared
        // amplification scope is the real gate here and never admits alloc/throw.
        var selection = SelectEffects(effects, only: opts.Only, exclude: opts.Exclude, includeIntrinsic: true);

        var deriveWatch = Stopwatch.StartNew();
        var findings = FactAmplificationDegreeDeriver.Derive(
            invocations: invocations,
            graph: graph,
            effects: selection.Effects,
            observationRules: rules.Observations,
            scope: rules.Observations.AmplificationOrEmpty,
            maxDepth: opts.MaxDepth,
            maxNodes: opts.MaxNodes
        );
        deriveWatch.Stop();
        timing.Record("degree", deriveWatch.Elapsed);

        var categories = rules.Observations.AmplificationCategoriesOrEmpty;
        var (main, separate, recursion) = Sections(findings, opts.MinDegree, opts.Top, categories);

        var epWatch = Stopwatch.StartNew();
        var attributed = await AttributeAsync(
            context: context,
            graph: graph,
            epData: epData,
            rules: rules,
            findings: [.. main, .. separate, .. recursion]
        );
        epWatch.Stop();
        timing.Record("entrypoints", epWatch.Elapsed);

        // Entry-point count is the LAST sort key of the required ranking, so it can only be applied once the
        // attribution above exists. Selection above already used (degree, kind, site) — a stable prefix of the
        // same order — so re-sorting here reorders ties, never the set.
        var ranked = Rerank(main, attributed, categories);
        var separateRanked = Rerank(separate, attributed, categories);
        var recRanked = Rerank(recursion, attributed, categories);

        var renderWatch = Stopwatch.StartNew();
        if (CommonOptions.IsTsv(opts.Format))
        {
            WriteTsv(io.TextOutput.Output, [.. ranked, .. separateRanked, .. recRanked]);
        }
        else
        {
            WriteHuman(io.TextOutput.Output, opts, ranked, separateRanked, recRanked, categories);
        }

        renderWatch.Stop();
        timing.Record("render", renderWatch.Elapsed);
        return 0;
    }

    // --- selection + ranking -----------------------------------------------------------------------------

    // Split the derived findings into the three display buckets and cap each at --top. `--min-degree` gates
    // the finite-degree buckets only: a recursive chain has no finite degree to compare, and suppressing it
    // under a numeric threshold would hide the worst class of all.
    internal static (
        IReadOnlyList<FactAmplificationDegreeDeriver.Finding> Main,
        IReadOnlyList<FactAmplificationDegreeDeriver.Finding> Separate,
        IReadOnlyList<FactAmplificationDegreeDeriver.Finding> Recursion
    ) Sections(
        IReadOnlyList<FactAmplificationDegreeDeriver.Finding> findings,
        int minDegree,
        int top,
        IReadOnlyList<FactAmplificationCategoryRule> categories
    )
    {
        var cap = Math.Max(0, top);
        var main = new List<FactAmplificationDegreeDeriver.Finding>();
        var separate = new List<FactAmplificationDegreeDeriver.Finding>();
        var recursion = new List<FactAmplificationDegreeDeriver.Finding>();
        foreach (var f in findings)
        {
            // An EXCLUDED category is dropped from every bucket, recursion included: the ruleset is saying
            // this effect kind is not worth showing at all (historically-noisy contention/assembly-load
            // amplifiers), and a recursive instance of it is no more actionable.
            var category = Category(categories, f);
            if (category.Excluded)
            {
                continue;
            }

            if (f.Recursion)
            {
                recursion.Add(f);
            }
            else if (f.Degree < minDegree)
            {
                continue;
            }
            else if (category.Separate)
            {
                separate.Add(f);
            }
            else
            {
                main.Add(f);
            }
        }

        return (
            Order(main, categories).Take(cap).ToList(),
            Order(separate, categories).Take(cap).ToList(),
            Order(recursion, categories).Take(cap).ToList()
        );
    }

    private static FactAmplificationCategoryRule Category(
        IReadOnlyList<FactAmplificationCategoryRule> categories,
        FactAmplificationDegreeDeriver.Finding f
    ) => AmplificationCategories.For(categories, f.EffectProvider, f.EffectOperation);

    // degree desc, then configured category weight, then the site, so selection is deterministic before
    // entry-point counts exist. With no categories configured the weight term is constant and the order
    // collapses to degree-then-site — neutral, not wrong.
    private static IEnumerable<FactAmplificationDegreeDeriver.Finding> Order(
        IEnumerable<FactAmplificationDegreeDeriver.Finding> findings,
        IReadOnlyList<FactAmplificationCategoryRule> categories
    ) =>
        findings
            .OrderByDescending(f => f.Degree == FactAmplificationDegreeDeriver.Unbounded ? int.MaxValue : f.Degree)
            .ThenBy(f => AmplificationCategories.Rank(categories, f.EffectProvider, f.EffectOperation))
            .ThenBy(f => f.EffectKind, StringComparer.Ordinal)
            .ThenBy(f => f.Head.FilePath, StringComparer.Ordinal)
            .ThenBy(f => f.Head.Line);

    private static IReadOnlyList<Attributed> Rerank(
        IReadOnlyList<FactAmplificationDegreeDeriver.Finding> findings,
        IReadOnlyDictionary<FactAmplificationDegreeDeriver.Finding, Attributed> attributed,
        IReadOnlyList<FactAmplificationCategoryRule> categories
    ) =>
        findings
            .Select(f => attributed[f])
            .OrderByDescending(a => a.Finding.Degree == FactAmplificationDegreeDeriver.Unbounded ? int.MaxValue : a.Finding.Degree)
            .ThenBy(a => AmplificationCategories.Rank(categories, a.Finding.EffectProvider, a.Finding.EffectOperation))
            .ThenBy(a => a.Finding.EffectKind, StringComparer.Ordinal)
            .ThenByDescending(a => a.EntryPointCount)
            .ThenBy(a => a.Finding.Head.FilePath, StringComparer.Ordinal)
            .ThenBy(a => a.Finding.Head.Line)
            .ToList();

    // --- entry-point attribution -------------------------------------------------------------------------

    // Which entry points can actually reach each reported chain HEAD. Forward (narrowed one-hop dispatch), the
    // same direction `callers --entrypoints` confirms with, because the reverse closure over-approximates at
    // shared virtual nodes.
    //
    // Run over ONE traversal session in batches: for each batch of EP seeds the reach set is checked against
    // the (at most a few hundred) chain-head method ids and then discarded, so peak memory is one batch of
    // reach sets rather than one per entry point. FactPathFinder.SeedsReachTarget is the same engine but
    // answers a single "does any seed reach any target" per group — it cannot say WHICH finding an entry point
    // reaches without one full pass per finding, which is why the session is driven directly here.
    private static async Task<IReadOnlyDictionary<FactAmplificationDegreeDeriver.Finding, Attributed>> AttributeAsync(
        Storage.Storage.RigDbContext context,
        FactGraphData graph,
        FactEntryPointDeriver.FactEntryPointData epData,
        RuleSet rules,
        IReadOnlyList<FactAmplificationDegreeDeriver.Finding> findings
    )
    {
        var attributed = new Dictionary<FactAmplificationDegreeDeriver.Finding, Attributed>();
        if (findings.Count == 0)
        {
            return attributed;
        }

        var (derived, _, promoted) = await DeriveEntryPointsAsync(context, epData, rules);
        var records = BuildEntryPointRecords(derived, promoted, epData);
        // An EP whose site maps to no indexed method has no seed id — skip it rather than seeding "" (which
        // yields an empty reach and would just be counted as "reaches nothing" anyway).
        var seeds = records.Where(r => !string.IsNullOrEmpty(r.DocId)).ToList();

        // Head method -> the findings whose chain starts there. Several findings can share a head (different
        // call sites in the same method), and every one of them has the same entry-point answer.
        var byHead = new Dictionary<string, List<FactAmplificationDegreeDeriver.Finding>>(StringComparer.Ordinal);
        foreach (var f in findings)
        {
            if (!byHead.TryGetValue(f.Head.Caller, out var bucket))
            {
                bucket = [];
                byHead[f.Head.Caller] = bucket;
            }

            bucket.Add(f);
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var examples = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var head in byHead.Keys)
        {
            counts[head] = 0;
            examples[head] = [];
        }

        const int Batch = 64;
        const int Examples = 3;
        var session = FactPathFinder.OpenSession(graph);
        for (var start = 0; start < seeds.Count; start += Batch)
        {
            var slice = seeds.GetRange(start, Math.Min(Batch, seeds.Count - start));
            var reaches = session.ReachesFromEachSeed(
                [.. slice.Select(s => s.DocId!)],
                maxDepth: int.MaxValue,
                maxNodes: 20000,
                mode: FactPathFinder.TraversalMode.SyncCut
            );
            for (var k = 0; k < slice.Count; k++)
            {
                foreach (var head in byHead.Keys)
                {
                    if (!reaches[k].Contains(head))
                    {
                        continue;
                    }

                    counts[head]++;
                    if (examples[head].Count < Examples)
                    {
                        examples[head].Add(FqnOrRoute(slice[k]));
                    }
                }
            }
        }

        foreach (var f in findings)
        {
            attributed[f] = new Attributed(f, counts[f.Head.Caller], examples[f.Head.Caller]);
        }

        return attributed;
    }

    // --- rendering ---------------------------------------------------------------------------------------

    // TSV column reference (tab-separated). Two row types, joined on the finding ordinal (column 2):
    //   amplification_degree \t id \t degree \t recursion \t confidence \t hops
    //     \t headMethod \t headFile \t headLine \t headIterationKind
    //     \t effectProvider \t effectOperation \t effectResource \t effectEnclosing \t effectFile \t effectLine
    //     \t effectDepth \t entryPoints \t exampleEntryPoints(csv)
    //   amplification_chain  \t id \t hopIndex \t caller \t callee \t iterationKind \t iterationDetail
    //     \t file \t line \t intraDepth
    // `degree` is -1 for a recursive (unbounded) chain, which the `recursion` column states explicitly.
    internal static void WriteTsv(TextWriter output, IReadOnlyList<Attributed> findings)
    {
        var id = 0;
        foreach (var a in findings)
        {
            id++;
            var f = a.Finding;
            output.WriteLine(
                string.Join(
                    '\t',
                    "amplification_degree",
                    id.ToString(CultureInfo.InvariantCulture),
                    f.Degree.ToString(CultureInfo.InvariantCulture),
                    f.Recursion ? "1" : "0",
                    f.Confidence,
                    f.Chain.Count.ToString(CultureInfo.InvariantCulture),
                    Clean(f.Head.Caller),
                    Clean(f.Head.FilePath),
                    f.Head.Line.ToString(CultureInfo.InvariantCulture),
                    Clean(f.Head.IterationKind),
                    Clean(f.EffectProvider),
                    Clean(f.EffectOperation),
                    Clean(f.EffectResource),
                    Clean(f.EffectEnclosing),
                    Clean(f.EffectFilePath),
                    f.EffectLine.ToString(CultureInfo.InvariantCulture),
                    f.EffectDepth.ToString(CultureInfo.InvariantCulture),
                    a.EntryPointCount.ToString(CultureInfo.InvariantCulture),
                    Clean(string.Join(',', a.ExampleEntryPoints))
                )
            );

            for (var hop = 0; hop < f.Chain.Count; hop++)
            {
                var h = f.Chain[hop];
                output.WriteLine(
                    string.Join(
                        '\t',
                        "amplification_chain",
                        id.ToString(CultureInfo.InvariantCulture),
                        hop.ToString(CultureInfo.InvariantCulture),
                        Clean(h.Caller),
                        Clean(h.Callee),
                        Clean(h.IterationKind),
                        Clean(h.IterationDetail),
                        Clean(h.FilePath),
                        h.Line.ToString(CultureInfo.InvariantCulture),
                        h.IntraDepth.ToString(CultureInfo.InvariantCulture)
                    )
                );
            }
        }
    }

    // Collapse EVERY whitespace run (tabs, CR, LF, the newlines a multi-line LINQ query detail carries) to a
    // single space, and trim. A raw newline inside a mined loop detail SPLITS a tsv row — `derive --format
    // tsv` still leaks them, and reproducing that here would break any consumer that parses these rows.
    internal static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var sb = new System.Text.StringBuilder(value!.Length);
        var pendingSpace = false;
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static void WriteHuman(
        TextWriter output,
        Options opts,
        IReadOnlyList<Attributed> main,
        IReadOnlyList<Attributed> separate,
        IReadOnlyList<Attributed> recursion,
        IReadOnlyList<FactAmplificationCategoryRule> categories
    )
    {
        output.WriteLine($"Amplification degree (min {opts.MinDegree}, top {opts.Top} per section, anchor reach <= {opts.MaxDepth} hops)");
        output.WriteLine(
            "Degree = independent loop contexts stacked from the head down to the effect. ✔ cross-method, ~ includes intra-method line-span nesting."
        );

        WriteSection(output, $"Super-linear ({main.Count})", main);
        // Heading comes from the CATEGORY that asked for its own section (`label`, falling back to `name`),
        // so the wording is the ruleset's, not core's.
        var separateLabel = separate.Count == 0 ? "" : Category(categories, separate[0].Finding).Label;
        WriteSection(
            output,
            string.IsNullOrWhiteSpace(separateLabel)
                ? $"Separate category ({separate.Count})"
                : $"{separateLabel} ({separate.Count})",
            separate
        );
        WriteSection(output, $"Recursive — unbounded degree ({recursion.Count})", recursion);

        if (main.Count == 0 && separate.Count == 0 && recursion.Count == 0)
        {
            output.WriteLine("  no findings at this degree.");
        }
    }

    private static void WriteSection(TextWriter output, string title, IReadOnlyList<Attributed> findings)
    {
        if (findings.Count == 0)
        {
            return;
        }

        output.WriteLine();
        output.WriteLine(title);
        var rank = 0;
        foreach (var a in findings)
        {
            rank++;
            var f = a.Finding;
            var degree = f.Degree == FactAmplificationDegreeDeriver.Unbounded ? "recursion" : $"degree {f.Degree}";
            var eps =
                a.EntryPointCount == 0 ? "no entry point reaches this"
                : a.ExampleEntryPoints.Count == 0 ? $"{a.EntryPointCount} entry points"
                : $"{a.EntryPointCount} entry points, e.g. {string.Join(", ", a.ExampleEntryPoints)}";
            output.WriteLine($"{Indent.L1}{rank, 3}. {f.Confidence} {degree}  {f.EffectKind}  ({eps})");
            for (var hop = 0; hop < f.Chain.Count; hop++)
            {
                var h = f.Chain[hop];
                var nested = h.IntraDepth > 1 ? $" ~x{h.IntraDepth} nested" : "";
                output.WriteLine(
                    $"{Indent.L3}{hop + 1}. {h.IterationKind} {Clean(h.IterationDetail)}{nested}  in {ShortName(h.Caller)}  {ShortenPath(h.FilePath)}:{h.Line}"
                );
            }

            output.WriteLine(
                $"{Indent.L3}-> {f.EffectKind} {ShortName(f.EffectEnclosing)}  {ShortenPath(f.EffectFilePath)}:{f.EffectLine}  (depth {f.EffectDepth})"
            );
        }
    }
}
