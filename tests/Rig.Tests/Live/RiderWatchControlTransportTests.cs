using System.IO.Pipes;
using System.Text.Json;
using Rig.Cli.Live;
using Shouldly;
using static Rig.Cli.Live.LiveQueryTransport;

namespace Rig.Tests.Live;

public sealed class RiderWatchControlTransportTests
{
    [Test]
    public void Responder_reports_exact_and_stale_status_and_accepts_only_restart()
    {
        var status = Request("status-id", RiderWatchControlResponder.StatusAction);
        var exact = RiderWatchControlResponder.Answer(status, 17, staleReason: null);
        exact.Status.ShouldBe(StatusOk);
        exact.HostProcessId.ShouldBe(Environment.ProcessId);
        exact.SourceStatus.ShouldBe(RiderFileEffectResponder.SourceExact);
        exact.GraphGeneration.ShouldBe(17);
        exact.RestartAccepted.ShouldBeFalse();

        var restart = Request("restart-id", RiderWatchControlResponder.RestartAction);
        var stale = RiderWatchControlResponder.Answer(restart, 18, "topology changed");
        stale.SourceStatus.ShouldBe(RiderFileEffectResponder.SourceStale);
        stale.RestartAccepted.ShouldBeTrue();
        stale.Reason.ShouldBe("topology changed");

        RiderWatchControlResponder.IsSupportedAction("status").ShouldBeTrue();
        RiderWatchControlResponder.IsSupportedAction("restart").ShouldBeTrue();
        RiderWatchControlResponder.IsSupportedAction("kill").ShouldBeFalse();
    }

    [Test]
    public async Task Typed_round_trip_routes_status_and_preserves_correlation()
    {
        var directory = Directory.CreateTempSubdirectory("rig-rider-watch-control-").FullName;
        try
        {
            var served = 0;
            await using var server = LiveQueryServer.Start(
                directory,
                (_, _) => Task.FromResult(LiveServeResult.Declined("rendered callback must not run")),
                new StringWriter(),
                serveWatchControl: (request, _) =>
                {
                    Interlocked.Increment(ref served);
                    return Task.FromResult(RiderWatchControlResponder.Answer(request, 42, staleReason: null));
                }
            );
            (await server.WaitUntilReadyAsync(TimeSpan.FromSeconds(10))).ShouldBeTrue();
            var request = new RiderWatchControlRequest(
                Protocol,
                RiderWatchControlResponder.Verb,
                directory,
                "request-42",
                RiderWatchControlResponder.StatusAction
            );

            var response = await AskAsync(server.PipeName, request);

            Volatile.Read(ref served).ShouldBe(1);
            response.Status.ShouldBe(StatusOk);
            response.RequestId.ShouldBe("request-42");
            response.Action.ShouldBe(RiderWatchControlResponder.StatusAction);
            response.HostProcessId.ShouldBe(Environment.ProcessId);
            response.GraphGeneration.ShouldBe(42);
            response.SourceStatus.ShouldBe(RiderFileEffectResponder.SourceExact);
            response.RestartAccepted.ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task Restart_is_acknowledged_before_the_host_shutdown_callback_runs()
    {
        var directory = Directory.CreateTempSubdirectory("rig-rider-watch-restart-").FullName;
        using var callbackEntered = new ManualResetEventSlim();
        using var releaseCallback = new ManualResetEventSlim();
        try
        {
            await using var server = LiveQueryServer.Start(
                directory,
                (_, _) => Task.FromResult(LiveServeResult.Declined("rendered callback must not run")),
                new StringWriter(),
                serveWatchControl: (request, _) => Task.FromResult(RiderWatchControlResponder.Answer(request, 43, staleReason: null)),
                requestWatchRestart: () =>
                {
                    callbackEntered.Set();
                    releaseCallback.Wait(TimeSpan.FromSeconds(10));
                }
            );
            (await server.WaitUntilReadyAsync(TimeSpan.FromSeconds(10))).ShouldBeTrue();
            var request = new RiderWatchControlRequest(
                Protocol,
                RiderWatchControlResponder.Verb,
                directory,
                "restart-43",
                RiderWatchControlResponder.RestartAction
            );

            var responseTask = AskAsync(server.PipeName, request);
            callbackEntered.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
            var completed = await Task.WhenAny(responseTask, Task.Delay(TimeSpan.FromSeconds(2)));

            completed.ShouldBe(responseTask, "the restart acknowledgement must be readable before shutdown begins");
            var response = await responseTask;
            response.RestartAccepted.ShouldBeTrue();
            response.RequestId.ShouldBe("restart-43");
        }
        finally
        {
            releaseCallback.Set();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task Typed_route_declines_wrong_protocol_directory_and_action_before_callback()
    {
        var directory = Directory.CreateTempSubdirectory("rig-rider-watch-control-host-").FullName;
        var other = Directory.CreateTempSubdirectory("rig-rider-watch-control-other-").FullName;
        try
        {
            var served = 0;
            await using var server = LiveQueryServer.Start(
                directory,
                (_, _) => Task.FromResult(LiveServeResult.Declined("rendered callback must not run")),
                new StringWriter(),
                serveWatchControl: (request, _) =>
                {
                    Interlocked.Increment(ref served);
                    return Task.FromResult(RiderWatchControlResponder.Answer(request, 1, staleReason: null));
                }
            );
            (await server.WaitUntilReadyAsync(TimeSpan.FromSeconds(10))).ShouldBeTrue();

            var wrongProtocol = await AskAsync(
                server.PipeName,
                Request("protocol", "status") with
                {
                    Protocol = Protocol + 1,
                    WorkingDirectory = directory,
                }
            );
            wrongProtocol.Status.ShouldBe(StatusDeclined);
            wrongProtocol.Reason.ShouldContain("protocol mismatch");

            var wrongDirectory = await AskAsync(server.PipeName, Request("directory", "status") with { WorkingDirectory = other });
            wrongDirectory.Status.ShouldBe(StatusDeclined);
            wrongDirectory.Reason.ShouldContain("is watching");

            var wrongAction = await AskAsync(server.PipeName, Request("action", "kill") with { WorkingDirectory = directory });
            wrongAction.Status.ShouldBe(StatusDeclined);
            wrongAction.Reason.ShouldContain("unsupported watch action");
            Volatile.Read(ref served).ShouldBe(0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            Directory.Delete(other, recursive: true);
        }
    }

    private static RiderWatchControlRequest Request(string requestId, string action) =>
        new(Protocol, RiderWatchControlResponder.Verb, Directory.GetCurrentDirectory(), requestId, action);

    private static async Task<RiderWatchControlResponse> AskAsync(string pipeName, RiderWatchControlRequest request)
    {
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(10_000);
        await WriteFrameAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(request, Json), CancellationToken.None);
        var frame = (await ReadFrameAsync(pipe, CancellationToken.None)).ShouldNotBeNull();
        return JsonSerializer.Deserialize<RiderWatchControlResponse>(frame, Json).ShouldNotBeNull();
    }
}
