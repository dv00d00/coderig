using System.Text.Json;
using Rig.Cli;
using Rig.Cli.CommandLine;
using Rig.Domain.Data;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Shouldly;

namespace Rig.Tests.Cli;

// `symbols` is the discovery step for every exact traversal. Its machine formats must expose the exact id
// and source location without human headings/footers, while preserving the human contract and filters.
public sealed class SymbolMachineFormatTests
{
    [Test]
    public async Task Tsv_has_a_named_full_fidelity_schema_and_keeps_truncation_off_stdout()
    {
        await using var fixture = await SymbolStore.CreateAsync();

        var (exit, stdout, stderr) = await fixture.RunAsync(
            "symbols",
            "BuildIndex",
            "--kind",
            "method",
            "--no-lambdas",
            "--limit",
            "1",
            "--format",
            "tsv"
        );

        exit.ShouldBe(0, stderr);
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[0].ShouldBe("id\tkind\tname\tsignature\tfile\tline\tassembly");
        var row = lines[1].Split('\t');
        row.Length.ShouldBe(7);
        row[0].ShouldStartWith("M:Demo.");
        row[1].ShouldBe("method");
        row[3].ShouldContain("BuildIndex");
        row[4].ShouldStartWith(fixture.Root);
        row[6].ShouldBe("Demo");
        stdout.ShouldNotContain("Symbols matching");
        stdout.ShouldNotContain("showing 1 of 2");
        stderr.ShouldContain("symbol results truncated");
        stderr.ShouldContain("showing 1 of 2");
    }

    [Test]
    public async Task Json_is_a_camel_case_envelope_with_total_and_truncation_metadata()
    {
        await using var fixture = await SymbolStore.CreateAsync();

        var (exit, stdout, stderr) = await fixture.RunAsync(
            "symbols",
            "BuildIndex",
            "--kind",
            "method",
            "--no-lambdas",
            "--limit",
            "1",
            "--format",
            "json"
        );

        exit.ShouldBe(0, stderr);
        stderr.ShouldBeEmpty("JSON carries its own truncation metadata");
        using var json = JsonDocument.Parse(stdout);
        var root = json.RootElement;
        root.GetProperty("query").GetString().ShouldBe("BuildIndex");
        root.GetProperty("kind").GetString().ShouldBe("method");
        root.GetProperty("shown").GetInt32().ShouldBe(1);
        root.GetProperty("total").GetInt32().ShouldBe(2);
        root.GetProperty("truncated").GetBoolean().ShouldBeTrue();
        var symbol = root.GetProperty("symbols")[0];
        symbol.GetProperty("id").GetString().ShouldStartWith("M:Demo.");
        symbol.GetProperty("signature").GetString().ShouldNotBeNull().ShouldContain("BuildIndex");
        symbol.GetProperty("file").GetString().ShouldStartWith(fixture.Root);
        symbol.GetProperty("assembly").GetString().ShouldBe("Demo");
        root.TryGetProperty("Query", out _).ShouldBeFalse("the JSON contract is camelCase");
    }

    [Test]
    public async Task Human_output_keeps_its_existing_heading_and_footer()
    {
        await using var fixture = await SymbolStore.CreateAsync();

        var (exit, stdout, stderr) = await fixture.RunAsync("symbols", "BuildIndex", "--no-lambdas", "--limit", "1");

        exit.ShouldBe(0, stderr);
        stdout.ShouldStartWith("Symbols matching 'BuildIndex'");
        stdout.ShouldContain("(showing 1 of 2 — use --limit to raise)");
        stdout.ShouldNotContain("id\tkind\tname");
    }

    private sealed class SymbolStore : IAsyncDisposable
    {
        private SymbolStore(string root) => Root = root;

        public string Root { get; }

        public static async Task<SymbolStore> CreateAsync()
        {
            var root = Directory.CreateTempSubdirectory("rig-symbol-format-").FullName;
            var symbols = new[]
            {
                Symbol("M:Demo.Alpha.BuildIndex(System.String)", "BuildIndex", "void BuildIndex(string value)", root, 10),
                Symbol("M:Demo.Beta.BuildIndex", "BuildIndex", "void BuildIndex()", root, 20),
                Symbol("M:Demo.Beta.BuildIndex~λ0", "BuildIndex~λ0", "lambda", root, 21),
            };
            var result = new AnalysisResult(
                SolutionPath: Path.Combine(root, "Demo.slnx"),
                SourceFiles: [],
                DiRegistrations: [],
                Symbols: symbols,
                References: [],
                TypeRelations: [],
                DispatchFacts: [],
                AllocationFacts: []
            );
            const string storeId = "symbols000001";
            var storeDir = StoreLayout.NewStoreDir(root, storeId);
            await using (var context = new RigDbContext(Path.Combine(storeDir, StoreLayout.DbFileName), pooling: false))
            {
                await Writes.SaveAsync(context, result);
            }

            StoreLayout.WriteLatestPointer(root, storeId);
            return new SymbolStore(root);
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
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            return ValueTask.CompletedTask;
        }

        private static SymbolFact Symbol(string id, string name, string signature, string root, int line) =>
            new(
                SymbolId: id,
                Kind: "method",
                Name: name,
                Namespace: "Demo",
                ContainingSymbolId: "T:Demo.Widget",
                Modifiers: "public",
                TypeKind: "",
                Signature: signature,
                FilePath: Path.Combine(root, $"{line}.cs"),
                Line: line,
                EndLine: line + 1,
                DefiningAssembly: "Demo",
                IsOverride: false
            );
    }
}
