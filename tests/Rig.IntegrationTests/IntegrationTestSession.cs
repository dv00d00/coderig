using Rig.Analysis;

namespace Rig.IntegrationTests;

public static class IntegrationTestSession
{
    [Before(TestSession)]
    public static void SerializeSolutionLoading() => SolutionAnalyzer.ProcessParallelismOverride = 1;

    [After(TestSession)]
    public static void RestoreSolutionLoadingDefault() => SolutionAnalyzer.ProcessParallelismOverride = null;
}
