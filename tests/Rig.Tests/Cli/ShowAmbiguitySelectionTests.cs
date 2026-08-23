using Rig.Cli;
using Rig.Cli.CommandLine;
using Rig.Domain.Data;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Shouldly;

namespace Rig.Tests.Cli;

// Source lookup should be precision-first. A partial pattern spanning unrelated declarations must not dump
// all of them; it lists exact follow-ups, while --all explicitly restores the former union behavior.
public sealed class ShowAmbiguitySelectionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Ambiguous_show_fails_closed_with_exact_rerunnable_candidates(bool tsv)
    {
        await using var fixture = await ShowStore.CreateAsync();
        var args = tsv ? new[] { "show", "BuildIndex", "--format", "tsv" } : new[] { "show", "BuildIndex" };

        var (exit, stdout, stderr) = await fixture.RunAsync(args);

        exit.ShouldBe(1);
        stdout.ShouldBeEmpty("an ambiguous source request must not emit partial human or TSV data");
        stderr.ShouldContain("Ambiguous symbol pattern 'BuildIndex' matched 2 distinct symbols");
        stderr.ShouldContain("rig show \"Demo.Alpha.BuildIndex\"");
        stderr.ShouldContain("rig show \"Demo.Beta.BuildIndex\"");
        stderr.ShouldContain("pass --all");
    }

    [Test]
    public async Task All_explicitly_restores_multi_declaration_rendering()
    {
        await using var fixture = await ShowStore.CreateAsync();

        var (exit, stdout, stderr) = await fixture.RunAsync("show", "BuildIndex", "--all");

        exit.ShouldBe(0, stderr);
        stdout.ShouldContain("Alpha.BuildIndex");
        stdout.ShouldContain("Beta.BuildIndex");
        stderr.ShouldContain("results span ALL of them");
    }

    [Test]
    public async Task Exact_fqn_selects_one_conceptual_target()
    {
        await using var fixture = await ShowStore.CreateAsync();

        var (exit, stdout, stderr) = await fixture.RunAsync("show", "Demo.Alpha.BuildIndex");

        exit.ShouldBe(0, stderr);
        stdout.ShouldContain("Alpha.BuildIndex");
        stdout.ShouldNotContain("Beta.BuildIndex");
        stderr.ShouldNotContain("Ambiguous symbol pattern");
    }

    [Test]
    public async Task Overloads_share_one_conceptual_target_but_exact_docid_selects_one_overload()
    {
        await using var fixture = await ShowStore.CreateAsync();

        var (fqnExit, fqnOut, fqnErr) = await fixture.RunAsync("show", "Demo.Overloads.Save");
        fqnExit.ShouldBe(0, fqnErr);
        fqnErr.ShouldNotContain("Ambiguous symbol pattern");
        fqnOut.Split("Overloads.Save", StringSplitOptions.None).Length.ShouldBe(3, "both overload declarations render");

        var exactId = "M:Demo.Overloads.Save(System.String)";
        var (idExit, idOut, idErr) = await fixture.RunAsync("show", exactId, "--format", "tsv");
        idExit.ShouldBe(0, idErr);
        idOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).ShouldAllBe(line => line.StartsWith(exactId + "\t"));
        idOut.ShouldNotContain("System.Int32");
    }

    private sealed class ShowStore : IAsyncDisposable
    {
        private ShowStore(string root) => Root = root;

        public string Root { get; }

        public static async Task<ShowStore> CreateAsync()
        {
            var root = Directory.CreateTempSubdirectory("rig-show-selection-").FullName;
            var symbols = new[]
            {
                Symbol("M:Demo.Alpha.BuildIndex", "BuildIndex", root, 10),
                Symbol("M:Demo.Beta.BuildIndex", "BuildIndex", root, 20),
                Symbol("M:Demo.Overloads.Save(System.String)", "Save", root, 30),
                Symbol("M:Demo.Overloads.Save(System.Int32)", "Save", root, 40),
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
            const string storeId = "show00000001";
            var storeDir = StoreLayout.NewStoreDir(root, storeId);
            await using (var context = new RigDbContext(Path.Combine(storeDir, StoreLayout.DbFileName), pooling: false))
            {
                await Writes.SaveAsync(context, result);
            }

            StoreLayout.WriteLatestPointer(root, storeId);
            return new ShowStore(root);
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

        private static SymbolFact Symbol(string id, string name, string root, int line) =>
            new(
                SymbolId: id,
                Kind: "method",
                Name: name,
                Namespace: "Demo",
                ContainingSymbolId: "T:Demo.Widget",
                Modifiers: "public",
                TypeKind: "",
                Signature: name + "()",
                FilePath: Path.Combine(root, "Missing.cs"),
                Line: line,
                EndLine: line + 1,
                DefiningAssembly: "Demo",
                IsOverride: false
            );
    }
}
