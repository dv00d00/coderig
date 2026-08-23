using System.CommandLine;
using Rig.Cli.CommandLine;

namespace Rig.Cli;

// The CLI entry point. Parsing, help, --version, and error chrome are owned by System.CommandLine; this just
// assembles the root command (Root.Build) over the caller's writers and invokes it. Every command's logic
// lives under Commands/*, and the shared parse/format/derive/cache invariants under CommandLine/, Rendering/,
// Graph/, Effects/, EntryPoints/, Caching/, Rules/ — one concern per file.
public static class CliApplication
{
    public static Task<int> RunAsync(string[] args, TextWriter output, TextWriter error) =>
        RunAsync(args, output, error, Directory.GetCurrentDirectory());

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, string workingDirectory)
    {
        // rig answers are LF on every platform. The machine formats (--format tsv/json) are consumed by
        // agents, scripts and the web layer, so a byte-identical answer must not depend on the host's
        // Environment.NewLine — a Windows run would otherwise emit \r\n and silently differ from CI.
        output.NewLine = "\n";
        error.NewLine = "\n";

        var root = Root.Build(output, error, workingDirectory);
        var configuration = new InvocationConfiguration { Output = output, Error = error };
        return await root.Parse(args).InvokeAsync(configuration);
    }
}
