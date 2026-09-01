using Rig.Domain.Data;

namespace Rig.Domain.Functions;

public sealed record FileEffectSelector
{
    public FileEffectSelector(string family, IReadOnlyList<EffectPredicate> predicates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentNullException.ThrowIfNull(predicates);
        if (predicates.Count == 0)
        {
            throw new ArgumentException("At least one effect predicate is required.", nameof(predicates));
        }

        if (predicates.Any(predicate => predicate is null || string.IsNullOrWhiteSpace(predicate.Provider)))
        {
            throw new ArgumentException("Every effect predicate must name a provider.", nameof(predicates));
        }

        Family = family;
        Predicates = Array.AsReadOnly(predicates.ToArray());
    }

    public string Family { get; }

    public IReadOnlyList<EffectPredicate> Predicates { get; }
}

// ViaDispatchOnly = this reach exists only through virtual/interface dispatch: whole-program
// devirtualization says the call CAN land on an effectful override, but no real call path proves it does. rig
// discloses that everywhere else (`reaches`' fan-out bucket says "NOT a real call"), so the file model carries
// it too rather than presenting an over-approximation as a plain fact. A depth-0 aggregate is always real — a
// seed performs the effect itself.
//
// Looped = the AMPLIFICATION tier (HazardKinds.Amplification / the `looped_effect` observation): this effect
// runs once per iteration of an enclosing loop, so the honest reading of the row is "N times per invocation",
// not "once". It is carried ONLY on depth-0 aggregates, and that is a deliberate limit rather than an
// oversight: `looped_effect` is a LEXICAL fact about the effect's own body, so it is knowable exactly where
// the effect is. Propagating it up the reverse closure would answer a different and much weaker question
// ("something looped exists somewhere below") which the tier-3 cross-method anchor answers properly, with a
// witness and a confidence. A distant aggregate therefore never claims repetition.
public sealed record FileEffectAggregate(string Family, int NearestDepth, bool ViaDispatchOnly = false, bool Looped = false);

public sealed record FileEffectMethod(string SymbolId, IReadOnlyList<FileEffectAggregate> Effects);

// Line is the 1-based source line of the CALL, carried straight from the CallEdge. It is what lets a
// consumer anchor a mark on the invocation it belongs to instead of re-resolving every invocation in the
// file, and it separates two calls to the same target from one body. There is no column: extraction never
// mined one (ReferenceFact stores Line only), so two calls to the same target on ONE line still collapse.
// TargetSymbolId is EMPTY (never null) for a row projected from an effect observed at a call into external
// library code: there is no in-solution callee, hence no node and no DocID to name. Consumers use it only to
// separate two projected targets on one line, so an empty value is a well-formed "the effect is right here".
public sealed record FileEffectCallSite(
    string EnclosingSymbolId,
    string TargetSymbolId,
    int Line,
    IReadOnlyList<FileEffectAggregate> Effects
);

// EffectSelectors are the families this model was projected for — ALL of them, whether or not the file has a
// row in each. A consumer reading "no cache row here" needs to know cache was actually asked about.
public sealed record FileEffectReadModel(
    string FilePath,
    IReadOnlyList<string> EffectSelectors,
    IReadOnlyList<FileEffectMethod> Methods,
    IReadOnlyList<FileEffectCallSite> CallSites
);

/// <summary>Projects every effect family's multi-source reverse closure into immutable, Rider-ready per-file read models.</summary>
public sealed class FileEffectReadModelIndex
{
    private static readonly StringComparer FilePathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly IReadOnlyDictionary<string, FileEffectReadModel> _files;

    private FileEffectReadModelIndex(IReadOnlyDictionary<string, FileEffectReadModel> files)
    {
        _files = files;
    }

    // Single-family entry point, kept because most callers and every fixture ask about one family.
    public static FileEffectReadModelIndex Build(
        FactGraphData graph,
        IEnumerable<SymbolFact> symbols,
        IEnumerable<DerivedEffect> effects,
        FileEffectSelector selector,
        IEnumerable<string>? indexedFilePaths = null
    ) => Build(graph, symbols, effects, [selector], indexedFilePaths);

    // Every family in ONE build. What is shared, and why that is the whole point: the canonical-method
    // projection and its per-file grouping run over every symbol in the solution (442k on MedDBase), and the
    // reverse closure walks the whole graph. Done per family they cost k times as much - measured at 15.6s for
    // TWO families on MedDBase, which is also why the first request used to time out in the Rider client.
    // Here the grouping happens once and the k closures come from one labelled walk.
    public static FileEffectReadModelIndex Build(
        FactGraphData graph,
        IEnumerable<SymbolFact> symbols,
        IEnumerable<DerivedEffect> effects,
        IReadOnlyList<FileEffectSelector> selectors,
        IEnumerable<string>? indexedFilePaths = null
    )
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(selectors);
        if (selectors.Count == 0)
        {
            throw new ArgumentException("At least one effect selector is required.", nameof(selectors));
        }

        var symbolRows = symbols.ToArray();
        var effectRows = effects as IReadOnlyList<DerivedEffect> ?? effects.ToArray();
        var families = Array.AsReadOnly(selectors.Select(selector => selector.Family).ToArray());
        var canonicalMethods = SymbolFactProjections.SelectCanonicalMethodFacts(symbolRows);
        var declaredMethodIds = canonicalMethods.Select(symbol => symbol.SymbolId).ToHashSet(StringComparer.Ordinal);
        var declaredOwnerByLambda = ResolveDeclaredLambdaOwners(graph, symbolRows, declaredMethodIds);
        var methodsByFile = canonicalMethods
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol.FilePath))
            .GroupBy(symbol => symbol.FilePath, FilePathComparer)
            .ToDictionary(group => group.Key, group => group.ToArray(), FilePathComparer);

        var selectedPerFamily = selectors
            .Select(selector =>
                effectRows
                    .Where(effect => selector.Predicates.Any(predicate => Matches(effect, predicate)))
                    .Select(effect => FoldLambdaOwner(effect, declaredOwnerByLambda))
                    .ToArray()
            )
            .ToArray();
        var ownersPerFamily = selectedPerFamily
            .Select(selected =>
                (IReadOnlyCollection<string>)
                    selected
                        .Select(effect => effect.EnclosingSymbolId)
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Cast<string>()
                        .Distinct(StringComparer.Ordinal)
                        .ToArray()
            )
            .ToArray();

        // One traversal for the union of every family's exact predicates. Projecting a file below is therefore
        // only a dictionary join, never a forward graph walk per method.
        var reachedPerFamily = FactPathFinder
            .ReachedByLabelledSeeds(
                graph,
                ownersPerFamily,
                maxDepth: int.MaxValue,
                maxNodes: int.MaxValue,
                narrowDispatch: true,
                mode: FactPathFinder.TraversalMode.SyncCut
            )
            .Select(CollapseInstantiations)
            .ToArray();

        // The SAME closure over real calls only. Its purpose is not precision but DISCLOSURE: a node present
        // here has a call path a reader can follow; a node only in the closure above is reachable solely by
        // devirtualization, and every surface must be able to say so (`Aggregate` below decides the basis).
        // One extra reverse walk, no extra graph load or derivation — the expensive parts are already paid.
        var realPerFamily = FactPathFinder
            .ReachedByLabelledSeeds(
                graph,
                ownersPerFamily,
                maxDepth: int.MaxValue,
                maxNodes: int.MaxValue,
                narrowDispatch: true,
                mode: FactPathFinder.TraversalMode.SyncCut,
                includeDispatch: false
            )
            .Select(CollapseInstantiations)
            .ToArray();
        var reachedByAnyFamily = reachedPerFamily.SelectMany(reached => reached.Keys).ToHashSet(StringComparer.Ordinal);

        var knownFiles = (indexedFilePaths ?? symbolRows.Select(symbol => symbol.FilePath))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(FilePathComparer)
            .OrderBy(path => path, FilePathComparer);
        var invocationEdgesByFile = graph
            .CallEdges.Where(edge =>
                string.Equals(edge.Kind, "invocation", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(edge.FilePath)
            )
            .GroupBy(edge => edge.FilePath, FilePathComparer)
            .ToDictionary(group => group.Key, group => group.ToArray(), FilePathComparer);
        var selectedByFilePerFamily = selectedPerFamily
            .Select(selected =>
                selected
                    .Where(effect => !string.IsNullOrWhiteSpace(effect.FilePath))
                    .GroupBy(effect => effect.FilePath, FilePathComparer)
                    .ToDictionary(group => group.Key, group => group.ToArray(), FilePathComparer)
            )
            .ToArray();

        var files = new Dictionary<string, FileEffectReadModel>(FilePathComparer);
        foreach (var filePath in knownFiles)
        {
            var fileMethods = methodsByFile.GetValueOrDefault(filePath) ?? [];
            var fileMethodIds = fileMethods.Select(method => method.SymbolId).ToHashSet(StringComparer.Ordinal);
            var fileEdges = invocationEdgesByFile.GetValueOrDefault(filePath) ?? [];

            var methodRows = new List<(string SymbolId, FileEffectAggregate Effect)>();
            var callSiteRows = new List<(CallSiteKey Key, FileEffectAggregate Effect)>();
            for (var family = 0; family < selectors.Count; family++)
            {
                var reached = reachedPerFamily[family];
                var real = realPerFamily[family];
                var currentFamily = families[family];

                // The AMPLIFICATION tier, keyed exactly where it is knowable. Two grains because two rows ask
                // different questions: a LINE asks "is the effect on this line looped" (owner + line), a
                // METHOD asks "does this body perform a looped effect anywhere" (owner alone).
                var fileFamilyEffects = selectedByFilePerFamily[family].GetValueOrDefault(filePath) ?? [];
                var loopedSites = fileFamilyEffects
                    .Where(effect => IsLooped(effect) && effect.EnclosingSymbolId is not null)
                    .Select(effect => new SourceSite(effect.EnclosingSymbolId!, effect.Line))
                    .ToHashSet();
                var loopedOwners = fileFamilyEffects
                    .Where(effect => IsLooped(effect) && effect.EnclosingSymbolId is not null)
                    .Select(effect => effect.EnclosingSymbolId!)
                    .ToHashSet(StringComparer.Ordinal);

                // The REAL depth wins whenever a real path exists, even when the dispatch closure found a
                // shorter one: a number that claims to be real must come from the real walk. Only a node the
                // real walk never reached is rendered as dispatch-derived.
                FileEffectAggregate? Aggregate(string node)
                {
                    if (real.TryGetValue(node, out var realDepth))
                    {
                        return new FileEffectAggregate(currentFamily, realDepth);
                    }

                    return reached.TryGetValue(node, out var anyDepth)
                        ? new FileEffectAggregate(currentFamily, anyDepth, ViaDispatchOnly: true)
                        : null;
                }

                foreach (var symbol in fileMethods)
                {
                    if (Aggregate(symbol.SymbolId) is { } methodEffect)
                    {
                        // A method row is looped when the method's OWN body performs a looped effect of this
                        // family — i.e. only at depth 0. At any distance the loop (if there is one) belongs to
                        // another body, which is tier 3's question, not this row's.
                        methodRows.Add(
                            (
                                symbol.SymbolId,
                                methodEffect with
                                {
                                    Looped = methodEffect.NearestDepth == 0 && loopedOwners.Contains(symbol.SymbolId),
                                }
                            )
                        );
                    }
                }

                var fileEffects = fileFamilyEffects;
                var sites = BuildCallSiteKeys(fileEdges, fileEffects, reached, reachedByAnyFamily, fileMethodIds, declaredOwnerByLambda);
                foreach (var site in sites)
                {
                    // No aggregate at all = the effect is at this very site with no in-solution callee to
                    // name (an external call), which is real by construction.
                    var siteEffect = Aggregate(site.TargetSymbolId) ?? new FileEffectAggregate(currentFamily, 0);

                    // A badge's BASIS describes the route from the ENCLOSING method, not the callee's own
                    // luck. A callee may sit on a real call path while the only way into it from here is a
                    // dispatch guess — then this line is a guess too, and saying otherwise would print a
                    // real-looking line badge under a dispatch-only method badge (observed on
                    // PathwayTreeNode.cs:45, `cache:18` beneath `get_Task cache:19?`). Every effect OWNER is
                    // seeded into both closures, so a body that performs the effect itself is always real here.
                    var enclosingHasRealPath = real.ContainsKey(site.EnclosingSymbolId);
                    if (!enclosingHasRealPath && !siteEffect.ViaDispatchOnly)
                    {
                        siteEffect = siteEffect with { ViaDispatchOnly = true };
                    }

                    // Repetition is a fact about THIS line: the effect sits at this site and this site is
                    // inside a loop. Deliberately keyed on (enclosing method, line) rather than on the callee
                    // — a callee that happens to loop internally does not make the caller's line repeat, and
                    // saying it did would put the mark on the wrong body.
                    if (siteEffect.NearestDepth == 0 && loopedSites.Contains(new SourceSite(site.EnclosingSymbolId, site.Line)))
                    {
                        siteEffect = siteEffect with { Looped = true };
                    }

                    callSiteRows.Add((site, siteEffect));

                    // A marked line without a matching method summary is an internally contradictory model.
                    // The reverse closure normally supplies this row; this projection is the semantic fallback
                    // for isolated direct owners and for graph-id rewrites that leave the call-site join intact.
                    var methodDepth = site.TargetSymbolId.Length == 0 ? 0 : siteEffect.NearestDepth + 1;
                    methodRows.Add(
                        (
                            site.EnclosingSymbolId,
                            new FileEffectAggregate(
                                currentFamily,
                                methodDepth,
                                siteEffect.ViaDispatchOnly,
                                Looped: methodDepth == 0 && loopedOwners.Contains(site.EnclosingSymbolId)
                            )
                        )
                    );
                }

                // A direct effect owner is a depth-zero seed even when it has no incoming/outgoing edges and
                // therefore never entered the traversal index. Lambda effects have already been folded to the
                // outer declared method, so this also removes invisible lambda hops from editor-facing depth.
                foreach (
                    var owner in fileEffects
                        .Select(effect => effect.EnclosingSymbolId)
                        .Where(owner => owner is not null && fileMethodIds.Contains(owner))
                        .Cast<string>()
                        .Distinct(StringComparer.Ordinal)
                )
                {
                    methodRows.Add((owner!, new FileEffectAggregate(currentFamily, 0, Looped: loopedOwners.Contains(owner!))));
                }
            }

            files[filePath] = new FileEffectReadModel(filePath, families, MergeMethods(methodRows), MergeCallSites(callSiteRows));
        }

        return new FileEffectReadModelIndex(files);
    }

    // Static monomorphization splits one generic member into `{baseId}~mono` NODES and redirects concrete
    // calls to them, while both ends of this projection speak BASE ids: the closure is seeded from effect
    // owners (reference facts, always base) and Rider resolves what it gets against a PSI declaration, which
    // carries no binding. So collapse every reached instantiation onto its base id, keeping the SHORTEST
    // distance - without it a generic callee is simply absent from the join, which cost
    // Writes.SaveFactsBatchedAsync all five of its InsertRows``1 call sites and would cost a method its whole
    // summary when its only route to the family runs through an instantiation.
    private static Dictionary<string, int> CollapseInstantiations(IReadOnlyDictionary<string, int> reached)
    {
        var reachedByBase = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pair in reached)
        {
            var canonical = MonomorphizedNodeId.BaseOf(pair.Key);
            if (!reachedByBase.TryGetValue(canonical, out var known) || pair.Value < known)
            {
                reachedByBase[canonical] = pair.Value;
            }
        }

        return reachedByBase;
    }

    // Lambda SymbolIds deliberately describe their declaring MEMBER, not their executable owner: a lambda in
    // a property is `P:...~lambdaN`, while its call-graph parent is the getter `M:get_...`. The extraction-time
    // methodGroup edge preserves that Roslyn decision. Follow those edges (including their classified handoff
    // form) through nested lambdas until a declared method/accessor is reached; never infer ownership by
    // trimming the synthetic id.
    private static IReadOnlyDictionary<string, string> ResolveDeclaredLambdaOwners(
        FactGraphData graph,
        IReadOnlyList<SymbolFact> symbols,
        IReadOnlySet<string> declaredMethodIds
    )
    {
        var lambdaIds = symbols
            .Where(symbol => string.Equals(symbol.Kind, "lambda", StringComparison.Ordinal))
            .Select(symbol => MonomorphizedNodeId.BaseOf(symbol.SymbolId))
            // Store-backed file queries deliberately load declared methods only. The graph is already
            // whole-solution, and extraction gives every synthetic lambda an unambiguous `~λN` marker, so
            // discover those nodes from preserved method-group/handoff edges without loading all lambda rows.
            // The marker only identifies the node; ownership still comes exclusively from semantic edges.
            .Concat(
                graph
                    .CallEdges.Where(edge => edge.Kind is EdgeKinds.MethodGroup or EdgeKinds.Handoff)
                    .Select(edge => MonomorphizedNodeId.BaseOf(edge.Callee))
                    .Where(IsSyntheticLambdaNode)
            )
            .ToHashSet(StringComparer.Ordinal);
        var parentsByLambda = graph
            .CallEdges.Where(edge => edge.Kind is EdgeKinds.MethodGroup or EdgeKinds.Handoff)
            .Select(edge => new { Parent = MonomorphizedNodeId.BaseOf(edge.Caller), Lambda = MonomorphizedNodeId.BaseOf(edge.Callee) })
            .Where(edge => lambdaIds.Contains(edge.Lambda))
            .GroupBy(edge => edge.Lambda, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => edge.Parent).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal
            );

        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        var resolving = new HashSet<string>(StringComparer.Ordinal);

        string? Resolve(string node)
        {
            if (declaredMethodIds.Contains(node))
            {
                return node;
            }
            if (resolved.TryGetValue(node, out var known))
            {
                return known;
            }
            if (!lambdaIds.Contains(node) || !resolving.Add(node))
            {
                return null;
            }

            var candidates = (parentsByLambda.GetValueOrDefault(node) ?? [])
                .Select(Resolve)
                .Where(owner => owner is not null)
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            resolving.Remove(node);
            if (candidates.Length != 1)
            {
                return null;
            }

            resolved[node] = candidates[0];
            return candidates[0];
        }

        foreach (var lambdaId in lambdaIds.Order(StringComparer.Ordinal))
        {
            Resolve(lambdaId);
        }

        return resolved;
    }

    private static bool IsSyntheticLambdaNode(string symbolId) => symbolId.Contains("~λ", StringComparison.Ordinal);

    private static DerivedEffect FoldLambdaOwner(DerivedEffect effect, IReadOnlyDictionary<string, string> declaredOwnerByLambda)
    {
        if (effect.EnclosingSymbolId is null)
        {
            return effect;
        }

        var canonical = MonomorphizedNodeId.BaseOf(effect.EnclosingSymbolId);
        return declaredOwnerByLambda.TryGetValue(canonical, out var owner) ? effect with { EnclosingSymbolId = owner } : effect;
    }

    // One row per (symbol, family): a method reaching several families carries one aggregate each, ordered so
    // the wire shape is stable.
    private static IReadOnlyList<FileEffectMethod> MergeMethods(IReadOnlyList<(string SymbolId, FileEffectAggregate Effect)> rows) =>
        Array.AsReadOnly(
            rows.GroupBy(row => row.SymbolId, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new FileEffectMethod(
                    group.Key,
                    Array.AsReadOnly(
                        group
                            .Select(row => row.Effect)
                            .GroupBy(effect => effect.Family, StringComparer.Ordinal)
                            .OrderBy(effects => effects.Key, StringComparer.Ordinal)
                            .Select(Best)
                            .ToArray()
                    )
                ))
                .ToArray()
        );

    // Same merge for call sites. Targeted and untargeted rows intentionally coexist: the latter is the only
    // lossless representation of a direct external effect on a line that also contains reachable calls.
    // Consumers own their anchoring policy (the text lens min-merges families; Rider prefers target matches).
    private static IReadOnlyList<FileEffectCallSite> MergeCallSites(IReadOnlyList<(CallSiteKey Key, FileEffectAggregate Effect)> rows)
    {
        return Array.AsReadOnly(
            rows.GroupBy(row => row.Key)
                .OrderBy(group => group.Key.EnclosingSymbolId, StringComparer.Ordinal)
                .ThenBy(group => group.Key.Line)
                .ThenBy(group => group.Key.TargetSymbolId, StringComparer.Ordinal)
                .Select(group => new FileEffectCallSite(
                    group.Key.EnclosingSymbolId,
                    group.Key.TargetSymbolId,
                    group.Key.Line,
                    Array.AsReadOnly(
                        group
                            .Select(row => row.Effect)
                            .GroupBy(effect => effect.Family, StringComparer.Ordinal)
                            .OrderBy(effects => effects.Key, StringComparer.Ordinal)
                            .Select(Best)
                            .ToArray()
                    )
                ))
                .ToArray()
        );
    }

    // One aggregate per family from several candidates: a real reach always beats a dispatch-only one, and the
    // shortest distance wins WITHIN the surviving basis. Mixing the two — taking the global minimum and keeping
    // the winner's flag — would let a short dispatch guess hide a real call, or a real call hide the fact that
    // the SHORT route is only a guess.
    private static FileEffectAggregate Best(IGrouping<string, FileEffectAggregate> candidates)
    {
        var real = candidates.Where(effect => !effect.ViaDispatchOnly).ToArray();
        var basis = real.Length > 0 ? real : candidates.ToArray();
        var nearest = basis.Min(effect => effect.NearestDepth);

        // Repetition is OR-ed only across the rows that actually WON — the ones at the rendered distance.
        // A looped row further away must not lend its mark to a nearer row that does not repeat, because the
        // badge then claims the thing the reader can see on that line runs N times when it runs once.
        var looped = basis.Any(effect => effect.NearestDepth == nearest && effect.Looped);
        return new FileEffectAggregate(candidates.Key, nearest, real.Length == 0, looped);
    }

    // The AMPLIFICATION tier's membership test, asked of the effect's own observations. Routed through
    // HazardKinds so this file cannot drift from the catalog (and so `parallel_fanout`, the obvious next
    // member of that set, starts marking lines the day it joins it without a change here).
    private static bool IsLooped(DerivedEffect effect) =>
        effect.Observations?.Any(observation => HazardKinds.IsAmplification(observation.Type)) == true;

    public FileEffectReadModel? Find(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return _files.GetValueOrDefault(filePath);
    }

    private static bool Matches(DerivedEffect effect, EffectPredicate predicate) =>
        string.Equals(effect.Provider, predicate.Provider, StringComparison.Ordinal)
        && (predicate.Operation is null || string.Equals(effect.Operation, predicate.Operation, StringComparison.Ordinal));

    // The call-site KEYS one family contributes to one file. The family aggregate is attached by the caller,
    // which also applies the cross-family precedence (see MergeCallSites).
    private static IReadOnlyList<CallSiteKey> BuildCallSiteKeys(
        IReadOnlyList<CallEdge> fileInvocationEdges,
        IReadOnlyList<DerivedEffect> selectedEffects,
        IReadOnlyDictionary<string, int> reached,
        IReadOnlySet<string> reachedByAnyFamily,
        IReadOnlySet<string> fileMethodIds,
        IReadOnlyDictionary<string, string> declaredOwnerByLambda
    )
    {
        // Both ends are canonicalised first: a call INTO an instantiation carries a `~mono` callee, and a
        // call FROM inside a cloned instantiation body carries a `~mono` caller that no file declares.
        var invocationEdges = fileInvocationEdges
            .Select(edge =>
                edge with
                {
                    Caller = FoldLambdaNode(MonomorphizedNodeId.BaseOf(edge.Caller), declaredOwnerByLambda),
                    Callee = MonomorphizedNodeId.BaseOf(edge.Callee),
                }
            )
            .Where(edge => fileMethodIds.Contains(edge.Caller))
            .ToArray();

        // A direct derived effect retains its owner + physical source site, but not the matched target.
        // Recover the target only when that site contains exactly one invocation edge and that target is not
        // a distinct transitive signal. An expression such as `Use(Read(), Other())` shares one line across
        // several calls, so guessing there would turn a semantic editor annotation into a false positive. A
        // unique depth-zero target in THIS family is safe: it already represents the direct signal. A target
        // reached only by another family, or at a positive depth, must coexist with the empty direct row.
        var directTargets = invocationEdges
            .GroupBy(edge => new SourceSite(edge.Caller, edge.Line))
            .Where(group => group.Select(edge => edge.Callee).Distinct(StringComparer.Ordinal).Take(2).Count() == 1)
            .Where(group =>
                group.All(edge =>
                    !reachedByAnyFamily.Contains(edge.Callee) || (reached.TryGetValue(edge.Callee, out var targetDepth) && targetDepth == 0)
                )
            )
            .ToDictionary(group => group.Key, group => group.First().Callee);
        var ownEffects = selectedEffects
            .Where(effect => effect.EnclosingSymbolId is not null && fileMethodIds.Contains(effect.EnclosingSymbolId))
            .ToArray();
        var directSites = ownEffects
            .Select(effect => new SourceSite(effect.EnclosingSymbolId!, effect.Line))
            .Where(directTargets.ContainsKey)
            .Select(site => new CallSiteKey(site.EnclosingSymbolId, directTargets[site], site.Line))
            .ToArray();

        // For an indirect call, the question a reader asks of a call site is "does going in here end in the
        // family?" — reverse REACHABILITY of the callee, not whether this particular call shortens the
        // distance. A strict `calleeDepth < callerDepth` test dropped every second effectful call out of one
        // body: the caller already owns the shorter distance through its first effectful callee, so a sibling
        // call whose own distance ties it went unmarked (IndexCommands.MaterializeGraphAsync kept
        // GraphMaterializer.BuildFromGraphAsync at 0 and silently lost
        // EntryPointContext.MaterializeEntryPointSitesAsync at 1). Dispatch remains the graph engine's
        // concern; Rider receives only the static DocIDs it can resolve against the current PSI invocation.
        var indirectSites = invocationEdges
            .Where(edge => reached.ContainsKey(edge.Callee))
            .Select(edge => new CallSiteKey(edge.Caller, edge.Callee, edge.Line))
            .ToArray();

        // A call into EXTERNAL/library code produces neither a CallEdge nor a graph node — nothing to join
        // and nothing to recover a target from — so `DbConnection.BeginTransactionAsync` /
        // `DbTransaction.CommitAsync` / `DbCommand.ExecuteNonQueryAsync` left their lines unmarked and the
        // method summary as the only signal (live: Writes.SaveFactsBatchedAsync, lines 338 and 499). The
        // effect fact carries its OWN physical site, which is enough: the row is emitted with NearestDepth 0
        // (the effect is right there) and an EMPTY target, because there is no in-solution symbol to name.
        // Suppress the empty form only when this exact direct effect was safely recovered to an otherwise
        // non-effectful unique target. A reachable target (in this or another family) is a separate semantic
        // fact and must coexist with the direct depth-zero row.
        var recoveredDirectSites = directSites.Select(site => new SourceSite(site.EnclosingSymbolId, site.Line)).ToHashSet();
        var externalSites = ownEffects
            .Where(effect => effect.Line > 0 && !recoveredDirectSites.Contains(new SourceSite(effect.EnclosingSymbolId!, effect.Line)))
            .Select(effect => new CallSiteKey(effect.EnclosingSymbolId!, "", effect.Line));

        return Array.AsReadOnly(directSites.Concat(indirectSites).Concat(externalSites).Distinct().ToArray());
    }

    private static string FoldLambdaNode(string node, IReadOnlyDictionary<string, string> declaredOwnerByLambda) =>
        declaredOwnerByLambda.GetValueOrDefault(node) ?? node;

    private readonly record struct SourceSite(string EnclosingSymbolId, int Line);

    private readonly record struct CallSiteKey(string EnclosingSymbolId, string TargetSymbolId, int Line);
}
