using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis.Text;
using Rig.Analysis;
using Rig.Analysis.Extraction;
using Rig.Analysis.Inventory;
using Rig.Analysis.Rules;
using Rig.Cli.Live;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Analysis;

public sealed class LiveSnapshotScaleTrialContractTests
{
    [Test]
    public void Engine_contract_accepts_only_the_legacy_arm()
    {
        LiveSnapshotScaleTrial.ValidateEngine("legacy").ShouldBe("legacy");
        Should
            .Throw<InvalidOperationException>(() => LiveSnapshotScaleTrial.ValidateEngine("snapshot"))
            .Message.ShouldContain("not implemented");
        Should
            .Throw<InvalidOperationException>(() => LiveSnapshotScaleTrial.ValidateEngine("future"))
            .Message.ShouldContain("Only 'legacy'");
        Should
            .Throw<InvalidOperationException>(() => LiveSnapshotScaleTrial.ValidateEngine(null))
            .Message.ShouldContain("RIG_LIVE_TRIAL_ENGINE");
        LiveSnapshotScaleTrial.ValidateRuntimeEngine("legacy", null);
        LiveSnapshotScaleTrial.ValidateRuntimeEngine("legacy", "legacy");
        Should
            .Throw<InvalidOperationException>(() => LiveSnapshotScaleTrial.ValidateRuntimeEngine("legacy", "snapshot"))
            .Message.ShouldContain("disagrees");
        LiveSnapshotScaleTrial.ValidateQueryMode(null).ShouldBe("reaches");
        LiveSnapshotScaleTrial.ValidateQueryMode("reaches").ShouldBe("reaches");
        Should
            .Throw<InvalidOperationException>(() => LiveSnapshotScaleTrial.ValidateQueryMode("tree"))
            .Message.ShouldContain("Only 'reaches'");
    }

    [Test]
    public void Corpus_reuse_requires_absence_or_an_explicit_regenerate_switch()
    {
        LiveSnapshotScaleTrial.ShouldGenerateCorpus(directoryExists: false, regenerate: false).ShouldBeTrue();
        LiveSnapshotScaleTrial.ShouldGenerateCorpus(directoryExists: true, regenerate: false).ShouldBeFalse();
        LiveSnapshotScaleTrial.ShouldGenerateCorpus(directoryExists: true, regenerate: true).ShouldBeTrue();
    }

    [Test]
    public async Task Jsonl_append_recovers_a_truncated_tail_and_regenerates_markdown()
    {
        var root = Directory.CreateTempSubdirectory("rig-live-trial-report-").FullName;
        try
        {
            var report = new LiveTrialReport(Path.Combine(root, "trial.jsonl"));
            var first = LiveSnapshotScaleTrial.TestRecord("run-a", 1, "initial-load", "hash-a");
            var second = LiveSnapshotScaleTrial.TestRecord("run-a", 2, "generation-1", "hash-b");

            await report.AppendAsync(first);
            File.ReadAllLines(report.JsonlPath).Length.ShouldBe(1);
            var firstRead = report.ReadAll();
            firstRead.Count.ShouldBe(1);
            firstRead[0].Phase.ShouldBe(first.Phase);
            firstRead[0].UnavailableMetrics.Keys.ShouldBe(first.UnavailableMetrics.Keys, ignoreOrder: false);

            await File.AppendAllTextAsync(report.JsonlPath, "{\"truncated\":");
            report.ReadAll().Select(record => record.Phase).ShouldBe(["initial-load"], ignoreOrder: false);
            await report.AppendAsync(second);

            File.ReadAllLines(report.JsonlPath).Length.ShouldBe(2);
            report.ReadAll().Select(record => record.Sequence).ShouldBe([1, 2], ignoreOrder: false);
            var markdown = File.ReadAllText(report.MarkdownPath);
            markdown.ShouldContain("initial-load");
            markdown.ShouldContain("generation-1");
            markdown.ShouldContain("malformed final JSONL row was discarded");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Canonical_hash_is_order_independent_and_content_sensitive()
    {
        LiveSnapshotScaleTrial.HashRows(["b", "a", "c"]).ShouldBe(LiveSnapshotScaleTrial.HashRows(["c", "b", "a"]));
        LiveSnapshotScaleTrial.HashRows(["a", "a", "b"]).ShouldBe(LiveSnapshotScaleTrial.HashRows(["b", "a"]));
        LiveSnapshotScaleTrial.HashRows(["b", "a", "c"]).ShouldNotBe(LiveSnapshotScaleTrial.HashRows(["b", "a", "changed"]));
        LiveTrialReport.Number(1234.5).ShouldBe("1234.5");
    }
}
