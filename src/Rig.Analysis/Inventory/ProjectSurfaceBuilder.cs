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
                // Using/extern directives participate even though they emit no symbol. A file-local alias
                // can change the semantic return/member type while both declaration tokens and DocID stay
                // unchanged; a global using can additionally rebind sibling files.
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
                // Generator hint paths can be rooted differently by different hosts. The hint filename plus
                // token hash is checkout-independent; duplicate hints remain duplicate multiset items.
                var hint = Path.GetFileName(source.FilePath.Replace('\\', '/'));
                items.Add(Item("gen", hint, SurfaceHashing.Tokens(source.Root)));
            }

            shards.Add(
                new ProjectSurfaceShard(
                    EmitterFilePath: source.FilePath,
                    IsGenerated: source.IsGenerated,
                    SurfaceHash: ProjectContentHash.Compute(items)
                )
            );
        }

        // Project-wide inputs have no single emitter. Keeping them as an explicit meta shard lets Slice 5B
        // recompute/replace them as a unit after Roslyn refreshes the project's compilation.
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

        var parse = parseOptions;
        var options = compilation.Options as CSharpCompilationOptions;
        meta.Add(
            Item(
                "opts",
                parse?.LanguageVersion.ToString() ?? "",
                options?.NullableContextOptions.ToString() ?? "",
                options?.AllowUnsafe == true ? "1" : "0",
                options?.OutputKind.ToString() ?? "",
                parse is null ? "" : string.Join(",", parse.PreprocessorSymbolNames.OrderBy(s => s, StringComparer.Ordinal))
            )
        );
        shards.Add(new ProjectSurfaceShard(EmitterFilePath: "", IsGenerated: false, SurfaceHash: ProjectContentHash.Compute(meta)));

        return new ProjectSurfaceSnapshot(
            ProjectName: projectName,
            ProjectFilePath: projectFilePath,
            AssemblyName: assemblyName,
            Shards: shards,
            SurfaceHash: ProjectContentHash.Compute(shards.Select(s => s.SurfaceHash))
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
