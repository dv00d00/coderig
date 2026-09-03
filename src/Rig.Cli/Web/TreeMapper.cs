using Rig.Analysis.Rules;
using Rig.Cli.Rendering;
using Rig.Cli.Services;
using Rig.Domain.Data;
using static Rig.Cli.Rendering.SymbolNameFormatter;

namespace Rig.Cli.Web;

// Maps a TreeQueryService result (TraceNode forest + effects + locations + emoji map) into the flat JSON DTO
// the SPA consumes. Pure projection — no engine logic. Effects are aggregated per enclosing method into
// distinct provider:operation with a call-site count and the repo's glyph, mirroring how `rig tree` annotates.
//
// Folds opaque/collapse seams the same way the pretty and llm renderers do (this consumer previously walked
// every child regardless, so the SPA received the full unfolded tree). A folded node is emitted as a labelled
// leaf with empty Children; a COLLAPSE node also carries the union of the effects it hides + the hidden count,
// so the SPA can render the seam summary without the subtree. Pass FactRenderRules.Empty (the ?raw= opt-out)
// to disable folding and get the exact unfolded tree.
internal static class TreeMapper
{
    public static TreeResponseDto ToResponse(
        string from,
        IReadOnlyList<TraceNode> roots,
        IReadOnlyList<DerivedEffect> effects,
        IReadOnlyDictionary<string, TreeQueryService.SymbolLocation> locations,
        IReadOnlyDictionary<string, string> emoji,
        FactRenderRules renderRules,
        IReadOnlySet<string>? compileErrorFiles = null
    )
    {
        var effectsByMethod = BuildEffectIndex(effects, emoji, compileErrorFiles);
        var effectRowsByMethod = effects
            .Where(e => e.EnclosingSymbolId is not null)
            .GroupBy(e => e.EnclosingSymbolId!, StringComparer.Ordinal)
            .ToDictionary(
                keySelector: g => g.Key,
                elementSelector: g => (IReadOnlyList<DerivedEffect>)g.ToList(),
                comparer: StringComparer.Ordinal
            );
        // Raw "provider:operation" multiset per enclosing method — the substrate for a collapsed seam's
        // hidden-effect union (same keying the llm renderer uses).
        var rawByMethod = effects
            .Where(e => e.EnclosingSymbolId is not null)
            .GroupBy(e => e.EnclosingSymbolId!, StringComparer.Ordinal)
            .ToDictionary(
                keySelector: g => g.Key,
                elementSelector: g => g.Select(e => $"{e.Provider}:{e.Operation}").ToList(),
                comparer: StringComparer.Ordinal
            );

        var dtoRoots = roots
            .Select(r =>
                MapNode(r, effectsByMethod, effectRowsByMethod, rawByMethod, locations, emoji, renderRules, compileErrorFiles, isRoot: true)
            )
            .ToList();
        return new TreeResponseDto(From: from, Matched: dtoRoots.Count > 0, Roots: dtoRoots);
    }

    private static TreeNodeDto MapNode(
        TraceNode node,
        IReadOnlyDictionary<string, IReadOnlyList<EffectDto>> effectsByMethod,
        IReadOnlyDictionary<string, IReadOnlyList<DerivedEffect>> effectRowsByMethod,
        IReadOnlyDictionary<string, List<string>> rawByMethod,
        IReadOnlyDictionary<string, TreeQueryService.SymbolLocation> locations,
        IReadOnlyDictionary<string, string> emoji,
        FactRenderRules renderRules,
        IReadOnlySet<string>? compileErrorFiles,
        bool isRoot
    )
    {
        var loc = locations.GetValueOrDefault(node.SymbolId);
        var ownEffects = effectsByMethod.GetValueOrDefault(node.SymbolId, []);

        // Opaque/collapse fold: draw a labelled leaf, hide the subtree. Roots never fold (mirrors the renderers).
        var fold = TreeFoldSupport.Decide(renderRules, node.SymbolId, isRoot: isRoot);
        if (fold.Kind != TreeFoldSupport.FoldKind.None)
        {
            var isCollapse = fold.Kind == TreeFoldSupport.FoldKind.Collapse;
            var hidden = 0;
            var seamEffects = ownEffects;
            if (isCollapse)
            {
                // Union of what the folded branch touches: this node's own raw effects + the subtree's, so the
                // seam leaf reports its reach (e.g. one "llblgen:fetch ×N" chip) without shipping the subtree.
                var (_, hiddenCount) = TreeFoldSupport.SummarizeHidden(node.Children, rawByMethod);
                hidden = hiddenCount;
                seamEffects = AggregateEffects(CollectSubtreeEffects(node, effectRowsByMethod), emoji, compileErrorFiles);
            }

            return ToDto(
                node,
                loc,
                effects: seamEffects,
                children: [],
                foldKind: isCollapse ? "collapse" : "opaque",
                foldLabel: fold.Label,
                foldHidden: hidden,
                bindingHealth: CompilationFilePath.Contains(compileErrorFiles, loc?.File) ? "compile_error" : "ok"
            );
        }

        return ToDto(
            node,
            loc,
            effects: ownEffects,
            children: node.Children.Select(c =>
                    MapNode(
                        c,
                        effectsByMethod,
                        effectRowsByMethod,
                        rawByMethod,
                        locations,
                        emoji,
                        renderRules,
                        compileErrorFiles,
                        isRoot: false
                    )
                )
                .ToList(),
            foldKind: null,
            foldLabel: null,
            foldHidden: 0,
            bindingHealth: CompilationFilePath.Contains(compileErrorFiles, loc?.File) ? "compile_error" : "ok"
        );
    }

    private static TreeNodeDto ToDto(
        TraceNode node,
        TreeQueryService.SymbolLocation? loc,
        IReadOnlyList<EffectDto> effects,
        IReadOnlyList<TreeNodeDto> children,
        string? foldKind,
        string? foldLabel,
        int foldHidden,
        string bindingHealth
    ) =>
        new(
            Id: node.SymbolId,
            Name: ShortName(node.SymbolId),
            Signature: ShortSignature(node.SymbolId),
            // Full (untruncated) predicate — the UI ellipsises on render, keeping full text in a tooltip.
            Guards: Rig.Cli.Rendering.TreeRenderer.ShortGuards(
                encoded: node.EnclosingGuards,
                loopDetail: node.LoopDetail,
                maxLength: int.MaxValue
            ),
            EdgeKind: node.EdgeKind,
            Fanout: node.Fanout,
            CallSites: node.CallSites,
            Truncated: node.Truncated,
            TruncationCause: node.TruncationCause == Rig.Domain.Data.TruncationCause.None ? null : node.TruncationCause.ToString(),
            DispatchBasis: node.DispatchBasis,
            File: loc?.File,
            Line: loc?.Line ?? 0,
            Effects: effects.ToArray(),
            Children: children,
            FoldKind: foldKind,
            FoldLabel: foldLabel,
            FoldHidden: foldHidden,
            Loop: node.LoopKind is null ? null : (string.IsNullOrEmpty(node.LoopDetail) ? node.LoopKind : node.LoopDetail),
            BindingHealth: bindingHealth
        );

    private static IReadOnlyList<DerivedEffect> CollectSubtreeEffects(
        TraceNode node,
        IReadOnlyDictionary<string, IReadOnlyList<DerivedEffect>> effectsByMethod
    )
    {
        var result = new List<DerivedEffect>();

        void Visit(TraceNode current)
        {
            if (effectsByMethod.TryGetValue(current.SymbolId, out var own))
            {
                result.AddRange(own);
            }

            if (!current.Truncated)
            {
                foreach (var child in current.Children)
                {
                    Visit(child);
                }
            }
        }

        Visit(node);
        return result;
    }

    private static IReadOnlyList<EffectDto> AggregateEffects(
        IEnumerable<DerivedEffect> effects,
        IReadOnlyDictionary<string, string> emoji,
        IReadOnlySet<string>? compileErrorFiles
    ) =>
        effects
            .GroupBy(effect => (effect.Provider, effect.Operation))
            .Select(group => new EffectDto(
                Provider: group.Key.Provider,
                Operation: group.Key.Operation,
                Glyph: EmojiLookup.For(emoji, provider: group.Key.Provider, operation: group.Key.Operation),
                Sites: group.Count(),
                BindingHealth: group.Any(effect => CompilationFilePath.Contains(compileErrorFiles, effect.FilePath))
                    ? "compile_error"
                    : "ok"
            ))
            .OrderBy(effect => effect.Provider, StringComparer.Ordinal)
            .ThenBy(effect => effect.Operation, StringComparer.Ordinal)
            .ToList();

    // enclosing method DocID -> its distinct (provider, operation) effects with site counts + glyph.
    private static IReadOnlyDictionary<string, IReadOnlyList<EffectDto>> BuildEffectIndex(
        IReadOnlyList<DerivedEffect> effects,
        IReadOnlyDictionary<string, string> emoji,
        IReadOnlySet<string>? compileErrorFiles
    ) =>
        effects
            .Where(e => e.EnclosingSymbolId is not null)
            .GroupBy(e => e.EnclosingSymbolId!, StringComparer.Ordinal)
            .ToDictionary(
                keySelector: g => g.Key,
                elementSelector: IReadOnlyList<EffectDto> (g) => AggregateEffects(g, emoji, compileErrorFiles),
                comparer: StringComparer.Ordinal
            );
}
