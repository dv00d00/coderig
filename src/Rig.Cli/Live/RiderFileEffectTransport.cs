using Rig.Analysis.Inventory;
using Rig.Domain.Functions;
using static Rig.Cli.Live.LiveQueryTransport;

namespace Rig.Cli.Live;

internal sealed record LiveRequestHeader(int Protocol, string Verb, string WorkingDirectory);

internal sealed record RiderFileEffectRequest(
    int Protocol,
    string Verb,
    string WorkingDirectory,
    string RequestId,
    string FilePath,
    string ClientSnapshotToken
);

internal sealed record RiderFileEffectMethod(string SymbolId, string Family, int NearestDepth);

internal sealed record RiderFileEffectCallSite(string EnclosingSymbolId, string TargetSymbolId, int Line, string Family, int NearestDepth);

internal sealed record RiderFileEffectResponse(
    int Protocol,
    string Status,
    string RequestId,
    string FilePath,
    string ClientSnapshotToken,
    long GraphGeneration,
    string SourceStatus,
    IReadOnlyList<RiderFileEffectMethod> Methods,
    IReadOnlyList<RiderFileEffectCallSite> CallSites,
    string Reason
);

internal sealed record RiderFileEffectCapture(
    long GraphGeneration,
    IReadOnlyList<string> IndexedProjectContexts,
    string? StaleReason,
    Func<FileEffectReadModelIndex> ReadModel
);

internal static class RiderFileEffectResponder
{
    internal const string Verb = "file-effects";
    internal const string SourceExact = "exact";
    internal const string SourceStale = "stale";
    internal const string SourceUnindexed = "unindexed";
    internal const string SourceAmbiguous = "ambiguous";

    // The deliberately coarse first product selector. One named family is backed by a UNION of provider
    // predicates, and FileEffectReadModelIndex performs one reverse traversal over that union.
    internal static FileEffectSelector SqlSelector { get; } =
        new(
            "sql",
            [
                new EffectPredicate("efcore"),
                new EffectPredicate("db_connection"),
                new EffectPredicate("db_reader"),
                new EffectPredicate("db_command"),
                new EffectPredicate("db_transaction"),
                new EffectPredicate("yessql"),
            ]
        );

    internal static RiderFileEffectResponse Respond(RiderFileEffectRequest request, RiderFileEffectCapture capture)
    {
        if (capture.StaleReason is not null)
        {
            return Answer(request, capture.GraphGeneration, SourceStale, [], [], capture.StaleReason);
        }

        if (capture.IndexedProjectContexts.Count == 0)
        {
            return Answer(
                request,
                capture.GraphGeneration,
                SourceUnindexed,
                [],
                [],
                "the requested physical file is not indexed in this resident generation"
            );
        }

        if (capture.IndexedProjectContexts.Count > 1)
        {
            return Answer(
                request,
                capture.GraphGeneration,
                SourceAmbiguous,
                [],
                [],
                $"the requested physical file is indexed in {capture.IndexedProjectContexts.Count} project contexts"
            );
        }

        var lookupPath = TryFullPath(request.FilePath) ?? request.FilePath;
        var model = capture.ReadModel().Find(lookupPath);
        var methods = model is null
            ? []
            : model
                .Methods.SelectMany(method =>
                    method.Effects.Select(effect => new RiderFileEffectMethod(method.SymbolId, effect.Family, effect.NearestDepth))
                )
                .OrderBy(method => method.SymbolId, StringComparer.Ordinal)
                .ThenBy(method => method.Family, StringComparer.Ordinal)
                .ToArray();
        var callSites = model is null
            ? []
            : model
                .CallSites.SelectMany(callSite =>
                    callSite.Effects.Select(effect => new RiderFileEffectCallSite(
                        callSite.EnclosingSymbolId,
                        callSite.TargetSymbolId,
                        callSite.Line,
                        effect.Family,
                        effect.NearestDepth
                    ))
                )
                .OrderBy(callSite => callSite.EnclosingSymbolId, StringComparer.Ordinal)
                .ThenBy(callSite => callSite.Line)
                .ThenBy(callSite => callSite.TargetSymbolId, StringComparer.Ordinal)
                .ThenBy(callSite => callSite.Family, StringComparer.Ordinal)
                .ToArray();
        return Answer(request, capture.GraphGeneration, SourceExact, methods, callSites, "");
    }

    internal static RiderFileEffectResponse Declined(RiderFileEffectRequest request, string reason) =>
        new(
            Protocol,
            StatusDeclined,
            request.RequestId,
            request.FilePath,
            request.ClientSnapshotToken,
            GraphGeneration: 0,
            SourceStale,
            Methods: [],
            CallSites: [],
            reason
        );

    internal static IReadOnlyList<string> IndexedProjectContexts(FactSnapshot snapshot, string filePath)
    {
        var fullPath = TryFullPath(filePath);
        if (fullPath is null)
        {
            return [];
        }

        var indexedProjectNames = snapshot
            .EnumerateSourceFiles()
            .Where(source => source.Status == "indexed" && PathsEqual(source.FilePath, fullPath))
            .Select(source => source.ProjectName)
            .ToHashSet(StringComparer.Ordinal);
        if (indexedProjectNames.Count == 0)
        {
            return [];
        }

        var contexts = snapshot
            .Solution.Projects.Where(project => indexedProjectNames.Contains(project.Name))
            .Where(project => project.Documents.Any(document => PathsEqual(document.FilePath, fullPath)))
            .Select(project =>
                string.IsNullOrWhiteSpace(project.FilePath)
                    ? $"project:{project.Id}"
                    : $"path:{TryFullPath(project.FilePath) ?? project.FilePath}"
            )
            .Distinct(StringComparer.Ordinal)
            .OrderBy(context => context, StringComparer.Ordinal)
            .ToArray();

        // Source-generated documents are indexed without a retained ordinary Roslyn Document. Their source
        // rows still carry the project context, which is sufficient to distinguish zero/one/many here.
        return contexts.Length > 0
            ? contexts
            : indexedProjectNames.Select(name => $"name:{name}").OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    private static RiderFileEffectResponse Answer(
        RiderFileEffectRequest request,
        long graphGeneration,
        string sourceStatus,
        IReadOnlyList<RiderFileEffectMethod> methods,
        IReadOnlyList<RiderFileEffectCallSite> callSites,
        string reason
    ) =>
        new(
            Protocol,
            StatusOk,
            request.RequestId,
            request.FilePath,
            request.ClientSnapshotToken,
            graphGeneration,
            sourceStatus,
            methods,
            callSites,
            reason
        );

    private static bool PathsEqual(string? left, string right) =>
        left is not null
        && TryFullPath(left) is { } fullLeft
        && string.Equals(
            fullLeft,
            right,
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
        );

    private static string? TryFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
