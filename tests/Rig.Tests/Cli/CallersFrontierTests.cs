using Rig.Cli;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Cli;

// `callers <x> --entrypoints` returning ZERO must be ATTRIBUTABLE, not merely discouraging. The bare
// "No rule-detected entry points reach 'X'." reads as "dead code" when the truth is usually "the chain runs
// up to a boundary this analysis cannot cross".
//
// Cost a WRONG CONCLUSION mid-review on 2026-07-27: `callers DocumentPreviewBuilder.GetUnsafe --entrypoints`
// reported zero while plain `callers` returned an 18-method chain, and the gap was attributed to lambdas —
// rig models lambdas fine (`~λ0` nodes appear in the chain). The real cause was Dom/template interpolation
// (`{MedicalRecord.Documents}` resolved reflectively), which is exactly what the frontier list surfaces.
// See docs/backlog/progress/cli-surface-and-help-refresh-2026-07.md item 5.
//
// Assertions were taken from ACTUAL installed-`rig` output against an indexed copy of this playground.
public sealed class CallersFrontierTests
{
    private static async Task<(int Exit, string Out, string Err)> RunAsync(string workingDirectory, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await CliApplication.RunAsync(args, output, error, workingDirectory);
        return (exit, output.ToString(), error.ToString());
    }

    private static async Task<string> IndexedPlaygroundAsync(TempPlayground playground)
    {
        var workingDirectory = Path.Combine(playground.RootDirectory, "workspace");
        var index = new StringWriter();
        (await CliApplication.RunAsync(["index", playground.SolutionPath], index, index, workingDirectory)).ShouldBe(0);
        return workingDirectory;
    }

    [Test]
    public async Task A_zero_entrypoint_answer_names_the_frontier_it_topped_out_at()
    {
        using var playground = await TempPlayground.CreateCoreAllocationsAsync();
        var workingDirectory = await IndexedPlaygroundAsync(playground);

        // This playground has no rule-detected entry points, so --entrypoints is always zero here — which is
        // precisely the shape that used to be indistinguishable from dead code.
        var (exit, stdout, _) = await RunAsync(workingDirectory, "callers", "CompilerLoweredScenarios.Sum", "--entrypoints");

        exit.ShouldBe(1); // still "no entry points reach it" — the VERDICT is unchanged
        stdout.ShouldContain("No rule-detected entry points reach");

        // ...but it is now attributable: the count, the frontier methods, and their locations.
        stdout.ShouldContain("The reverse chain tops out at");
        stdout.ShouldContain("with no in-solution caller");
        stdout.ShouldContain("CompilerLoweredScenarios.LoweredRun"); // a real frontier method, verified
        stdout.ShouldContain("CompilerLoweredScenarios.cs"); // located, so it can be opened

        // The interpretation is stated, because a frontier is NOT proof of dead code — this is the sentence
        // whose absence produced the wrong conclusion.
        stdout.ShouldContain("BOUNDARY, not proof of dead code");
        stdout.ShouldContain("reflection");
    }

    [Test]
    public async Task A_target_nothing_calls_says_the_chain_is_empty_not_cut_short()
    {
        using var playground = await TempPlayground.CreateCoreAllocationsAsync();
        var workingDirectory = await IndexedPlaygroundAsync(playground);

        // `Program.Main` has no in-solution caller AT ALL (the runtime invokes it). "Nothing calls it" and
        // "the chain runs up to a boundary" are materially different answers and must not share wording:
        // the first genuinely suggests an unused/externally-invoked root, the second suggests a blind spot.
        var (exit, stdout, _) = await RunAsync(workingDirectory, "callers", "Program.Main", "--entrypoints");

        exit.ShouldBe(1);
        stdout.ShouldContain("Nothing in the analysed solution calls it");
        stdout.ShouldContain("the chain is empty, not cut short");
        // Must NOT claim a frontier — there is no chain to top out.
        stdout.ShouldNotContain("tops out at");
    }

    [Test]
    public async Task A_successful_entrypoint_answer_carries_no_frontier_noise()
    {
        // Negative control: the frontier block is paid for and printed ONLY on the zero path. If it leaked
        // into successful answers it would be noise on every query and stop being read.
        using var playground = await TempPlayground.CreateCoreAllocationsAsync();
        var workingDirectory = await IndexedPlaygroundAsync(playground);

        // Plain `callers` (not --entrypoints) succeeds here — it returns the caller chain.
        var (_, stdout, _) = await RunAsync(workingDirectory, "callers", "CompilerLoweredScenarios.Sum");

        stdout.ShouldNotContain("tops out at");
        stdout.ShouldNotContain("BOUNDARY, not proof of dead code");
    }
}
