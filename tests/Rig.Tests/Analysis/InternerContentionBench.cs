using System.Collections.Concurrent;
using System.Diagnostics;

namespace Rig.Tests.Analysis;

// OPT-IN MICRO-MEASUREMENT — a no-op in the normal suite. Establishes the MECHANISM behind the
// interned arm's boot-time variance (base boots were 114.0/114.6s; interned 123.9/151.4s) before any
// optimisation is attempted. Hypothesis under test: ConcurrentDictionary.GetOrAdd contention/resizes
// across parallel per-project extraction — ~4.5M intern calls, ~1.67M distinct values (both measured
// on the real MedDBase boot), from ~ProcessorCount extraction threads.
//
// Four arms, same workload (realistic string lengths, measured distinct/total skew):
//   cd-default   ConcurrentDictionary with default capacity — the shipped shape. Pays every resize:
//                growing to 1.67M entries takes ~20 doublings, each acquiring EVERY bucket lock and
//                rehashing under it, serializing all writers.
//   cd-presized  same, capacity reserved up front — isolates the RESIZE share of the cost.
//   sharded      per-thread plain Dictionary, merged once at the end — the no-contention ceiling.
//   cd-warm      pre-POPULATED ConcurrentDictionary, lookup-only — the resident steady state (every
//                re-extraction after boot is ~all hits, which are lock-free reads).
//
//   $env:RIG_INTERN_BENCH="1"
//   dotnet run --project tests/Rig.Tests --no-build -- --treenode-filter "/*/*/InternerContentionBench/*"
//
// Report goes to a FILE (RIG_INTERN_BENCH_REPORT or %TEMP%\rig-interner-bench.log).
public sealed class InternerContentionBench
{
    [Test]
    public void Measure_concurrent_dictionary_fill_strategies_at_extraction_shape()
    {
        if (Environment.GetEnvironmentVariable("RIG_INTERN_BENCH") != "1")
        {
            return; // opt-in
        }

        var reportPath =
            Environment.GetEnvironmentVariable("RIG_INTERN_BENCH_REPORT") ?? Path.Combine(Path.GetTempPath(), "rig-interner-bench.log");
        void Say(string line)
        {
            Console.WriteLine(line);
            try
            {
                File.AppendAllText(reportPath, line + Environment.NewLine);
            }
            catch (IOException) { }
        }

        // The measured shape: 1.67M distinct values, ~4.5M intern calls. Value lengths sampled to
        // mirror the store's mix (DocIDs ~62 chars for the per-project distinct sum, encoded
        // invocation chains ~100+, hashes 16). A deterministic seed keeps arms identical.
        const int distinctCount = 1_670_000;
        const int totalCalls = 4_500_000;
        var threads = Environment.ProcessorCount;

        var distinct = new string[distinctCount];
        var seedRandom = new Random(42);
        for (var i = 0; i < distinctCount; i++)
        {
            var len = seedRandom.Next(3) switch
            {
                0 => 16, // BodyHash-ish
                1 => 62, // DocID-ish
                _ => 110, // encoded-chain-ish
            };
            distinct[i] = string.Create(
                len,
                i,
                static (span, seed) =>
                {
                    var rnd = new Random(seed);
                    for (var j = 0; j < span.Length; j++)
                    {
                        span[j] = (char)('a' + rnd.Next(26));
                    }
                }
            );
        }

        // Per-thread call sequences: each call picks a distinct index with the real skew (a Zipf-ish
        // bias — a minority of values account for most repeats, like literals-adjacent chains do).
        var perThread = totalCalls / threads;
        var sequences = new int[threads][];
        for (var t = 0; t < threads; t++)
        {
            var rnd = new Random(1000 + t);
            var seq = new int[perThread];
            for (var i = 0; i < perThread; i++)
            {
                // 60% of calls hit the hottest 10% of values; the rest spread uniformly.
                seq[i] = rnd.Next(10) < 6 ? rnd.Next(distinctCount / 10) : rnd.Next(distinctCount);
            }
            sequences[t] = seq;
        }

        Say($"[interner-bench] threads={threads} distinct={distinctCount} calls={totalCalls}");

        double RunThreads(Action<int> body)
        {
            var watch = Stopwatch.StartNew();
            Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, body);
            watch.Stop();
            return watch.Elapsed.TotalSeconds;
        }

        for (var round = 1; round <= 3; round++)
        {
            // cd-default: the shipped StringInterner shape.
            var cdDefault = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
            var tDefault = RunThreads(t =>
            {
                foreach (var i in sequences[t])
                {
                    var v = distinct[i];
                    cdDefault.GetOrAdd(v, v);
                }
            });

            // cd-presized: resize cost removed, write contention kept.
            var cdPresized = new ConcurrentDictionary<string, string>(
                concurrencyLevel: threads,
                capacity: distinctCount * 2,
                comparer: StringComparer.Ordinal
            );
            var tPresized = RunThreads(t =>
            {
                foreach (var i in sequences[t])
                {
                    var v = distinct[i];
                    cdPresized.GetOrAdd(v, v);
                }
            });

            // sharded: per-thread plain Dictionary then one merge — the no-contention ceiling.
            var shards = new Dictionary<string, string>[threads];
            var tSharded = RunThreads(t =>
            {
                var local = new Dictionary<string, string>(capacity: distinctCount / threads * 2, StringComparer.Ordinal);
                foreach (var i in sequences[t])
                {
                    var v = distinct[i];
                    local.TryAdd(v, v);
                }
                shards[t] = local;
            });
            var mergeWatch = Stopwatch.StartNew();
            var merged = new Dictionary<string, string>(capacity: distinctCount * 2, StringComparer.Ordinal);
            foreach (var shard in shards)
            {
                foreach (var (k, v) in shard)
                {
                    merged.TryAdd(k, v);
                }
            }
            mergeWatch.Stop();

            // cd-warm: the resident steady state — lookup-only on a full table (lock-free reads).
            var tWarm = RunThreads(t =>
            {
                foreach (var i in sequences[t])
                {
                    var v = distinct[i];
                    cdPresized.GetOrAdd(v, v);
                }
            });

            Say(
                $"[interner-bench] round {round}: cd-default {tDefault:F2}s | cd-presized {tPresized:F2}s"
                    + $" | sharded {tSharded:F2}s (+merge {mergeWatch.Elapsed.TotalSeconds:F2}s)"
                    + $" | cd-warm(hits) {tWarm:F2}s"
                    + $" | sizes {cdDefault.Count}/{cdPresized.Count}/{merged.Count}"
            );
        }
    }
}
