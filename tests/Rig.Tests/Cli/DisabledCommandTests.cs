using Rig.Cli;
using Shouldly;

namespace Rig.Tests.Cli;

// A disabled command must REDIRECT, not dead-end. `dead` is switched off (it ran on the all-hops dispatch
// superset the one-hop engine no longer matches), but leaving it unregistered produced System.CommandLine's
// "'dead' was not matched" — which reads as a typo or a broken install, while older docs and muscle memory
// keep sending people to it. See docs/backlog/todo/cli-surface-and-help-refresh-2026-07.md item 8.
public sealed class DisabledCommandTests
{
    private static async Task<(int Exit, string Out, string Err)> Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await CliApplication.RunAsync(args, output, error, Directory.GetCurrentDirectory());
        return (exit, output.ToString(), error.ToString());
    }

    [Test]
    public async Task Invoking_dead_explains_why_and_names_the_workaround()
    {
        var (exit, _, err) = await Run("dead");

        exit.ShouldNotBe(0); // still a failure — it cannot answer the question
        err.ShouldContain("disabled");
        err.ShouldContain("one-hop"); // the REASON, so the user can judge when it might return
        err.ShouldContain("rig callers"); // the WORKAROUND
        // The misleading parser error is what this replaces.
        err.ShouldNotContain("was not matched");
    }

    [Test]
    public async Task Dead_tolerates_the_arguments_it_used_to_accept()
    {
        // An invocation copied from older docs must still reach the explanation rather than dying earlier on
        // an unrecognized argument — otherwise the redirect never gets shown to the person who needs it.
        var (exit, _, err) = await Run("dead", "SomeType.Method", "--format", "tsv");

        exit.ShouldNotBe(0);
        err.ShouldContain("disabled");
        err.ShouldNotContain("was not matched");
        err.ShouldNotContain("Unrecognized");
    }

    [Test]
    public async Task The_help_listing_marks_dead_as_disabled()
    {
        // Visible at DECISION time, not only after someone has already tried it.
        var (_, stdout, _) = await Run("--help");

        var line = stdout.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("dead", StringComparison.Ordinal));
        line.ShouldNotBeNull();
        line.ShouldContain("DISABLED");
    }
}
