using Rig.Storage.Queries;
using static Rig.Cli.Caching.QueryCacheKeys;

namespace Rig.Cli.Live;

// WHERE a query command memoizes an expensive DERIVED artifact — the second half of the fact-source seam, and
// the one thing `tree` needs that `reaches`/`path`/`callers` did not: `tree` is the only cached traversal
// (a forest + its effects is the dominant cost, ~3.8s on the real store).
//
// The two implementations are NOT the same mechanism, deliberately:
//
//   * STORE  (StoreQueryArtifactCache): the writable `.rig/cache.db` beside the read-only rig.db. Entries are
//     BLOBS, so an artifact survives the process, and every key is namespaced by StoreKey (rig.db size+mtime)
//     so a reindex misses.
//   * LIVE   (LiveQueryArtifactCache): a per-GENERATION in-memory dictionary of the OBJECTS themselves. No
//     disk, no codec round-trip, and never `.rig/cache.db` — facts change per edit, so a cache keyed on a
//     store's identity is not merely useless there, it is a liability (it would serve a pre-edit answer).
//     Invalidation is the generation itself: a new AnalysisResult means a new LiveFactSource and hence a new,
//     empty dictionary, the same model every other live artifact already uses.
//
// What makes this ONE abstraction rather than two: the KEY DERIVATION stays in QueryCacheKeys, shared. Both
// paths hash the same material through the same functions, so the axes an artifact is a function of are stated
// once and cannot drift between the two — the failure mode a hand-rolled live memo would have (a missing axis
// serving one query's tree for another) is unrepresentable rather than merely tested for.
//
// The `decode`/`encode` codecs are PARAMETERS, not members: they are what the store arm needs and what the
// live arm has no use for, so they belong at the call site with the key. A store Get decodes a blob; a live Get
// hands back the object it was given. `Get` returning null is always a plain MISS — never an error — so a type
// mismatch, a corrupt blob or a disabled cache all degrade to "recompute", never to a wrong answer.
internal interface IQueryArtifactCache : IDisposable
{
    // The STORE-IDENTITY axis every artifact key is namespaced by, threaded into QueryCacheKeys.TreeCacheKey
    // by the command so both paths key through one function. Store: rig.db size+mtime (a reindex shifts it).
    // Live: the constant "live" — the generation axis is the per-generation DICTIONARY, not a token in the key,
    // so nothing in a key needs to move for a new generation to be a clean slate.
    string StoreKey { get; }

    // The artifact under `key`, or null on a miss. `decode` is applied to the stored blob on the store path and
    // ignored on the live path (nothing was ever encoded).
    T? Get<T>(string key, Func<byte[], T?> decode)
        where T : class;

    // Memoize `value` under `key`. BEST-EFFORT on both paths: a cache write must never fail a query, so an
    // encode/IO failure (store) or a full memo (live) simply doesn't cache.
    void Put<T>(string key, T value, Func<T, byte[]> encode)
        where T : class;
}

// The .rig/cache.db arm. Adds NO behaviour over what TreeCommand did inline: the same QueryCache.Open (null =
// caching disabled), the same codecs, and the same TryCache best-effort write wrapper — so the store path's
// cache slots, hit rates and blob contents are unchanged by the seam.
internal sealed class StoreQueryArtifactCache : IQueryArtifactCache
{
    private readonly QueryCache? _cache;

    // `useCache:false` (--no-cache) yields an instance with no underlying cache: every Get misses and every Put
    // is a no-op, which is exactly what the old `cache = opts.NoCache ? null : …` + null-guarded keys did.
    internal StoreQueryArtifactCache(string rigDirectory, string storeKey, bool useCache)
    {
        StoreKey = storeKey;
        _cache = useCache ? QueryCache.Open(rigDirectory: rigDirectory, storeKey: storeKey) : null;
    }

    public string StoreKey { get; }

    public T? Get<T>(string key, Func<byte[], T?> decode)
        where T : class => _cache?.Get(key) is { } blob ? decode(blob) : null;

    public void Put<T>(string key, T value, Func<T, byte[]> encode)
        where T : class
    {
        if (_cache is null)
        {
            return;
        }

        TryCache(() => _cache.Put(key, encode(value)));
    }

    public void Dispose() => _cache?.Dispose();
}

// The per-generation arm: the objects themselves, in a dictionary owned by the LiveFactSource whose facts they
// were derived from. Disposal is a no-op — the memo belongs to the generation and outlives any one query.
//
// `useCache:false` (--no-cache, unreachable from today's live surface but honoured rather than ignored) passes
// a null memo, which behaves exactly like the store's disabled cache: every Get misses, every Put is dropped.
internal sealed class LiveQueryArtifactCache(BoundedArtifactMemo? memo) : IQueryArtifactCache
{
    // A CONSTANT, not a generation token: the memo dictionary is per-generation, so two generations can never
    // share a slot however identical their keys. Keeping the key material's SHAPE identical to the store's is
    // what makes "the live key is the disk key minus the store-identity axis" a checkable statement.
    public string StoreKey => "live";

    // `decode` is deliberately unused: nothing was encoded, so there is no blob to decode. A stored artifact of
    // an unexpected type degrades to a miss (`as T` -> null) rather than an InvalidCastException — it cannot
    // happen today (each artifact has its own key namespace) and if it ever did, recomputing is the safe answer.
    public T? Get<T>(string key, Func<byte[], T?> decode)
        where T : class => memo?.Get(key) as T;

    public void Put<T>(string key, T value, Func<T, byte[]> encode)
        where T : class => memo?.Put(key, value);

    public void Dispose() { }
}

// The live memo's storage: a FIFO-bounded key -> artifact map, one per fact generation.
//
// Bounded on purpose. Every other live artifact is O(1) per generation (one traversal graph, one effect set),
// so an unbounded Lazy is free; tree artifacts are O(QUERIES) — a distinct forest per pattern/depth/limit, each
// potentially a 50k-node tree — so an unbounded memo would make a long-lived generation's footprint a function
// of how many questions were asked. The cap trades a recompute for a bounded resident host, which is the right
// way round for a process that is meant to sit in the background all day.
internal sealed class BoundedArtifactMemo
{
    // Four slots per tree query (forest, locations, seam, library calls), so this holds the artifacts of the
    // ~32 most recent distinct queries — comfortably more than a working set, far less than a leak.
    private const int Capacity = 128;

    private readonly object _gate = new();
    private readonly Dictionary<string, object> _entries = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();

    public object? Get(string key)
    {
        lock (_gate)
        {
            return _entries.GetValueOrDefault(key);
        }
    }

    public void Put(string key, object value)
    {
        lock (_gate)
        {
            // Re-Put of a live key refreshes the value in place and keeps its original queue position: the
            // artifact is a pure function of the key, so the value cannot differ, and re-queuing it would let a
            // repeated query starve the rest of the memo.
            if (!_entries.TryAdd(key, value))
            {
                _entries[key] = value;
                return;
            }

            _order.Enqueue(key);
            while (_order.Count > Capacity && _order.TryDequeue(out var evicted))
            {
                _entries.Remove(evicted);
            }
        }
    }
}
