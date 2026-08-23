using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using Rig.Cli.CommandLine;
using Rig.Cli.Services;
using Rig.Cli.Telemetry;
using Rig.Domain.Functions;
using static Rig.Cli.Effects.EffectDerivation;
using static Rig.Cli.Rendering.SymbolNameFormatter;

namespace Rig.Cli.Commands;

// `rig hotspots` — a browsable refactoring worklist over transparent per-method metrics. No blended score:
// --sort chooses one named measurement and the output preserves every component used to interpret it.
internal static class HotspotsCommand
{
    internal static readonly string[] Sorts = ["callers", "callees", "effects", "density", "hazards", "amplification", "dispatch"];

    internal static Command Build(TextWriter output, TextWriter error, string workingDirectory)
    {
        var sort = new Option<string>("--sort")
        {
            Description = "Rank by callers, callees, effects, density, hazards, amplification, or dispatch (default density).",
            DefaultValueFactory = _ => "density",
        };
        sort.AcceptOnlyFromAmong(Sorts);
        var top = new Option<int>("--top") { Description = "Maximum rows to show (default 50).", DefaultValueFactory = _ => 50 };
        var noLambdas = new Option<bool>("--no-lambdas") { Description = "Exclude synthetic lambda methods from the ranking." };
        var intrinsic = CommonOptions.Intrinsic();
        var rules = CommonOptions.Rules();
        var format = CommonOptions.Format(allowedValues: ["tsv"]);
        var store = CommonOptions.Store();
        var time = CommonOptions.Time();
        var command = new Command("hotspots", "Rank first-party methods by transparent fan/effect/hazard/dispatch metrics.")
        {
            sort,
            top,
            noLambdas,
            intrinsic,
            rules,
            format,
            store,
            time,
        };
        command.SetAction(pr =>
            CommandGuard.RunGuardedAsync(
                workingDirectory,
                error,
                () =>
                    RunAsync(
                        new Options(
                            Sort: pr.GetValue(sort)!,
                            Top: pr.GetValue(top),
                            NoLambdas: pr.GetValue(noLambdas),
                            Intrinsic: pr.GetValue(intrinsic),
                            ExtraRules: CommonOptions.RulesOf(pr.GetValue(rules)),
                            Format: pr.GetValue(format),
                            Time: pr.GetValue(time)
                        ),
                        new CommandIo(
                            new TextOutput(output, error),
                            new WorkspaceLocation(workingDirectory, pr.GetValue(store))
                        )
                    )
            )
        );
        return command;
    }

    internal sealed record Options(
        string Sort,
        int Top,
        bool NoLambdas,
        bool Intrinsic,
        IReadOnlyList<string> ExtraRules,
        string? Format,
        bool Time
    );

    private static async Task<int> RunAsync(Options options, CommandIo io)
    {
        using var timing = QueryTiming.Start(options.Time, io.TextOutput.Error);
        var queryWatch = Stopwatch.StartNew();
        var artifact = await HotspotsQueryService.BuildAsync(
            workingDirectory: io.WorkspaceLocation.WorkingDirectory,
            storeRef: io.WorkspaceLocation.StoreRef,
            intrinsic: options.Intrinsic,
            extraRules: options.ExtraRules
        );
        queryWatch.Stop();
        timing.Record("hotspots", queryWatch.Elapsed);
        WriteIntrinsicNote(artifact.HiddenIntrinsic, io.TextOutput.Error);

        var rows = SelectRows(artifact.Rows, options.Sort, options.Top, options.NoLambdas);

        var renderWatch = Stopwatch.StartNew();
        if (CommonOptions.IsTsv(options.Format))
        {
            WriteTsv(io.TextOutput.Output, rows);
        }
        else
        {
            WriteHuman(io.TextOutput.Output, rows, options);
        }
        renderWatch.Stop();
        timing.Record("render", renderWatch.Elapsed);
        return 0;
    }

    internal static IReadOnlyList<FactHotspotReport.Row> Order(IEnumerable<FactHotspotReport.Row> rows, string sort)
    {
        IOrderedEnumerable<FactHotspotReport.Row> ordered = sort switch
        {
            "callers" => rows.OrderByDescending(r => r.CallerMethods).ThenByDescending(r => r.IncomingCallSites),
            "callees" => rows.OrderByDescending(r => r.CalleeMethods).ThenByDescending(r => r.OutgoingCallSites),
            "effects" => rows.OrderByDescending(r => r.EffectSites).ThenByDescending(r => r.EffectKinds),
            "density" => rows.OrderByDescending(r => r.EffectSitesPer100Lines).ThenByDescending(r => r.EffectSites),
            "hazards" => rows.OrderByDescending(r => r.HazardSites).ThenByDescending(r => r.HazardKinds),
            "amplification" => rows.OrderByDescending(r => r.AmplificationSites),
            "dispatch" => rows.OrderByDescending(r => r.DispatchRank).ThenByDescending(r => r.ResidualDispatchFan),
            _ => throw new ArgumentException($"Unknown hotspot sort '{sort}'.", nameof(sort)),
        };
        return ordered.ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
    }

    internal static IReadOnlyList<FactHotspotReport.Row> SelectRows(
        IEnumerable<FactHotspotReport.Row> rows,
        string sort,
        int top,
        bool noLambdas
    ) =>
        Order(rows.Where(r => !r.IsGenerated && (!noLambdas || !r.IsLambda)), sort).Take(Math.Max(0, top)).ToList();

    internal static void WriteTsv(TextWriter output, IReadOnlyList<FactHotspotReport.Row> rows)
    {
        output.WriteLine(
            "id\tname\tfile\tline\tlines\tcaller_methods\tincoming_call_sites\tcallee_methods\toutgoing_call_sites\t"
                + "effect_sites\teffect_kinds\teffect_sites_per_100_lines\thazard_sites\thazard_kinds\tamplification_sites\t"
                + "residual_dispatch_fan\tdispatch_incoming_edges\tdispatch_rank\tgenerated\tlambda"
        );
        foreach (var r in rows)
        {
            output.WriteLine(
                string.Join(
                    '\t',
                    Clean(r.Id),
                    Clean(r.Name),
                    Clean(r.File),
                    r.Line.ToString(CultureInfo.InvariantCulture),
                    r.Lines.ToString(CultureInfo.InvariantCulture),
                    r.CallerMethods.ToString(CultureInfo.InvariantCulture),
                    r.IncomingCallSites.ToString(CultureInfo.InvariantCulture),
                    r.CalleeMethods.ToString(CultureInfo.InvariantCulture),
                    r.OutgoingCallSites.ToString(CultureInfo.InvariantCulture),
                    r.EffectSites.ToString(CultureInfo.InvariantCulture),
                    r.EffectKinds.ToString(CultureInfo.InvariantCulture),
                    r.EffectSitesPer100Lines.ToString("0.####", CultureInfo.InvariantCulture),
                    r.HazardSites.ToString(CultureInfo.InvariantCulture),
                    r.HazardKinds.ToString(CultureInfo.InvariantCulture),
                    r.AmplificationSites.ToString(CultureInfo.InvariantCulture),
                    r.ResidualDispatchFan.ToString(CultureInfo.InvariantCulture),
                    r.DispatchIncomingEdges.ToString(CultureInfo.InvariantCulture),
                    r.DispatchRank.ToString(CultureInfo.InvariantCulture),
                    r.IsGenerated ? "true" : "false",
                    r.IsLambda ? "true" : "false"
                )
            );
        }

        static string Clean(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }

    private static void WriteHuman(TextWriter output, IReadOnlyList<FactHotspotReport.Row> rows, Options options)
    {
        output.WriteLine(
            $"Hotspots by {options.Sort} (top {options.Top}; generated excluded; lambdas {(options.NoLambdas ? "excluded" : "included")})"
        );
        output.WriteLine("No blended score: counts are distinct methods / static call or effect sites.");
        foreach (var (row, index) in rows.Select((row, index) => (row, index + 1)))
        {
            output.WriteLine($"{index,3}. {ShortName(row.Id)}  {ShortenPath(row.File)}:{row.Line}  ({row.Lines} lines)");
            output.WriteLine(
                $"     callers {row.CallerMethods}/{row.IncomingCallSites} sites · callees {row.CalleeMethods}/{row.OutgoingCallSites} sites"
                    + $" · effects {row.EffectSites}/{row.EffectKinds} kinds @ {row.EffectSitesPer100Lines.ToString("0.##", CultureInfo.InvariantCulture)}/100 lines"
                    + $" · hazards {row.HazardSites}/{row.HazardKinds} kinds · amplification {row.AmplificationSites}"
                    + $" · dispatch {row.ResidualDispatchFan}×{row.DispatchIncomingEdges}={row.DispatchRank}"
            );
        }
    }
}
