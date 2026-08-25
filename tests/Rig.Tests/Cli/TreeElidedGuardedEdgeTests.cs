using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rig.Analysis;
using Rig.Analysis.Extraction;
using Rig.Cli;
using Rig.Cli.CommandLine;
using Rig.Cli.Services;
using Rig.Cli.Web;
using Rig.Domain.Data;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Shouldly;

namespace Rig.Tests.Cli;

// A guard is EDGE information: `if (p) F(); else F();` is TWO call edges to one callee under complementary
// predicates, and the pair together says the callee runs unconditionally. The effect-pruning display path used
// to drop one of them — the second occurrence renders as "⋯elided", answered the prune predicate from its own
// (always empty) child list, tested effectless, and vanished — leaving `F ⎇ [p]`: a must-run call rendered as
// conditional, with no ×N calls and no ⋯elided marker to hint at the loss. `--format tsv` was right all along
// because it does not prune, so display and tsv disagreed — the failure shape fa1cc1ce fenced off once already.
//
// Chosen semantics: ONE RENDERED EDGE PER DISTINCT GUARD. Sibling edges are never merged across differing
// guards (FactPathFinder.BuildTree's sibling collapse already keys on EnclosingGuards, so `×N calls` only ever
// aggregates identical conditionality) and the prune must not silently delete one either. An unconditional
// call must never render as guarded.
//
// The fixture is deliberately two hops deep — `Handle -> Emit -> Write -> File.AppendAllText` — because the
// bug needs an elided node with NO effect of its OWN whose subtree reaches one: that is exactly the node the
// old rule mis-answered. If Emit held the io:write itself, the old prune would have kept it and the bug would
// not reproduce. Every expected string below is the ACTUAL output of these commands on this fixture.
public sealed class TreeElidedGuardedEdgeTests
{
    // `Emit()` on BOTH arms of one if/else → two edges carrying `IsPriority` and `!IsPriority`.
    private const string Source = """
        namespace App
        {
            public sealed class Svc
            {
                public bool IsPriority;

                public void Handle()
                {
                    if (IsPriority)
                    {
                        Emit();
                    }
                    else
                    {
                        Emit();
                    }
                }

                private void Emit() => Write();

                private void Write() => System.IO.File.AppendAllText("audit.log", "record");
            }
        }
        """;

    private static AnalysisResult Analyze(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "Snippet.cs");
        var compilation = CSharpCompilation.Create(
            "Snippet",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        var model = compilation.GetSemanticModel(tree);
        var extracted = FactExtractor.Extract(
            new SourceModel("Snippet", "Snippet.cs", tree, tree.GetRoot(), model),
            new SymbolStringCache()
        );
        return new AnalysisResult(
            SolutionPath: "Snippet",
            SourceFiles: [],
            DiRegistrations: [],
            Symbols: extracted.Symbols,
            References: extracted.References,
            TypeRelations: extracted.TypeRelations,
            DispatchFacts: extracted.Dispatch
        );
    }

    // One temp workspace holding one store built from the fixture above.
    private sealed class Workspace : IAsyncDisposable
    {
        internal required string WorkingDirectory { get; init; }

        internal static async Task<Workspace> CreateAsync()
        {
            var wd = Path.Combine(Path.GetTempPath(), $"rig-tree-elided-guard-{Guid.NewGuid():n}");
            Directory.CreateDirectory(wd);
            var dir = StoreLayout.NewStoreDir(wd, "elidedguard");
            await using var ctx = new RigDbContext(Path.Combine(dir, StoreLayout.DbFileName), pooling: false);
            await Writes.SaveAsync(ctx, Analyze(Source), provenance: null);
            return new Workspace { WorkingDirectory = wd };
        }

        internal async Task<List<string>> RunAsync(params string[] args)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var exit = await CliApplication.RunAsync([.. args], output, error, WorkingDirectory);
            exit.ShouldBe(0, error.ToString());
            return output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                Directory.Delete(WorkingDirectory, recursive: true);
            }
            catch
            { /* best-effort cleanup */
            }

            return ValueTask.CompletedTask;
        }
    }

    [Test]
    public async Task Both_complementary_branch_edges_render_in_the_pretty_tree()
    {
        await using var ws = await Workspace.CreateAsync();

        var lines = await ws.RunAsync("tree", "App.Svc.Handle", "--guards");

        // THE REGRESSION. Pre-fix this was three lines, the `!IsPriority` edge silently absent:
        //   Svc.Handle
        //   └─ Svc.Emit ⎇ [IsPriority]
        //      └─ Svc.Write  {📁 io:write IO.File}
        lines.ShouldBe([
            "Svc.Handle",
            "├─ Svc.Emit ⎇ [IsPriority]",
            "│  └─ Svc.Write  {📁 io:write IO.File}",
            "└─ Svc.Emit ⎇ [!IsPriority] ⋯elided",
        ]);

        // Said as the invariant rather than as a golden string: a callee reached on BOTH arms of one if/else
        // must not be presented under one arm's condition alone. One edge per distinct guard, both polarities.
        var edges = lines.Where(l => l.Contains("Svc.Emit", StringComparison.Ordinal)).ToList();
        edges.Count.ShouldBe(2);
        edges.Count(l => l.Contains("⎇ [IsPriority]", StringComparison.Ordinal)).ShouldBe(1);
        edges.Count(l => l.Contains("⎇ [!IsPriority]", StringComparison.Ordinal)).ShouldBe(1);
        // Nor may the pair be merged into one line: `×N calls` aggregates identical conditionality only, so a
        // count here would assert that two DIFFERENT guards are the same guard.
        lines.ShouldNotContain(l => l.Contains("calls", StringComparison.Ordinal));
    }

    // The depth-1 (name, guard) multiset a surface reports for the fixture. `--format tsv` is the reference:
    // it never prunes, and it was the only surface that stayed correct through the bug.
    private static List<string> Depth1FromTsv(List<string> tsvRows) =>
        tsvRows
            .Select(r => r.Split('\t'))
            .Where(f => f[0] == "1")
            .Select(f => $"{f[1].Split('.')[^1]}|{f[^1]}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

    [Test]
    public async Task The_pretty_tree_tsv_llm_and_web_mapper_agree_on_the_depth_1_edge_set()
    {
        // The invariant fa1cc1ce established — a display surface may not disagree with `--format tsv` about
        // WHICH edges exist — asserted across all four surfaces at once, because the prune rule that broke it
        // was implemented four times (pretty renderer, llm renderer, web DTO consumer, SPA).
        await using var ws = await Workspace.CreateAsync();

        var expected = Depth1FromTsv(await ws.RunAsync("tree", "App.Svc.Handle", "--guards", "--format", "tsv"));
        // Reference set, from the actual tsv rows: both Emit edges, one per branch polarity.
        expected.ShouldBe(["Emit|!IsPriority", "Emit|IsPriority"]);

        // Pretty tree: depth-1 lines are the ones whose connector sits at column 0.
        var pretty = (await ws.RunAsync("tree", "App.Svc.Handle", "--guards"))
            .Where(l => l.StartsWith("├─ ", StringComparison.Ordinal) || l.StartsWith("└─ ", StringComparison.Ordinal))
            .Select(l => l[3..])
            .Select(l => $"{l.Split(' ')[0].Split('.')[^1]}|{GuardOf(l)}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        pretty.ShouldBe(expected);

        // llm / llm-ids: depth is column 0 / column 2, guards is the trailing column under --guards.
        var llm = (await ws.RunAsync("tree", "App.Svc.Handle", "--guards", "--format", "llm"))
            .Skip(1)
            .Select(r => r.Split('\t'))
            .Where(f => f[0] == "1")
            .Select(f => $"{f[1].Split('.')[^1]}|{f[^1]}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        llm.ShouldBe(expected);

        var llmIds = (await ws.RunAsync("tree", "App.Svc.Handle", "--guards", "--format", "llm-ids"))
            .Skip(1)
            .Select(r => r.Split('\t'))
            .Where(f => f[2] == "1")
            .Select(f => $"{f[3].Split('.')[^1]}|{f[^1]}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        llmIds.ShouldBe(expected);

        // Web: the DTO the SPA consumes. TreeMapper does not prune — it must ship BOTH edges (the browser
        // prunes client-side, with the same corrected rule, in wwwroot/components.js).
        var built = await TreeQueryService.BuildAsync(ws.WorkingDirectory, "App.Svc.Handle");
        var dto = TreeMapper.ToResponse("App.Svc.Handle", built.Roots, built.Effects, built.Locations, built.EffectEmoji, built.Render);
        var web = dto
            .Roots.SelectMany(r => r.Children)
            .Select(c => $"{c.Name.Split('.')[^1]}|{c.Guards}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        web.ShouldBe(expected);
    }

    // The ⎇ [...] predicate of one rendered tree line ("" when the line carries no guard glyph).
    private static string GuardOf(string line)
    {
        var open = line.IndexOf("⎇ [", StringComparison.Ordinal);
        if (open < 0)
        {
            return "";
        }

        var close = line.IndexOf(']', open);
        return line[(open + 3)..close];
    }

    [Test]
    public async Task An_elided_edge_that_reaches_no_effect_is_still_pruned()
    {
        // The fix must not become "never prune an elided edge" — that would flood the effect-pruned tree with
        // every repeated pure-computation call. `Tag()` is called on both arms and reaches nothing effectful,
        // so BOTH its edges stay pruned while the effectful `Emit` pair renders.
        var wd = Path.Combine(Path.GetTempPath(), $"rig-tree-elided-guard-{Guid.NewGuid():n}");
        Directory.CreateDirectory(wd);
        try
        {
            var dir = StoreLayout.NewStoreDir(wd, "elidedguardpure");
            await using (var ctx = new RigDbContext(Path.Combine(dir, StoreLayout.DbFileName), pooling: false))
            {
                await Writes.SaveAsync(
                    ctx,
                    Analyze(
                        """
                        namespace App
                        {
                            public sealed class Svc
                            {
                                public bool IsPriority;

                                public void Handle()
                                {
                                    if (IsPriority)
                                    {
                                        Tag();
                                        Emit();
                                    }
                                    else
                                    {
                                        Tag();
                                        Emit();
                                    }
                                }

                                private void Tag() => Label();

                                private int Label() => 42;

                                private void Emit() => Write();

                                private void Write() => System.IO.File.AppendAllText("audit.log", "record");
                            }
                        }
                        """
                    ),
                    provenance: null
                );
            }

            var output = new StringWriter();
            var error = new StringWriter();
            (await CliApplication.RunAsync(["tree", "App.Svc.Handle", "--guards"], output, error, wd)).ShouldBe(0, error.ToString());
            var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();

            lines.ShouldContain(l =>
                l.Contains("Svc.Emit", StringComparison.Ordinal) && l.Contains("⎇ [IsPriority]", StringComparison.Ordinal)
            );
            lines.ShouldContain(l =>
                l.Contains("Svc.Emit", StringComparison.Ordinal) && l.Contains("⎇ [!IsPriority]", StringComparison.Ordinal)
            );
            lines.ShouldNotContain(l => l.Contains("Svc.Tag", StringComparison.Ordinal));
            lines.ShouldNotContain(l => l.Contains("Svc.Label", StringComparison.Ordinal));
        }
        finally
        {
            try
            {
                Directory.Delete(wd, recursive: true);
            }
            catch
            { /* best-effort cleanup */
            }
        }
    }
}
