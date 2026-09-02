using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Rig.Cli;
using Rig.Cli.Web;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Cli;

// `/api/callers?mode=entrypoints` versus `rig callers <to> --entrypoints --format tsv --include-reverse-only`:
// the SAME SET, row for row, over the real in-process host (RigWebHost.Build — what `rig serve` runs) against
// a real indexed playground store.
//
// This is the gate on the collapse of the two `callers` implementations onto one engine. The web lens used to
// have its own graph load, its own event-subscription marking, its own reverse walk and its own forward
// verification, and it DROPPED the reverse-only rows while the roots lens beside it kept them flagged and the
// CLI hid both — three policies for one partition. There is now one policy: every row carries
// `forwardConfirmed`, so the web's set is the CLI's `--include-reverse-only` set and the CLI's DEFAULT set is
// the `forwardConfirmed: true` subset of it. Both directions are asserted, so neither surface can drift back.
//
// The disclosure assertions are pasted from an actual run against this playground:
//   {"to":"NotificationsController.OnSaved","matched":true,"entryPoints":[{…"forwardConfirmed":true}],
//    "asyncReachableEpCount":2,"frontier":[{"id":"M:…NotificationsController.OnSaved","name":"Notifications
//    Controller.OnSaved",…,"line":38}]}
//   {"to":"TeamWorkflow.ProcessBatchAsync","matched":false,"entryPoints":[],"asyncReachableEpCount":0,
//    "frontier":[{"id":"M:…TeamWorkflow.ProcessBatchAsync(…)",…,"line":50}]}
public sealed class CallersEntryPointsParityTests
{
    // Targets chosen to cover the answer shapes the lens has: one entry point, several, a 0-EP answer whose
    // frontier attributes the zero, a 0-EP answer reachable only across an event handoff (the async hint), and
    // a pattern that matches nothing at all.
    private static readonly string[] Targets =
    [
        "TeamRepository.AddAsync",
        "TeamsController.Create",
        "ListAsync",
        "NotificationsController.OnSaved",
        "TeamWorkflow.ProcessBatchAsync",
        "SavePublisher.Raise",
        "NoSuchSymbolAtAll",
    ];

    [Test]
    public async Task The_web_entry_point_lens_returns_the_cli_include_reverse_only_set()
    {
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        var workingDirectory = playground.WorkingDirectory;
        var indexLog = new StringWriter();
        (await CliApplication.RunAsync(["index", playground.SolutionPath], indexLog, indexLog, workingDirectory)).ShouldBe(
            0,
            indexLog.ToString()
        );

        await using var host = await WebHost.StartAsync(workingDirectory);

        var totalRows = 0;
        foreach (var target in Targets)
        {
            var (matched, web) = await host.GetEntryPointsAsync(target);
            var flagged = await TsvRowsAsync(workingDirectory, target, includeReverseOnly: true);
            var confirmedOnly = await TsvRowsAsync(workingDirectory, target, includeReverseOnly: false);

            // The web set IS the diagnostic (flagged) set — same rows, same columns, same order.
            web.ShouldBe(flagged, $"'{target}': /api/callers?mode=entrypoints diverged from `--format tsv --include-reverse-only`.");
            // …and the CLI's default set is exactly the forward-confirmed projection of it, so a client that
            // hides the reverse-only rows reproduces the CLI headline without asking the server to filter.
            confirmedOnly.ShouldBe(
                web.Where(row => row.ForwardConfirmed).ToList(),
                $"'{target}': the default CLI listing is not the forwardConfirmed subset of the web answer."
            );
            (web.Count > 0).ShouldBe(matched, $"'{target}': `matched` disagrees with the row count.");
            totalRows += web.Count;
        }

        // Anti-vacuity: seven empty answers would compare equal and prove nothing. 1 + 2 + 3 + 1 + 0 + 0 + 0.
        totalRows.ShouldBe(7);
    }

    // The two --entrypoints disclosures now ride on the response instead of only existing as CLI prose: the
    // async-handoff hint as a COUNT, and the reverse frontier as ROWS. Without them a web client reading
    // `entryPoints: []` cannot tell "reached only across a handoff" from "the chain tops out at a boundary".
    [Test]
    public async Task The_web_entry_point_lens_carries_the_async_hint_and_the_frontier()
    {
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        var workingDirectory = playground.WorkingDirectory;
        var indexLog = new StringWriter();
        (await CliApplication.RunAsync(["index", playground.SolutionPath], indexLog, indexLog, workingDirectory)).ShouldBe(
            0,
            indexLog.ToString()
        );

        await using var host = await WebHost.StartAsync(workingDirectory);

        // A sync answer that UNDER-reports: one EP reaches it synchronously, two reach it on the async
        // surface — the CLI prints "… +1 more entry point(s) reach this via async/scheduled handoff".
        var onSaved = await host.GetJsonAsync("NotificationsController.OnSaved");
        onSaved.GetProperty("matched").GetBoolean().ShouldBeTrue();
        onSaved.GetProperty("entryPoints").GetArrayLength().ShouldBe(1);
        onSaved.GetProperty("asyncReachableEpCount").GetInt32().ShouldBe(2);

        // A 0-EP answer whose zero is ATTRIBUTABLE: the chain tops out at the target itself, which is the
        // "nothing in the analysed solution calls it" case rather than a cut-short chain.
        var batch = await host.GetJsonAsync("TeamWorkflow.ProcessBatchAsync");
        batch.GetProperty("matched").GetBoolean().ShouldBeFalse();
        batch.GetProperty("entryPoints").GetArrayLength().ShouldBe(0);
        batch.GetProperty("asyncReachableEpCount").GetInt32().ShouldBe(0);
        var frontier = batch.GetProperty("frontier").EnumerateArray().ToList();
        frontier.Count.ShouldBe(1);
        frontier[0].GetProperty("id").GetString()
            .ShouldBe("M:EntryPointEffects.Api.Services.TeamWorkflow.ProcessBatchAsync(System.Collections.Generic.IReadOnlyList{System.Int32})");
        frontier[0].GetProperty("name").GetString().ShouldBe("TeamWorkflow.ProcessBatchAsync");
        frontier[0].GetProperty("line").GetInt32().ShouldBe(50);

        // A pattern that resolves to no node has no frontier to report — the empty answer is not dressed up
        // as a boundary.
        var missing = await host.GetJsonAsync("NoSuchSymbolAtAll");
        missing.GetProperty("frontier").GetArrayLength().ShouldBe(0);
    }

    // One row of the entry-point lens, in the six columns both surfaces carry. `services` is deliberately not
    // compared: the playground has no deployments.json, so it is empty on both sides and would pin nothing.
    private sealed record Row(string Kind, string Route, string? File, int Line, bool ForwardConfirmed, string Fqn);

    // `rig callers <to> --entrypoints --format tsv [--include-reverse-only]`, parsed back into rows.
    // tsv columns: kind, route, file, line, requires, loadedServices, activeServices, forwardConfirmed, fqn.
    private static async Task<List<Row>> TsvRowsAsync(string workingDirectory, string target, bool includeReverseOnly)
    {
        string[] arguments = includeReverseOnly
            ? ["callers", target, "--entrypoints", "--format", "tsv", "--include-reverse-only"]
            : ["callers", target, "--entrypoints", "--format", "tsv"];
        var output = new StringWriter { NewLine = "\n" };
        var error = new StringWriter { NewLine = "\n" };
        await CliApplication.RunAsync(arguments, output, error, workingDirectory);
        return output
            .ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t'))
            .Select(c => new Row(
                Kind: c[0],
                Route: c[1],
                File: string.IsNullOrEmpty(c[2]) ? null : c[2],
                Line: int.Parse(c[3], System.Globalization.CultureInfo.InvariantCulture),
                ForwardConfirmed: bool.Parse(c[7]),
                Fqn: c[8]
            ))
            .ToList();
    }

    // The real host on an ephemeral port, plus the two reads these tests make of it. Mirrors
    // SourceEndpointTests' fixture, including the free-port retry (Kestrel refuses port 0 on `localhost`).
    private sealed class WebHost : IAsyncDisposable
    {
        private WebApplication _app = null!;
        private HttpClient _client = null!;

        public static async Task<WebHost> StartAsync(string workingDirectory)
        {
            var host = new WebHost();
            for (var attempt = 1; ; attempt++)
            {
                host._app = RigWebHost.Build(workingDirectory, FreePort());
                try
                {
                    await host._app.StartAsync();
                    break;
                }
                catch (IOException) when (attempt < 5)
                {
                    await host._app.DisposeAsync();
                }
            }

            host._client = new HttpClient { BaseAddress = new Uri(host._app.Urls.First()), Timeout = TimeSpan.FromMinutes(2) };
            return host;
        }

        public async Task<JsonElement> GetJsonAsync(string target)
        {
            using var response = await _client.GetAsync($"/api/callers?mode=entrypoints&from={Uri.EscapeDataString(target)}");
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
            return JsonDocument.Parse(body).RootElement.Clone();
        }

        public async Task<(bool Matched, List<Row> Rows)> GetEntryPointsAsync(string target)
        {
            var root = await GetJsonAsync(target);
            var rows = root
                .GetProperty("entryPoints")
                .EnumerateArray()
                .Select(e => new Row(
                    Kind: e.GetProperty("kind").GetString()!,
                    Route: e.GetProperty("route").GetString()!,
                    File: e.GetProperty("file").GetString(),
                    Line: e.GetProperty("line").GetInt32(),
                    ForwardConfirmed: e.GetProperty("forwardConfirmed").GetBoolean(),
                    Fqn: e.GetProperty("fqn").GetString()!
                ))
                .ToList();
            return (root.GetProperty("matched").GetBoolean(), rows);
        }

        public async ValueTask DisposeAsync()
        {
            _client?.Dispose();
            if (_app is not null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
        }

        // A port the OS says is free right now (bind :0 on loopback, read it back, release).
        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, port: 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
