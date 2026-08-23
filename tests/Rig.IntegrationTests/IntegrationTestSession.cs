using Rig.Analysis;

namespace Rig.IntegrationTests;

public static class IntegrationTestSession
{
    private static string? _previousDisableNodeReuse;

    [Before(TestSession)]
    public static void SerializeSolutionLoading()
    {
        // This executable deliberately runs many Buildalyzer design-time builds against different temporary
        // copies of the same project graph. Reusing an MSBuild node across those roots can retain evaluated
        // project state from the preceding copy and intermittently drop ProjectReference results. Keep the
        // containment local to the slow integration process; production indexing retains normal node reuse.
        _previousDisableNodeReuse = Environment.GetEnvironmentVariable("MSBUILDDISABLENODEREUSE");
        Environment.SetEnvironmentVariable("MSBUILDDISABLENODEREUSE", "1");
        SolutionAnalyzer.ProcessParallelismOverride = 1;
    }

    [After(TestSession)]
    public static void RestoreSolutionLoadingDefault()
    {
        SolutionAnalyzer.ProcessParallelismOverride = null;
        Environment.SetEnvironmentVariable("MSBUILDDISABLENODEREUSE", _previousDisableNodeReuse);
    }
}
