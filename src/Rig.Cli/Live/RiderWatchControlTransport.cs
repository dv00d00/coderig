using static Rig.Cli.Live.LiveQueryTransport;

namespace Rig.Cli.Live;

internal sealed record RiderWatchControlRequest(int Protocol, string Verb, string WorkingDirectory, string RequestId, string Action);

internal sealed record RiderWatchControlResponse(
    int Protocol,
    string Status,
    string RequestId,
    string Action,
    int HostProcessId,
    long GraphGeneration,
    string SourceStatus,
    bool RestartAccepted,
    string Reason
);

internal static class RiderWatchControlResponder
{
    internal const string Verb = "watch-control";
    internal const string StatusAction = "status";
    internal const string RestartAction = "restart";

    internal static RiderWatchControlResponse Answer(RiderWatchControlRequest request, long graphGeneration, string? staleReason) =>
        new(
            Protocol,
            StatusOk,
            request.RequestId,
            request.Action,
            Environment.ProcessId,
            graphGeneration,
            staleReason is null ? RiderFileEffectResponder.SourceExact : RiderFileEffectResponder.SourceStale,
            RestartAccepted: string.Equals(request.Action, RestartAction, StringComparison.Ordinal),
            staleReason ?? ""
        );

    internal static RiderWatchControlResponse Declined(RiderWatchControlRequest request, string reason) =>
        new(
            Protocol,
            StatusDeclined,
            request.RequestId,
            request.Action,
            Environment.ProcessId,
            GraphGeneration: 0,
            RiderFileEffectResponder.SourceStale,
            RestartAccepted: false,
            reason
        );

    internal static bool IsSupportedAction(string action) =>
        string.Equals(action, StatusAction, StringComparison.Ordinal) || string.Equals(action, RestartAction, StringComparison.Ordinal);
}
