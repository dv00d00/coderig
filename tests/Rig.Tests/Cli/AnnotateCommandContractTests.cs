using System.Diagnostics;
using Rig.Cli;
using Rig.Cli.CommandLine;
using Rig.Domain.Data;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Shouldly;

namespace Rig.Tests.Cli;

public sealed class AnnotateCommandContractTests
{
    [Test]
    public async Task Bare_annotate_renders_the_committed_file_through_its_final_line()
    {
        await using var fixture = await AnnotateStore.CreateAsync();

        var result = await fixture.RunAsync("annotate", fixture.FilePath, "--format", "tsv");

        result.Exit.ShouldBe(0, result.Err);
        var sourceRows = SourceRows(result.Out);
        sourceRows.Count.ShouldBeGreaterThan(1);
        sourceRows.Count.ShouldBe(20);
        sourceRows[^1].ShouldBe("src\t20\tworktree\t\t}");
    }

    [Test]
    public async Task Contradictory_range_fails_before_trying_to_open_a_store()
    {
        var root = Directory.CreateTempSubdirectory("rig-annotate-range-").FullName;
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();

            var exit = await CliApplication.RunAsync(
                ["annotate", "NeverIndexed.cs", "--from", "19", "--to", "7", "--format", "tsv"],
                output,
                error,
                root
            );

            exit.ShouldNotBe(0);
            error.ToString().ShouldContain("--from 19");
            error.ToString().ShouldContain("--to 7");
            SourceRows(output.ToString()).ShouldBeEmpty();
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task Missing_method_fails_with_pattern_and_declared_candidates()
    {
        await using var fixture = await AnnotateStore.CreateAsync();

        var result = await fixture.RunAsync("annotate", fixture.FilePath, "--method", "DefinitelyMissing", "--format", "tsv");

        result.Exit.ShouldNotBe(0);
        result.Err.ShouldContain("DefinitelyMissing");
        result.Err.ShouldContain("Effectful");
        result.Err.ShouldContain("Quiet");
        result.Err.ShouldContain("Last");
        SourceRows(result.Out).ShouldBeEmpty();
    }

    [Test]
    public async Task Declared_but_effectless_method_fails_with_the_range_escape_hatch()
    {
        await using var fixture = await AnnotateStore.CreateAsync();

        var result = await fixture.RunAsync("annotate", fixture.FilePath, "--method", "Quiet", "--format", "tsv");

        result.Exit.ShouldNotBe(0);
        result.Err.ShouldContain("Quiet");
        result.Err.ShouldContain("no effects in this store");
        result.Err.ShouldContain("--from/--to");
        SourceRows(result.Out).ShouldBeEmpty();
    }

    [Test]
    public async Task Effectful_method_renders_only_its_declared_window()
    {
        await using var fixture = await AnnotateStore.CreateAsync();

        var result = await fixture.RunAsync("annotate", fixture.FilePath, "--method", "Effectful", "--format", "tsv");

        result.Exit.ShouldBe(0, result.Err);
        var sourceRows = SourceRows(result.Out);
        sourceRows.Count.ShouldBe(4);
        sourceRows[0].ShouldStartWith("src\t7\tworktree\t");
        sourceRows.ShouldContain(row => row == "src\t9\tworktree\tio!\t        File.WriteAllText(\"out.txt\", \"value\");");
        sourceRows[^1].ShouldStartWith("src\t10\tworktree\t");
        sourceRows.ShouldNotContain(row => row.StartsWith("src\t12\t", StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> SourceRows(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(line => line.StartsWith("src\t", StringComparison.Ordinal)).ToArray();

    private sealed class AnnotateStore : IAsyncDisposable
    {
        private const string EffectfulId = "M:Demo.Work.Effectful";
        private const string QuietId = "M:Demo.Work.Quiet";
        private const string LastId = "M:Demo.Work.Last";

        private static readonly string[] Source =
        [
            "using System.IO;",
            "",
            "namespace Demo;",
            "",
            "public sealed class Work",
            "{",
            "    public void Effectful()",
            "    {",
            "        File.WriteAllText(\"out.txt\", \"value\");",
            "    }",
            "",
            "    public void Quiet()",
            "    {",
            "        var answer = 42;",
            "    }",
            "",
            "    public void Last()",
            "    {",
            "    }",
            "}",
        ];

        private AnnotateStore(string root, string filePath)
        {
            Root = root;
            FilePath = filePath;
        }

        public string Root { get; }

        public string FilePath { get; }

        public static async Task<AnnotateStore> CreateAsync()
        {
            var root = Directory.CreateTempSubdirectory("rig-annotate-contract-").FullName;
            var filePath = Path.Combine(root, "Work.cs");
            File.WriteAllText(filePath, string.Join('\n', Source) + "\n");
            Git(root, "init", "-q");
            Git(root, "add", "Work.cs");
            Git(root, "-c", "user.email=rig@test", "-c", "user.name=rig", "commit", "-q", "-m", "fixture");
            var commit = Git(root, "rev-parse", "HEAD");

            var symbols = new[]
            {
                Symbol(EffectfulId, "Effectful", filePath, line: 7, endLine: 10),
                Symbol(QuietId, "Quiet", filePath, line: 12, endLine: 15),
                Symbol(LastId, "Last", filePath, line: 17, endLine: 19),
            };
            var result = new AnalysisResult(
                SolutionPath: Path.Combine(root, "Demo.slnx"),
                SourceFiles: [new SourceFileInfo("Demo", filePath, "indexed", "high", "project", "", "")],
                DiRegistrations: [],
                Symbols: symbols,
                References:
                [
                    new ReferenceFact(
                        TargetSymbolId: "M:System.IO.File.WriteAllText(System.String,System.String)",
                        RefKind: RefKinds.Invocation,
                        EnclosingSymbolId: EffectfulId,
                        TargetAssembly: "System.Private.CoreLib",
                        TargetInSource: false,
                        FilePath: filePath,
                        Line: 9
                    ),
                ],
                TypeRelations: [],
                DispatchFacts: [],
                AllocationFacts: []
            );

            var storeId = commit[..12];
            var storeDirectory = StoreLayout.NewStoreDir(root, storeId);
            await using (var context = new RigDbContext(Path.Combine(storeDirectory, StoreLayout.DbFileName), pooling: false))
            {
                await Writes.SaveAsync(context, result, provenance: new GitProvenance(commit, "main", Dirty: false));
            }

            StoreLayout.WriteLatestPointer(root, storeId);
            return new AnnotateStore(root, filePath);
        }

        public async Task<(int Exit, string Out, string Err)> RunAsync(params string[] args)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var exit = await CliApplication.RunAsync(args, output, error, Root);
            return (exit, output.ToString(), error.ToString());
        }

        public ValueTask DisposeAsync()
        {
            TryDelete(Root);
            return ValueTask.CompletedTask;
        }

        private static SymbolFact Symbol(string id, string name, string filePath, int line, int endLine) =>
            new(
                SymbolId: id,
                Kind: "method",
                Name: name,
                Namespace: "Demo",
                ContainingSymbolId: "T:Demo.Work",
                Modifiers: "public",
                TypeKind: "",
                Signature: $"void {name}()",
                FilePath: filePath,
                Line: line,
                EndLine: endLine,
                DefiningAssembly: "Demo",
                IsOverride: false
            );

        private static string Git(string workingDirectory, params string[] args)
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in args)
            {
                psi.ArgumentList.Add(argument);
            }

            using var process = Process.Start(psi).ShouldNotBeNull();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            process.ExitCode.ShouldBe(0, $"git {string.Join(' ', args)}: {stdout}{stderr}");
            return stdout.Trim();
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
