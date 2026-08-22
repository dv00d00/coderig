namespace Rig.Tests.Fixtures;

internal static class TestResourceKeys
{
    // The two resident-index equivalence tests each retain and rebuild a workspace. Keep them sequential
    // without globally draining unrelated tests; their SolutionAnalyzer calls also pin inner parallelism.
    internal const string ResidentIndexWorkspace = "resident-index-workspace";
}
