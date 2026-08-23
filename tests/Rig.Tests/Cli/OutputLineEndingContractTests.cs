using Rig.Cli;
using Shouldly;

namespace Rig.Tests.Cli;

// THE ONE PLACE THE LF CONTRACT IS PINNED.
//
// rig answers are LF on every platform. The machine formats (`--format tsv/json`) are consumed by agents,
// scripts and the web layer; the LiveScale corpus hash is defined over LF content (CorpusGenerator.NormalizeLf,
// and the manifest's own HashAlgorithm line says "+ LF"); and a live answer has to be comparable with the
// store answer for the same tree regardless of which host produced it. A `\r` that varies by OS is therefore a
// real defect, not a formatting detail.
//
// It lives HERE, alone, on purpose. The live/store parity suites used to enforce it incidentally, by comparing
// rendered answers byte-for-byte — which meant a Windows run failed with thousands of "differences" whose lines
// were visually identical, and the actual contract was never stated anywhere. Those suites now compare
// canonical forms (AnswerStreamParity.Canonical); this test is what still fails if the contract itself breaks.
public sealed class OutputLineEndingContractTests
{
    private static async Task<(string Out, string Err)> Run(string workingDirectory, params string[] args)
    {
        // Deliberately BARE writers: a StringWriter defaults to Environment.NewLine, so anything that reaches
        // LF here did so because rig configured it, not because the fixture pre-normalized it.
        var output = new StringWriter();
        var error = new StringWriter();
        await CliApplication.RunAsync(args, output, error, workingDirectory);
        return (output.ToString(), error.ToString());
    }

    [Test]
    [Arguments("runs")]
    [Arguments("dead")]
    public async Task Cli_answers_carry_no_carriage_return_on_any_platform(string command)
    {
        var workingDirectory = Directory.CreateTempSubdirectory("rig-lf-contract-").FullName;
        try
        {
            var (stdout, stderr) = await Run(workingDirectory, command);

            // Anti-vacuity: an empty answer would satisfy "contains no \r" for free.
            (stdout.Length + stderr.Length).ShouldBeGreaterThan(0, $"`rig {command}` wrote nothing — the assertion would be vacuous.");
            stdout.ShouldNotContain("\r", customMessage: $"`rig {command}` stdout is not LF-only.");
            stderr.ShouldNotContain("\r", customMessage: $"`rig {command}` stderr is not LF-only.");
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }
}
