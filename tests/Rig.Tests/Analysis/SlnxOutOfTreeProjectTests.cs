using System.Diagnostics;
using Rig.Analysis;
using Rig.Analysis.Inventory;
using Rig.Analysis.Rules;
using Rig.Domain.Data;
using Shouldly;

namespace Rig.Tests.Analysis;

public sealed class SlnxOutOfTreeProjectTests
{
    [Test]
    public async Task Slnx_project_outside_solution_directory_contributes_symbols_and_cross_project_call_edges()
    {
        var root = Directory.CreateTempSubdirectory("rig-slnx-sibling-").FullName;
        try
        {
            var solutionDirectory = Directory.CreateDirectory(Path.Combine(root, "Solution")).FullName;
            var mainDirectory = Directory.CreateDirectory(Path.Combine(solutionDirectory, "Main")).FullName;
            var solutionSharedDirectory = Directory.CreateDirectory(Path.Combine(solutionDirectory, "Shared")).FullName;
            var siblingDirectory = Directory.CreateDirectory(Path.Combine(root, "Sibling")).FullName;
            var mainProject = Path.Combine(mainDirectory, "Main.csproj");
            var siblingProject = Path.Combine(siblingDirectory, "Sibling.csproj");

            await File.WriteAllTextAsync(
                siblingProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                </Project>
                """
            );
            await File.WriteAllTextAsync(
                Path.Combine(siblingDirectory, "SiblingApi.cs"),
                "namespace Sibling; public static class SiblingApi { public static void Touch() { } }"
            );
            await File.WriteAllTextAsync(
                mainProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../../Sibling/Sibling.csproj" />
                    <Compile Include="$(SolutionDir)Shared/SolutionBound.cs" Link="SolutionBound.cs" />
                  </ItemGroup>
                </Project>
                """
            );
            await File.WriteAllTextAsync(
                Path.Combine(mainDirectory, "Caller.cs"),
                "namespace Main; public static class Caller { public static void Call() => Sibling.SiblingApi.Touch(); }"
            );
            await File.WriteAllTextAsync(
                Path.Combine(solutionSharedDirectory, "SolutionBound.cs"),
                "namespace Main; public sealed class LoadedThroughOriginalSolutionDir { }"
            );

            var solutionPath = Path.Combine(solutionDirectory, "OutOfTree.slnx");
            await File.WriteAllTextAsync(
                solutionPath,
                """
                <Solution>
                  <Project Path="Main/Main.csproj" />
                  <Project Path="../Sibling/Sibling.csproj" />
                </Solution>
                """
            );
            var normalizedTempPrefix = SolutionSourceLoader.NormalizedSlnxTempFilePrefix(solutionPath);
            NormalizedTempFiles(normalizedTempPrefix).ShouldBeEmpty();
            await RunDotnetAsync(["build", solutionPath, "--nologo"], solutionDirectory);

            var result = await SolutionAnalyzer.AnalyzeAsync(solutionPath, RuleSetLoader.Load(solutionDirectory), parallelism: 1);

            result.Symbols.ShouldNotBeNull().ShouldContain(symbol => symbol.SymbolId == "T:Main.Caller");
            result.Symbols.ShouldNotBeNull().ShouldContain(symbol => symbol.SymbolId == "T:Main.LoadedThroughOriginalSolutionDir");
            result.Symbols.ShouldNotBeNull().ShouldContain(symbol => symbol.SymbolId == "T:Sibling.SiblingApi");
            result
                .References.ShouldNotBeNull()
                .ShouldContain(reference =>
                    reference.RefKind == RefKinds.Invocation
                    && reference.EnclosingSymbolId == "M:Main.Caller.Call"
                    && reference.TargetSymbolId == "M:Sibling.SiblingApi.Touch"
                    && reference.TargetInSource
                );
            NormalizedTempFiles(normalizedTempPrefix).ShouldBeEmpty();
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static IEnumerable<string> NormalizedTempFiles(string prefix) =>
        Directory.EnumerateFiles(Path.GetDirectoryName(prefix)!, Path.GetFileName(prefix) + "*.slnx");

    private static async Task RunDotnetAsync(string[] arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet process.");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0, output + Environment.NewLine + error);
    }

    private static void DeleteDirectory(string directory)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 2)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 2)
            {
                Thread.Sleep(100);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }
}
