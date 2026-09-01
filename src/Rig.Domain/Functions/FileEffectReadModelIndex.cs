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

public sealed record FileEffectAggregate(string Family, int NearestDepth);

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
        var methodsByFile = SymbolFactProjections
            .SelectCanonicalMethodFacts(symbolRows)
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol.FilePath))
            .GroupBy(symbol => symbol.FilePath, FilePathComparer)
            .ToDictionary(group => group.Key, group => group.ToArray(), FilePathComparer);

        var selectedPerFamily = selectors
            .Select(selector => effectRows.Where(effect => selector.Predicates.Any(predicate => Matches(effect, predicate))).ToArray())
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
                foreach (var symbol in fileMethods)
                {
                    if (reached.TryGetValue(symbol.SymbolId, out var depth))
                    {
                        methodRows.Add((symbol.SymbolId, new FileEffectAggregate(families[family], depth)));
                    }
                }

                var fileEffects = selectedByFilePerFamily[family].GetValueOrDefault(filePath) ?? [];
                foreach (var site in BuildCallSiteKeys(fileEdges, fileEffects, reached, fileMethodIds))
                {
                    var depth = reached.TryGetValue(site.TargetSymbolId, out var known) ? known : 0;
                    callSiteRows.Add((site, new FileEffectAggregate(families[family], depth)));
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

    // One row per (symbol, family): a method reaching several families carries one aggregate each, ordered so
    // the wire shape is stable.
    private static IReadOnlyList<FileEffectMethod> MergeMethods(IReadOnlyList<(string SymbolId, FileEffectAggregate Effect)> rows) =>
        Array.AsReadOnly(
            rows.GroupBy(row => row.SymbolId, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new FileEffectMethod(
                    group.Key,
                    Array.AsReadOnly(group.Select(row => row.Effect).OrderBy(effect => effect.Family, StringComparer.Ordinal).ToArray())
                ))
                .ToArray()
        );

    // Same merge for call sites, plus the CROSS-FAMILY arm of the precedence rule BuildCallSiteKeys already
    // applies within one family: an untargeted row (an effect at a call into external code, empty target) is
    // dropped when ANY family produced a targeted row at the same (enclosing, line). Otherwise a line whose db
    // effect is external and whose cache effect resolves would emit both, and the client - which anchors an
    // untargeted row on the leftmost invocation of the line - could mark the wrong call.
    private static IReadOnlyList<FileEffectCallSite> MergeCallSites(IReadOnlyList<(CallSiteKey Key, FileEffectAggregate Effect)> rows)
    {
        var targeted = rows.Where(row => row.Key.TargetSymbolId.Length > 0)
            .Select(row => new SourceSite(row.Key.EnclosingSymbolId, row.Key.Line))
            .ToHashSet();
        return Array.AsReadOnly(
            rows.Where(row =>
                    row.Key.TargetSymbolId.Length > 0 || !targeted.Contains(new SourceSite(row.Key.EnclosingSymbolId, row.Key.Line))
                )
                .GroupBy(row => row.Key)
                .OrderBy(group => group.Key.EnclosingSymbolId, StringComparer.Ordinal)
                .ThenBy(group => group.Key.Line)
                .ThenBy(group => group.Key.TargetSymbolId, StringComparer.Ordinal)
                .Select(group => new FileEffectCallSite(
                    group.Key.EnclosingSymbolId,
                    group.Key.TargetSymbolId,
                    group.Key.Line,
                    Array.AsReadOnly(group.Select(row => row.Effect).OrderBy(effect => effect.Family, StringComparer.Ordinal).ToArray())
                ))
                .ToArray()
        );
    }

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
        IReadOnlySet<string> fileMethodIds
    )
    {
        // Both ends are canonicalised first: a call INTO an instantiation carries a `~mono` callee, and a
        // call FROM inside a cloned instantiation body carries a `~mono` caller that no file declares.
        var invocationEdges = fileInvocationEdges
            .Select(edge =>
                edge with
                {
                    Caller = MonomorphizedNodeId.BaseOf(edge.Caller),
                    Callee = MonomorphizedNodeId.BaseOf(edge.Callee),
                }
            )
            .Where(edge => fileMethodIds.Contains(edge.Caller))
            .ToArray();

        // A direct derived effect retains its owner + physical source site, but not the matched target.
        // Recover the target only when that site contains exactly one invocation edge. An expression such
        // as `Use(Read(), Other())` shares one line across several calls, so guessing there would turn a
        // semantic editor annotation into a false positive; the method summary remains available instead. A site
        // with NO in-solution edge at all falls through to the external-call arm below.
        var directTargets = invocationEdges
            .GroupBy(edge => new SourceSite(edge.Caller, edge.Line))
            .Where(group => group.Select(edge => edge.Callee).Distinct(StringComparer.Ordinal).Take(2).Count() == 1)
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
        // An edge-derived row wins on a shared (enclosing, line): it carries a target Rider can resolve.
        var targetedSites = directSites.Concat(indirectSites).Select(site => new SourceSite(site.EnclosingSymbolId, site.Line)).ToHashSet();
        var externalSites = ownEffects
            .Where(effect => effect.Line > 0 && !targetedSites.Contains(new SourceSite(effect.EnclosingSymbolId!, effect.Line)))
            .Select(effect => new CallSiteKey(effect.EnclosingSymbolId!, "", effect.Line));

        return Array.AsReadOnly(directSites.Concat(indirectSites).Concat(externalSites).Distinct().ToArray());
    }

    private readonly record struct SourceSite(string EnclosingSymbolId, int Line);

    private readonly record struct CallSiteKey(string EnclosingSymbolId, string TargetSymbolId, int Line);
}
