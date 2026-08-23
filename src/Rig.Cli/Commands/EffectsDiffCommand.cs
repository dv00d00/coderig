using System.CommandLine;
using Rig.Cli.CommandLine;
using Rig.Cli.Rendering;
using Rig.Cli.Services;
using Rig.Cli.Telemetry;
using Rig.Domain.Functions;
using static Rig.Cli.Rendering.SymbolNameFormatter;

namespace Rig.Cli.Commands;

// `rig effects-diff <a> <b>` — generic effect-set diff between two entry points. Computes each EP's
// forward-reachable effect resource-keys (optionally filtered by `--only provider:op`) and reports the
// symmetric difference: resources one reaches that the other doesn't. Purely mechanical — it has no
// opinion about what a difference MEANS.
//
// "write-set divergence" (a UI/save path vs an import/API path writing different tables — an incident-born
// consistency check) is one USAGE: `rig effects-diff Save Import --only llblgen:write,bulk_write,delete`.
// The operator/agent supplies the domain interpretation ("these two are the same logical op; this gap is a
// bug"); the tool just diffs. Wraps FactEffectSetDiffDeriver (the pure deriver).
internal static class EffectsDiffCommand
{
    internal static Command Build(TextWriter output, TextWriter error, string workingDirectory)
    {
        var a = CommonOptions.Pattern(name: "a", description: "First entry-point method pattern.");
        var b = CommonOptions.Pattern(name: "b", description: "Second entry-point method pattern.");
        var only = new Option<string[]?>("--only")
        {
            Description =
                "Effect provider[:operation] to include (repeatable). Default: ALL effects. "
                + "Write-set divergence = --only llblgen:write --only llblgen:bulk_write --only llblgen:delete.",
            CustomParser = r => r.Tokens.Select(t => t.Value).ToArray(),
            AllowMultipleArgumentsPerToken = false,
        };
        var label = new Option<string?>("--label") { Description = "Optional label for the pair in output." };
        var format = CommonOptions.Format();
        var time = CommonOptions.Time();
        var store = CommonOptions.Store();
        var cmd = new Command(
            name: "effects-diff",
            description: "Diff the forward-reachable effect-sets of two entry points (symmetric difference, optionally filtered)."
        )
        {
            a,
            b,
            only,
            label,
            format,
            time,
            store,
        };
        cmd.SetAction(pr =>
            CommandGuard.RunGuardedAsync(
                workingDirectory,
                error,
                () =>
                    RunAsync(
                        new Options(
                            APattern: pr.GetValue(a)!,
                            BPattern: pr.GetValue(b)!,
                            Only: pr.GetValue(only),
                            Label: pr.GetValue(label),
                            Format: pr.GetValue(format),
                            Time: pr.GetValue(time)
                        ),
                        new CommandIo(
                            new TextOutput(Output: output, Error: error),
                            new WorkspaceLocation(WorkingDirectory: workingDirectory, StoreRef: pr.GetValue(store))
                        )
                    )
            )
        );
        return cmd;
    }

    private sealed record Options(string APattern, string BPattern, string[]? Only, string? Label, string? Format, bool Time);

    private static async Task<int> RunAsync(Options opts, CommandIo io)
    {
        var tsv = CommonOptions.IsTsv(opts.Format);

        using var timing = QueryTiming.Start(opts.Time, io.TextOutput.Error);
        var result = await EffectsDiffQueryService.BuildAsync(
            workingDirectory: io.WorkspaceLocation.WorkingDirectory,
            aPattern: opts.APattern,
            bPattern: opts.BPattern,
            only: opts.Only,
            label: opts.Label,
            storeRef: io.WorkspaceLocation.StoreRef
        );
        timing.Record("graph load", result.GraphLoadElapsed);
        timing.Record("traversal", result.TraversalElapsed);

        if (!result.Matched)
        {
            RenderResolutionFailure(result.A, side: "a", io, tsv);
            if (result.A.Status == EffectsDiffQueryService.TargetStatus.Matched)
            {
                RenderResolutionFailure(result.B, side: "b", io, tsv);
            }
            return 1;
        }

        var renderWatch = System.Diagnostics.Stopwatch.StartNew();
        if (tsv)
        {
            // columns: label, category, resource_key, side, present_ep, absent_ep
            // category = the present EP's provider:op(s) for this resource (comma-joined) — labels the row's
            // KIND (e.g. permission:assert = a guard; llblgen:write = a durable write).
            foreach (var f in result.Findings)
            {
                io.TextOutput.Output.WriteLine(
                    $"{f.Label}\t{string.Join(",", f.Categories)}\t{f.ResourceKey}\t{f.Direction}\t{f.PresentEpId}\t{f.AbsentEpId}"
                );
            }

            renderWatch.Stop();
            timing.Record("render", renderWatch.Elapsed);

            return 0;
        }

        if (result.Findings.Count == 0)
        {
            io.TextOutput.Output.WriteLine($"No effect-set difference between '{opts.APattern}' and '{opts.BPattern}'.");

            renderWatch.Stop();
            timing.Record("render", renderWatch.Elapsed);

            return 0;
        }

        io.TextOutput.Output.WriteLine(
            $"Effect-set difference: {result.Findings.Count} resource(s) differ between A='{opts.APattern}' and B='{opts.BPattern}'."
        );
        foreach (var f in result.Findings)
        {
            var side = f.Direction == EffectDiffSide.AOnly ? "A-only" : "B-only";
            var category = f.Categories.Count > 0 ? string.Join(",", f.Categories) : "?";
            io.TextOutput.Output.WriteLine(
                $"{Indent.L1}{category}  {f.ResourceKey}  [{side}]  reached by: {ShortName(f.PresentEpId)}  not by: {ShortName(f.AbsentEpId)}"
            );
        }

        renderWatch.Stop();
        timing.Record("render", renderWatch.Elapsed);

        return 0;
    }

    private static void RenderResolutionFailure(
        EffectsDiffQueryService.TargetResolution target,
        string side,
        CommandIo io,
        bool tsv
    )
    {
        if (target.Status == EffectsDiffQueryService.TargetStatus.Matched)
        {
            return;
        }

        if (target.Status == EffectsDiffQueryService.TargetStatus.NoMatch)
        {
            (tsv ? io.TextOutput.Error : io.TextOutput.Output).WriteLine($"No symbol matches '{target.Pattern}'.");
            return;
        }

        var line = $"Ambiguous: '{target.Pattern}' ({side}) matched {target.Matches.Count} nodes — narrow it.";
        if (tsv)
        {
            io.TextOutput.Error.WriteLine(line);
            return;
        }

        io.TextOutput.Output.WriteLine(line);
        foreach (var candidate in target.Matches.Take(10))
        {
            io.TextOutput.Output.WriteLine($"{Indent.L1}{candidate}");
        }

        if (target.Matches.Count > 10)
        {
            io.TextOutput.Output.WriteLine($"{Indent.L1}… and {target.Matches.Count - 10} more");
        }
    }
}
