using Rig.Analysis.Rules;
using Rig.Cli.CommandLine;
using Rig.Cli.Commands;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using static Rig.Cli.Caching.QueryCacheKeys;
using static Rig.Cli.Effects.EffectDerivation;
using static Rig.Cli.Graph.TraversalGraphLoader;

namespace Rig.Cli.Services;

// Per-method hazard marks for the tree rooted at `fromPattern` — the SAME whole-store hazard set
// `rig tree --view hazards` surfaces: the effect-attached findings (race_window / lazy_init_race /
// dual_write / n_plus_1 / …) PLUS the graph-tier ones (cache_coherence / event_cycle / static_init_capture),
// filtered to the tree's reachable methods. Hazards are a whole-store fact (EP-independent), cached by
// (store + rules); this just filters that set to the tree. The web overlays these marks on tree nodes.
public static class HazardsService
{
    // One mark per (method, hazard type): the worst confidence the method carries for that type, and how many
    // sites fired. The client groups these by method id and paints a ⚠ on the node.
    public sealed record HazardMark(string MethodId, string Type, string Confidence, int Sites);

    public static async Task<IReadOnlyList<HazardMark>> ForTreeAsync(
        string workingDirectory,
        string fromPattern,
        string? storeRef = null,
        bool gate = true,
        IReadOnlyList<string>? extraRules = null,
        // AMPLIFICATION tier (looped_effect on an in-scope provider:operation — see HazardKinds): ON by default,
        // like the CLI's `--no-amplification` opt-out, and exposed as `?amplification=false` on /api/hazards.
        // Amplification marks come back in the SAME HazardMark shape, so the tree overlay renders them with no
        // client change — the client only labels them (their Type is looped_effect, which is what distinguishes
        // them from the hazard set: HazardKinds.IsHazard(looped_effect) is false by design).
        bool amplification = true,
        // TIER 3 (cross-method amplification, HazardKinds.CrossMethodAmplification): anchor-grain marks on the
        // CALLER method of each looped call site whose closure reaches a rules-gated effect (network calls by
        // default — the gate is data, not "reads"). ON by default when the rules declare the section;
        // `?crossMethod=false` opts out.
        bool crossMethod = true
    )
    {
        var rules = RuleSetLoader.Load(workingDirectory: workingDirectory, extraRules: extraRules ?? [], loadedPaths: out var loadedPaths);
        var ws = new WorkspaceLocation(WorkingDirectory: workingDirectory, StoreRef: storeRef);
        await using var context = await OpenReadContextGatedAsync(ws);

        // Build the tree (sync, full) to get the set of reachable methods to filter hazards to.
        var computation = await TreeQueryService.ComputeAsync(
            context: context,
            rules: rules,
            shaped: rules,
            fromPattern: fromPattern,
            maxDepth: int.MaxValue,
            maxNodes: FactPathFinder.DefaultTreeNodeBudget,
            mode: CommonOptions.Mode(async: false),
            raw: false
        );
        var treeMethods = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in computation.Roots)
        {
            CollectMethods(root, treeMethods);
        }

        if (treeMethods.Count == 0)
        {
            return [];
        }

        // Store-correct cache keys (mirror TreeCommand/DeriveCommand), so `--store` and rule edits key right.
        var rigDir = StoreLayout.ResolveReadStoreDir(ws);
        var storeKey = StoreKey(Path.Combine(rigDir, StoreLayout.DbFileName));
        var rulesHash = RulesFingerprint.ComputeFromPaths(loadedPaths);

        // Effect-attached hazards: the whole-store hazard-augmented effect set (cached, shared with `derive`),
        // filtered to the tree's methods, then flattened to findings.
        var hazardEffects = await LoadOrDeriveHazardEffectsAsync(
            context: context,
            rigDirectory: rigDir,
            storeKey: storeKey,
            rulesHash: rulesHash,
            rules: rules,
            useCache: true,
            epData: computation.EpData,
            gate: gate
        );
        var filteredEffects = hazardEffects
            .Where(e => e.EnclosingSymbolId is not null && treeMethods.Contains(e.EnclosingSymbolId))
            .ToList();
        var effectFindings = DeriveCommand.HazardFindings(filteredEffects).Where(f => treeMethods.Contains(f.Enclosing));

        // Graph-tier hazards (cache_coherence / event_cycle / static_init_capture) — not effect-attached.
        var graphFindings = (
            await LoadOrDeriveGraphHazardFindingsAsync(
                context: context,
                rigDirectory: rigDir,
                storeKey: storeKey,
                rulesHash: rulesHash,
                rules: rules,
                useCache: true
            )
        ).Where(f => treeMethods.Contains(f.Enclosing));

        // Amplification findings over the SAME tree-filtered effects — a second SOURCE folded into the same mark
        // stream (exactly as graphFindings is), not a separate endpoint: one fetch, one overlay.
        var amplificationFindings = amplification
            ? DeriveCommand
                .AmplificationFindings(filteredEffects, rules.Observations.AmplificationOrEmpty)
                .Where(f => treeMethods.Contains(f.Enclosing))
            : [];

        var marks = effectFindings
            .Concat(graphFindings)
            .Concat(amplificationFindings)
            .GroupBy(f => (f.Enclosing, f.Type))
            .Select(g => new HazardMark(
                MethodId: g.Key.Enclosing,
                Type: g.Key.Type,
                Confidence: g.OrderBy(f => ConfidenceRank(f.Confidence)).First().Confidence,
                Sites: g.Count()
            ))
            .ToList();

        // Tier-3 cross-method amplification: anchor-grain findings marked on the CALLER method (where a human
        // would fix the loop). A third source folded into the same mark stream; the client only labels the type.
        if (crossMethod && rules.CrossMethodAmplification is { } xm)
        {
            var anchors = Effects.CrossMethodAmplificationDataset.AnchorFindings(
                Effects.CrossMethodAmplificationDataset.Pairs(
                    // PoC warm cache: both loads are whole-store and (store, rules)-pure, and this is the
                    // ONE finding tier with no disk cache at all — so on a resident `rig serve` these two
                    // lines were the entire ~30-60s /api/hazards cost, paid again on every request.
                    invocations: await Caching.WarmStore.InvocationsAsync(context: context, storeDir: rigDir),
                    graph: await Caching.WarmStore.GraphAsync(context: context, rules: rules, storeDir: rigDir, rulesHash: rulesHash),
                    effects: hazardEffects,
                    observationRules: rules.Observations,
                    rule: xm
                )
            );
            marks.AddRange(
                anchors
                    .Where(a => treeMethods.Contains(a.Caller))
                    .GroupBy(a => a.Caller)
                    .Select(g => new HazardMark(
                        MethodId: g.Key,
                        Type: HazardKinds.CrossMethodAmplification,
                        Confidence: g.OrderBy(a => ConfidenceRank(a.Confidence)).First().Confidence,
                        Sites: g.Count()
                    ))
            );
        }

        return marks;
    }

    private static void CollectMethods(TraceNode node, HashSet<string> into)
    {
        into.Add(node.SymbolId);
        foreach (var child in node.Children)
        {
            CollectMethods(child, into);
        }
    }

    // high < medium < low so OrderBy(...).First() picks the WORST tier a method carries; unknown sorts last.
    private static int ConfidenceRank(string confidence) =>
        confidence switch
        {
            "high" => 0,
            "medium" => 1,
            "low" => 2,
            _ => 3,
        };
}
