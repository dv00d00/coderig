using Rig.Cli;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Cli;

// `rig runs` must stay usable when the store set is PARTLY stale. Stores are per-commit and accumulate, so an
// old one lying around is the normal steady state — but the schema gate throws at open, and one throw used to
// abort the whole listing mid-stream (exit 2, remaining stores never printed). Since `runs` is the documented
// health check and step 1 of the review workflow, a single stale store made step 1 unusable. Hit for real on
// 2026-07-27: `rig runs` on the MedDBase workspace died on `Store schema v1, this rig expects v3` and never
// listed the healthy stores. See docs/backlog/todo/cli-surface-and-help-refresh-2026-07.md items 2-3.
public sealed class RunsStaleStoreTests
{
    // An UNREADABLE store: the dir + a rig.db the schema gate will reject (empty file => no meta table).
    // Mirrors StoreLayoutTests.MakeStore — presence is all the resolver checks, so this is enumerated as a
    // store and then fails at open, which is exactly the condition under test.
    private static void MakeUnreadableStore(string workingDirectory, string storeId)
    {
        var dir = Path.Combine(workingDirectory, ".rig", storeId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "rig.db"), "");
    }

    private static async Task<(int Exit, string Out, string Err)> Runs(string workingDirectory)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await CliApplication.RunAsync(["runs"], output, error, workingDirectory);
        return (exit, output.ToString(), error.ToString());
    }

    [Test]
    public async Task An_unreadable_store_is_marked_and_does_not_abort_the_listing()
    {
        using var playground = await TempPlayground.CreateCoreAllocationsAsync();
        var workingDirectory = Path.Combine(playground.RootDirectory, "workspace");
        var index = new StringWriter();
        (await CliApplication.RunAsync(["index", playground.SolutionPath], index, index, workingDirectory)).ShouldBe(0);

        // Two unreadable stores whose ids sort BEFORE the real one (`ts-…`), so they are enumerated FIRST —
        // the pre-fix ordering that killed the listing before the healthy store was ever reached.
        MakeUnreadableStore(workingDirectory, "aaa000000000");
        MakeUnreadableStore(workingDirectory, "bbb111111111");

        var (exit, stdout, _) = await Runs(workingDirectory);

        // Exit 0: the listing SUCCEEDED and reported the stale stores. A non-zero exit is what broke the
        // health check, and it is the assertion that would have caught the original bug.
        exit.ShouldBe(0);

        // Both bad stores are named and marked rather than silently skipped...
        stdout.ShouldContain("aaa000000000");
        stdout.ShouldContain("bbb111111111");
        stdout.ShouldContain("⚠ unreadable");

        // ...and the HEALTHY store past them still rendered its detail lines. This is the regression proper:
        // pre-fix, output stopped at the first bad store and `symbols=` never appeared.
        stdout.ShouldContain("symbols=");
        stdout.ShouldContain("solution=");

        // The trailing summary states the scale, for a reader who only sees the tail.
        stdout.ShouldContain("2 of 3 store(s) unreadable");
    }

    [Test]
    public async Task A_fully_healthy_store_set_reports_no_warning()
    {
        // Negative control: the marker and the summary must appear ONLY when something is actually wrong,
        // otherwise the warning becomes noise and stops being read.
        using var playground = await TempPlayground.CreateCoreAllocationsAsync();
        var workingDirectory = Path.Combine(playground.RootDirectory, "workspace");
        var index = new StringWriter();
        (await CliApplication.RunAsync(["index", playground.SolutionPath], index, index, workingDirectory)).ShouldBe(0);

        var (exit, stdout, _) = await Runs(workingDirectory);

        exit.ShouldBe(0);
        stdout.ShouldContain("symbols=");
        stdout.ShouldNotContain("⚠ unreadable");
        stdout.ShouldNotContain("store(s) unreadable");
    }

    [Test]
    public async Task The_schema_failure_message_names_the_offending_store()
    {
        // `impact` opens TWO stores (--base and --head) and other paths resolve one implicitly from
        // .rig/LATEST, so a bare "store schema v1, expects v3" left the user guessing which one to re-index.
        // The message must carry the db path of the store actually opened.
        using var playground = await TempPlayground.CreateCoreAllocationsAsync();
        var workingDirectory = Path.Combine(playground.RootDirectory, "workspace");
        var index = new StringWriter();
        (await CliApplication.RunAsync(["index", playground.SolutionPath], index, index, workingDirectory)).ShouldBe(0);
        MakeUnreadableStore(workingDirectory, "aaa000000000");

        var (_, stdout, _) = await Runs(workingDirectory);

        // The db path of the BAD store appears in its own warning line (not the default/LATEST store's path,
        // which is what the generic message used to misattribute).
        var warning = stdout.Split('\n').Single(l => l.Contains("⚠ unreadable", StringComparison.Ordinal));
        warning.ShouldContain(Path.Combine("aaa000000000", "rig.db"));
    }
}
