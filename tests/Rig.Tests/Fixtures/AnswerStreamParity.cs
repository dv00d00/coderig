namespace Rig.Tests.Fixtures;

internal static class AnswerStreamParity
{
    // Answer parity is about CONTENT, not ENCODING. The two sides of a live/store comparison are produced by
    // different writers — `CliApplication.RunAsync` configures its streams to LF, while a test that invokes a
    // command's RunAsync directly passes a bare StringWriter (Environment.NewLine) — so a byte comparison
    // fails on Windows over a difference no consumer can observe, and prints a diff whose lines look
    // identical. Compare canonical forms instead: line endings normalized to LF, exactly one trailing
    // newline. Everything a routing/derivation regression would actually change — line content, order,
    // spacing within a line, which stream carried it — still fails the comparison.
    //
    // The LF contract itself is NOT enforced here. It is pinned once, deliberately, in
    // Rig.Tests.Cli.OutputLineEndingContractTests; leaving it to be caught incidentally by these assertions
    // is what made them brittle in the first place.
    public static string Canonical(string stream) =>
        stream.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n') + "\n";

    public static string WithoutImmutableStoreDisclosure(string stream) =>
        Canonical(string.Join("\n", Canonical(stream).Split('\n').Where(line => !line.StartsWith("store: ", StringComparison.Ordinal))));
}
