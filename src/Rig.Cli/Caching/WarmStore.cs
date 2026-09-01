using Rig.Analysis.Rules;
using Rig.Cli.CommandLine;
using Rig.Domain.Data;
using Rig.Storage.Queries;
using Rig.Storage.Storage;

namespace Rig.Cli.Caching;

// PROOF OF CONCEPT — process-lifetime warm cache for expensive WHOLE-STORE loads:
// the shaped call graph (Reads.LoadShapedGraphAsync, ~4.5s on the MedDBase store) and the invocation-ref
// table (Reads.LoadInvocationRefsAsync, ~2.4M rows), plus a separately bounded solution-wide file-effect
// projection. They are pure functions of store/rules, so a resident process pays each factory once.
//
// WHY THIS EXISTS: rig's three existing cache layers (cache.db, the web client's IndexedDB, the disk
// artifact cache) are all DISK caches — they cache the ANSWER. Nothing caches the intermediate state a
// one-shot process throws away on exit, so a warm cache HIT still pays the graph load before it can serve
// the cached answer. In a one-shot CLI that is unavoidable; in a resident process (`rig serve`, or a
// future `rig watch`) it is pure waste. This class is the missing layer.
//
// KEYED EXACTLY LIKE THE DISK CACHE — (store identity, rules fingerprint):
//   * store identity  = rig.db size + last-write-time (QueryCacheKeys.StoreKey). `rig index` publishes via
//     atomic rename, so a REINDEX or any in-place rewrite shifts it. That is the "invalidate on disk write"
//     property for free: a stale entry can never be served, with no file watcher in the correctness path.
//     A new COMMIT gets its own store directory, so it is a different key by construction.
//   * rules fingerprint = RulesFingerprint over the loaded rule files, so a rig.rules.json edit misses.
// Deliberately NOT keyed on anything per-compile (no MVID, no build stamp) — same discipline as
// QueryCacheKeys, and for the same reason.
//
// BOUNDED: heavyweight graph/invocation entries use `Capacity`; solution file-effect indexes use their own
// two-entry LRU. FactGraphData on the MedDBase store costs ~1.5 GB of
// disk reads to build, so retained footprint is the real constraint and a resident process must not hold
// an unbounded number. `impact` wants two graphs at once (base + head) and would thrash at capacity 1 —
// its call sites are deliberately NOT routed through here yet; raise the cap first and measure.
//
// ENV KNOBS (PoC only — a real version takes options, not environment):
//   RIG_WARM_CAP=<n>   LRU capacity. 0 disables the cache entirely (the A/B control arm).
//   RIG_WARM_LOG=1     log hit/miss + load duration to stderr.
internal static class WarmStore
{
    // Default 4, NOT 1: the cache holds MULTIPLE ARTIFACT KINDS per store (shaped graph + invocation refs
    // today), so a capacity of 1 makes them evict each other and every request misses both — measured, and
    // the reason the first PoC run showed no win at all. Budget = kinds x stores you want resident.
    private static readonly int Capacity = ReadIntEnv("RIG_WARM_CAP", defaultValue: 4);
    private static readonly bool Log = Environment.GetEnvironmentVariable("RIG_WARM_LOG") == "1";

    // One gate for the whole cache: loads are multi-second and memory-heavy, so serializing them is the
    // POINT — two concurrent /api requests against a cold store must not both materialize the graph.
    private static readonly SemaphoreSlim Gate = new(initialCount: 1, maxCount: 1);
    private static readonly SemaphoreSlim ResidentFileEffectGate = new(initialCount: 1, maxCount: 1);

    // Insertion-ordered; last element = most recently used. Tiny (Capacity is 1-3), so a List scan is
    // cheaper than a dictionary + intrusive list.
    private static readonly List<(string Key, object Value)> Entries = [];

    // A resident file-effect artifact holds the solution-wide reverse projection: one entry serves every
    // physical file in that store. Keep it separate from graph/invocations because its factory calls both;
    // sharing Gate would deadlock. Two entries allow a brief old/new-store overlap during reindex without
    // retaining an unbounded set of 442k-symbol indexes.
    private const int ResidentFileEffectCapacity = 2;
    private static readonly List<(string Key, object Value)> ResidentFileEffectEntries = [];

    // The shaped whole-store graph. Pattern-INDEPENDENT (unlike the bounded per-traversal loads in
    // TraversalGraphLoader), which is exactly why it is cacheable by (store, rules) alone.
    internal static Task<FactGraphData> GraphAsync(
        RigDbContext context,
        RuleSet rules,
        string storeDir,
        string rulesHash,
        CancellationToken ct = default
    ) =>
        GetOrLoadAsync(
            key: $"graph|{StoreIdentity(storeDir)}|{rulesHash}",
            label: "shaped graph",
            load: () => Reads.LoadShapedGraphAsync(context: context, rules: rules, ct: ct)
        );

    // The invocation-ref table. Rules-INDEPENDENT (a raw fact projection), so the rules fingerprint is
    // deliberately absent from the key — a rule edit must not evict 2.4M rows that did not change.
    internal static Task<IReadOnlyList<FactInvocation>> InvocationsAsync(
        RigDbContext context,
        string storeDir,
        CancellationToken ct = default
    ) =>
        GetOrLoadAsync(
            key: $"invocations|{StoreIdentity(storeDir)}",
            label: "invocation refs",
            load: () => Reads.LoadInvocationRefsAsync(context, ct)
        );

    // ONE semantic projection for every indexed physical file in a solution. The key deliberately has no
    // file path: after the first load, choosing another file is only a dictionary lookup in the retained index.
    // FileEffectsSchema remains the derivation/payload hedge even though this is a process-only cache.
    internal static Task<T> ResidentFileEffectsAsync<T>(string storeDir, string rulesHash, Func<Task<T>> load)
        where T : class =>
        GetOrLoadResidentFileEffectAsync(
            key: $"filefx-solution|v{QueryCacheKeys.FileEffectsSchema}|{StoreIdentity(storeDir)}|{rulesHash}",
            label: "file effects (solution)",
            load: load
        );

    // Pre-warm both artifacts off the request path — the `serve` startup + post-reindex re-warm hook. Errors
    // are swallowed by design: a failed pre-warm must never take the server down, it just means the next real
    // request pays the load it would have paid anyway.
    internal static async Task PrewarmAsync(
        RigDbContext context,
        RuleSet rules,
        string storeDir,
        string rulesHash,
        CancellationToken ct = default
    )
    {
        try
        {
            await GraphAsync(context, rules, storeDir, rulesHash, ct);
            await InvocationsAsync(context, storeDir, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (Log)
            {
                Console.Error.WriteLine($"[warm] prewarm failed (non-fatal): {ex.Message}");
            }
        }
    }

    // Store identity = the store DIRECTORY plus rig.db's size + mtime. The size+mtime pair is reused verbatim
    // from the disk-cache key derivation so the two layers can never disagree about which store they are
    // describing; the directory is what makes the identity unique. Without it, two DIFFERENT stores whose
    // rig.db happens to share a length and a last-write tick key the same entry, and one answers for the
    // other — a whole file model belonging to another checkout. That is not hypothetical: it is how
    // freshly-created stores of identical content behave, and it surfaced as cross-fixture contamination in
    // the annotate tests (badges from one store's paths served against another's).
    private static string StoreIdentity(string storeDir) =>
        $"{Path.GetFullPath(storeDir).TrimEnd(Path.DirectorySeparatorChar)}|{QueryCacheKeys.StoreKey(Path.Combine(storeDir, StoreLayout.DbFileName))}";

    private static async Task<T> GetOrLoadAsync<T>(string key, string label, Func<Task<T>> load)
        where T : class
    {
        if (Capacity <= 0)
        {
            return await load();
        }

        // Fast path OUTSIDE the gate: a hit must not queue behind an in-flight cold load of a different key.
        // Racing readers may briefly disagree on LRU order; that costs an eviction, never a wrong answer.
        if (TryGet(key) is T warm)
        {
            if (Log)
            {
                Console.Error.WriteLine($"[warm] HIT  {label}");
            }

            return warm;
        }

        await Gate.WaitAsync();
        try
        {
            // Re-check: another caller may have loaded this exact key while we waited on the gate.
            if (TryGet(key) is T raced)
            {
                if (Log)
                {
                    Console.Error.WriteLine($"[warm] HIT  {label} (after gate)");
                }

                return raced;
            }

            var started = Environment.TickCount64;
            var loaded = await load();
            if (Log)
            {
                Console.Error.WriteLine($"[warm] MISS {label} — loaded in {Environment.TickCount64 - started} ms");
            }

            Insert(key, loaded);
            return loaded;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<T> GetOrLoadResidentFileEffectAsync<T>(string key, string label, Func<Task<T>> load)
        where T : class
    {
        if (Capacity <= 0)
        {
            return await load();
        }

        if (TryGet(ResidentFileEffectEntries, key) is T warm)
        {
            if (Log)
            {
                Console.Error.WriteLine($"[warm] HIT  {label}");
            }

            return warm;
        }

        await ResidentFileEffectGate.WaitAsync();
        try
        {
            if (TryGet(ResidentFileEffectEntries, key) is T raced)
            {
                if (Log)
                {
                    Console.Error.WriteLine($"[warm] HIT  {label} (after gate)");
                }

                return raced;
            }

            var started = Environment.TickCount64;
            var loaded = await load();
            Insert(ResidentFileEffectEntries, ResidentFileEffectCapacity, key, loaded);
            if (Log)
            {
                Console.Error.WriteLine($"[warm] MISS {label} — loaded in {Environment.TickCount64 - started} ms");
            }

            return loaded;
        }
        finally
        {
            ResidentFileEffectGate.Release();
        }
    }

    private static object? TryGet(string key) => TryGet(Entries, key);

    private static object? TryGet(List<(string Key, object Value)> entries, string key)
    {
        lock (entries)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                if (!string.Equals(entries[i].Key, key, StringComparison.Ordinal))
                {
                    continue;
                }

                // Touch: move to the most-recently-used end.
                var hit = entries[i];
                entries.RemoveAt(i);
                entries.Add(hit);
                return hit.Value;
            }
        }

        return null;
    }

    private static void Insert(string key, object value) => Insert(Entries, Capacity, key, value);

    private static void Insert(List<(string Key, object Value)> entries, int capacity, string key, object value)
    {
        lock (entries)
        {
            entries.RemoveAll(e => string.Equals(e.Key, key, StringComparison.Ordinal));
            entries.Add((key, value));
            while (entries.Count > capacity)
            {
                entries.RemoveAt(0); // evict least-recently-used
            }
        }
    }

    private static int ReadIntEnv(string name, int defaultValue) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var parsed) ? parsed : defaultValue;
}
