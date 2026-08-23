namespace Rig.Tests.Fixtures;

internal static class AnswerStreamParity
{
    public static string WithoutImmutableStoreDisclosure(string stream) =>
        string.Join(
            Environment.NewLine,
            stream.Split(Environment.NewLine).Where(line => !line.StartsWith("store: ", StringComparison.Ordinal))
        );
}
