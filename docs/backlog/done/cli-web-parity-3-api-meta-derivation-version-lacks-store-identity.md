# `/api/meta` derivationVersion carries no store identity — browsers serve stale trees indefinitely

**Status:** todo · **Priority: HIGH** (silent wrong answers in the web UI, on a warm cache, with no user-visible
signal; the CLI is unaffected so the two surfaces disagree on the same store) · **Found:** 2026-08-20 (fact-store
patch audit, during the [live-background-index](../done/live-background-index.md) spike pool) ·
**Family:** web / cache-invalidation

**Terminal note — 2026-09-03:** shipped in `b59b6aba`. `/api/meta` now resolves the selected store first and
folds that store's `QueryCacheKeys.StoreKey` into `derivationVersion`, so a same-commit reindex invalidates
IndexedDB just as it invalidates the server disk cache.

## The bug

The web client keys its IndexedDB cache on `derivationVersion` from `/api/meta`. That value is computed
without any **store identity** axis:

```csharp
// src/Rig.Cli/Web/RigApiEndpoints.cs:336-343
private static string DerivationVersion(string workingDirectory)
{
    RuleSetLoader.Load(workingDirectory, extraRules: [], loadedPaths: out var loadedPaths);
    var rulesHash = RulesFingerprint.ComputeFromPaths(loadedPaths);
    var schema = QueryCacheKeys.DerivationSchemaToken();
    var bytes = System.Text.Encoding.UTF8.GetBytes(schema + "|" + rulesHash);
    return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))[..16];
}
```

So the client's invalidation signal moves on a **rules edit** or a **schema bump**, and on nothing else.

The other half of the client key is the store id — but that is the store **DIRECTORY NAME**
(`wwwroot/api.js:78-90,111-115`, key shape `version|tree|${storeId}|…`), and `StoreLayout.NewStoreId` is
stable for a given working state (`StoreLayout.cs:87-96`). A per-commit store keeps its directory name across
re-indexes of that commit.

**Net effect: re-index the same commit and every browser keeps serving the pre-reindex tree, hazards and
impact — forever, with no staleness signal.**

## Why this matters more than it looks

- The **server** side is correctly hedged: every disk cache key folds in `QueryCacheKeys.StoreKey` (rig.db
  size + mtime), so `rig` on the CLI returns fresh results from the same store. Only the browser is stale, so
  the two surfaces silently disagree — the exact failure class as
  [web-api-seed-and-effect-disclosure-parity](../todo/cli-web-parity-1-web-api-seed-and-effect-disclosure-parity.md).
- It is hit by an ordinary workflow: re-index a commit after an extraction fix (which is precisely when the
  facts you care about changed) and the web view keeps the old answer.
- CLAUDE.md's cache section documents store identity as one of the three load-bearing axes. The client is
  missing that axis entirely — this is a gap in the hedge, not a deliberate trade.

## Fix

Fold `QueryCacheKeys.StoreKey(<resolved store>/rig.db)` into `DerivationVersion`'s hash material. That is the
same primitive every server key already uses, so the client inherits the reindex tripwire for free and stays
in lockstep with the server exactly as `DerivationSchemaToken()` already keeps it in lockstep on schema bumps.

Note the `--store`/store-ref dimension: `/api/meta` should compute the identity of the store the request will
actually read, not unconditionally the LATEST one.

## Acceptance

1. `rig serve` on a store, load the web tree for some entry point, confirm it renders.
2. Re-index the SAME commit (`rig index <sln>` — a fresh publish, so rig.db size/mtime move).
3. Reload the browser. The tree must be re-fetched, not served from IndexedDB.
   Before the fix it is served from cache; after, `derivationVersion` differs so the client key misses.

## Related

- Do NOT re-introduce an assembly MVID / build-timestamp axis to achieve this — removed 2026-07-06 because it
  moved on every recompile and destroyed the expensive impact diff. Store identity + rules + schema is the
  whole hedge; see the cache-invalidation section of CLAUDE.md.
- Blocks nothing in [live-background-index](../done/live-background-index.md), but a live/mutable store
  makes this permanent rather than occasional, so it should land before any resident-index work ships to the
  web surface.
- [CLI/web collapse onto one engine per question](../todo/cli-web-collapse-map.md) — relates only. That family
  collapses duplicated compute; this is a client-cache axis and survives it untouched. One overlap: the
  collapse family's child 1 is the only slice that edits `RigApiEndpoints.cs`, which is where
  `DerivationVersion` lives.
