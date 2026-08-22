using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Rig.Analysis.Extraction;
using Rig.Domain;
using Rig.Domain.Data;

namespace Rig.Analysis.Inventory;

// Collapses one live Roslyn project into immutable, Roslyn-free per-emitter shards. Each shard uses
// ProjectContentHash's full sorted multiset (not XOR), and the project aggregate repeats that operation over
// shard hashes. The retained path is replacement provenance only and deliberately is not part of the aggregate.
internal static class ProjectSurfaceBuilder
{
    internal static ProjectSurfaceShard BuildEmitter(SourceModel source, SourceExtractionResult facts)
    {
        var items = new List<string>(facts.Symbols.Count + facts.TypeRelations.Count + facts.Dispatch.Count + 2);

        foreach (var symbol in facts.Symbols)
        {
            if (symbol.SurfaceHash.Length == 0)
            {
                continue;
            }
            items.Add(
                Item(
                    "sym",
                    symbol.SymbolId,
                    symbol.Kind,
                    symbol.Modifiers,
                    symbol.IsOverride ? "1" : "0",
                    symbol.IsIterator ? "1" : "0",
                    symbol.SurfaceHash
                )
            );
        }

        foreach (var relation in facts.TypeRelations)
        {
            items.Add(Item("rel", relation.TypeSymbolId, relation.RelationKind, relation.RelatedSymbolId));
        }

        foreach (var dispatch in facts.Dispatch)
        {
            items.Add(Item("disp", dispatch.SourceMember, dispatch.Kind, dispatch.TargetMember));
        }

        if (source.Root is CompilationUnitSyntax unit)
        {
            foreach (var usingDirective in unit.DescendantNodes().OfType<UsingDirectiveSyntax>())
            {
                items.Add(Item("using", SurfaceHashing.Tokens(usingDirective)));
            }
            foreach (var externAlias in unit.DescendantNodes().OfType<ExternAliasDirectiveSyntax>())
            {
                items.Add(Item("extern", SurfaceHashing.Tokens(externAlias)));
            }
        }

        if (source.IsGenerated)
        {
            var hint = Path.GetFileName(source.FilePath.Replace('\\', '/'));
            items.Add(Item("gen", hint, SurfaceHashing.Tokens(source.Root)));
        }

        return new ProjectSurfaceShard(source.FilePath, source.IsGenerated, ProjectContentHash.Compute(items));
    }

    internal static ProjectSurfaceShard BuildMeta(CSharpParseOptions? parseOptions, Compilation compilation)
    {
        var meta = new List<string>();
        var assemblyAttributes = compilation.Assembly.GetAttributes();
        for (var i = 0; i < assemblyAttributes.Length; i++)
        {
            meta.Add(Item("asmattr", i.ToString(CultureInfo.InvariantCulture), Attribute(assemblyAttributes[i])));
        }
        var moduleAttributes = compilation.SourceModule.GetAttributes();
        for (var i = 0; i < moduleAttributes.Length; i++)
        {
            meta.Add(Item("modattr", i.ToString(CultureInfo.InvariantCulture), Attribute(moduleAttributes[i])));
        }

        var options = compilation.Options as CSharpCompilationOptions;
        meta.Add(
            Item(
                "opts",
                parseOptions?.LanguageVersion.ToString() ?? "",
                options?.NullableContextOptions.ToString() ?? "",
                options?.AllowUnsafe == true ? "1" : "0",
                options?.OutputKind.ToString() ?? "",
                parseOptions is null ? "" : string.Join(",", parseOptions.PreprocessorSymbolNames.OrderBy(s => s, StringComparer.Ordinal))
            )
        );
        return new ProjectSurfaceShard("", false, ProjectContentHash.Compute(meta));
    }

    internal static string Aggregate(IEnumerable<ProjectSurfaceShard> shards) =>
        ProjectContentHash.Compute(shards.Select(s => s.SurfaceHash));

    public static ProjectSurfaceSnapshot Build(
        string projectName,
        string projectFilePath,
        CSharpParseOptions? parseOptions,
        Compilation compilation,
        IReadOnlyList<SourceModel> sources,
        IReadOnlyList<SourceExtractionResult> extractions
    )
    {
        if (sources.Count != extractions.Count)
        {
            throw new ArgumentException("Surface sources and extraction results must be positionally aligned.");
        }

        var assemblyName = compilation.AssemblyName ?? projectName;
        var shards = new List<ProjectSurfaceShard>(sources.Count + 1);
        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            var facts = extractions[i];
            shards.Add(BuildEmitter(source, facts));
        }

        // Project-wide inputs have no single emitter. Keeping them as an explicit meta shard lets Slice 5B
        // recompute/replace them as a unit after Roslyn refreshes the project's compilation.
        shards.Add(BuildMeta(parseOptions, compilation));

        return new ProjectSurfaceSnapshot(
            ProjectName: projectName,
            ProjectFilePath: projectFilePath,
            AssemblyName: assemblyName,
            Shards: shards,
            SurfaceHash: Aggregate(shards)
        );
    }

    private static string Attribute(AttributeData attribute)
    {
        // AttributeData.ToString is semantic (resolved attribute type + evaluated constructor/named values),
        // unlike source tokens, so an alias or const-backed value cannot leave the project hash unchanged.
        return attribute.ToString() ?? "";
    }

    private static string Item(params string[] fields)
    {
        var result = new StringBuilder();
        foreach (var field in fields)
        {
            result.Append(field.Length.ToString(CultureInfo.InvariantCulture));
            result.Append(':');
            result.Append(field);
        }
        return result.ToString();
    }
}
