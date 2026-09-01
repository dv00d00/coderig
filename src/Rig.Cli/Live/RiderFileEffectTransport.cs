using Rig.Analysis.Inventory;
using Rig.Analysis.Rules;
using Rig.Domain.Data;
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

// TargetSymbolId is possibly EMPTY (never null): an effect observed at a call into external library code has
// no in-solution callee, so there is no DocID to send. The field is kept because the client uses it to
// disambiguate two projected targets on ONE line; an empty value means "the effect is at this line itself".
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
    string Reason,
    // Machine-routable companions to Reason (see FileEffectUnavailable). Empty on an exact answer. Additive:
    // a client that predates them keeps reading Status/SourceStatus/Reason unchanged, so the protocol version
    // is deliberately NOT bumped.
    string ReasonCode = "",
    string ReasonScope = ""
);

// Why a CODE and a SCOPE travel with the prose: the client has to ROUTE the cause, and sniffing a sentence is
// not routing. `Scope` says WHERE it belongs — a `file` cause is persistent and actionable on the open file (it
// did not compile, it is not indexed), so Rider announces it on that file; a `host` cause is global and often
// transient (booting, reconciling, a topology change), so it belongs to the status widget and must NOT put a
// row on every open file — a 148s MedDBase cold boot would otherwise nag once per visible document.
internal sealed record FileEffectUnavailable(string Code, string Scope, string Text);

internal sealed record RiderFileEffectCapture(
    long GraphGeneration,
    IReadOnlyList<string> IndexedProjectContexts,
    FileEffectUnavailable? Unavailable,
    // ONE index covering every family (FileEffectReadModelIndex.Build takes the whole selector list and walks
    // the graph once), not one index per family.
    Func<FileEffectReadModelIndex> ReadModel
);

internal static class RiderFileEffectResponder
{
    internal const string Verb = "file-effects";
    internal const string SourceExact = "exact";
    internal const string SourceStale = "stale";
    internal const string SourceUnindexed = "unindexed";
    internal const string SourceAmbiguous = "ambiguous";

    // The two SCOPES a cause can have, and the closed set of causes. Kept here rather than in the host so
    // there is exactly ONE table deciding where a cause is announced; the client switches on the string.
    internal const string ScopeFile = "file";
    internal const string ScopeHost = "host";

    internal const string ReasonFileCompileErrors = "file_compile_errors";
    internal const string ReasonProjectPartial = "project_partial";
    internal const string ReasonNotIndexed = "not_indexed";
    internal const string ReasonAmbiguousContext = "ambiguous_context";
    internal const string ReasonProjectUnreconciled = "project_unreconciled";
    internal const string ReasonUnlocatedCompileErrors = "compilation_unlocated_errors";
    internal const string ReasonTopologyChanged = "topology_changed";
    internal const string ReasonWatcherOverflow = "watcher_overflow";
    internal const string ReasonDeclined = "declined";

    // The families to project, FROM THE RULES — one selector per declared family, its predicates being the
    // providers declared in it. This used to be two hardcoded selectors naming `efcore`/`db_connection`/
    // `yessql`/`io` in core C#, which is the thing AmplificationCategories forbids ("no effect name may appear
    // in rig core C#") and which made the plugin blind on the codebase it is built for: MedDBase's 16,780
    // llblgen effects matched no selector, so 47% of its effects could not be rendered at all.
    //
    // A rule set that declares no families gets NO selectors and the responder answers with no rows — honest,
    // and the disclosure says so. It is not this file's business to invent a default family for a provider
    // vocabulary it cannot know.
    internal static IReadOnlyList<FileEffectSelector> SelectorsFor(RuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return ProviderCatalog
            .EffectfulFamilies(rules)
            .Select(family => new FileEffectSelector(
                family.Family,
                family.Providers.Select(provider => new EffectPredicate(provider)).ToArray()
            ))
            .ToArray();
    }

    internal static RiderFileEffectResponse Respond(RiderFileEffectRequest request, RiderFileEffectCapture capture)
    {
        if (capture.Unavailable is { } unavailable)
        {
            return Answer(request, capture.GraphGeneration, SourceStale, [], [], unavailable.Text, unavailable.Code, unavailable.Scope);
        }

        if (capture.IndexedProjectContexts.Count == 0)
        {
            return Answer(
                request,
                capture.GraphGeneration,
                SourceUnindexed,
                [],
                [],
                "the requested physical file is not indexed in this resident generation",
                ReasonNotIndexed,
                ScopeFile
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
                $"the requested physical file is indexed in {capture.IndexedProjectContexts.Count} project contexts",
                ReasonAmbiguousContext,
                ScopeFile
            );
        }

        var lookupPath = TryFullPath(request.FilePath) ?? request.FilePath;
        var models = capture.ReadModel().Find(lookupPath) is { } found ? new[] { found } : [];
        var methods = models
            .SelectMany(model => model.Methods)
            .SelectMany(method =>
                method.Effects.Select(effect => new RiderFileEffectMethod(method.SymbolId, effect.Family, effect.NearestDepth))
            )
            .OrderBy(method => method.SymbolId, StringComparer.Ordinal)
            .ThenBy(method => method.Family, StringComparer.Ordinal)
            .ToArray();
        var callSites = models
            .SelectMany(model => model.CallSites)
            .SelectMany(callSite =>
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
        return Answer(request, capture.GraphGeneration, SourceExact, methods, callSites, "", "", "");
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
            reason,
            ReasonDeclined,
            ScopeHost
        );

    // The FILE-SCOPED half of the unavailability gate, as a pure function of the requested file, the projects
    // that declare it, the unreconciled project names and the compilation health. Pure so it can be tested
    // without a resident host: the causes that are NOT file-attributable (topology change, watcher overflow,
    // unlocated diagnostics) stay in the host, which owns those flags.
    //
    // What this replaces: a whole-SOLUTION gate. On MedDBase 2 of 11,976 files failed to compile — in
    // MedDBase.PACS and a payment-gateway data tier — and every one of the 227 projects answered `stale` with
    // empty rows, so the Rider plugin was permanently blank on the codebase it is built for. Per-file grain is
    // what CompilationHealth was designed for (see its header: Roslyn re-reports at every site where binding
    // actually failed, so the diagnostic set is already the contamination closure, and propagating to siblings
    // is measurably fatal).
    internal static FileEffectUnavailable? UnavailableForFile(
        string filePath,
        IReadOnlySet<string> projectNames,
        IReadOnlyCollection<string> unreconciledProjectNames,
        CompilationHealth? health
    )
    {
        var unreconciled = unreconciledProjectNames
            .Where(projectNames.Contains)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (unreconciled.Length > 0)
        {
            // HOST scope on purpose: this clears itself within one reconcile (0.9s measured), and a warning
            // that flaps on every save teaches the reader to ignore it. The status widget carries the debt.
            return new FileEffectUnavailable(
                ReasonProjectUnreconciled,
                ScopeHost,
                $"{unreconciled.Length} project(s) declaring this file unreconciled: {string.Join(", ", unreconciled)}"
            );
        }

        if (health is null)
        {
            return null;
        }

        var fullPath = TryFullPath(filePath);
        if (fullPath is not null && health.Files.FirstOrDefault(file => PathsEqual(file.FilePath, fullPath)) is { } broken)
        {
            return new FileEffectUnavailable(
                ReasonFileCompileErrors,
                ScopeFile,
                $"this file had {broken.ErrorCount} compile error(s) ({broken.ErrorCodes}) when it was indexed, "
                    + $"so its facts may be missing or wrong: {broken.FirstMessage}"
            );
        }

        var partial = health.PartialProjects.Where(project => projectNames.Contains(project.ProjectName)).ToArray();
        if (partial.Length > 0)
        {
            return new FileEffectUnavailable(
                ReasonProjectPartial,
                ScopeFile,
                "the project(s) declaring this file contributed less than their whole selves: "
                    + string.Join(", ", partial.Select(project => $"{project.ProjectName} ({project.Reason})"))
            );
        }

        return null;
    }

    internal static IReadOnlyList<string> IndexedProjectContexts(FactSnapshot snapshot, string filePath) =>
        IndexedProjectScope(snapshot, filePath).Contexts;

    // The project contexts a physical file is indexed in, plus the PROJECT NAMES behind them. The names are
    // what scopes the unavailability gate: a compile error or a reconciliation debt in some unrelated project
    // must not blank this file's answer (measured on MedDBase: 2 broken files out of 11,976 blanked all 227
    // projects). One scan feeds both — the source-file enumeration is the expensive half.
    internal static (IReadOnlyList<string> Contexts, IReadOnlySet<string> ProjectNames) IndexedProjectScope(
        FactSnapshot snapshot,
        string filePath
    )
    {
        var fullPath = TryFullPath(filePath);
        if (fullPath is null)
        {
            return ([], new HashSet<string>(StringComparer.Ordinal));
        }

        var indexedProjectNames = snapshot
            .EnumerateSourceFiles()
            .Where(source => source.Status == "indexed" && PathsEqual(source.FilePath, fullPath))
            .Select(source => source.ProjectName)
            .ToHashSet(StringComparer.Ordinal);
        if (indexedProjectNames.Count == 0)
        {
            return ([], indexedProjectNames);
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
        return (
            contexts.Length > 0
                ? contexts
                : indexedProjectNames.Select(name => $"name:{name}").OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            indexedProjectNames
        );
    }

    private static RiderFileEffectResponse Answer(
        RiderFileEffectRequest request,
        long graphGeneration,
        string sourceStatus,
        IReadOnlyList<RiderFileEffectMethod> methods,
        IReadOnlyList<RiderFileEffectCallSite> callSites,
        string reason,
        string reasonCode,
        string reasonScope
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
            reason,
            reasonCode,
            reasonScope
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
