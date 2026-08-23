using System.CommandLine;
using Rig.Cli;
using Rig.Cli.CommandLine;
using Shouldly;

namespace Rig.Tests.Cli;

// The depth option is shared by path/tree/callers/reaches. Agents naturally type the conventional
// `--max-depth`; retain both historical spellings so existing scripts keep working.
public sealed class CommonDepthAliasTests
{
    [Test]
    [Arguments("--max-depth")]
    [Arguments("--maxdepth")]
    [Arguments("--depth")]
    public async Task Every_depth_spelling_binds_the_same_value(string spelling)
    {
        var depth = CommonOptions.Depth();
        int? observed = null;
        var command = new RootCommand { depth };
        command.SetAction(parseResult =>
        {
            observed = parseResult.GetValue(depth);
            return 0;
        });

        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await command.Parse([spelling, "7"]).InvokeAsync(new InvocationConfiguration { Output = output, Error = error });

        exit.ShouldBe(0, error.ToString());
        observed.ShouldBe(7);
    }

    [Test]
    [Arguments("path")]
    [Arguments("tree")]
    [Arguments("callers")]
    [Arguments("reaches")]
    public async Task Every_depth_aware_command_advertises_all_spellings(string command)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        (await CliApplication.RunAsync([command, "--help"], output, error)).ShouldBe(0, error.ToString());

        var help = output.ToString();
        help.ShouldContain("--max-depth");
        help.ShouldContain("--maxdepth");
        help.ShouldContain("--depth");
    }
}
