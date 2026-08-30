using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

var frameworkReferences = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
    .Select(path => MetadataReference.CreateFromFile(path))
    .ToImmutableArray<MetadataReference>();

var scenarios = new List<(string Name, CSharpCompilation Root)>();

scenarios.Add(("same_compilation", SameCompilation()));

var sourceLeaf = CompileOne("Spike.Source.Leaf", LeafSource(), frameworkReferences);
var sourceMiddle = CompileOne("Spike.Source.Middle", MiddleSource(), [.. frameworkReferences, sourceLeaf.ToMetadataReference()]);
scenarios.Add(
    (
        "two_source_reference_hops",
        CompileOne("Spike.Source.Root", RootSource(), [.. frameworkReferences, sourceMiddle.ToMetadataReference()])
    )
);

var metadataLeaf = EmitReference(sourceLeaf);
var mixedMiddle = CompileOne("Spike.Mixed.Middle", MiddleSource(), [.. frameworkReferences, metadataLeaf]);
scenarios.Add(
    (
        "source_calls_direct_metadata_effect",
        CompileOne("Spike.Mixed.Root", RootSource(), [.. frameworkReferences, mixedMiddle.ToMetadataReference()])
    )
);

var hiddenMetadataMiddle = CompileOne(
    "Spike.HiddenMetadata.Middle",
    """
    public static class HiddenMiddle
    {
        public static void Run() => Db.Touch();
    }
    """,
    [.. frameworkReferences, sourceLeaf.ToMetadataReference()]
);
var hiddenMetadataMiddleReference = EmitReference(hiddenMetadataMiddle);
var sourceOuterMiddle = CompileOne(
    "Spike.HiddenMetadata.Outer",
    """
    public static class Middle
    {
        public static void Run() => HiddenMiddle.Run();
    }
    """,
    [.. frameworkReferences, hiddenMetadataMiddleReference, metadataLeaf]
);
scenarios.Add(
    (
        "source_then_hidden_metadata_body",
        CompileOne(
            "Spike.HiddenMetadata.Root",
            RootSource(),
            [.. frameworkReferences, sourceOuterMiddle.ToMetadataReference()]
        )
    )
);

var metadataMiddle = EmitReference(mixedMiddle);
scenarios.Add(("metadata_first_hop", CompileOne("Spike.Metadata.Root", RootSource(), [.. frameworkReferences, metadataMiddle])));

scenarios.Add(("interface_dispatch", InterfaceDispatchCompilation()));

Console.WriteLine("scenario\teffects\tmax_depth\tchains\tunresolved_boundaries");
foreach (var (name, compilation) in scenarios)
{
    var compileErrors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
    if (compileErrors.Length > 0)
    {
        throw new InvalidOperationException($"{name} did not compile:{Environment.NewLine}{string.Join(Environment.NewLine, compileErrors)}");
    }

    var diagnostics = await compilation
        .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ReachabilitySpikeAnalyzer()))
        .GetAnalyzerDiagnosticsAsync();
    var result = diagnostics.Single(diagnostic => diagnostic.Id == ReachabilitySpikeAnalyzer.ResultDiagnosticId);
    Console.WriteLine(
        string.Join(
            '\t',
            name,
            result.Properties["effects"],
            result.Properties["maxDepth"],
            result.Properties["chains"],
            result.Properties["boundaries"]
        )
    );
}

return;

CSharpCompilation SameCompilation() =>
    Compile(
        "Spike.SameCompilation",
        [
            AttributeSource(),
            LeafSource(includeEffectAttribute: false),
            MiddleSource(),
            RootSource(includeRootAttribute: false),
        ],
        frameworkReferences
    );

CSharpCompilation InterfaceDispatchCompilation() =>
    Compile(
        "Spike.InterfaceDispatch",
        [
            AttributeSource(),
            LeafSource(includeEffectAttribute: false),
            """
            public interface IWorker
            {
                void Run();
            }

            public sealed class Worker : IWorker
            {
                public void Run() => Db.Touch();
            }
            """,
            """
            public static class Root
            {
                [SpikeRoot]
                public static void Start(IWorker worker) => worker.Run();
            }
            """,
        ],
        frameworkReferences
    );

static string AttributeSource() =>
    """
    using System;

    public sealed class SpikeRootAttribute : Attribute;
    public sealed class SpikeEffectAttribute : Attribute;
    """;

static string LeafSource(bool includeEffectAttribute = true) =>
    $$"""
    {{(includeEffectAttribute ? "using System;\npublic sealed class SpikeEffectAttribute : Attribute;" : "")}}

    public static class Db
    {
        [SpikeEffect]
        public static void Touch() { }
    }
    """;

static string MiddleSource() =>
    """
    public static class Middle
    {
        public static void Run() => Db.Touch();
    }
    """;

static string RootSource(bool includeRootAttribute = true) =>
    $$"""
    {{(includeRootAttribute ? "using System;\npublic sealed class SpikeRootAttribute : Attribute;" : "")}}

    public static class Root
    {
        [SpikeRoot]
        public static void Start() => Middle.Run();
    }
    """;

static CSharpCompilation CompileOne(string assemblyName, string source, IEnumerable<MetadataReference> references) =>
    Compile(assemblyName, [source], references);

static CSharpCompilation Compile(string assemblyName, IEnumerable<string> sources, IEnumerable<MetadataReference> references) =>
    CSharpCompilation.Create(
        assemblyName,
        sources.Select((source, index) => CSharpSyntaxTree.ParseText(source, path: $"{assemblyName}.{index}.cs")),
        references,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
    );

static PortableExecutableReference EmitReference(CSharpCompilation compilation)
{
    using var stream = new MemoryStream();
    var result = compilation.Emit(stream);
    if (!result.Success)
    {
        throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
    }

    return MetadataReference.CreateFromImage(stream.ToArray());
}

[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class ReachabilitySpikeAnalyzer : DiagnosticAnalyzer
{
    internal const string ResultDiagnosticId = "SPIKE001";

    private static readonly DiagnosticDescriptor ResultDescriptor = new(
        ResultDiagnosticId,
        "Reachability spike result",
        "{0}",
        "Spike",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [ResultDescriptor];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var universe = CompilationUniverse.Create(context.Compilation);
        foreach (var root in RootMethods(context.Compilation))
        {
            var result = Traverse(root, universe, context.CancellationToken);
            var properties = ImmutableDictionary<string, string?>.Empty
                .Add("effects", result.Chains.Count.ToString())
                .Add("maxDepth", result.Chains.Count == 0 ? "-" : result.Chains.Max(chain => chain.Depth).ToString())
                .Add("chains", result.Chains.Count == 0 ? "-" : string.Join(" | ", result.Chains.Select(chain => chain.Text)))
                .Add("boundaries", result.Boundaries.Count == 0 ? "-" : string.Join(" | ", result.Boundaries));
            var message = $"effects={properties["effects"]}; boundaries={properties["boundaries"]}";
            context.ReportDiagnostic(
                Diagnostic.Create(ResultDescriptor, root.Locations.FirstOrDefault(), properties, message)
            );
        }
    }

    private static IEnumerable<IMethodSymbol> RootMethods(Compilation compilation)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration) is { } method && HasAttribute(method, "SpikeRootAttribute"))
                {
                    yield return method;
                }
            }
        }
    }

    private static TraversalResult Traverse(IMethodSymbol root, CompilationUniverse universe, CancellationToken cancellationToken)
    {
        var chains = new List<EffectChain>();
        var boundaries = new SortedSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        Walk(root, [ShortName(root)], depth: 0);
        return new TraversalResult(chains, boundaries.ToArray());

        void Walk(IMethodSymbol method, IReadOnlyList<string> path, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = $"{method.ContainingAssembly.Identity}:{method.GetDocumentationCommentId() ?? method.ToDisplayString()}";
            if (!visited.Add(key))
            {
                return;
            }

            if (!universe.TryGetBody(method, cancellationToken, out var body, out var model))
            {
                boundaries.Add(Boundary(method));
                return;
            }

            foreach (var invocationSyntax in body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetOperation(invocationSyntax, cancellationToken) is not IInvocationOperation invocation)
                {
                    continue;
                }

                var target = invocation.TargetMethod;
                var targetPath = path.Append(ShortName(target)).ToArray();
                if (HasAttribute(target, "SpikeEffectAttribute"))
                {
                    chains.Add(new EffectChain(depth + 1, string.Join(" -> ", targetPath)));
                    continue;
                }

                Walk(target, targetPath, depth + 1);
            }
        }
    }

    private static bool HasAttribute(IMethodSymbol method, string name) =>
        method.GetAttributes().Any(attribute => attribute.AttributeClass?.Name == name);

    private static string ShortName(IMethodSymbol method) => $"{method.ContainingType.Name}.{method.Name}";

    private static string Boundary(IMethodSymbol method)
    {
        var kind = method.ContainingType.TypeKind == TypeKind.Interface || method.IsAbstract ? "dispatch" : "metadata";
        return $"{kind}:{ShortName(method)}@{method.ContainingAssembly.Name}";
    }

    private sealed record EffectChain(int Depth, string Text);

    private sealed record TraversalResult(IReadOnlyList<EffectChain> Chains, IReadOnlyList<string> Boundaries);

    private sealed class CompilationUniverse
    {
        private readonly IReadOnlyDictionary<SyntaxTree, Compilation> _owners;

        private CompilationUniverse(IReadOnlyDictionary<SyntaxTree, Compilation> owners)
        {
            _owners = owners;
        }

        internal static CompilationUniverse Create(Compilation root)
        {
            var owners = new Dictionary<SyntaxTree, Compilation>();
            var pending = new Stack<Compilation>();
            var seen = new HashSet<Compilation>(ReferenceEqualityComparer.Instance);
            pending.Push(root);

            while (pending.TryPop(out var compilation))
            {
                if (!seen.Add(compilation))
                {
                    continue;
                }

                foreach (var tree in compilation.SyntaxTrees)
                {
                    owners[tree] = compilation;
                }

                foreach (var reference in compilation.References.OfType<CompilationReference>())
                {
                    pending.Push(reference.Compilation);
                }
            }

            return new CompilationUniverse(owners);
        }

        internal bool TryGetBody(
            IMethodSymbol method,
            CancellationToken cancellationToken,
            out SyntaxNode body,
            out SemanticModel model
        )
        {
            foreach (var reference in method.DeclaringSyntaxReferences)
            {
                var declaration = reference.GetSyntax(cancellationToken);
                if (!_owners.TryGetValue(declaration.SyntaxTree, out var compilation))
                {
                    continue;
                }

                body = declaration switch
                {
                    MethodDeclarationSyntax { Body: { } block } => block,
                    MethodDeclarationSyntax { ExpressionBody.Expression: { } expression } => expression,
                    _ => null!,
                };
                if (body is not null)
                {
                    model = compilation.GetSemanticModel(declaration.SyntaxTree);
                    return true;
                }
            }

            body = null!;
            model = null!;
            return false;
        }
    }
}
