using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rig.Analysis.Inventory;
using Shouldly;

namespace Rig.Tests.Analysis;

public sealed class CompilationDiagnosticLocationTests
{
    [Test]
    public void Detached_replacement_tree_keeps_its_explicit_file_path()
    {
        const string path = "/src/Edited.cs";
        var tree = CSharpSyntaxTree.ParseText("class Edited { void Run() { Missing(); } }", path: path);
        var compilation = CSharpCompilation.Create("Edited", [tree], [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
        compilation.GetDiagnostics().ShouldContain(diagnostic => diagnostic.Id == "CS0103");
        var diagnostic = compilation.GetDiagnostics().First(diagnostic => diagnostic.Id == "CS0103");
        using var workspace = new AdhocWorkspace();
        var unrelatedProject = workspace.AddProject("Host", LanguageNames.CSharp);

        var resolved = SolutionSourceLoader.DiagnosticFilePath(unrelatedProject, diagnostic);
        var collector = new CompilationHealthCollector();
        collector.AddError(diagnostic, resolved);
        var health = collector.Build();

        resolved.ShouldBe(path);
        health.Files.ShouldHaveSingleItem().FilePath.ShouldBe(path);
        health.UnlocatedErrorCount.ShouldBe(0);
    }
}
