using System.CommandLine;
using Rig.Analysis.Rules;
using Rig.Cli.Caching;
using Rig.Cli.CommandLine;
using Rig.Cli.Deployments;
using Rig.Cli.Impact;
using Rig.Cli.Rendering;
using Rig.Cli.Telemetry;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using static Rig.Cli.Caching.QueryCacheKeys;
using static Rig.Cli.Effects.EffectDerivation;
using static Rig.Cli.EntryPoints.EntryPointContext;
using static Rig.Cli.Graph.TraversalGraphLoader;
using static Rig.Cli.Rendering.EntryPointListRenderer;
using static Rig.Cli.Rendering.SymbolNameFormatter;

namespace Rig.Cli.Commands;

// `rig impact --base <store> --head <store>` — a PURE two-store derived-facts diff. Both sides are REQUIRED
// indexed per-commit stores (a sha / short-sha / store-id matching an indexed `.rig/<id>/` store — NOT a git
// ref, NOT a working tree). It reports, per entry point, what the change DID, derived entirely from the two
// immutable stores:
//   (1) the ENTRY-POINT SET diff — EPs added/removed vs the base, paired on (Kind, Route);
//   (2) the BEHAVIORAL per-EP diff — entry points whose reachable EFFECT set changed (the high-signal handful);
//   (3) the STRUCTURAL per-EP diff — entry points whose reachable TREE changed (demoted to a cause-classified
//       one-liner by default; --structural expands it).
//
// There is NO git diff and NO speculative blast radius: the old git-working-tree seed (changed methods →
// reverse/forward reach) fed only the now-removed behavioral-delta section and the removed --reach blast
// radius. Every signal here is store-vs-store, so the output is the PROVEN diff between two indexed commits.
//
// This command is PURE ORCHESTRATION over the shipped engine — it adds no graph code. It runs `derive`'s
// DeriveEntryPoints/DeriveEffects on each store and diffs the per-EP forward-reach footprints. The compute
// half (the store-vs-store diff + its domain records) lives in ImpactEngine / ImpactModel (Rig.Cli.Impact);
// this file is the command wiring + rendering.
internal static class ImpactCommand
{
    internal static Command Build(TextWriter output, TextWriter error, string workingDirectory)
    {
        // Both sides are INDEXED STORE REFS (sha / short-sha / store-id matching a per-commit `.rig/<id>/`
        // store), resolved the same way every read command's --store is. --base-store / --head-store are
        // aliases for symmetry; they take the same store-ref form (the historical --base-store path/dir form
        // is gone — all four names resolve through ResolveReadStoreDir).
        var @base = new Option<string?>("--base", "--base-store")
        {
            Description = "BASE (before) side: an indexed commit store ref (sha / short-sha / store-id). Required.",
        };
        var head = new Option<string?>("--head", "--head-store")
        {
            Description = "HEAD (after) side: an indexed commit store ref (sha / short-sha / store-id). Required.",
        };
        var async = CommonOptions.Async();
        var includeDelivery = CommonOptions.IncludeDelivery();
        var rules = CommonOptions.Rules();
        var format = CommonOptions.Format();
        var limit = CommonOptions.Limit();
        var noCache = CommonOptions.NoCache();
        var noGate = CommonOptions.NoGate();
        var noAmplification = CommonOptions.NoAmplification();
        var time = CommonOptions.Time();
        var only = CommonOptions.Only();
        var exclude = CommonOptions.Exclude();
        var intrinsic = CommonOptions.Intrinsic();
        var structural = new Option<bool>("--structural")
        {
            Description =
                "Also list every entry point whose reachable TREE changed — including the (usually large) set affected "
                + "only by a data-shape ripple (a record gaining a field changes every reaching EP's reach without "
                + "changing its behavior). Off by default: the default output lists EPs whose EFFECT set changed (the "
                + "behavioral signal) plus a one-line structural-only summary. This expands that summary to the full list.",
        };
        // CI guardrail for behavior-preserving MRs (refactors / framework migrations): exit non-zero if ANY
        // entry point's reachable EFFECT set changed (the per-EP behavioral delta — the same count the header
        // reports as "N with a changed behavior"). Structural-only reachable-tree ripple does NOT trip it (a
        // data-shape change with no new/lost effect is exactly what a refactor is allowed to do). The diff is
        // formatting/rename-immune, so this gates on behavior, not text.
        var expectNoEffectChange = new Option<bool>("--expect-no-effect-change")
        {
            Description = "CI gate: exit 1 if any entry point's reachable effect set changed (for behavior-preserving MRs).",
        };
        // The gate --expect-no-effect-change structurally CANNOT provide: a guard tightened around an
        // unchanged effect adds no call and no effect, so the effect-set gate passes while the effect silently
        // stops firing for a subset of inputs. Separate flag rather than folded in, because NARROWED is a
        // syntactic over-approximation (conjunct containment, no solver) and must not be able to fail a CI job
        // that opted only into the deterministic effect-set gate.
        var expectNoGuardNarrowing = new Option<bool>("--expect-no-guard-narrowing")
        {
            Description =
                "CI gate: exit 1 if any call edge's guard NARROWED (an effect now fires on strictly fewer paths) — "
                + "catches audit suppression / permission tightening that --expect-no-effect-change cannot see.",
        };
        var cmd = new Command(
            name: "impact",
            description: "Two-store diff: the entry-point + per-EP effect/reach changes between two indexed commits (--base <store> --head <store>)."
        )
        {
            @base,
            head,
            async,
            includeDelivery,
            rules,
            format,
            limit,
            noCache,
            noGate,
            noAmplification,
            time,
            structural,
            only,
            exclude,
            intrinsic,
            expectNoEffectChange,
            expectNoGuardNarrowing,
        };
        // Both stores are mandatory — impact is a pure two-store diff, there is no working-tree/git fallback
        // and no LATEST default. Error clearly (before opening anything) if either ref is missing.
        cmd.Validators.Add(result =>
        {
            var hasBase = !string.IsNullOrWhiteSpace(result.GetValue(@base));
            var hasHead = !string.IsNullOrWhiteSpace(result.GetValue(head));
            if (!hasBase || !hasHead)
            {
                result.AddError(
                    "rig impact requires both --base <store> and --head <store> (indexed commit store refs: sha / short-sha / store-id)."
                );
            }
        });
        cmd.SetAction(pr =>
            CommandGuard.RunGuardedAsync(
                workingDirectory,
                error,
                () =>
                    RunAsync(
                        new Options(
                            BaseRef: pr.GetValue(@base)!,
                            HeadRef: pr.GetValue(head)!,
                            Async: pr.GetValue(async),
                            IncludeDelivery: pr.GetValue(includeDelivery),
                            ExtraRules: CommonOptions.RulesOf(pr.GetValue(rules)),
                            Format: pr.GetValue(format),
                            Limit: pr.GetValue(limit),
                            NoCache: pr.GetValue(noCache),
                            Gate: !pr.GetValue(noGate),
                            Amplification: !pr.GetValue(noAmplification),
                            Time: pr.GetValue(time),
                            Structural: pr.GetValue(structural),
                            Only: CommonOptions.FilterSet(pr.GetValue(only)),
                            Exclude: CommonOptions.FilterSet(pr.GetValue(exclude)),
                            Intrinsic: pr.GetValue(intrinsic),
                            ExpectNoEffectChange: pr.GetValue(expectNoEffectChange),
                            ExpectNoGuardNarrowing: pr.GetValue(expectNoGuardNarrowing)
                        ),
                        new CommandIo(
                            new TextOutput(Output: output, Error: error),
                            new WorkspaceLocation(WorkingDirectory: workingDirectory, StoreRef: null)
                        )
                    )
            )
        );
        return cmd;
    }

    // Bound option values for `rig impact`. Raw user inputs kept as parsed strings/values; derived locals
    // (tsv, max, mode) live at the top of RunAsync so cross-flag derivation stays in one place.
    private sealed record Options(
        string BaseRef,
        string HeadRef,
        bool Async,
        bool IncludeDelivery,
        IReadOnlyList<string> ExtraRules,
        string? Format,
        int? Limit,
        bool NoCache,
        bool Gate,
        // Amplification finding tier in the per-EP delta (ep_amplification_added/_removed) — ON by default;
        // --no-amplification drops those rows (and keys its own cache entry). See CommonOptions.NoAmplification.
        bool Amplification,
        bool Time,
        bool Structural,
        HashSet<string> Only,
        HashSet<string> Exclude,
        bool Intrinsic,
        bool ExpectNoEffectChange,
        bool ExpectNoGuardNarrowing
    );

    private static async Task<int> RunAsync(Options opts, CommandIo io)
    {
        var tsv = CommonOptions.IsTsv(opts.Format);
        var max = opts.Limit ?? int.MaxValue;
        var mode = CommonOptions.Mode(async: opts.Async, includeDelivery: opts.IncludeDelivery); // --async => walk sound handoffs (delivery fan-out excluded unless --include-delivery)

        // --time: sample CPU/mem/disk across the whole run + record each DiffAsync phase (fed via onPhase
        // below), then on scope exit print the per-phase breakdown to stderr AND dump rig-impact-telemetry.csv
        // next to the HEAD store — the SAME format `index --time` emits, so the telemetry dashboard renders it.
        // Declared first so it disposes LAST (after the store context), capturing the full run. No-op without --time.
        using var timing = QueryTiming.Start(
            opts.Time,
            io.TextOutput.Error,
            csvDirectory: opts.Time ? StoreLayout.ResolveReadStoreDir(io.WorkspaceLocation with { StoreRef = opts.HeadRef }) : null,
            csvFileName: "rig-impact-telemetry.csv"
        );

        // The HEAD store: opened once here so the deployment-map read (render chrome) shares it with DiffAsync
        // (which also reads it for provenance + the cold derivation). Opening issues no query.
        await using var context = await OpenReadContextGatedAsync(io.WorkspaceLocation with { StoreRef = opts.HeadRef });

        // Trust disclosure is deliberately FIRST: before deployments, cache decode, graph derivation, and
        // filter validation. A later failure must never hide that the two persisted fact sets are incompatible.
        var provenance = await ImpactEngine.ProbeProvenancePairAsync(
            headContext: context,
            ws: io.WorkspaceLocation,
            baseRef: opts.BaseRef,
            headRef: opts.HeadRef
        );
        var baseStoreDirectory = StoreLayout.ResolveReadStoreDir(io.WorkspaceLocation with { StoreRef = opts.BaseRef });
        var headStoreDirectory = StoreLayout.ResolveReadStoreDir(io.WorkspaceLocation with { StoreRef = opts.HeadRef });
        StoreAnswerDisclosure.WriteCompilationHealth("base", baseStoreDirectory);
        StoreAnswerDisclosure.WriteCompilationHealth("head", headStoreDirectory);
        var extractionCompatible = WriteExtractionVersionWarning(provenance.Base, provenance.Head, io.TextOutput.Error);

        // F4: load the DeploymentMap ONCE (render-only — the --structural chips). Not part of the diff, so it
        // stays here rather than in DiffAsync (which the web endpoint also calls, without deployment chrome).
        var deployments = await LoadDeploymentsAsync(context, io.WorkspaceLocation.WorkingDirectory, io.TextOutput.Error);

        // The store-vs-store diff (warm-cached or freshly derived) — the SAME artifact the web /api/impact
        // returns, so `rig impact` and the web view cannot diverge.
        var art = await ImpactEngine.DiffAsync(
            headContext: context,
            ws: io.WorkspaceLocation,
            baseRef: opts.BaseRef,
            headRef: opts.HeadRef,
            mode: mode,
            gate: opts.Gate,
            noCache: opts.NoCache,
            extraRules: opts.ExtraRules,
            // --time: record each top-level phase as it completes into the timing scope (whose master clock +
            // sampler started above). Null (no --time) leaves DiffAsync's fast path untouched.
            onPhase: opts.Time
                ? (name, ms) =>
                {
                    timing.Record(name, TimeSpan.FromMilliseconds(ms));
                    return Task.CompletedTask;
                }
                : null,
            amplification: opts.Amplification,
            provenance: provenance
        );

        // Validate the filter tokens against the effective rule set. Worth the extra rules load (json parse,
        // trivial next to a two-store diff): a typo'd `--only llbgen:write` would otherwise filter everything
        // out and read as "no behavioural change" — the exact silent-false-negative this filter exists to fix.
        // BEFORE the selection, because PrepareFilterTokens also EXPANDS family tokens into their providers —
        // run after it, a `--only <family>` selects nothing (and /api/impact prepares them before Select too).
        if (opts.Only.Count > 0 || opts.Exclude.Count > 0)
        {
            var ruleSet = RuleSetLoader.Load(
                workingDirectory: io.WorkspaceLocation.WorkingDirectory,
                extraRules: opts.ExtraRules,
                loadedPaths: out _
            );
            PrepareFilterTokens(only: opts.Only, exclude: opts.Exclude, rules: ruleSet, errorWriter: io.TextOutput.Error);
        }

        // --only / --exclude / the default intrinsic hiding, plus the counts and the structural partition that
        // follow from them: ONE selection, in the engine, shared with /api/impact so the two cannot diverge.
        // Applied POST-CACHE — the cached diff artifact stays complete and filter-independent, so no
        // ImpactSchema bump and no cache fragmentation across filter combos (same contract as reaches/tree/derive).
        var view = ImpactEngine.Select(art.Diff, only: opts.Only, exclude: opts.Exclude, includeIntrinsic: opts.Intrinsic);
        WriteIntrinsicNote(view.HiddenIntrinsic, io.TextOutput.Error);

        RenderImpact(
            output: io.TextOutput.Output,
            view: view,
            baseProv: art.BaseProvenance,
            headProv: art.HeadProvenance,
            mode: mode,
            deployments: deployments,
            fqnSites: art.FqnSites,
            tsv: tsv,
            structural: opts.Structural,
            max: max
        );
        // The gates read the SELECTED view so output and CI verdict can never disagree; impact_summary's
        // intrinsic_hidden column is what keeps that from being a silent loosening. Both gates are evaluated
        // (so a run opting into both sees both verdicts) and the exit code is the OR — either failing fails CI.
        var effectExit = ExpectNoEffectChangeExit(opts.ExpectNoEffectChange, view.BehavioralEpCount, io.TextOutput.Error);
        var guardExit = ExpectNoGuardNarrowingExit(
            opts.ExpectNoGuardNarrowing,
            ImpactEngine.NarrowedGuardCount(view.GuardConditions),
            io.TextOutput.Error,
            extractionCompatible
        );
        return effectExit != 0 ? effectExit : guardExit;
    }

    // Display label for a guard-delta endpoint. ShortName truncates at the first `(`, which silently DROPS the
    // `~λN` lambda marker that lives after the parameter list — so the four separately-guarded lambdas of one
    // method all rendered as the same `PersonEventEntity.Save`, making four distinct edges look like duplicate
    // rows. Re-append the marker so the rows are distinguishable and each one can be found in `tree --guards`.
    internal static string GuardEndpointLabel(string symbolId)
    {
        var marker = symbolId.IndexOf("~λ", StringComparison.Ordinal);
        return marker < 0 ? ShortName(symbolId) : ShortName(symbolId) + symbolId[marker..];
    }

    // Disclose a VERSION-SKEWED pair of stores before the guard rows are believed.
    //
    // Guards on lambda edges did not exist before 2026-07-27, so diffing a store indexed by an older `rig`
    // against a newer one makes thousands of lambda edges look freshly guarded — a flood of NARROWED rows and a
    // failed --expect-no-guard-narrowing that is indistinguishable, row by row, from a real audit suppression.
    // Left silent this would manufacture exactly the kind of false report that cost a full session on
    // 2026-07-27, so it warns loudly and names the fix rather than quietly suppressing the rows (suppression
    // would hide a genuine narrowing that happened to be in the same diff).
    internal static void WriteGuardSkewWarning(ImpactDiff diff, TextWriter error)
    {
        if (diff.GuardCoverage is not { SkewSuspected: true } c)
        {
            return;
        }

        error.WriteLine(
            $"WARNING: the two stores disagree on guarded lambda edges ({c.BaseLambdaGuards} base vs {c.HeadLambdaGuards} head). "
                + "Guards on lambda/method-group edges did not exist before 2026-07-27, so one of these stores predates that "
                + "fix and EVERY guard_condition_delta row below is suspect — a pre-fix store reports must-run for edges the "
                + "post-fix one reports as guarded. Re-index BOTH commits with the current rig before trusting these rows or "
                + "the --expect-no-guard-narrowing verdict."
        );
    }

    // ExtractionVersion is the deliberate compatibility contract for persisted facts. Build strings are
    // included only to help diagnose which binary produced each side; they never decide compatibility.
    internal static bool WriteExtractionVersionWarning(StoreProvenance baseProvenance, StoreProvenance headProvenance, TextWriter error)
    {
        if (baseProvenance.IsExtractionCompatibleWith(headProvenance))
        {
            return true;
        }

        static string Versions(StoreProvenance provenance) =>
            provenance.ExtractionVersionsOrEmpty.Count == 0
                ? "<missing>"
                : string.Join(",", provenance.ExtractionVersionsOrEmpty.Select(v => $"v{v}"));
        static string Builds(StoreProvenance provenance) =>
            provenance.ProducingRigBuildsOrEmpty.Count == 0 ? "<unknown>" : string.Join(",", provenance.ProducingRigBuildsOrEmpty);

        error.WriteLine(
            $"WARNING: incompatible extraction versions: base {baseProvenance.Label} has [{Versions(baseProvenance)}] "
                + $"(rig [{Builds(baseProvenance)}]); head {headProvenance.Label} has [{Versions(headProvenance)}] "
                + $"(rig [{Builds(headProvenance)}]). A mixed or mismatched store pair cannot be compared safely. "
                + "Re-index BOTH stores with the current rig before trusting impact or the --expect-no-guard-narrowing verdict."
        );
        return false;
    }

    // The `--expect-no-guard-narrowing` CI gate. Narrowing = a call edge whose gating condition gained
    // conjuncts, so an effect it leads to now fires on strictly fewer paths. This is the class
    // --expect-no-effect-change structurally cannot catch: no call and no effect changed, only the predicate.
    //
    // Only NARROWED gates. Widened/Changed rows are reported for review but a gate tripping on them would fire
    // on ordinary feature work. The verdict goes to STDERR so a --format tsv stdout stays machine-clean.
    internal static int ExpectNoGuardNarrowingExit(bool expect, int narrowedCount, TextWriter error, bool extractionCompatible = true)
    {
        if (!expect)
        {
            return 0;
        }

        if (!extractionCompatible)
        {
            error.WriteLine(
                "--expect-no-guard-narrowing FAILED: extraction versions are mixed or mismatched, so the guard diff is not trustworthy. Re-index BOTH stores with the current rig. exit 1."
            );
            return 1;
        }

        if (narrowedCount > 0)
        {
            error.WriteLine(
                $"--expect-no-guard-narrowing FAILED: {narrowedCount} call edge(s) had their guard NARROWED — an effect now "
                    + "fires on strictly fewer paths (see the guard_condition_delta rows). exit 1."
            );
            return 1;
        }

        error.WriteLine("--expect-no-guard-narrowing OK: no call edge's guard narrowed.");
        return 0;
    }

    // The `--expect-no-effect-change` CI gate. Behavioral change = an entry point present in BOTH commits whose
    // reachable EFFECT set changed (impactDiff.PerEp — the header's "N with a changed behavior"). Structural-only
    // reachable-tree ripple is NOT a behavioral change and never trips the gate. The verdict goes to STDERR so a
    // `--format tsv` run's stdout stays machine-clean; the exit code is the CI signal (1 = changed, 0 = clean).
    internal static int ExpectNoEffectChangeExit(bool expect, int behavioralEpCount, TextWriter error)
    {
        if (!expect)
        {
            return 0;
        }

        if (behavioralEpCount > 0)
        {
            error.WriteLine(
                $"--expect-no-effect-change FAILED: {behavioralEpCount} entry point(s) changed behavior (reachable effect set) — see the per-EP section. exit 1."
            );
            return 1;
        }

        error.WriteLine("--expect-no-effect-change OK: no entry point's effect set changed.");
        return 0;
    }

    // The render of a computed impact diff — shared by the cold (just-computed) and warm (cache-replayed)
    // paths so a hit is BYTE-IDENTICAL to a recompute. A pure function of the diff + provenance + deployments
    // + the FQN site map + the presentation flags: tsv emits the typed rows, else the human sections. FqnForCard
    // only ever looks up the diff's own sites, so the warm path's site SUBSET serves it exactly as the full map.
    private static void RenderImpact(
        TextWriter output,
        ImpactView view,
        StoreProvenance baseProv,
        StoreProvenance headProv,
        FactPathFinder.TraversalMode mode,
        DeploymentMap deployments,
        Dictionary<(string, int), string> fqnSites,
        bool tsv,
        bool structural,
        int max
    )
    {
        if (tsv)
        {
            EmitTsv(output, view, fqnSites, max, structural);
            return;
        }

        WriteHeader(output, baseProv, headProv, mode, view);
        WriteEpDiffHuman(output, baseProv, view.Diff.Ep, max);
        // Before the per-EP cards: a predicate-only change produces NO per-EP effect delta, so if this section
        // came after the effect section a reviewer scanning top-down would read "no behavioural change" and stop.
        WriteGuardConditionsHuman(output, view.GuardConditions, max);
        // PRIMARY signal: the entry points whose reachable EFFECT set changed (the behavioral handful). Always
        // shown — this is the "what actually does something different" answer.
        WritePerEpHuman(output, baseProv, view.PerEp, fqnSites, max);
        // The structural reachable-tree diff is mostly data-shape ripple (a record field add lights up every
        // reaching EP). By default we DEMOTE it to a one-line, cause-classified breadcrumb so a no-net-new-effect
        // migration still can't hide; --structural expands it to the full per-EP list.
        if (structural)
        {
            WriteAffected(output, baseProv, view.Diff, deployments, fqnSites, max);
        }
        else
        {
            WriteStructuralBreadcrumb(output, baseProv, view);
        }
    }

    private static void EmitEpDiffTsv(TextWriter output, EpDiff? diff)
    {
        if (diff is null)
        {
            return;
        }

        foreach (var (kind, route) in diff.Added)
        {
            output.WriteLine($"ep_added\t{kind}\t{route}");
        }

        foreach (var (kind, route) in diff.Removed)
        {
            output.WriteLine($"ep_removed\t{kind}\t{route}");
        }
    }

    // The guard-condition section: call edges whose gating predicate moved. Silent when nothing moved, so it
    // costs nothing on the common case. A NARROWED row is the highest-signal line `impact` can print — it is
    // the ONLY output a predicate-only change produces — so it leads, and the base→head conditions are shown
    // in full rather than summarized: the whole point is to read what the condition became.
    private static void WriteGuardConditionsHuman(TextWriter output, IReadOnlyList<GuardConditionDelta> deltas, int max)
    {
        if (deltas.Count == 0)
        {
            return;
        }

        var narrowed = deltas.Count(d => d.Verdict == GuardVerdict.Narrowed);
        output.WriteLine();
        output.WriteLine(
            $"Guard conditions changed on {deltas.Count} call edge(s): {narrowed} narrowed, "
                + $"{deltas.Count(d => d.Verdict == GuardVerdict.Widened)} widened, "
                + $"{deltas.Count(d => d.Verdict == GuardVerdict.Changed)} changed"
        );
        if (narrowed > 0)
        {
            output.WriteLine($"{Indent.L1}NARROWED = the effect now fires on strictly FEWER paths — audit suppression, permission");
            output.WriteLine($"{Indent.L1}tightening and feature-flag gating all have this shape, and NONE of them change the effect set.");
        }

        // GROUPED BY THE CONDITION, not per edge. One source-level `if` typically gates several edges — the MR
        // !11025 change produced FOUR (the method's separately-guarded argument lambdas), which as four rows
        // carrying identical conditions read as duplicates and invited the reader to distrust the output. The
        // reviewable unit is the predicate that moved; the edges it gates are its detail. TSV stays per-edge —
        // that is the machine contract, and there the labels are distinct.
        var groups = deltas
            .GroupBy(d => (d.Verdict, d.Caller, d.BaseCondition, d.HeadCondition))
            .Select(g => new
            {
                g.Key,
                Callees = g.Select(x => x.Callee).ToList(),
                Effects = g.SelectMany(x => x.Effects).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
                EpCount = g.Max(x => x.EpCount),
                Samples = g.First().SampleRoutes,
            })
            .ToList();

        foreach (var g in groups.Take(max))
        {
            var verdict = g.Key.Verdict.ToString().ToUpperInvariant();
            output.WriteLine($"{Indent.L1}{verdict}  in {ShortName(g.Key.Caller)}");
            output.WriteLine($"{Indent.L2}base:  {(g.Key.BaseCondition.Length == 0 ? "(unconditional)" : g.Key.BaseCondition)}");
            output.WriteLine($"{Indent.L2}head:  {(g.Key.HeadCondition.Length == 0 ? "(unconditional)" : g.Key.HeadCondition)}");
            output.WriteLine(
                $"{Indent.L2}gates {g.Callees.Count} edge(s): {string.Join(", ", g.Callees.Take(4).Select(GuardEndpointLabel))}"
                    + (g.Callees.Count > 4 ? $", +{g.Callees.Count - 4} more" : "")
            );
            // Effects are capped for readability — a lambda that reaches half the solution legitimately lists
            // 20+ providers, which buries the one that matters. Scope with --only to see a specific provider.
            output.WriteLine(
                $"{Indent.L2}reaching: {string.Join(", ", g.Effects.Take(8))}"
                    + (g.Effects.Count > 8 ? $", +{g.Effects.Count - 8} more (use --only to scope)" : "")
            );
            if (g.EpCount > 0)
            {
                var samples = g.Samples.Count > 0 ? $" — e.g. {string.Join("; ", g.Samples)}" : "";
                output.WriteLine($"{Indent.L2}reached by {g.EpCount} entry point(s){samples}");
            }
        }

        if (groups.Count > max)
        {
            output.WriteLine($"{Indent.L1}… +{groups.Count - max} more (raise --limit, or --format tsv for all)");
        }
    }

    private static void WriteEpDiffHuman(TextWriter output, StoreProvenance baseProv, EpDiff? diff, int max)
    {
        output.WriteLine();
        if (diff is null)
        {
            return;
        }

        output.WriteLine($"Entry-point diff vs '{baseProv.ShortLabel}': +{diff.Added.Count} added, -{diff.Removed.Count} removed");
        foreach (var (kind, route) in diff.Added.Take(max))
        {
            output.WriteLine($"{Indent.L1}+ {kind} {route}");
        }

        foreach (var (kind, route) in diff.Removed.Take(max))
        {
            output.WriteLine($"{Indent.L1}- {kind} {route}");
        }
    }

    // Display label for a reach node: a `R:`-prefixed degenerate field/property-access node (Phase 3) renders
    // as its short member name tagged `(field/prop access)`; an ordinary method DocID renders via ShortName.
    private static string ReachNodeLabel(string node) =>
        node.StartsWith(ImpactEngine.RefNodePrefix, StringComparison.Ordinal)
            ? $"{ShortName(node[ImpactEngine.RefNodePrefix.Length..])} (field/prop access)"
            : ShortName(node);

    private static void EmitPerEpTsv(TextWriter output, IReadOnlyList<EpFootprintDelta> deltas, Dictionary<(string, int), string> fqnSites)
    {
        foreach (var d in deltas)
        {
            var fqn = ImpactEngine.FqnForCard(route: d.Route, filePath: d.FilePath, line: d.Line, idBySite: fqnSites);
            output.WriteLine(
                $"ep_delta\t{d.Kind}\t{d.Route}\t{fqn}\t{d.BranchEffects}\t{d.BaseEffects}\t+{d.Added.Count}\t-{d.Removed.Count}\t~{d.Amplified.Count}"
            );
            foreach (var (provider, operation, resource, enclosing) in d.Added)
            {
                output.WriteLine($"ep_effect_added\t{d.Kind}\t{d.Route}\t{provider}\t{operation}\t{resource}\t{enclosing}");
            }

            foreach (var (provider, operation, resource, enclosing) in d.Removed)
            {
                output.WriteLine($"ep_effect_removed\t{d.Kind}\t{d.Route}\t{provider}\t{operation}\t{resource}\t{enclosing}");
            }

            foreach (var a in d.Amplified)
            {
                output.WriteLine(
                    $"ep_effect_amplified\t{d.Kind}\t{d.Route}\t{a.Provider}\t{a.Operation}\t{a.Resource}\t{a.Enclosing}\t{a.BaseCount}\t{a.BranchCount}\t{a.BaseInLoop}\t{a.BranchInLoop}"
                );
            }

            // FR-1e: a guard (lock/async_lock) added/removed on a path that still mutates shared state.
            //  ep_guard_delta  <kind>  <route>  <+guards comma-joined>  <-guards comma-joined>
            if (ImpactEngine.HasGuardDeltaOnSharedMutation(d))
            {
                var (gAdded, gRemoved) = ImpactEngine.GuardEffectDelta(d);
                output.WriteLine($"ep_guard_delta\t{d.Kind}\t{d.Route}\t{string.Join(',', gAdded)}\t{string.Join(',', gRemoved)}");
            }

            // HAZARD DELTA: one row per hazard finding GAINED / LOST on this EP's reach.
            //  ep_hazard_added / ep_hazard_removed  <kind>  <route>  <type>  <confidence>  <cell>  <enclosing>
            foreach (var h in d.HazardsAddedOrEmpty)
            {
                output.WriteLine($"ep_hazard_added\t{d.Kind}\t{d.Route}\t{h.Type}\t{h.Confidence}\t{h.Cell}\t{h.Enclosing}");
            }

            foreach (var h in d.HazardsRemovedOrEmpty)
            {
                output.WriteLine($"ep_hazard_removed\t{d.Kind}\t{d.Route}\t{h.Type}\t{h.Confidence}\t{h.Cell}\t{h.Enclosing}");
            }

            // AMPLIFICATION DELTA (the looped_effect tier): one row per (EP × provider:operation) whose effect
            // newly runs inside — or no longer runs inside — an iteration context, with the reachable site count.
            // TERSE on purpose: never one row per site. Distinct row types so a consumer filters on column 1
            // (`awk -F'\t' '$1=="ep_amplification_added"'`) without touching the `ep_hazard_*` stream.
            //  ep_amplification_added / _removed  <kind>  <route>  <provider>  <operation>  <sites>
            foreach (var a in d.AmplificationsAddedOrEmpty)
            {
                output.WriteLine($"ep_amplification_added\t{d.Kind}\t{d.Route}\t{a.Provider}\t{a.Operation}\t{a.Sites}");
            }

            foreach (var a in d.AmplificationsRemovedOrEmpty)
            {
                output.WriteLine($"ep_amplification_removed\t{d.Kind}\t{d.Route}\t{a.Provider}\t{a.Operation}\t{a.Sites}");
            }
        }
    }

    // PRIMARY section: the entry points whose reachable EFFECT set changed — the behavioral signal. This is the
    // small, high-information set (a handful), as opposed to the structural reachable-tree diff which is mostly
    // data-shape ripple.
    private static void WritePerEpHuman(
        TextWriter output,
        StoreProvenance baseProv,
        IReadOnlyList<EpFootprintDelta> deltas,
        Dictionary<(string, int), string> fqnSites,
        int max
    )
    {
        output.WriteLine();
        if (deltas.Count == 0)
        {
            output.WriteLine(
                $"Behavioral changes per entry point vs '{baseProv.ShortLabel}': none — no entry point's reachable-effect set changed."
            );
            return;
        }

        // The behavioral set = (effect-set changed) ∪ (amplified) — an EP whose set is stable but has an
        // amplified effect (now produced more / in a loop) is in `deltas` too (DiffFootprints lists it).
        output.WriteLine(
            $"Behavioral changes per entry point vs '{baseProv.ShortLabel}' (reachable-effect set changed or effect amplified): {deltas.Count}"
        );
        foreach (var d in deltas.Take(max))
        {
            // Render the FQN (round-trips into `rig tree`), same as the structural list; falls back to the route.
            var label = ImpactEngine.FqnForCard(route: d.Route, filePath: d.FilePath, line: d.Line, idBySite: fqnSites);
            var ampPart = d.Amplified.Count > 0 ? $", ~{d.Amplified.Count} amplified" : "";
            var hazAdded = d.HazardsAddedOrEmpty;
            var hazRemoved = d.HazardsRemovedOrEmpty;
            var hazPart = hazAdded.Count > 0 || hazRemoved.Count > 0 ? $", hazards +{hazAdded.Count}/-{hazRemoved.Count}" : "";
            var ampAdded = d.AmplificationsAddedOrEmpty;
            var ampRemoved = d.AmplificationsRemovedOrEmpty;
            var loopPart = ampAdded.Count > 0 || ampRemoved.Count > 0 ? $", looped +{ampAdded.Count}/-{ampRemoved.Count}" : "";
            output.WriteLine(
                $"{Indent.L2}{d.Kind} {label}  (effects {d.BaseEffects}→{d.BranchEffects}, +{d.Added.Count}/-{d.Removed.Count}{ampPart}{hazPart}{loopPart})"
            );
            foreach (var (provider, operation, resource, enclosing) in d.Added.Take(max))
            {
                output.WriteLine($"{Indent.L3}+ {provider} {operation}{Resource(resource)}  ({enclosing})");
            }

            foreach (var (provider, operation, resource, enclosing) in d.Removed.Take(max))
            {
                output.WriteLine($"{Indent.L3}- {provider} {operation}{Resource(resource)}  ({enclosing})");
            }

            // Amplified effects: same key on both sides, but produced MORE or now in a loop. Marked `~` and
            // worded for REVIEW (not "regression") — the static signal can't tell a hot-cache re-read from a
            // real extra cold call. Note: a harmless ×1->×2 will show; that's the chosen tradeoff.
            foreach (var a in d.Amplified.Take(max))
            {
                output.WriteLine(
                    $"{Indent.L3}~ {a.Provider} {a.Operation}{Resource(a.Resource)}  ({AmplifyNote(a)})  ({a.Enclosing})  [review]"
                );
            }

            // FR-1e: a lock/guard was added or removed on a path that STILL mutates shared state — the
            // concurrency protection around an inherently-shared cell changed. High-signal: this is the
            // exact shape of the lock-guarded-class race (a guard lost, or a fix that adds one). Flagged for
            // review, not a verdict.
            if (ImpactEngine.HasGuardDeltaOnSharedMutation(d))
            {
                var (gAdded, gRemoved) = ImpactEngine.GuardEffectDelta(d);
                var moves = new List<string>();
                moves.AddRange(gAdded.Select(g => $"+{g}"));
                moves.AddRange(gRemoved.Select(g => $"-{g}"));
                output.WriteLine(
                    $"{Indent.L3}⚠ guard delta on a shared-mutation path: {string.Join(" ", moves)}  (shared_state mutation still reachable)  [review]"
                );
            }

            // HAZARD DELTA: the hazard findings (race_window / lazy_init_race / n_plus_1 /
            // unserializable_payload) this EP's reach GAINED (+) or LOST (-) — a refactor that opened a race on
            // this path, or a fix that closed one. Shown with the confidence tier + the cell/context, mirroring
            // the effect +/- lines. Flagged for review, not a verdict.
            foreach (var h in hazAdded.Take(max))
            {
                output.WriteLine($"{Indent.L3}+ hazard {h.Type} ({h.Confidence}){Cell(h.Cell)}  ({h.Enclosing})  [review]");
            }

            foreach (var h in hazRemoved.Take(max))
            {
                output.WriteLine($"{Indent.L3}- hazard {h.Type} ({h.Confidence}){Cell(h.Cell)}  ({h.Enclosing})");
            }

            // AMPLIFICATION DELTA: this EP's reach newly runs (or stopped running) a provider:operation inside an
            // iteration context. Wrapping a loop around an existing call leaves the effect SET identical, so the
            // +/- effect lines above say nothing while the cost went ×N — this line is the whole reason the tier
            // is wired into impact. One line per provider:operation with the site count, never per site.
            foreach (var a in d.AmplificationsAddedOrEmpty.Take(max))
            {
                output.WriteLine($"{Indent.L3}+ 🔁 {a.ProviderOperation} now in loop  ({a.Sites} site(s))  [review]");
            }

            foreach (var a in d.AmplificationsRemovedOrEmpty.Take(max))
            {
                output.WriteLine($"{Indent.L3}- 🔁 {a.ProviderOperation} no longer in loop  ({a.Sites} site(s))");
            }
        }

        static string Resource(string resource) => string.IsNullOrEmpty(resource) ? "" : $" {resource}";
        static string Cell(string cell) => string.IsNullOrEmpty(cell) ? "" : $" {cell}";
    }

    // The amplification annotation: the count move (×base -> ×branch) and/or a loop-entry note, both when
    // both fired. Worded as an observation, not a verdict — it pairs with the `[review]` tag in the line.
    private static string AmplifyNote(EpEffectAmplified a)
    {
        var parts = new List<string>();
        if (a.BranchCount > a.BaseCount)
        {
            parts.Add($"×{a.BaseCount} -> ×{a.BranchCount}");
        }

        if (a.BranchInLoop && !a.BaseInLoop)
        {
            parts.Add("now in loop");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "amplified";
    }

    // The header: the one-line PROVEN-diff takeaway, then which two commits/branches were compared. Both sides
    // are indexed commits — there is no working tree.
    private static void WriteHeader(
        TextWriter output,
        StoreProvenance baseProv,
        StoreProvenance headProv,
        FactPathFinder.TraversalMode mode,
        ImpactView view
    )
    {
        var asyncNote = mode switch
        {
            FactPathFinder.TraversalMode.AsyncExact => "  (--async: handoffs included; delivery fan-out excluded)",
            FactPathFinder.TraversalMode.AsyncInclude => "  (--async --include-delivery: delivery fan-out included)",
            _ => "",
        };

        output.WriteLine(DiffSummary(baseProv, view));
        output.WriteLine();
        output.WriteLine($"Impact: {baseProv.Label}  ->  {headProv.Label}{asyncNote}");
        if (SyncModeDisclosure(mode) is { } note)
        {
            output.WriteLine(note);
        }
    }

    // Bug A (impact-silent-async-handoff-underreport): in the DEFAULT sync mode, impact CUTS async/scheduled
    // handoff edges (background workers, actor inboxes, events), so an effect reachable from an entry point
    // ONLY across such a handoff is absent from that EP's footprint — and the diff previously said nothing
    // about it, letting a reviewer read the behavioral count as the whole picture (`callers` discloses the
    // same limitation; impact did not). Disclose the mode + the exclusion + the remedy so the count is never
    // taken as unconditional. The async modes already state their scope in WriteHeader's asyncNote, so this
    // is SYNC-only (returns null otherwise — nothing is excluded to disclose). Internal for unit-testing the
    // per-mode text. Deliberately NOT a quantified "+N EPs via async" count: that needs a second async-mode
    // reach pass over both stores (~2x the walk), too costly to impose on every default run — the actionable
    // remedy (`--async`) is one flag away, so the disclosure points there instead.
    internal static string? SyncModeDisclosure(FactPathFinder.TraversalMode mode) =>
        mode == FactPathFinder.TraversalMode.SyncCut
            ? "Note: computed in SYNC mode — paths through async/scheduled handoffs (background workers, actor "
                + "inboxes, events) are not followed, so effects reachable only that way are excluded. Re-run "
                + "with --async to include them."
            : null;

    // The one-line takeaway: the PROVEN change vs the base store — entry points added/removed, entry points
    // whose behavior (reachable-effect set) changed, and entry points whose reachable tree changed.
    private static string DiffSummary(StoreProvenance baseProv, ImpactView view)
    {
        // The "changed behavior" headline counts EFFECT-set changes only; hazard-only EPs are reported by
        // hazardNote below (PerEp includes them, but they aren't an effect-set change — no double-count). This
        // is the view's ONE behavioral count, the same one impact_summary's behavioral_eps prints.
        var behavioralEps = view.BehavioralEpCount;
        var added = view.Diff.Ep?.Added.Count ?? 0;
        var removed = view.Diff.Ep?.Removed.Count ?? 0;
        // FR-1e: count the EPs whose guard (lock/atomic) around a still-reachable shared mutation changed.
        // Only appended when non-zero so the common (no-guard-change) summary line stays unchanged.
        var guardEps = view.PerEp.Count(ImpactEngine.HasGuardDeltaOnSharedMutation);
        var guardNote = guardEps > 0 ? $" ⚠ {guardEps} with a guard delta on a shared-mutation path." : "";
        // Hazard delta: count the EPs that GAINED or LOST a hazard finding (race_window / n+1 / …). Appended
        // only when non-zero so the common (no-hazard-change) summary line stays unchanged.
        var hazardEps = view.PerEp.Count(d => d.HazardsAddedOrEmpty.Count > 0 || d.HazardsRemovedOrEmpty.Count > 0);
        var hazardNote = hazardEps > 0 ? $" ⚠ {hazardEps} with a hazard delta." : "";
        return $"Diff vs '{baseProv.ShortLabel}': {PlusMinus(added: added, removed: removed)} entry point(s)"
            + $"; {behavioralEps} entry point(s) with a changed behavior, {view.AffectedEpCount} with a changed reachable tree.{guardNote}{hazardNote}";
    }

    private static string PlusMinus(int added, int removed) => $"+{added}/-{removed}";

    // The DEMOTED structural view (default): one line stating how many EPs have a changed reachable tree but NO
    // behavioral (effect-set) change, broken down by cause so a data-shape ripple reads as exactly that — and a
    // no-net-new-effect migration still surfaces as a non-zero `other` count that can't hide. `--structural`
    // expands this to the full per-EP list (WriteAffected). EPs whose effect set DID change are already shown by
    // WritePerEpHuman, so they're excluded here (the two sections partition the affected set, no double-count).
    private static void WriteStructuralBreadcrumb(TextWriter output, StoreProvenance baseProv, ImpactView view)
    {
        output.WriteLine();
        var structuralOnly = view.StructuralOnly;
        if (structuralOnly.Count == 0)
        {
            output.WriteLine($"Structural-only reachable-tree changes vs '{baseProv.ShortLabel}': none.");
            return;
        }

        var byCause = structuralOnly.GroupBy(s => s.Cause).ToDictionary(g => g.Key, g => g.Count());
        int N(StructuralCause c) => byCause.GetValueOrDefault(c);
        var parts = new List<string>();
        if (N(StructuralCause.RecordShape) > 0)
        {
            parts.Add($"{N(StructuralCause.RecordShape)} record-shape (reach a changed field/property)");
        }

        if (N(StructuralCause.CtorSig) > 0)
        {
            parts.Add($"{N(StructuralCause.CtorSig)} ctor-signature");
        }

        if (N(StructuralCause.InPlace) > 0)
        {
            parts.Add($"{N(StructuralCause.InPlace)} in-place body change");
        }

        if (N(StructuralCause.Other) > 0)
        {
            parts.Add($"{N(StructuralCause.Other)} other method-level churn");
        }

        output.WriteLine(
            $"Structural-only reachable-tree changes vs '{baseProv.ShortLabel}' (no behavioral effect change): {structuralOnly.Count} entry point(s)"
        );
        output.WriteLine($"{Indent.L1}{string.Join(", ", parts)}");
        // The `other` bucket is the one that can hide a real migration (method churn with no NET-new effect kind),
        // so call it out explicitly when present — that's the line a reviewer should not skip.
        if (N(StructuralCause.Other) > 0)
        {
            output.WriteLine(
                $"{Indent.L1}↳ {N(StructuralCause.Other)} are method-level churn — review these (a migration can change reach without a net-new effect kind)."
            );
        }

        output.WriteLine($"{Indent.L1}--structural to list them all (or --format tsv).");
    }

    // The affected entry points, computed STRUCTURALLY: each EP whose full reachable symbol set differs
    // base↔branch ("two trees, diffed"), grouped by kind with deployment chips and the per-EP +added/-removed
    // reachable methods. Independent of effect classification — catches the obj→sql kind of migration the
    // effect-set diff collapses, and excludes false positives whose reach didn't actually move.
    private static void WriteAffected(
        TextWriter output,
        StoreProvenance baseProv,
        ImpactDiff diff,
        DeploymentMap deployments,
        Dictionary<(string, int), string> fqnSites,
        int max
    )
    {
        output.WriteLine();
        output.WriteLine($"Affected entry points (reachable tree changed) vs '{baseProv.ShortLabel}': {diff.AffectedEps.Count}");
        if (diff.AffectedEps.Count == 0)
        {
            output.WriteLine($"{Indent.L1}none — no entry point's reachable structure changed.");
            return;
        }

        foreach (var kindGroup in diff.AffectedEps.GroupBy(d => d.Kind, StringComparer.Ordinal).OrderByDescending(g => g.Count()))
        {
            output.WriteLine($"{Indent.L1}{kindGroup.Key}: {kindGroup.Count()}");
            foreach (var d in kindGroup.Take(max / 4 + 1))
            {
                // +added/-removed/~changed counted by DISTINCT STEM, so a 30-overload ctor swap reads as ~1,
                // not +30/-30. The `~` lines are signature changes (same stem on both sides); the +/- lines
                // are genuine reach gains/losses, labelled by ShortName (added/removed raw DocIDs, deduped to
                // their stem for display so an overload set doesn't print N near-identical lines).
                // The in-place suffix (Phase 2) flags an EP affected by a reachable method's BODY change with no
                // structural reach move — so an EP with empty +/-/~ but a changed constant still reads as why.
                var inPlaceNote = d.InPlaceCount > 0 ? $", in-place: {d.InPlaceCount} reached method body(ies) changed" : "";
                // Label the card with the FQN (round-trips into `rig tree`); fall back to the path route when the
                // EP site maps to no indexed method symbol. The diff still keys on (Kind, Route) internally.
                var label = ImpactEngine.FqnForCard(route: d.Route, filePath: d.FilePath, line: d.Line, idBySite: fqnSites);
                var route = $"{label}  (+{d.AddedStems.Count}/-{d.RemovedStems.Count}/~{d.ChangedStems.Count} reachable{inPlaceNote})";
                WriteEntryPointLine(output, deployments, route: route, filePath: d.FilePath, line: d.Line, requires: d.Requires);
                foreach (var s in d.AddedStems.Take(3))
                {
                    output.WriteLine($"{Indent.L3}+ {ReachNodeLabel(s)}");
                }

                foreach (var s in d.RemovedStems.Take(3))
                {
                    output.WriteLine($"{Indent.L3}- {ReachNodeLabel(s)}");
                }

                foreach (var s in d.ChangedStems.Take(3))
                {
                    output.WriteLine($"{Indent.L3}~ {ShortName(s)} (signature changed)");
                }

                foreach (var s in (d.InPlace ?? []).Take(3))
                {
                    output.WriteLine($"{Indent.L3}≈ {ShortName(s)} (body changed in place)");
                }
            }

            if (kindGroup.Count() > max / 4 + 1)
            {
                output.WriteLine($"{Indent.L3}… +{kindGroup.Count() - (max / 4 + 1)} more (raise --limit, or --format tsv for all)");
            }
        }
    }

    private static void EmitTsv(TextWriter output, ImpactView view, Dictionary<(string, int), string> fqnSites, int max, bool structural)
    {
        var diff = view.Diff;
        // One stream of typed rows for CI/tooling. First column is the row kind. Every row here is the
        // STORE-vs-STORE derived-facts diff: the EP set diff + the per-EP footprint/reach diff between the two
        // indexed commits. There is NO git working-tree diff and NO speculative reverse-reach blast radius — the
        // old `changed` / `effect_added` / `effect_removed` / `obs_*` rows and the `entrypoint` / `effect`
        // (reverse-reach) rows are gone; read the per-EP rows (ep_delta / ep_effect_*) for the same effects,
        // attributed and correct.
        //  affected_ep  <kind>  <route>  <fqn>  <cause>  <file>  <line>  <+addedStems>  <-removedStems>  <~changedStems>  <inplace>   (proven; <route> is the path-style diff key, <fqn> the dotted name `rig tree` matches — equals <route> when unresolved; <cause> is behavioral|record-shape|ctor-sig|in-place|other — behavioral = the EP is carried by the per-EP deltas (an effect, hazard, amplification-tier or guard change), the rest are structural-only; counts are DISTINCT param-free stems; inplace = reachable bodies changed)
        //  structural_summary  <total>  <behavioral>  <record-shape>  <ctor-sig>  <in-place>  <other>   (one row: the cause breakdown of the affected-EP set — behavioral counts the EPs the per-EP deltas carry, the rest are structural-only)
        //  ep_reach_+   <kind>  <route>  <symbolId>                            (newly in the EP's reach — raw method DocID, or an `R:`-prefixed field/property-access target, Phase 3)
        //  ep_reach_-   <kind>  <route>  <symbolId>                            (dropped from the EP's reach — raw method DocID, or an `R:`-prefixed field/property-access target, Phase 3)
        //  ep_reach_~   <kind>  <route>  <stem>                                (a reachable method whose SIGNATURE changed — param-free stem)
        //  ep_reach_inplace  <kind>  <route>  <symbolId>                       (a reachable method whose BODY changed in place — raw DocID, Phase 2)
        //  ep_added     <kind>  <route>                                        (an entry point present only on the HEAD store)
        //  ep_removed   <kind>  <route>                                        (an entry point present only on the BASE store)
        //  ep_delta     <kind>  <route>  <fqn>  <branchEffects>  <baseEffects>  <+added>  <-removed>  <~amplified>   (one per EP whose reachable-effect footprint changed: set membership and/or amplification; counts are effect KEYS)
        //  ep_effect_added    <kind>  <route>  <provider>  <operation>  <resource>  <enclosing>   (an effect KEY newly in the EP's footprint)
        //  ep_effect_removed  <kind>  <route>  <provider>  <operation>  <resource>  <enclosing>   (an effect KEY dropped from the EP's footprint)
        //  ep_effect_amplified  <kind>  <route>  <provider>  <operation>  <resource>  <enclosing>  <baseCount>  <branchCount>  <baseInLoop>  <branchInLoop>   (Feature 1: SAME key on both stores but produced MORE — branchCount>baseCount — and/or MOVED INTO A LOOP — branchInLoop && !baseInLoop. count = # distinct reachable effect-bearing producing nodes. A REVIEW flag, not a verdict: can't tell a hot-cache re-read from a real extra cold call.)
        //  ep_guard_delta  <kind>  <route>  <+guards>  <-guards>   (FR-1e: a lock/async_lock acquire/release ADDED (+, comma-joined provider:operation) or REMOVED (-) on a path whose branch reach STILL carries a shared_state mutation — the concurrency guard around an inherently-shared cell changed. A REVIEW flag covering both the lost-guard race and the guard-adding fix.)
        //  ep_hazard_added    <kind>  <route>  <type>  <confidence>  <cell>  <enclosing>   (a hazard finding — race_window / lazy_init_race / n_plus_1 / unserializable_payload, see HazardKinds — newly present on the EP's reach: a refactor opened it. cell = the observation Context, enclosing = the param-free producing method. A REVIEW flag, not a verdict.)
        //  ep_hazard_removed  <kind>  <route>  <type>  <confidence>  <cell>  <enclosing>   (a hazard finding that DROPPED from the EP's reach base->head: a fix closed it. Same columns as ep_hazard_added.)
        //  ep_amplification_added    <kind>  <route>  <provider>  <operation>  <sites>   (the AMPLIFICATION tier, looped_effect: this provider:operation is now reached INSIDE an iteration context and was not before — i.e. a loop was introduced around an existing effect, which leaves the effect SET unchanged while the cost goes ×N, so no ep_effect_* row can show it. TERSE: one row per (EP x provider:operation), <sites> = reachable producing sites, never one row per site. Scoped to the rules-declared network-crossing providers (observations.amplification); suppressed by --no-amplification. A REVIEW flag, not a verdict.)
        //  ep_amplification_removed  <kind>  <route>  <provider>  <operation>  <sites>   (the converse: the provider:operation is no longer reached inside an iteration context — a loop was removed / the call hoisted. Same columns.)

        //  guard_condition_delta  <verdict>  <caller>  <callee>  <effects>  <eps>  <baseCondition>  <headCondition>
        //     (A call edge whose CONTROL-DEPENDENCE CONDITION moved while the call and its effects stayed put —
        //     the predicate-only change class ep_effect_* is structurally blind to, and the reason a MedDBase
        //     audit suppression passed --expect-no-effect-change. <verdict> is narrowed (base conjuncts ⊂ head:
        //     fires on strictly FEWER paths — the review headline, and what --expect-no-guard-narrowing gates on),
        //     widened (⊃: fires on MORE), or changed (incomparable — deliberately not sub-classified, since
        //     without a solver we cannot say which way the truth set moved). <effects> = comma-joined
        //     provider:operation reachable FROM the callee, i.e. what this condition gates (intrinsics excluded;
        //     --only/--exclude filter these rows too). <eps> = how many entry points reach the caller. Conditions
        //     are comment- and whitespace-normalized so they are single-line and greppable. Classification is
        //     SYNTACTIC conjunct containment, an over-approximation: it recognises "AND another clause onto the
        //     existing guard" and falls back to `changed` otherwise.)
        //  impact_summary  eps=<n>  behavioral_eps=<n>  effect_added=<n>  effect_removed=<n>  effect_amplified=<n>  guard_delta=<n>  guard_narrowed=<n>  guard_widened=<n>  guard_changed=<n>  intrinsic_hidden=<n>
        //     (behavioral_eps counts the EPs whose reachable EFFECT set changed — the SAME number the human
        //     header prints, NOT the ep_delta row count: an EP retained for a hazard / amplification-tier /
        //     guard delta alone has an ep_delta row and is deliberately not counted here.
        //     ALWAYS FIRST, and never capped by --limit. A reviewer or agent that truncates the stream still
        //     reads the true totals: the original failure this fixes was a `Select-Object -First 300` capture
        //     whose 300 lines were all ep_reach_+ rows for ONE entry point, so the diff read as "no behavioural
        //     change" while 190 EPs newly wrote a table. intrinsic_hidden discloses what the default alloc/throw
        //     filter withheld — and, because the --expect-no-effect-change gate counts the FILTERED set, it is
        //     also the audit trail for why a gate that would once have tripped now passes.)
        var guardConditions = diff.GuardConditionsOrEmpty;
        output.WriteLine(
            "impact_summary\t"
                + $"eps={diff.AffectedEps.Count}\t"
                + $"behavioral_eps={view.BehavioralEpCount}\t"
                + $"effect_added={diff.PerEp.Sum(d => d.Added.Count)}\t"
                + $"effect_removed={diff.PerEp.Sum(d => d.Removed.Count)}\t"
                + $"effect_amplified={diff.PerEp.Sum(d => d.Amplified.Count)}\t"
                + $"guard_delta={diff.PerEp.Count(ImpactEngine.HasGuardDeltaOnSharedMutation)}\t"
                + $"guard_narrowed={guardConditions.Count(g => g.Verdict == GuardVerdict.Narrowed)}\t"
                + $"guard_widened={guardConditions.Count(g => g.Verdict == GuardVerdict.Widened)}\t"
                + $"guard_changed={guardConditions.Count(g => g.Verdict == GuardVerdict.Changed)}\t"
                + $"intrinsic_hidden={view.HiddenIntrinsic}"
        );

        // GUARD-CONDITION DELTA: one row per call edge whose gating predicate moved. Emitted right after the
        // summary and BEFORE the bulky per-EP rows, because a NARROWED row is the highest-signal line in the
        // whole stream — it is the only signal that survives a predicate-only change — and a truncated capture
        // must not lose it.
        foreach (var g in guardConditions)
        {
            output.WriteLine(
                $"guard_condition_delta\t{g.Verdict.ToString().ToLowerInvariant()}\t{GuardEndpointLabel(g.Caller)}\t{GuardEndpointLabel(g.Callee)}\t"
                    + $"{string.Join(',', g.Effects)}\t{g.EpCount}\t{g.BaseCondition}\t{g.HeadCondition}"
            );
        }

        // Cause per EP: behavioral when the selected per-EP deltas carry it (it's in PerEp), else the structural
        // sub-cause — read off the view's structural-only partition, which classified it once.
        var causeByEp = new Dictionary<(string Kind, string Route), StructuralCause>();
        foreach (var s in view.StructuralOnly)
        {
            causeByEp[(s.Ep.Kind, s.Ep.Route)] = s.Cause; // indexer, not ToDictionary: a decoded cache blob must never throw here
        }

        string CauseTag(EpReachDelta e) =>
            !causeByEp.TryGetValue((e.Kind, e.Route), out var cause)
                ? "behavioral"
                : cause switch
                {
                    StructuralCause.RecordShape => "record-shape",
                    StructuralCause.CtorSig => "ctor-sig",
                    StructuralCause.InPlace => "in-place",
                    _ => "other",
                };

        // structural_summary: the cause breakdown of the WHOLE affected set (not capped by --limit) so tooling
        // gets the true totals even when the per-EP rows below are truncated.
        var causeCounts = diff
            .AffectedEps.GroupBy(CauseTag, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        int CC(string k) => causeCounts.GetValueOrDefault(k);
        output.WriteLine(
            $"structural_summary\t{diff.AffectedEps.Count}\t{CC("behavioral")}\t{CC("record-shape")}\t{CC("ctor-sig")}\t{CC("in-place")}\t{CC("other")}"
        );

        // ORDER IS THE TRUNCATION CONTRACT: summaries, then the BEHAVIOURAL deltas, then the bulky structural
        // roster. A capped or head-ed read therefore loses the least-important rows first. Previously the
        // per-EP effect deltas were emitted LAST, behind up to 49k ep_reach_* rows — which is precisely how a
        // 300-line capture of a real MR ended up containing zero effect deltas and reading as "no change".
        EmitEpDiffTsv(output, diff.Ep);
        EmitPerEpTsv(output, diff.PerEp, fqnSites);

        foreach (var e in diff.AffectedEps.Take(max))
        {
            var fqn = ImpactEngine.FqnForCard(route: e.Route, filePath: e.FilePath, line: e.Line, idBySite: fqnSites);
            output.WriteLine(
                $"affected_ep\t{e.Kind}\t{e.Route}\t{fqn}\t{CauseTag(e)}\t{e.FilePath}\t{e.Line}\t+{e.AddedStems.Count}\t-{e.RemovedStems.Count}\t~{e.ChangedStems.Count}\t{e.InPlaceCount}"
            );

            // The PER-SYMBOL reach lists are the structural layer and are ~86% of all output (49,328 of 57,624
            // rows on a real 33-file MR) — overwhelmingly data-shape ripple, since a record gaining a field
            // relights every reaching EP. `affected_ep` above already carries the AGGREGATE counts, so the
            // default keeps the roster and drops the enumeration; --structural restores it. (Before this,
            // --structural affected only the human renderer and was a no-op for --format tsv.)
            if (!structural)
            {
                continue;
            }

            foreach (var s in e.Added)
            {
                output.WriteLine($"ep_reach_+\t{e.Kind}\t{e.Route}\t{s}");
            }

            foreach (var s in e.Removed)
            {
                output.WriteLine($"ep_reach_-\t{e.Kind}\t{e.Route}\t{s}");
            }

            foreach (var s in e.ChangedStems)
            {
                output.WriteLine($"ep_reach_~\t{e.Kind}\t{e.Route}\t{s}");
            }

            foreach (var s in e.InPlace ?? [])
            {
                output.WriteLine($"ep_reach_inplace\t{e.Kind}\t{e.Route}\t{s}");
            }
        }
    }
}
