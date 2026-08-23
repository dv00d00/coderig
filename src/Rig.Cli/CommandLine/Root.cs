using System.CommandLine;
using Rig.Cli.Commands;

namespace Rig.Cli.CommandLine;

// Assembles the rig root command from the per-command Build factories. Each command closes over the shared
// output/error writers + working directory, so the framework owns parsing/help/version/error-chrome and just
// dispatches to the command actions. This is the single place the CLI surface (the 15 subcommands) is
// declared; ordering here is the order they list in `rig --help`.
internal static class Root
{
    internal static RootCommand Build(TextWriter output, TextWriter error, string workingDirectory) =>
        new(
            """
            Runtime Intelligence Graph

            Query commands (tree, reaches, callers, …) read the .rig store from the current directory.
            Run them from the directory that contains the .rig/ folder, or create one with:

              rig index <solution>          # one-time: build the fact store
              rig runs                      # what's indexed
              rig entrypoints               # list entry points
              rig tree <EP> --view summary  # what an entry point touches
            """
        )
        {
            IndexCommands.BuildIndex(output, error, workingDirectory),
            IndexCommands.BuildGraph(output, error, workingDirectory),
            FactCommands.BuildRuns(output, error, workingDirectory),
            FactCommands.BuildDi(output, error, workingDirectory),
            FactCommands.BuildSymbols(output, error, workingDirectory),
            FactCommands.BuildRefs(output, error, workingDirectory),
            ShowCommand.Build(output, error, workingDirectory),
            PathCommand.Build(output, error, workingDirectory),
            TreeCommand.Build(output, error, workingDirectory),
            CallersCommand.Build(output, error, workingDirectory),
            ReachesCommand.Build(output, error, workingDirectory),
            DispatchFansCommand.Build(output, error, workingDirectory),
            HotspotsCommand.Build(output, error, workingDirectory),
            DeriveCommand.Build(output, error, workingDirectory),
            EffectsDiffCommand.Build(output, error, workingDirectory),
            EntryPointsCommand.Build(output, error, workingDirectory),
            ImpactCommand.Build(output, error, workingDirectory),
            ServeCommand.Build(output, error, workingDirectory),
            WatchCommand.Build(output, error, workingDirectory),
            // `dead` is DISABLED, but registered as an explaining STUB rather than simply absent. It ran on
            // the all-hops dispatch superset that the one-hop engine no longer matches (see the two-stage
            // dispatch notes in CLAUDE.md), so its answers would now be wrong. Left unregistered, it failed
            // with System.CommandLine's "'dead' was not matched", which reads like a typo or a broken install
            // — and it is still referenced by older docs and by anyone's muscle memory, so that error was
            // actively misleading. The stub states WHY and gives the workaround. Re-enable by restoring
            // DeadCommand.Build once `dead` is moved onto the one-hop engine.
            DisabledCommand.Build(
                name: "dead",
                reason: "`dead` is temporarily disabled: it ran on the all-hops dispatch superset, which the one-hop "
                    + "traversal engine no longer matches, so its results would be unsound.",
                workaround: "Approximate it with `rig callers <method> --roots` (empty result => no in-solution caller).",
                error: error
            ),
            FactCommands.BuildFiles(output, error, workingDirectory),
            FactCommands.BuildProfile(output, error, workingDirectory),
        };
}
