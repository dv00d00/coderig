using System.Reflection;
using Rig.Analysis.Rules;
using Rig.Cli;
using Rig.Cli.CommandLine;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Storage;

// THE GATE THAT WAS MISSING. `reference_facts` -> FactInvocation was mapped in three hand-maintained places
// (the EF whole-store loader, the raw-ADO BOUNDED loader behind the reaches/tree/path fast path, and the
// in-memory twin), and they DRIFTED: the bounded loader never selected `EnclosingScopes`, so every
// lexical-scope observation (lock_held_across_effect / transaction_spans_effect, derived in FactEffectDeriver
// from exactly that field) was silently absent from rig's most-used commands while `derive` reported it. Same
// store, same rules, two different answers — for months.
//
// SqlReachabilityTests.Bounded_graph_reproduces_full_graph_reach_in_both_modes covered the GRAPH the two paths
// walk; nothing covered the INPUTS they derive effects from, which is why this survived. So the comparison
// here is over the INPUTS, and it is done by REFLECTION over the record's properties rather than a
// hand-written field list: a field added to FactInvocation is then compared automatically, and the next
// loader that skips one fails a test instead of shipping.
//
// Both halves carry an anti-vacuity guard. A field-by-field comparison of two empty sets passes, and a
// comparison of records whose EnclosingScopes is null on BOTH sides passes on the very bug this exists to
// catch — so the compared set must be non-empty AND at least one record must carry a non-null EnclosingScopes.
[ClassDataSource<AnalyzedPlaygrounds>(Shared = SharedType.PerTestSession)]
public sealed class ReachInputProjectionTests(AnalyzedPlaygrounds playgrounds)
{
    // Broad (every DocID in the playground contains the namespace, so the closure is effectively the whole
    // store) and narrow (a two-method closure) — the bounded loader must agree with the whole-store loader at
    // both ends of the size range, since the bug was invisible precisely because it was size-independent.
    private static readonly string[] Patterns = ["LegacyNet48Web", "LockZoo", "TransactionZoo"];

    [Test]
    public async Task Bounded_invocation_refs_are_field_equal_to_the_whole_store_loader()
    {
        var playground = await playgrounds.LegacyNet48Async();

        await WithMaterializedStoreAsync(
            playground,
            async context =>
            {
                var wholeStore = await Reads.LoadInvocationRefsAsync(context);
                wholeStore.ShouldNotBeEmpty();

                var comparedTotal = 0;
                var scopedTotal = 0;
                foreach (var pattern in Patterns)
                {
                    var bounded = (
                        await SqlReachability.LoadReachInputsAsync(context, pattern, SqlReachability.Direction.Forward)
                    ).Invocations;

                    // The bounded loader selects the refs whose ENCLOSING symbol is in the reach closure, so the
                    // whole-store set restricted to those same enclosing symbols is exactly what it must return.
                    var enclosing = bounded.Select(i => i.Enclosing).Where(e => e is not null).ToHashSet(StringComparer.Ordinal);
                    var expected = wholeStore.Where(i => i.Enclosing is not null && enclosing.Contains(i.Enclosing)).ToList();

                    Rendered(bounded)
                        .ShouldBe(
                            Rendered(expected),
                            customMessage: $"bounded != whole-store FactInvocation records for pattern '{pattern}'"
                        );

                    bounded.ShouldNotBeEmpty($"pattern '{pattern}' bounded no invocations — the comparison would be vacuous");
                    comparedTotal += bounded.Count;
                    scopedTotal += bounded.Count(i => i.Nesting.Scopes is not null);
                }

                // ANTI-VACUITY: without a single non-null EnclosingScopes in the compared set, this test would
                // have passed against the very bug it exists to catch (both sides null == "equal").
                scopedTotal.ShouldBeGreaterThan(0, "no compared record carried EnclosingScopes — the comparison is vacuous");
                comparedTotal.ShouldBeGreaterThan(0);
                Report(
                    $"[reach-inputs] invocations compared: {comparedTotal} record(s) across {Patterns.Length} pattern(s) "
                        + $"({scopedTotal} carrying EnclosingScopes); whole-store total {wholeStore.Count}"
                );

                // …and the FIX itself, pinned at the loader: the soap call inside `lock (_gate)` must arrive with
                // its lock scope on the bounded path, because that field is what FactEffectDeriver turns into the
                // lock_held_across_effect observation.
                var lockSite = (
                    await SqlReachability.LoadReachInputsAsync(context, "LockZoo.SubmitUnderLock", SqlReachability.Direction.Forward)
                )
                    .Invocations.Where(i => i.Enclosing?.Contains("SubmitUnderLock", StringComparison.Ordinal) == true)
                    .FirstOrDefault(i => i.Nesting.Scopes is not null);
                lockSite.ShouldNotBeNull("no invocation inside LockZoo.SubmitUnderLock carried EnclosingScopes on the bounded path");
                lockSite!.Nesting.Scopes!.ShouldContain("lock");
            }
        );
    }

    // The other reference_facts-derived records the same two loaders build. Their projections are still
    // hand-maintained on each path (4-5 stable fields, and sharing the invocation column set would WIDEN their
    // SELECTs for no correctness gain) — so the drift is fenced by this gate instead of by construction.
    [Test]
    public async Task Bounded_ctor_and_throw_refs_are_field_equal_to_the_whole_store_loaders()
    {
        var playground = await playgrounds.LegacyNet48Async();

        await WithMaterializedStoreAsync(
            playground,
            async context =>
            {
                var wholeStoreCtors = (await Reads.LoadFactEntryPointDataAsync(context)).CtorRefs;
                var wholeStoreThrows = await Reads.LoadThrowRefsAsync(context);

                var compared = 0;
                foreach (var pattern in Patterns)
                {
                    var bounded = await SqlReachability.LoadReachInputsAsync(context, pattern, SqlReachability.Direction.Forward);

                    AssertSameRefs(bounded.CtorRefs, wholeStoreCtors, pattern, "ctor");
                    AssertSameRefs(bounded.ThrowRefs, wholeStoreThrows, pattern, "throw");
                    compared += bounded.CtorRefs.Count + bounded.ThrowRefs.Count;
                }

                compared.ShouldBeGreaterThan(0, "no ctor/throw refs were compared — the comparison would be vacuous");
                Report($"[reach-inputs] ctor+throw refs compared: {compared} record(s) across {Patterns.Length} pattern(s)");
            }
        );
    }

    // END TO END, on the command a user actually runs. The loader-level test above proves the field arrives; this
    // proves it comes out the other end of `reaches` — through the CLI, over a store indexed by the CLI (so the
    // derived graph exists and the SQL fast path, not the EF fallback, is what serves the answer).
    //
    // No new playground fixture: LegacyNet48Web's LockZoo.SubmitUnderLock already holds a lock across a SOAP
    // call and TransactionZoo.SubmitInsideTransaction already wraps one in a transaction `using` — both were
    // already pinned on the WHOLE-STORE path by FactDerivationTests, which is exactly why the bounded path's
    // silence was a divergence and not a missing fact. Adding a fixture would have perturbed the fact counts
    // other tests pin for no gain.
    [Test]
    public async Task Reaches_reports_the_lexical_scope_observations_the_bounded_loader_used_to_drop()
    {
        using var playground = await TempPlayground.CreateLegacyNet48Async();

        var indexLog = new StringWriter();
        (await CliApplication.RunAsync(["index", playground.SolutionPath], indexLog, indexLog, playground.WorkingDirectory)).ShouldBe(
            0,
            indexLog.ToString()
        );

        // …and it IS the bounded path being exercised: `reaches` only takes the raw-ADO fast path when the store
        // carries a derived graph. Without this guard the test would pass on the EF fallback, which never had
        // the bug — the exact way a gate ends up too small to host the defect under test.
        await using (var probe = new RigDbContext(StoreLayout.DbPath(playground.WorkingDirectory), readOnly: true))
        {
            (await SqlReachability.HasGraphAsync(probe)).ShouldBeTrue(
                "the indexed store has no derived graph, so `reaches` would answer from the EF fallback and the bounded loader would go untested"
            );
        }

        var lockOut = await ReachesAsync(playground, "LockZoo.SubmitUnderLock");
        lockOut.ShouldContain("From: LockZoo.SubmitUnderLock");
        // The exact rendering, from a real run against a real store (EntryPointListRenderer.SpanTag):
        //   d0  soap submit  External.HealthcodeServiceProxy  <- LockZoo.SubmitUnderLock  ⚠ lock-held-across
        lockOut.ShouldContain("soap submit  External.HealthcodeServiceProxy  <- LockZoo.SubmitUnderLock  ⚠ lock-held-across");

        // The transaction scope rides the same field, so it recovered with it — and its NEGATIVE twin
        // (SubmitWithoutTransaction, the same SOAP call outside any transaction) must stay unmarked, or the
        // assertion above would also pass against a build that tagged every effect unconditionally.
        var txOut = await ReachesAsync(playground, "TransactionZoo");
        txOut.ShouldContain("soap submit  External.HealthcodeServiceProxy  <- TransactionZoo.SubmitInsideTransaction  ⚠ inside-open-tx");
        txOut.ShouldContain("soap submit  External.HealthcodeServiceProxy  <- TransactionZoo.SubmitWithoutTransaction");
        txOut.ShouldNotContain("TransactionZoo.SubmitWithoutTransaction  ⚠");
    }

    private static async Task<string> ReachesAsync(TempPlayground playground, string pattern)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await CliApplication.RunAsync(["reaches", pattern], output, error, playground.WorkingDirectory);
        exit.ShouldBe(0, $"`reaches {pattern}` failed:{Environment.NewLine}{output}{error}");
        return output.ToString();
    }

    // SymbolRef sets, compared the same way: the whole-store list restricted to the enclosing symbols the
    // bounded list covers must be exactly the bounded list. Both loaders dedup (by (file,line) for ctor refs,
    // (file,line,target) for throw refs), so the comparison is over SETS of rendered records.
    private static void AssertSameRefs(IReadOnlyList<SymbolRef> bounded, IReadOnlyList<SymbolRef> wholeStore, string pattern, string kind)
    {
        var enclosing = bounded.Select(r => r.Enclosing).Where(e => e is not null).ToHashSet(StringComparer.Ordinal);
        var expected = wholeStore.Where(r => r.Enclosing is not null && enclosing.Contains(r.Enclosing)).ToList();
        Rendered(bounded).ShouldBe(Rendered(expected), customMessage: $"bounded != whole-store {kind} refs for pattern '{pattern}'");
    }

    // Every public property of the record, by REFLECTION — so a new field is compared without anyone
    // remembering to add it here. Sorted, because neither loader promises an order.
    //
    // Nested GROUP properties (FactInvocation's Args / Loop / Nesting — readonly record structs, see Facts.cs)
    // are flattened one level so each grouped member is still compared as its own name=value pair. Relying on
    // the struct's generated ToString instead would fold a null and an empty string into the same rendering,
    // which is precisely the distinction this gate exists to see.
    private static string[] Rendered<T>(IEnumerable<T> records)
    {
        var properties = Fields(typeof(T));
        properties.ShouldNotBeEmpty();
        return records
            .Select(record => string.Join('|', properties.Select(field => $"{field.Name}={field.Read(record!) ?? "<null>"}")))
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToArray();
    }

    // One compared leaf: its qualified name and how to read it off a record instance.
    private sealed record Field(string Name, Func<object, object?> Read);

    private static Field[] Fields(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .SelectMany(p =>
                IsGroup(p.PropertyType)
                    ? Fields(p.PropertyType).Select(inner => new Field($"{p.Name}.{inner.Name}", record => inner.Read(p.GetValue(record)!)))
                    : [new Field(p.Name, p.GetValue)]
            )
            .ToArray();

    // A field GROUP: a non-primitive value type declared in the fact model (FactCallArguments / FactLoopContext
    // / FactCallSiteNesting). Strings, ints and bools are leaves.
    private static bool IsGroup(Type type) =>
        type is { IsValueType: true, IsPrimitive: false, IsEnum: false } && type.Namespace == "Rig.Domain.Data";

    // Compared counts to a FILE (RIG_PARITY_REPORT, the same channel LiveFactSourceParityTests uses) — never
    // Console, which TUnit swallows in its default mode. No assertion depends on it; the anti-vacuity guards do
    // that job. It exists so "how much did this actually compare?" is answerable without editing the test.
    private static void Report(string line)
    {
        var path = Environment.GetEnvironmentVariable("RIG_PARITY_REPORT");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        lock (ReportLock)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    File.AppendAllText(path, line + Environment.NewLine);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(10);
                }
            }
        }
    }

    private static readonly object ReportLock = new();

    // Mirrors SqlReachabilityTests.WithMaterializedStoreAsync (private there): save the analyzed playground to a
    // throwaway store, build the derived graph (the bounded loader needs it), then read.
    private static async Task WithMaterializedStoreAsync(AnalyzedPlayground playground, Func<RigDbContext, Task> assert)
    {
        var rules = RuleSetLoader.Load(playground.WorkingDirectory);
        var directory = Path.Combine(Path.GetTempPath(), "rig-reachinputs-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "rig.db");
        try
        {
            await using (var write = new RigDbContext(databasePath, pooling: false))
            {
                await Writes.SaveAsync(write, playground.Result);
            }

            await using (var build = new RigDbContext(databasePath, pooling: false))
            {
                await GraphMaterializer.BuildAsync(build, rules.Handoff.ToArray(), factoryRules: rules.Factory);
            }

            await using (var read = new RigDbContext(databasePath, pooling: false))
            {
                await assert(read);
            }
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            { /* best-effort cleanup */
            }
        }
    }
}
