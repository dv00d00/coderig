using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Rig.Analysis.Extraction;
using Rig.Analysis.Inventory;
using Rig.Analysis.Rules;
using Rig.Domain.Data;
using RuleSet = Rig.Domain.Data.RuleSet;

namespace Rig.Analysis;

public static class SolutionAnalyzer
{
    // Process-wide host/test safety valve. Explicit per-call parallelism always wins. The isolated
    // integration-test executable sets this to one for its lifetime so Buildalyzer/MSBuild project
    // loads cannot fan out internally even though the test runner itself is already serialized.
    internal static int? ProcessParallelismOverride { get; set; }

    public static async Task<AnalysisResult> AnalyzeAsync(
        string solutionPath,
        RuleSet rules,
        CancellationToken cancellationToken = default,
        Action<string>? progress = null,
        string? projectIdentity = null,
        // When non-null, restrict the solution index to this set of project paths (the entry-project
        // closure from `rig index --from`); still ONE cross-project Roslyn workspace / run.
        IReadOnlySet<string>? scopeProjectPaths = null,
        // Max concurrent design-time builds / compilations (null = conservative default).
        int? parallelism = null,
        // Drop test projects (by name convention) from the indexed set (the index default).
        bool excludeTests = false,
        // Optional per-phase timing collector (rig index --time). Records projections here; the loader
        // records workspace-build / wire-generators / the fused compile+read+extract pass.
        PhaseTimings? timings = null,
        // Directory for the design-time-build cache (rig index --reuse-build-cache). Null = disabled.
        string? buildCacheDir = null,
        // --verify-build-cache: build everything ignoring hits and diff fresh vs cached, reporting mismatches.
        bool verifyBuildCache = false,
        // Explicit TFM selected for multi-targeted projects. Single-targeted projects retain their declared TFM.
        string? framework = null,
        // Run the MSBuild `Restore` target before each design-time build (rig index --restore). OFF by
        // default — see CompileOnlyOptions for why, and what an unrestored project looks like.
        bool restore = false
    )
    {
        parallelism ??= ProcessParallelismOverride;
        var solutionFullPath = Path.GetFullPath(solutionPath);
        var phase = timings is null ? null : Stopwatch.StartNew();

        // The DI method-name set is a pure function of the rules — built once for the run so each file's
        // DI pass can syntactically reject non-registration invocations before paying a semantic bind.
        var diMethodNames = DiRegistrationExtractor.BuildMethodNameSet(rules);

        // RUN-scoped (string-keyed, so unlike SymbolStringCache it pins no compilations): the per-project
        // extraction batches share one instance per distinct retained string. Dies with this call — the
        // canonical strings live on in the returned facts, the table does not.
        var interner = StringInterner.CreateDefault();

        progress?.Invoke("Loading solution");
        var sourceSet = await SolutionSourceLoader.LoadAsync(
            solutionPath: solutionFullPath,
            rules: rules,
            extractProject: models => ExtractProject(models, rules, diMethodNames, parallelism, interner, cancellationToken),
            cancellationToken: cancellationToken,
            progress: progress,
            scopeProjectPaths: scopeProjectPaths,
            parallelism: parallelism,
            framework: framework,
            excludeTests: excludeTests,
            timings: timings,
            buildCacheDir: buildCacheDir,
            verifyBuildCache: verifyBuildCache,
            restore: restore
        );
        return ExtractFromSourceSet(
            solutionPath: solutionPath,
            solutionFullPath: solutionFullPath,
            sourceSet: sourceSet,
            rules: rules,
            projectIdentity: projectIdentity,
            progress: progress,
            timings: timings,
            phase: phase
        );
    }

    // SPIKE seam (incremental indexing): AnalyzeAsync, but hands the built RigWorkspace back to the
    // caller instead of letting it go out of scope, so a document edit can be applied in-memory
    // (Solution.WithDocumentText / RigWorkspace.ChangeDocumentText) and re-extracted via
    // ExtractFromSolutionAsync. The caller owns the workspace's lifetime. Behaviour of the returned
    // AnalysisResult is identical to AnalyzeAsync.
    // The knobs below are pass-throughs to LoadAsync, and they are NOT optional garnish at real scale: with
    // buildCacheDir null the design-time-build cache is DISABLED, so a 227-project solution pays a full cold
    // MSBuild pass rather than reusing .rig/dtb-cache, and excludeTests:false indexes test projects that
    // `rig index` drops. Without them this entry point is only usable on a toy playground, and any timing it
    // produces is not comparable to a `rig index` baseline.
    internal static async Task<(AnalysisResult Result, RigWorkspace Workspace)> AnalyzeRetainingWorkspaceAsync(
        string solutionPath,
        RuleSet rules,
        CancellationToken cancellationToken = default,
        Action<string>? progress = null,
        int? parallelism = null,
        bool excludeTests = false,
        PhaseTimings? timings = null,
        string? buildCacheDir = null,
        string? framework = null,
        bool restore = false,
        // The resident host passes ITS interner so the base generation's strings are canonical in the
        // same table every later re-extraction uses (cross-generation aliasing — the point of the
        // resident interner). Null = a run-scoped one, exactly like AnalyzeAsync.
        StringInterner? interner = null
    )
    {
        parallelism ??= ProcessParallelismOverride;
        var solutionFullPath = Path.GetFullPath(solutionPath);
        RigWorkspace? retained = null;
        var diMethodNames = DiRegistrationExtractor.BuildMethodNameSet(rules);
        interner ??= StringInterner.CreateDefault();
        var sourceSet = await SolutionSourceLoader.LoadAsync(
            solutionPath: solutionFullPath,
            rules: rules,
            extractProject: models => ExtractProject(models, rules, diMethodNames, parallelism, interner, cancellationToken),
            cancellationToken: cancellationToken,
            progress: progress,
            parallelism: parallelism,
            excludeTests: excludeTests,
            timings: timings,
            buildCacheDir: buildCacheDir,
            framework: framework,
            restore: restore,
            retainWorkspace: workspace => retained = workspace
        );
        var result = ExtractFromSourceSet(
            solutionPath: solutionPath,
            solutionFullPath: solutionFullPath,
            sourceSet: sourceSet,
            rules: rules,
            projectIdentity: null,
            progress: progress,
            timings: null,
            phase: null
        );
        return (result, retained!);
    }

    // SPIKE seam (incremental indexing): re-runs the compile+read+extract pass over an
    // already-built (possibly incrementally edited) Solution — no Buildalyzer, no workspace assembly.
    // Pairs with AnalyzeRetainingWorkspaceAsync: retain the workspace, WithDocumentText, then this.
    internal static async Task<AnalysisResult> ExtractFromSolutionAsync(
        Microsoft.CodeAnalysis.Solution solution,
        string solutionPath,
        RuleSet rules,
        CancellationToken cancellationToken = default,
        Action<string>? progress = null
    )
    {
        var solutionFullPath = Path.GetFullPath(solutionPath);
        var diMethodNames = DiRegistrationExtractor.BuildMethodNameSet(rules);
        var interner = StringInterner.CreateDefault();
        var sourceSet = await SolutionSourceLoader.ReadSolutionSourcesAsync(
            solution: solution,
            solutionPath: solutionFullPath,
            rules: rules,
            extractProject: models => ExtractProject(models, rules, diMethodNames, parallelism: null, interner, cancellationToken),
            cancellationToken: cancellationToken,
            progress: progress
        );
        return ExtractFromSourceSet(
            solutionPath: solutionPath,
            solutionFullPath: solutionFullPath,
            sourceSet: sourceSet,
            rules: rules,
            projectIdentity: null,
            progress: progress,
            timings: null,
            phase: null
        );
    }

    // Per-DOCUMENT extraction primitive (live-background-index slice 3): facts for JUST the given
    // documents, over an already-built (possibly incrementally edited) Solution. Mirrors
    // ExtractFromSolutionAsync's pipeline — same classification, same compilation-bound semantic
    // models, same ExtractFromSourceSet back half, same FilePath ordering — the ONLY difference is
    // which documents are visited. Two deliberate, disclosed divergences from the whole-solution read
    // pass in ReadSolutionSourcesAsync:
    //   - no per-project GetDiagnostics pass: it exists there for error REPORTING and as a whole-project
    //     bind warm-up; facts do not depend on it, and paying a whole-project bind per single-file
    //     re-extract would defeat the per-file path's point.
    //   - no source-generator pass: generated documents belong to the PROJECT, not to any input
    //     document, so a per-document call cannot own them. A resident overlay keeps serving generated
    //     files' BASE facts until a whole-project/solution re-extraction refreshes them.
    internal static async Task<AnalysisResult> ExtractFromDocumentsAsync(
        Microsoft.CodeAnalysis.Solution solution,
        IReadOnlyCollection<DocumentId> documents,
        string solutionPath,
        RuleSet rules,
        CancellationToken cancellationToken = default,
        Action<string>? progress = null,
        StringInterner? interner = null
    )
    {
        var solutionFullPath = Path.GetFullPath(solutionPath);
        var (sourceFiles, orderedSources, extractionResults, health, _) = await ReadAndExtractDocumentsAsync(
            solution: solution,
            documents: documents,
            solutionFullPath: solutionFullPath,
            rules: rules,
            cancellationToken: cancellationToken,
            interner: interner ?? StringInterner.CreateDefault()
        );

        var extractedSources = new List<ExtractedSource>(orderedSources.Count);
        for (var i = 0; i < orderedSources.Count; i++)
        {
            extractedSources.Add(new ExtractedSource(orderedSources[i].ProjectName, orderedSources[i].FilePath, extractionResults[i]));
        }

        var sourceSet = new SolutionSourceSet(
            sourceFiles.OrderBy(f => f.FilePath, StringComparer.OrdinalIgnoreCase).ToList(),
            extractedSources,
            health,
            []
        );

        return ExtractFromSourceSet(
            solutionPath: solutionPath,
            solutionFullPath: solutionFullPath,
            sourceSet: sourceSet,
            rules: rules,
            projectIdentity: null,
            progress: progress,
            timings: null,
            phase: null
        );
    }

    // BATCHED per-file extraction (live-background-index integration slice): one call covers the whole
    // pending set, and the result comes back PARTITIONED BY FILE PATH — the resident overlay's
    // replacement grain. The partition is EXACT for every fact kind, including TypeRelation/Dispatch:
    // facts are grouped from the per-SourceModel extraction results BEFORE any flattening, and those
    // edge facts carry that same SourceModel FilePath as emitter provenance. Each file's lists are
    // exactly its own emissions — the same property the
    // one-call-per-path loop bought, without the per-call bill. Batching is what makes ReconcileAsync
    // affordable: the per-call setup the single-file path re-paid per file (the DI method-name set, and
    // ExtractFromSourceSet's XmlDiMiner.Mine over the rules' XML files) is paid at most once per batch
    // here (XmlDiMiner not at all — the per-file slices never carry XML/static DI rows, exactly as
    // FileFacts.From filtered them out), each project's compilation is bound once per batch instead of
    // once per file, and extraction runs Parallel across the whole batch instead of file-at-a-time.
    //
    // Every distinct file path among `documents` gets an entry, even when nothing was extracted for it
    // (excluded project, no compilation, not classified "indexed") — mirroring the single-file path,
    // where such a call produced an EMPTY slice that replaces the file's base rows on merge.
    internal static async Task<Dictionary<string, FileFacts>> ExtractFromDocumentsByFileAsync(
        Microsoft.CodeAnalysis.Solution solution,
        IReadOnlyCollection<DocumentId> documents,
        string solutionPath,
        RuleSet rules,
        CancellationToken cancellationToken = default,
        // The resident overlay's interner (host-lifetime): a re-extracted generation's strings alias the
        // base generation's instead of duplicating the retained string set per edit.
        StringInterner? interner = null
    )
    {
        var solutionFullPath = Path.GetFullPath(solutionPath);
        var (sourceFiles, orderedSources, extractionResults, health, surfaceContributions) = await ReadAndExtractDocumentsAsync(
            solution: solution,
            documents: documents,
            solutionFullPath: solutionFullPath,
            rules: rules,
            cancellationToken: cancellationToken,
            interner: interner ?? StringInterner.CreateDefault()
        );

        // Seed an (empty) builder for every input document's path, then fill from the per-source
        // results. A file linked into several projects contributes one SourceModel per project context;
        // its slice is the concatenation, exactly as the old single-path call (all DocumentIds at once)
        // produced.
        var builders = new Dictionary<string, FileFactsBuilder>(StringComparer.OrdinalIgnoreCase);
        foreach (var documentId in documents)
        {
            var filePath = solution.GetDocument(documentId)?.FilePath;
            if (filePath is not null && !builders.ContainsKey(filePath))
            {
                builders[filePath] = new FileFactsBuilder();
            }
        }

        foreach (var sourceFile in sourceFiles)
        {
            builders[sourceFile.FilePath].SourceFiles.Add(sourceFile);
        }

        // Compile health is a per-file fact like any other, so it rides the same replacement grain: a
        // re-extracted file whose diagnostics are now CLEAN contributes an EMPTY list here, which drops
        // its base row on merge. That is the whole mechanism by which a fixed file stops being flagged —
        // in a resident process a flag that only ever accumulates would stick for the process lifetime.
        foreach (var fileHealth in health.Files)
        {
            if (builders.TryGetValue(fileHealth.FilePath, out var healthBuilder))
            {
                healthBuilder.CompileHealth.Add(fileHealth);
            }
        }

        foreach (var contribution in surfaceContributions)
        {
            if (builders.TryGetValue(contribution.Shard.EmitterFilePath, out var surfaceBuilder))
            {
                surfaceBuilder.ProjectSurfaces.Add(contribution);
            }
        }

        for (var i = 0; i < orderedSources.Count; i++)
        {
            var facts = extractionResults[i];
            var builder = builders[orderedSources[i].FilePath];
            builder.DiRegistrations.AddRange(facts.DiRegistrations);
            builder.Symbols.AddRange(facts.Symbols);
            builder.References.AddRange(facts.References);
            builder.TypeRelations.AddRange(facts.TypeRelations);
            builder.Dispatch.AddRange(facts.Dispatch);
            builder.Allocations.AddRange(facts.Allocations);
        }

        var slices = new Dictionary<string, FileFacts>(builders.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (filePath, builder) in builders)
        {
            slices[filePath] = new FileFacts(
                SourceFiles: builder.SourceFiles.ToImmutableArray(),
                DiRegistrations: builder.DiRegistrations.ToImmutableArray(),
                Symbols: builder.Symbols.ToImmutableArray(),
                References: builder.References.ToImmutableArray(),
                TypeRelations: builder.TypeRelations.ToImmutableArray(),
                Dispatch: builder.Dispatch.ToImmutableArray(),
                Allocations: builder.Allocations.ToImmutableArray(),
                CompileHealth: builder.CompileHealth.ToImmutableArray(),
                ProjectSurfaces: builder.ProjectSurfaces.ToImmutableArray()
            );
        }

        return slices;
    }

    // Lazy Slice-5 surface refresh. Ordinary source shards already arrived through the eager by-file
    // extraction; refinement pays only for the current compilation's project-wide meta inputs and source
    // generator output. Failures return an unclassifiable value so the caller retains coarse debt.
    internal static async Task<ProjectSurfaceRefresh> RefreshProjectSurfaceAsync(
        Microsoft.CodeAnalysis.Solution solution,
        ProjectId projectId,
        RuleSet rules,
        CancellationToken cancellationToken,
        StringInterner? interner = null
    )
    {
        var project = solution.GetProject(projectId);
        if (project is null || project.Language != LanguageNames.CSharp || rules.IsExcludedProject(project.Name))
        {
            return new ProjectSurfaceRefresh([], new ProjectSurfaceShard("", false, ""), false);
        }

        try
        {
            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is null)
            {
                return new ProjectSurfaceRefresh([], new ProjectSurfaceShard("", false, ""), false);
            }

            var health = new CompilationHealthCollector();
            var generated = await SolutionSourceLoader.RunSourceGeneratorsAsync(
                project,
                compilation,
                progress: null,
                health,
                cancellationToken
            );
            if (health.Build().GeneratorFailures.Any())
            {
                throw new InvalidOperationException(
                    $"Source-generator refresh failed for '{project.Name}'; its surface remains Unknown and coarse debt is retained."
                );
            }

            var diMethodNames = DiRegistrationExtractor.BuildMethodNameSet(rules);
            var extractions = ExtractProject(
                generated,
                rules,
                diMethodNames,
                parallelism: null,
                interner ?? StringInterner.CreateDefault(),
                cancellationToken
            );
            var generatedShards = generated
                .Select((source, i) => ProjectSurfaceBuilder.BuildEmitter(source, extractions[i]))
                .ToImmutableArray();
            var generatedFacts = ImmutableDictionary.CreateBuilder<string, FileFacts>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < generated.Count; i++)
            {
                var source = generated[i];
                var facts = extractions[i];
                generatedFacts[source.FilePath] = new FileFacts(
                    [new SourceFileInfo(project.Name, source.FilePath, "indexed", "high", "generated", "source_generator", "")],
                    facts.DiRegistrations.ToImmutableArray(),
                    facts.Symbols.ToImmutableArray(),
                    facts.References.ToImmutableArray(),
                    facts.TypeRelations.ToImmutableArray(),
                    facts.Dispatch.ToImmutableArray(),
                    facts.Allocations.ToImmutableArray(),
                    []
                );
            }
            return new ProjectSurfaceRefresh(
                generatedShards,
                ProjectSurfaceBuilder.BuildMeta(project.ParseOptions as Microsoft.CodeAnalysis.CSharp.CSharpParseOptions, compilation),
                true,
                generatedFacts.ToImmutable()
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Refinement is an optimization gate. Any Roslyn/generator failure leaves the surface
            // Unknown so ResidentIndex retains the conservative cascade for coarse reconciliation.
            return new ProjectSurfaceRefresh([], new ProjectSurfaceShard("", false, ""), false);
        }
    }

    private sealed class FileFactsBuilder
    {
        public List<SourceFileInfo> SourceFiles { get; } = [];
        public List<DiRegistrationInfo> DiRegistrations { get; } = [];
        public List<SymbolFact> Symbols { get; } = [];
        public List<ReferenceFact> References { get; } = [];
        public List<TypeRelationFact> TypeRelations { get; } = [];
        public List<DispatchFact> Dispatch { get; } = [];
        public List<AllocationFact> Allocations { get; } = [];
        public List<FileCompileHealth> CompileHealth { get; } = [];
        public List<ProjectSurfaceContribution> ProjectSurfaces { get; } = [];
    }

    // Shared front half of the two per-document entry points above: classify + read + bind the given
    // documents (one GetCompilationAsync per project GROUP — each compilation is bound once per call,
    // however many of its files are in the batch), then extract them all in ONE ExtractProject pass
    // (Parallel across the batch, one SymbolStringCache, one DI method-name set). Returns the
    // classification rows plus the extraction results, positionally aligned with the ordered sources.
    private static async Task<(
        List<SourceFileInfo> SourceFiles,
        List<SourceModel> OrderedSources,
        SourceExtractionResult[] ExtractionResults,
        CompilationHealth Health,
        List<ProjectSurfaceContribution> ProjectSurfaces
    )> ReadAndExtractDocumentsAsync(
        Microsoft.CodeAnalysis.Solution solution,
        IReadOnlyCollection<DocumentId> documents,
        string solutionFullPath,
        RuleSet rules,
        CancellationToken cancellationToken,
        StringInterner? interner
    )
    {
        var sources = new List<SourceModel>();
        var sourceFiles = new List<SourceFileInfo>();
        var health = new CompilationHealthCollector();
        var surfaceContributions = new Dictionary<(ProjectId Project, string FilePath), ProjectSurfaceContribution>();

        foreach (var projectGroup in documents.GroupBy(d => d.ProjectId))
        {
            var project = solution.GetProject(projectGroup.Key);
            if (project is null || project.Language != LanguageNames.CSharp || rules.IsExcludedProject(project.Name))
            {
                continue;
            }

            foreach (var documentId in projectGroup)
            {
                var path = project.GetDocument(documentId)?.FilePath;
                if (path is not null)
                {
                    surfaceContributions[(project.Id, path)] = new ProjectSurfaceContribution(
                        project.Name,
                        project.FilePath ?? "",
                        project.AssemblyName ?? project.Name,
                        new ProjectSurfaceShard(path, false, ""),
                        IsClassifiable: false
                    );
                }
            }

            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is null)
            {
                continue; // no semantic model possible — same partial-analysis stance as the full pass
            }

            foreach (var documentId in projectGroup)
            {
                var document = project.GetDocument(documentId);
                if (document?.FilePath?.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) != true)
                {
                    continue;
                }

                var classification = SourceFileClassifier.Classify(
                    solutionPath: solutionFullPath,
                    project: project,
                    filePath: document.FilePath,
                    rules: rules
                );

                sourceFiles.Add(
                    new SourceFileInfo(
                        ProjectName: project.Name,
                        FilePath: document.FilePath,
                        Status: classification.Status,
                        Confidence: classification.Confidence,
                        Basis: classification.Basis,
                        Reason: classification.Reason,
                        Evidence: classification.Evidence
                    )
                );

                if (classification.Status != "indexed")
                {
                    continue;
                }

                var tree = await document.GetSyntaxTreeAsync(cancellationToken);
                var root = tree is null ? null : await tree.GetRootAsync(cancellationToken);
                if (tree is null || root is null)
                {
                    continue;
                }

                // Bind through the project compilation (not document.GetSemanticModelAsync), exactly as
                // LoadProjectSourcesAsync does — the document's tree is one of this compilation's trees.
                var semanticModel = compilation.GetSemanticModel(tree);

                // Per-TREE diagnostics, not the whole-project compilation.GetDiagnostics() the cold pass
                // runs. Two reasons, both load-bearing:
                //   - COST: a whole-project bind per single-file re-extract would defeat this path's
                //     point (the same reason this method has no project-wide diagnostics pass at all).
                //     Per-tree binds only the trees this call is already binding for extraction.
                //   - GRAIN: the per-file bucket wants exactly the diagnostics located in THIS file,
                //     which is exactly what SemanticModel.GetDiagnostics() reports. Diagnostics located
                //     in OTHER files are that file's business, and the cascade re-extracts it (spec 4.1:
                //     the error from breaking a declaration lands in the DEPENDENT, and Roslyn re-reports
                //     it there, so per-file scope needs no propagation).
                // Consequence, stated: compilation-LEVEL (location-less) diagnostics are not observed on
                // this path, so an incremental generation keeps the cold load's UnlocatedErrorCount.
                foreach (var diagnostic in semanticModel.GetDiagnostics(cancellationToken: cancellationToken))
                {
                    if (diagnostic.Severity == DiagnosticSeverity.Error)
                    {
                        // Key on the DOCUMENT's path, not the diagnostic's reported path: the resident
                        // overlay replaces by that exact string, and a differently-normalised key would
                        // leave a stale flag nothing can clear.
                        health.AddError(diagnostic, document.FilePath);
                    }
                }

                sources.Add(
                    new SourceModel(
                        ProjectName: project.Name,
                        FilePath: document.FilePath,
                        Tree: tree,
                        Root: root,
                        SemanticModel: semanticModel,
                        ProjectFilePath: project.FilePath ?? "",
                        AssemblyName: compilation.AssemblyName ?? project.Name
                    )
                );
            }
        }

        // Extract NOW, while the compilations bound above are alive, in the same OrdinalIgnoreCase
        // FilePath order the whole-solution pass uses — then drop the SourceModels (slice 2: nothing
        // downstream may retain a SemanticModel or red root).
        var orderedSources = sources.OrderBy(s => s.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
        var diMethodNames = DiRegistrationExtractor.BuildMethodNameSet(rules);
        var extractionResults = ExtractProject(orderedSources, rules, diMethodNames, parallelism: null, interner, cancellationToken);
        for (var i = 0; i < orderedSources.Count; i++)
        {
            var source = orderedSources[i];
            var matchingProjects = solution
                .Projects.Where(p =>
                    (
                        !string.IsNullOrWhiteSpace(source.ProjectFilePath)
                        && p.FilePath is not null
                        && string.Equals(
                            Path.GetFullPath(p.FilePath),
                            Path.GetFullPath(source.ProjectFilePath),
                            StringComparison.OrdinalIgnoreCase
                        )
                    ) || (string.IsNullOrWhiteSpace(source.ProjectFilePath) && p.Name == source.ProjectName)
                )
                .ToArray();
            if (matchingProjects.Length != 1)
            {
                continue;
            }
            var project = matchingProjects[0];

            surfaceContributions[(project.Id, source.FilePath)] = new ProjectSurfaceContribution(
                source.ProjectName,
                source.ProjectFilePath,
                source.AssemblyName,
                ProjectSurfaceBuilder.BuildEmitter(source, extractionResults[i]),
                IsClassifiable: true
            );
        }
        return (sourceFiles, orderedSources, extractionResults, health.Build(), surfaceContributions.Values.ToList());
    }

    // Per-PROJECT extraction sink (live-background-index slice 2), invoked by the loader while that
    // project's Compilation is alive. Returns one result per model, positionally. The loader drops the
    // SourceModels the moment this returns — nothing here may retain one.
    //
    // Parallel.For into pre-allocated slots (NOT AsParallel().AsOrdered()): writing result[i] for
    // source[i] keeps the output deterministic by input position — which the FactIndex surrogate keys
    // depend on — WITHOUT PLINQ's order-preserving merge. Distinct slots per iteration, so no write races.
    // This runs NESTED inside the loader's Parallel.ForEachAsync over projects at the same cap;
    // Parallel queues to the thread pool rather than creating threads, so total concurrency stays
    // pool-bounded, not DOP².
    private static SourceExtractionResult[] ExtractProject(
        IReadOnlyList<SourceModel> models,
        RuleSet rules,
        IReadOnlySet<string> diMethodNames,
        int? parallelism,
        StringInterner? interner,
        CancellationToken cancellationToken
    )
    {
        // PER PROJECT, not per run: the cache keys are strong ISymbol references, and a source symbol
        // reaches its owning CSharpCompilation — so a run-global instance pins every compilation in the
        // solution for the whole run. Every memo is a pure function of its key, so a per-project instance
        // emits byte-identical strings; it becomes unreachable when this returns. Run-wide string sharing
        // is the interner's job (string-keyed — pins nothing), which the cache routes its values through.
        var symbolCache = new SymbolStringCache(interner);
        var results = new SourceExtractionResult[models.Count];
        Parallel.For(
            fromInclusive: 0,
            toExclusive: models.Count,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = parallelism ?? Environment.ProcessorCount,
                CancellationToken = cancellationToken,
            },
            i => results[i] = ExtractSource(models[i], rules, symbolCache, diMethodNames)
        );
        return results;
    }

    // The fact-ASSEMBLY back half of AnalyzeAsync: concatenate the per-file extraction results the
    // loader already produced (facts are extracted per project, inside the loader's compile+read+extract
    // pass, while each project's Compilation is alive — see ExtractProject). By the time this runs the
    // source set is Roslyn-free: no SemanticModel, no syntax root, nothing pinning a compilation.
    private static AnalysisResult ExtractFromSourceSet(
        string solutionPath,
        string solutionFullPath,
        SolutionSourceSet sourceSet,
        RuleSet rules,
        string? projectIdentity,
        Action<string>? progress,
        PhaseTimings? timings,
        Stopwatch? phase
    )
    {
        // Start the projection clock fresh after the loader's phases so it isn't double-counted.
        phase?.Restart();
        var extractionResults = sourceSet.ExtractedSources.Select(e => e.Facts).ToArray();

        progress?.Invoke($"Assembling facts from {extractionResults.Length} extracted source files");
        progress?.Invoke("Building projections");

        // Pre-size the concatenated lists to the exact total (one cheap O(1)-per-result count pass) so the
        // AddRange below never grows-and-copies — at ~2.5M facts the default doubling churns ~21 backing
        // arrays per list, the last copies a multi-million-element array to the LOH. Counts are O(1)
        // (List-backed IReadOnlyLists).
        var totalDi = 0;
        var totalSymbols = 0;
        var totalReferences = 0;
        var totalRelations = 0;
        var totalDispatch = 0;
        var totalAllocations = 0;
        foreach (var result in extractionResults)
        {
            totalDi += result.DiRegistrations.Count;
            totalSymbols += result.Symbols.Count;
            totalReferences += result.References.Count;
            totalRelations += result.TypeRelations.Count;
            totalDispatch += result.Dispatch.Count;
            totalAllocations += result.Allocations.Count;
        }

        List<DiRegistrationInfo> diRegistrations = new(totalDi);
        List<SymbolFact> symbolFacts = new(totalSymbols);
        List<ReferenceFact> referenceFacts = new(totalReferences);
        List<TypeRelationFact> typeRelationFacts = new(totalRelations);
        List<DispatchFact> dispatchFacts = new(totalDispatch);
        List<AllocationFact> allocationFacts = new(totalAllocations);

        for (var i = 0; i < extractionResults.Length; i++)
        {
            var result = extractionResults[i];
            diRegistrations.AddRange(result.DiRegistrations);
            symbolFacts.AddRange(result.Symbols);
            referenceFacts.AddRange(result.References);
            typeRelationFacts.AddRange(result.TypeRelations);
            dispatchFacts.AddRange(result.Dispatch);
            allocationFacts.AddRange(result.Allocations);
        }

        // Mine XML service descriptor files (e.g. App_Data/Common/Xml/Services/*.xml) and
        // any inline static mappings, then merge with code-detected DI registrations.
        var xmlRegistrations = XmlDiMiner.Mine(rules);
        var staticRegistrations = rules.StaticDiMappings.Select(m => new DiRegistrationInfo(
            ServiceType: m.ServiceType,
            ImplementationType: m.ImplementationType,
            Lifetime: m.Lifetime,
            RegistrationKind: m.RegistrationKind,
            FilePath: string.Empty,
            Line: 0,
            Confidence: "high",
            Basis: "rules",
            Reason: "static_di_mapping",
            Evidence: string.Empty
        ));

        var allDiRegistrations = diRegistrations.Concat(xmlRegistrations).Concat(staticRegistrations).ToArray();
        if (phase is not null)
        {
            timings!.Record("projections+xml-di", phase.Elapsed);
        }

        if (xmlRegistrations.Count > 0)
        {
            progress?.Invoke($"XML DI miner: {xmlRegistrations.Count} mappings from {rules.XmlDiFiles.Count} path(s)");
        }

        progress?.Invoke(
            $"Analysis complete: {symbolFacts.Count} symbols, "
                + $"{referenceFacts.Count} references, {allDiRegistrations.Length} di registrations"
        );

        // Memory-profiling pause (RIG_PROFILE_PAUSE). Since slice 2 the source set is Roslyn-free: no
        // SemanticModel or red root is rooted here any more — per-file models die inside the loader's
        // per-project pass, so the co-resident peak this pause used to capture now occurs DURING
        // compile+read+extract, not at this seam. The pause point (and its label, which measurement
        // scripts key on) is kept so before/after gcdumps land at the same program point; what may still
        // be live here is a workspace RETAINED by a caller (AnalyzeRetainingWorkspaceAsync), whose
        // compilations are exactly what slice 2 deliberately does not release. No-op unless the env var
        // is set.
        ProfilingPause.MaybePause("extract-peak (roslyn live)");

        // For project-level indexing, record the specific project path
        var sourceProjectPath =
            solutionFullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || solutionFullPath.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
                ? solutionFullPath
                : null;

        return new AnalysisResult(
            solutionPath,
            sourceSet.SourceFiles,
            allDiRegistrations,
            ProjectIdentity: projectIdentity,
            SourceProjectPath: sourceProjectPath,
            Symbols: symbolFacts,
            References: referenceFacts,
            TypeRelations: typeRelationFacts,
            DispatchFacts: dispatchFacts,
            AllocationFacts: allocationFacts,
            CompilationHealth: sourceSet.Health,
            ProjectSurfaces: sourceSet.ProjectSurfaces
        );
    }

    private static SourceExtractionResult ExtractSource(
        SourceModel source,
        RuleSet rules,
        SymbolStringCache symbolCache,
        IReadOnlySet<string> diMethodNames
    )
    {
        var facts = FactExtractor.Extract(source, symbolCache);

        return new SourceExtractionResult(
            DiRegistrationExtractor.FindDiRegistrations(source, rules, diMethodNames).ToArray(),
            facts.Symbols,
            facts.References,
            facts.TypeRelations,
            facts.Dispatch,
            facts.Allocations
        );
    }
}
