using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rig.Analysis;
using Rig.Analysis.Extraction;
using Rig.Cli;
using Rig.Cli.CommandLine;
using Rig.Domain.Data;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Shouldly;

namespace Rig.Tests.Cli;

// `rig impact` end-to-end on the PREDICATE-ONLY change class: two stores whose only difference is the
// condition gating a call. Nothing is added or removed, so the entry-point diff, the per-EP effect footprint
// and the reach-set diff are ALL empty — which is exactly why `--expect-no-effect-change` passed MedDBase MR
// !11025 while an audit silently stopped firing for document rows.
//
// The fixture reproduces that MR's shape in miniature: an audit-log write behind a guard that gains a
// conjunct. `File.AppendAllText` matches the builtin `io:write` rule, so the gated effect is a real
// rule-matched one — no test-only rules file, and NOT an intrinsic (a guard change around a bare `new`/`throw`
// is deliberately not reported). Effect rules only fire on EXTERNAL targets, which is why the sink is a BCL
// call rather than an in-source `AuditSink` type.
//
// See docs/backlog/todo/impact-guard-delta-for-predicate-only-changes.md.
public sealed class ImpactGuardConditionTests
{
    // The guarded edge is Handle -> Emit; Emit reaches the io:write. `guard` is spliced in as the gating
    // condition so base and head differ ONLY in the predicate — no call, symbol or effect moves.
    private static string Source(string guard) =>
        $$"""
            namespace App
            {
                public sealed class Svc
                {
                    public bool Merge;
                    public bool StopAudits;
                    public bool HasDocument;

                    public void Handle()
                    {
                        if ({{guard}})
                        {
                            Emit();
                        }
                    }

                    private void Emit() => System.IO.File.AppendAllText("audit.log", "record changed");
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

    private static async Task<string> MaterializeAsync(string workingDirectory, AnalysisResult result, string storeId)
    {
        var dir = StoreLayout.NewStoreDir(workingDirectory, storeId);
        await using var ctx = new RigDbContext(Path.Combine(dir, StoreLayout.DbFileName), pooling: false);
        await Writes.SaveAsync(ctx, result, provenance: null);
        return storeId;
    }

    // Two stores differing only in `Handle`'s guard, then `rig impact` with the given extra args.
    private static async Task<(int Exit, string Out, string Err)> RunAsync(string baseGuard, string headGuard, params string[] extra)
    {
        var wd = Path.Combine(Path.GetTempPath(), $"rig-impact-guardcond-{Guid.NewGuid():n}");
        Directory.CreateDirectory(wd);
        try
        {
            var baseId = await MaterializeAsync(wd, Analyze(Source(baseGuard)), "guardcondbase");
            var headId = await MaterializeAsync(wd, Analyze(Source(headGuard)), "guardcondhead");

            var output = new StringWriter();
            var error = new StringWriter();
            var exit = await CliApplication.RunAsync(["impact", "--base", baseId, "--head", headId, .. extra], output, error, wd);
            return (exit, output.ToString(), error.ToString());
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

    [Test]
    public async Task A_tightened_predicate_is_reported_as_NARROWED_with_both_conditions()
    {
        // THE REGRESSION. Base gates the audit on `!Merge`; head ANDs on a document check — the MR !11025 shape.
        var (exit, stdout, _) = await RunAsync(
            baseGuard: "!Merge",
            headGuard: "!Merge && (!HasDocument || !StopAudits)",
            "--format",
            "tsv"
        );

        exit.ShouldBe(0); // reporting only — no gate was requested
        var row = stdout
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .SingleOrDefault(l => l.StartsWith("guard_condition_delta\t", StringComparison.Ordinal));
        row.ShouldNotBeNull("a predicate-only change must produce a guard_condition_delta row");

        var f = row!.Split('\t');
        f[1].ShouldBe("narrowed"); // fires on strictly FEWER paths
        f[2].ShouldBe("Svc.Handle"); // caller — the frame whose branch moved
        f[3].ShouldBe("Svc.Emit"); // callee — what the condition now gates differently
        f[4].ShouldBe("io:write"); // WHAT it gates: reachable from the callee, not the callee itself
        f[6].ShouldBe("!Merge"); // base condition
        f[7].ShouldBe("!Merge && (!HasDocument || !StopAudits)"); // head condition, normalized to one line

        // The whole point: the effect-set signals are silent, so this row is the ONLY evidence of the change.
        var summary = stdout.Split('\n').First(l => l.StartsWith("impact_summary\t", StringComparison.Ordinal));
        summary.ShouldContain("effect_added=0");
        summary.ShouldContain("effect_removed=0");
        summary.ShouldContain("behavioral_eps=0");
        summary.ShouldContain("guard_narrowed=1");
        summary.ShouldContain("guard_widened=0");
    }

    [Test]
    public async Task The_narrowing_gate_fails_the_change_that_expect_no_effect_change_passes()
    {
        // The pair of verdicts that motivated a SEPARATE flag. Same two stores, same run: the effect-set gate
        // reports OK (correctly — no effect changed), the narrowing gate fails. If narrowing had been folded
        // into --expect-no-effect-change, opting into the deterministic gate would have silently acquired a
        // syntactic over-approximation.
        var (exit, _, stderr) = await RunAsync(
            baseGuard: "!Merge",
            headGuard: "!Merge && !StopAudits",
            "--expect-no-effect-change",
            "--expect-no-guard-narrowing"
        );

        exit.ShouldBe(1);
        stderr.ShouldContain("--expect-no-effect-change OK");
        stderr.ShouldContain("--expect-no-guard-narrowing FAILED");
        stderr.ShouldContain("1 call edge(s)");
    }

    [Test]
    public async Task A_relaxed_predicate_is_WIDENED_and_does_not_trip_the_narrowing_gate()
    {
        // The opposite direction is reported but must NOT gate: a guard being relaxed is ordinary feature work,
        // and a gate that tripped on it would be unusable in CI.
        var (exit, stdout, stderr) = await RunAsync(
            baseGuard: "!Merge && !StopAudits",
            headGuard: "!Merge",
            "--format",
            "tsv",
            "--expect-no-guard-narrowing"
        );

        exit.ShouldBe(0);
        stderr.ShouldContain("--expect-no-guard-narrowing OK");
        stdout.ShouldContain("guard_condition_delta\twidened\t");
        stdout.ShouldContain("guard_widened=1");
        stdout.ShouldContain("guard_narrowed=0");
    }

    [Test]
    public async Task An_unchanged_predicate_emits_no_row_even_when_reformatted()
    {
        // Fence against the noisiest possible failure mode: if formatting moved the verdict, every reindex of a
        // reformatted file would report phantom narrowing. Same predicate, different whitespace + a comment.
        var (exit, stdout, stderr) = await RunAsync(
            baseGuard: "!Merge && !StopAudits",
            headGuard: "!Merge &&\n            // documents are audited elsewhere now\n            !StopAudits",
            "--format",
            "tsv",
            "--expect-no-guard-narrowing"
        );

        exit.ShouldBe(0);
        stdout.ShouldNotContain("guard_condition_delta");
        stdout.ShouldContain("guard_narrowed=0");
        stdout.ShouldContain("guard_changed=0");
        stderr.ShouldContain("--expect-no-guard-narrowing OK");
    }

    [Test]
    public async Task The_effect_filter_scopes_guard_rows_the_same_way_it_scopes_effect_rows()
    {
        // --only/--exclude apply to these rows through the same token grammar, so a reviewer can ask "guard
        // changes that gate an audit" instead of reading an unfiltered wall. A row whose entire effect list is
        // filtered out is dropped — the condition still moved, but not around anything that was asked about.
        var (_, keptStdout, _) = await RunAsync("!Merge", "!Merge && !StopAudits", "--format", "tsv", "--only", "io");
        keptStdout.ShouldContain("guard_condition_delta\tnarrowed\t");

        var (_, droppedStdout, _) = await RunAsync("!Merge", "!Merge && !StopAudits", "--format", "tsv", "--only", "redis");
        droppedStdout.ShouldNotContain("guard_condition_delta");
        droppedStdout.ShouldContain("guard_narrowed=0");
    }

    [Test]
    public async Task The_human_section_leads_with_the_verdict_and_explains_what_narrowed_means()
    {
        // A predicate-only change produces NO per-EP cards, so if this section were printed after the effect
        // section a reviewer scanning top-down would read "no behavioural change" and stop.
        var (_, stdout, _) = await RunAsync("!Merge", "!Merge && !StopAudits");

        stdout.ShouldContain("Guard conditions changed on 1 call edge(s): 1 narrowed");
        // Grouped by the CONDITION that moved, with the gated edges as its detail — one source-level `if`
        // commonly gates several edges (four, on MR !11025), and per-edge rows carrying identical conditions
        // read as duplicates.
        stdout.ShouldContain("NARROWED  in Svc.Handle");
        stdout.ShouldContain("base:  !Merge");
        stdout.ShouldContain("head:  !Merge && !StopAudits");
        stdout.ShouldContain("gates 1 edge(s): Svc.Emit");
        stdout.ShouldContain("reaching: io:write");
        stdout.ShouldContain("fires on strictly FEWER paths");

        // The guard section must precede the effect section, which is the whole reason it is rendered early.
        stdout
            .IndexOf("Guard conditions changed", StringComparison.Ordinal)
            .ShouldBeLessThan(stdout.IndexOf("reachable-effect set", StringComparison.Ordinal));
    }
}
