using System.Diagnostics;
using System.Globalization;
using Rig.Analysis;
using Rig.Analysis.Rules;
using Rig.Cli.Live;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Live;

// OPT-IN SELF-CALIBRATION HARNESS. It runs only through Rig.ManualIntegrationTests and measures the Rider
// file read model on rig's own solution, so the falsification corpus is available in every checkout.
//
//   dotnet run --project tests/Rig.ManualIntegrationTests -- --maximum-parallel-tests 1 \
//     --treenode-filter "/*/*/RiderFileEffectReadModelSelfTrial/*"
//
// Override only when comparing another checkout:
//   RIG_RIDER_SELF_SOLUTION=/abs/path/RuntimeIntelligenceGraph.slnx
//   RIG_RIDER_SELF_REPORT=/abs/path/report.log
public sealed class RiderFileEffectReadModelSelfTrial
{
    private static readonly FileEffectSelector SqlSelector = new(
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

    [Test]
    public async Task Measure_self_read_model_and_forward_reverse_agreement()
    {
        var repoRoot = FindRepoRoot();
        var solutionPath = Environment.GetEnvironmentVariable("RIG_RIDER_SELF_SOLUTION");
        solutionPath = string.IsNullOrWhiteSpace(solutionPath)
            ? Path.Combine(repoRoot, "RuntimeIntelligenceGraph.slnx")
            : Path.GetFullPath(solutionPath);
        var analysisRoot = Path.GetDirectoryName(solutionPath)!;
        var reportPath =
            Environment.GetEnvironmentVariable("RIG_RIDER_SELF_REPORT")
            ?? Path.Combine(Path.GetTempPath(), "rig-rider-file-effects-self.log");
        File.WriteAllText(reportPath, $"# rig Rider file-effect self trial{Environment.NewLine}");

        void Say(string line)
        {
            Console.WriteLine(line);
            File.AppendAllText(reportPath, line + Environment.NewLine);
        }

        var rules = RuleSetLoader.Load(analysisRoot);
        var setupWatch = Stopwatch.StartNew();
        var (facts, workspace) = await SolutionAnalyzer.AnalyzeRetainingWorkspaceAsync(
            solutionPath: solutionPath,
            rules: rules,
            progress: message => Say($"[setup] {message}"),
            excludeTests: true,
            buildCacheDir: Path.Combine(analysisRoot, ".rig", "dtb-cache")
        );
        setupWatch.Stop();
        using var _ = workspace;

        var symbols = facts.Symbols ?? [];
        var indexedFiles = facts
            .SourceFiles.Where(file => file.Status == "indexed")
            .Select(file => file.FilePath)
            .Distinct(FilePathComparer())
            .OrderBy(path => path, FilePathComparer())
            .ToArray();
        indexedFiles.Length.ShouldBeGreaterThan(0, "the self solution must contain indexed source files");
        Say(
            $"[trial] setup: {Seconds(setupWatch.Elapsed)}s | files={indexedFiles.Length} "
                + $"symbols={symbols.Count} refs={facts.References?.Count ?? 0}"
        );

        var live = new LiveFactSource(facts, rules);
        var warmWatch = Stopwatch.StartNew();
        live.WarmQueryArtifacts(CancellationToken.None);
        warmWatch.Stop();
        Say($"[trial] warm query artifacts: {Seconds(warmWatch.Elapsed)}s | {live.BuildTimeLine()}");

        var selectedEffects = live.Effects.Where(MatchesSql).ToArray();
        var directOwners = selectedEffects
            .Select(effect => effect.EnclosingSymbolId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        selectedEffects.Length.ShouldBeGreaterThan(0, "the self corpus must contain at least one selected SQL effect");
        directOwners.Count.ShouldBeGreaterThan(0, "selected SQL effects must have graph-owning methods");

        var managedBefore = GC.GetTotalMemory(forceFullCollection: true);
        var workingSetBefore = Process.GetCurrentProcess().WorkingSet64;
        var buildWatch = Stopwatch.StartNew();
        var index = live.FileEffects(SqlSelector);
        buildWatch.Stop();
        var managedAfter = GC.GetTotalMemory(forceFullCollection: true);
        var workingSetAfter = Process.GetCurrentProcess().WorkingSet64;

        var positiveMethods = new HashSet<string>(StringComparer.Ordinal);
        var positiveFiles = 0;
        foreach (var file in indexedFiles)
        {
            var model = index.Find(file);
            model.ShouldNotBeNull($"indexed file '{file}' must have an authoritative model");
            if (model!.Methods.Count == 0)
            {
                continue;
            }

            positiveFiles++;
            positiveMethods.UnionWith(model.Methods.Select(method => method.SymbolId));
        }

        Say(
            $"[trial] file model: {Milliseconds(buildWatch.Elapsed)}ms | selectedEffects={selectedEffects.Length} "
                + $"owners={directOwners.Count} positiveFiles={positiveFiles}/{indexedFiles.Length} "
                + $"positiveMethods={positiveMethods.Count} managedDelta={Mebibytes(managedAfter - managedBefore)}MiB "
                + $"workingSetDelta={Mebibytes(workingSetAfter - workingSetBefore)}MiB"
        );

        positiveMethods.Count.ShouldBeGreaterThan(0, "the reverse projection must produce positive methods on the self corpus");

        var warmIndexWatch = Stopwatch.StartNew();
        var equivalentIndex = live.FileEffects(
            new FileEffectSelector("sql", SqlSelector.Predicates.Reverse().Concat(SqlSelector.Predicates).ToArray())
        );
        warmIndexWatch.Stop();
        equivalentIndex.ShouldBeSameAs(index);
        Say($"[trial] equivalent selector cache hit: {Microseconds(warmIndexWatch.Elapsed)}us");

        const int lookupPasses = 500;
        var lookupRows = 0L;
        var lookupWatch = Stopwatch.StartNew();
        for (var pass = 0; pass < lookupPasses; pass++)
            foreach (var file in indexedFiles)
            {
                lookupRows += index.Find(file)?.Methods.Count ?? 0;
            }
        lookupWatch.Stop();
        GC.KeepAlive(lookupRows);
        var lookupCount = checked((long)lookupPasses * indexedFiles.Length);
        var averageLookupMicroseconds = lookupWatch.Elapsed.TotalMicroseconds / lookupCount;
        Say(
            $"[trial] warm file lookup: {lookupCount} lookups in {Milliseconds(lookupWatch.Elapsed)}ms "
                + $"| avg={averageLookupMicroseconds.ToString("F3", CultureInfo.InvariantCulture)}us"
        );

        var canonicalMethods = SymbolFactProjections.SelectCanonicalMethodFacts(symbols);
        var positiveSample = EvenSample(
            canonicalMethods.Where(method => positiveMethods.Contains(method.SymbolId)).Select(method => method.SymbolId).ToArray(),
            24
        );
        var negativeSample = EvenSample(
            canonicalMethods.Where(method => !positiveMethods.Contains(method.SymbolId)).Select(method => method.SymbolId).ToArray(),
            24
        );
        positiveSample.Length.ShouldBeGreaterThan(0);
        negativeSample.Length.ShouldBeGreaterThan(0);

        var sample = positiveSample.Concat(negativeSample).ToArray();
        var forwardWatch = Stopwatch.StartNew();
        var reaches = FactPathFinder.ReachesFromEachSeed(
            live.TraversalGraph,
            sample,
            maxDepth: int.MaxValue,
            maxNodes: int.MaxValue,
            narrowDispatch: true,
            mode: FactPathFinder.TraversalMode.SyncCut
        );
        forwardWatch.Stop();

        var mismatches = new List<string>();
        for (var i = 0; i < sample.Length; i++)
        {
            var reverseSaysPositive = positiveMethods.Contains(sample[i]);
            var forwardSaysPositive = reaches[i].Overlaps(directOwners);
            if (reverseSaysPositive != forwardSaysPositive)
            {
                mismatches.Add($"{sample[i]} reverse={reverseSaysPositive} forward={forwardSaysPositive}");
            }
        }

        Say(
            $"[trial] semantic sample: positive={positiveSample.Length} negative={negativeSample.Length} "
                + $"forwardTime={Milliseconds(forwardWatch.Elapsed)}ms mismatches={mismatches.Count}"
        );
        foreach (var mismatch in mismatches.Take(20))
        {
            Say($"[mismatch] {mismatch}");
        }

        mismatches.ShouldBeEmpty("the reverse file model must agree with exact-id forward one-hop reachability");
        Say($"[trial] report: {reportPath}");
    }

    private static bool MatchesSql(DerivedEffect effect) =>
        SqlSelector.Predicates.Any(predicate =>
            string.Equals(effect.Provider, predicate.Provider, StringComparison.Ordinal)
            && (predicate.Operation is null || string.Equals(effect.Operation, predicate.Operation, StringComparison.Ordinal))
        );

    private static string[] EvenSample(IReadOnlyList<string> values, int limit)
    {
        if (values.Count <= limit)
        {
            return values.ToArray();
        }

        return Enumerable
            .Range(0, limit)
            .Select(index => values[index * (values.Count - 1) / (limit - 1)])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RuntimeIntelligenceGraph.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find the rig repository root.");
    }

    private static StringComparer FilePathComparer() =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string Seconds(TimeSpan elapsed) => elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture);

    private static string Milliseconds(TimeSpan elapsed) => elapsed.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture);

    private static string Microseconds(TimeSpan elapsed) => elapsed.TotalMicroseconds.ToString("F2", CultureInfo.InvariantCulture);

    private static string Mebibytes(long bytes) => (bytes / (1024.0 * 1024)).ToString("F2", CultureInfo.InvariantCulture);
}
