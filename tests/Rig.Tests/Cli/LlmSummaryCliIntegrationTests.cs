using Rig.Cli;
using Rig.Cli.Rendering;
using Rig.Domain.Data;
using Shouldly;

namespace Rig.Tests.Cli;

// CLI-level tests for --format llm: verify the option parses, mutual exclusion works, and output has the right shape.
public sealed class LlmSummaryCliTests
{
    [Test]
    public async Task Format_llm_with_full_is_accepted()
    {
        // --view full --format llm is a valid combination (Full projection).
        var output = new StringWriter();
        var error = new StringWriter();

        // No index exists, but it should fail with "No symbol matches" (exit 1) not a validation error.
        // We only check that the CLI does NOT emit a "can't be combined" validation error.
        var exitCode = await CliApplication.RunAsync(["tree", "X", "--view", "full", "--format", "llm"], output, error);

        // May fail for "no index" reasons, but not for a validation/parse error.
        error.ToString().ShouldNotContain("can't be combined");
        error.ToString().ShouldNotContain("--format llm");
    }

    [Test]
    public async Task Format_llm_with_effects_is_accepted()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["tree", "X", "--view", "effects", "--format", "llm"], output, error);

        error.ToString().ShouldNotContain("can't be combined");
    }

    [Test]
    public async Task Format_llm_alone_is_accepted()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["tree", "X", "--format", "llm"], output, error);

        error.ToString().ShouldNotContain("can't be combined");
    }

    [Test]
    public async Task Format_llm_combined_with_summary_is_rejected()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["tree", "X", "--format", "llm", "--view", "summary"], output, error);

        exitCode.ShouldBe(1);
        error.ToString().ShouldContain("can't be combined");
        error.ToString().ShouldContain("--view summary");
    }

    [Test]
    public async Task Format_llm_combined_with_hazards_is_rejected()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["tree", "X", "--format", "llm", "--view", "hazards"], output, error);

        exitCode.ShouldBe(1);
        error.ToString().ShouldContain("can't be combined");
        error.ToString().ShouldContain("--view hazards");
    }

    [Test]
    public async Task Unknown_view_value_is_rejected()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["tree", "X", "--view", "invalid"], output, error);

        exitCode.ShouldBe(1);
        error.ToString().ShouldContain("invalid");
        error.ToString().ShouldContain("not recognized");
    }

    [Test]
    public async Task Format_llm_effects_emits_header_and_correct_shape_on_real_index()
    {
        using var playground = await Rig.Tests.Fixtures.TempPlayground.CreateEntryPointEffectsAsync();
        var workingDirectory = System.IO.Path.Combine(playground.RootDirectory, "workspace");
        var rulesPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(playground.SolutionPath)!, "rig.rules.json");
        var sw = new StringWriter();
        var err = new StringWriter();

        (await CliApplication.RunAsync(["index", playground.SolutionPath], sw, err, workingDirectory)).ShouldBe(0);

        sw.GetStringBuilder().Clear();
        (
            await CliApplication.RunAsync(
                ["tree", "PaymentGatewayCaller.Dispatch", "--view", "effects", "--format", "llm", "--rules", rulesPath],
                sw,
                err,
                workingDirectory
            )
        ).ShouldBe(0);

        var lines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
        // --view effects → EffectsFlat projection: 7-column header (with parent-name column).
        lines[0].ShouldBe("depth\tparent\tname\tarity\tcalls\teffects\tflags");
        // At least one data row (effects exist: gateway_ask, gateway_tell)
        lines.Count.ShouldBeGreaterThan(1);
        // Every data row must have exactly 7 tab-separated columns
        foreach (var line in lines.Skip(1))
        {
            var cols = line.Split('\t');
            cols.Length.ShouldBe(7);
            // name column (index 2) must not contain full CLR namespace prefixes
            cols[2].ShouldNotContain("System.");
            // effects column (index 5) must be ASCII, no × character, no internal spaces
            if (cols[5].Length > 0)
            {
                cols[5].ShouldNotContain("×");
                cols[5].ShouldNotContain(" ");
            }
        }
    }

    [Test]
    public async Task Full_format_llm_emits_more_rows_than_effects_format_llm()
    {
        using var playground = await Rig.Tests.Fixtures.TempPlayground.CreateEntryPointEffectsAsync();
        var workingDirectory = System.IO.Path.Combine(playground.RootDirectory, "workspace");
        var rulesPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(playground.SolutionPath)!, "rig.rules.json");
        var sw = new StringWriter();
        var err = new StringWriter();

        (await CliApplication.RunAsync(["index", playground.SolutionPath], sw, err, workingDirectory)).ShouldBe(0);

        sw.GetStringBuilder().Clear();
        (
            await CliApplication.RunAsync(
                ["tree", "PaymentGatewayCaller.Dispatch", "--view", "full", "--format", "llm", "--rules", rulesPath],
                sw,
                err,
                workingDirectory
            )
        ).ShouldBe(0);
        var fullLines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        sw.GetStringBuilder().Clear();
        (
            await CliApplication.RunAsync(
                ["tree", "PaymentGatewayCaller.Dispatch", "--view", "effects", "--format", "llm", "--rules", rulesPath],
                sw,
                err,
                workingDirectory
            )
        ).ShouldBe(0);
        var effectsLines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        // --view full --format llm has at least as many rows as --view effects --format llm (effects is the subset)
        fullLines.ShouldBeGreaterThanOrEqualTo(effectsLines);
    }

    [Test]
    public async Task Default_format_llm_row_count_is_between_full_and_effects_flat()
    {
        using var playground = await Rig.Tests.Fixtures.TempPlayground.CreateEntryPointEffectsAsync();
        var workingDirectory = System.IO.Path.Combine(playground.RootDirectory, "workspace");
        var rulesPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(playground.SolutionPath)!, "rig.rules.json");
        var sw = new StringWriter();
        var err = new StringWriter();

        (await CliApplication.RunAsync(["index", playground.SolutionPath], sw, err, workingDirectory)).ShouldBe(0);

        sw.GetStringBuilder().Clear();
        (
            await CliApplication.RunAsync(
                ["tree", "PaymentGatewayCaller.Dispatch", "--view", "full", "--format", "llm", "--rules", rulesPath],
                sw,
                err,
                workingDirectory
            )
        ).ShouldBe(0);
        var fullLines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        sw.GetStringBuilder().Clear();
        (
            await CliApplication.RunAsync(
                ["tree", "PaymentGatewayCaller.Dispatch", "--format", "llm", "--rules", rulesPath],
                sw,
                err,
                workingDirectory
            )
        ).ShouldBe(0);
        var defaultLines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        sw.GetStringBuilder().Clear();
        (
            await CliApplication.RunAsync(
                ["tree", "PaymentGatewayCaller.Dispatch", "--view", "effects", "--format", "llm", "--rules", rulesPath],
                sw,
                err,
                workingDirectory
            )
        ).ShouldBe(0);
        var effectsLines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        // full >= default (effectful-paths with spine) >= effects-flat
        fullLines.ShouldBeGreaterThanOrEqualTo(defaultLines);
        defaultLines.ShouldBeGreaterThanOrEqualTo(effectsLines);
    }

    [Test]
    public async Task Format_llm_ids_alone_is_accepted()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["tree", "X", "--format", "llm-ids"], output, error);

        error.ToString().ShouldNotContain("can't be combined");
    }

    [Test]
    public async Task Format_llm_ids_with_full_is_accepted()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["tree", "X", "--view", "full", "--format", "llm-ids"], output, error);

        error.ToString().ShouldNotContain("can't be combined");
    }

    [Test]
    public async Task Format_llm_ids_with_effects_is_accepted()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["tree", "X", "--view", "effects", "--format", "llm-ids"], output, error);

        error.ToString().ShouldNotContain("can't be combined");
    }

    [Test]
    public async Task Format_llm_ids_combined_with_summary_is_rejected()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["tree", "X", "--format", "llm-ids", "--view", "summary"], output, error);

        exitCode.ShouldBe(1);
        error.ToString().ShouldContain("can't be combined");
        error.ToString().ShouldContain("--view summary");
    }

    [Test]
    public async Task Format_llm_ids_combined_with_hazards_is_rejected()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["tree", "X", "--format", "llm-ids", "--view", "hazards"], output, error);

        exitCode.ShouldBe(1);
        error.ToString().ShouldContain("can't be combined");
        error.ToString().ShouldContain("--view hazards");
    }

    [Test]
    public async Task Format_llm_ids_emits_8_column_header_and_rows_on_real_index()
    {
        using var playground = await Rig.Tests.Fixtures.TempPlayground.CreateEntryPointEffectsAsync();
        var workingDirectory = System.IO.Path.Combine(playground.RootDirectory, "workspace");
        var rulesPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(playground.SolutionPath)!, "rig.rules.json");
        var sw = new StringWriter();
        var err = new StringWriter();

        (await CliApplication.RunAsync(["index", playground.SolutionPath], sw, err, workingDirectory)).ShouldBe(0);

        sw.GetStringBuilder().Clear();
        (
            await CliApplication.RunAsync(
                ["tree", "PaymentGatewayCaller.Dispatch", "--view", "full", "--format", "llm-ids", "--rules", rulesPath],
                sw,
                err,
                workingDirectory
            )
        ).ShouldBe(0);

        var lines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
        // 8-column header
        lines[0].ShouldBe("id\tparent_id\tdepth\tname\tarity\tcalls\teffects\tflags");
        lines.Count.ShouldBeGreaterThan(1);

        // Collect id → row for parent_id validation.
        var idToRowIndex = new Dictionary<int, int>(capacity: lines.Count);
        for (var i = 1; i < lines.Count; i++)
        {
            var cols = lines[i].Split('\t');
            cols.Length.ShouldBe(8, $"Expected 8 columns for llm-ids; got {cols.Length} on: {lines[i]}");
            var rowId = int.Parse(cols[0], System.Globalization.CultureInfo.InvariantCulture);
            idToRowIndex[rowId] = i;
        }

        // ids must be monotonic 1-based
        var sortedIds = idToRowIndex.Keys.OrderBy(x => x).ToList();
        for (var i = 0; i < sortedIds.Count; i++)
        {
            sortedIds[i].ShouldBe(i + 1, $"id at position {i} should be {i + 1}");
        }

        // Every non-root row's parent_id must refer to a row with a smaller id.
        for (var i = 1; i < lines.Count; i++)
        {
            var cols = lines[i].Split('\t');
            var parentIdStr = cols[1];
            if (string.IsNullOrEmpty(parentIdStr))
            {
                continue; // root row — no parent
            }

            var parentId = int.Parse(parentIdStr, System.Globalization.CultureInfo.InvariantCulture);
            var thisId = int.Parse(cols[0], System.Globalization.CultureInfo.InvariantCulture);
            parentId.ShouldBeLessThan(thisId, $"parent_id {parentId} should be less than row id {thisId}");
            idToRowIndex.ContainsKey(parentId).ShouldBeTrue($"parent_id {parentId} does not match any emitted row id");
        }
    }

    [Test]
    public async Task Default_format_llm_output_is_reconstructable()
    {
        using var playground = await Rig.Tests.Fixtures.TempPlayground.CreateEntryPointEffectsAsync();
        var workingDirectory = System.IO.Path.Combine(playground.RootDirectory, "workspace");
        var rulesPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(playground.SolutionPath)!, "rig.rules.json");
        var sw = new StringWriter();
        var err = new StringWriter();

        (await CliApplication.RunAsync(["index", playground.SolutionPath], sw, err, workingDirectory)).ShouldBe(0);

        sw.GetStringBuilder().Clear();
        (
            await CliApplication.RunAsync(
                ["tree", "PaymentGatewayCaller.Dispatch", "--format", "llm", "--rules", rulesPath],
                sw,
                err,
                workingDirectory
            )
        ).ShouldBe(0);

        var lines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
        // Default projection → EffectfulPaths: 6-column header (no parent column — depth+order encode linkage).
        lines[0].ShouldBe("depth\tname\tarity\tcalls\teffects\tflags");

        // Reconstructability via depth+order: every non-root row must have a preceding row at depth-1.
        var lastAtDepth = new Dictionary<int, string>(capacity: 8);
        foreach (var dataLine in lines.Skip(1))
        {
            var cols = dataLine.Split('\t');
            cols.Length.ShouldBe(6, $"Expected 6 columns (no parent) for default/EffectfulPaths; got {cols.Length} on: {dataLine}");
            var depth = int.Parse(cols[0], System.Globalization.CultureInfo.InvariantCulture);
            var name = cols[1];
            if (depth > 0)
            {
                lastAtDepth.ShouldContainKey(
                    depth - 1,
                    $"Row '{name}' at depth {depth} has no preceding row at depth {depth - 1} — not reconstructable by depth+order."
                );
            }

            lastAtDepth[depth] = name;
        }
    }
}
