# The file-effects artifact has no `*Schema` constant, so a logic change never invalidates warm clients

**Status:** done · **Found:** 2026-09-01 reviewing the web file-lens branch ·
**Family:** caching

## Outcome

`FileEffectsSchema` now gates both `DerivationSchemaToken()` and the shared file-effects cache-key function
used by `WarmStore`. A synthetic key-contract test pins the schema material and its inclusion in the browser
token, so web, CLI and Rider-hosted projections invalidate together.

## What happens

`/api/file-effects` is cached by the web client in IndexedDB (`wwwroot/api.js`, keyed on `/api/meta`'s
`derivationVersion`), but the file-effects artifact contributes **no `*Schema` constant** to
`Rig.Cli/Caching/QueryCacheKeys.cs` and therefore nothing to `DerivationSchemaToken()`.

Per `CLAUDE.md`'s cache-invalidation contract, the store identity and rules fingerprint axes cover a reindex
and a rules edit — the per-artifact schema constant is the ONLY axis that covers a *same-input,
different-output* logic change. There is no such axis here, so a change to the file read model's projection
(depth semantics, per-line merge, call-site precedence) keeps every warm browser serving the pre-change answer
indefinitely.

This blocks any behavioural fix to the read model landing visibly, starting with
[the depth-0 drop](./file-lens-drops-depth-zero-effect-when-the-line-also-has-a-targeted-call.md).

## Fix

1. Add `internal const int FileEffectsSchema = 1;` to `QueryCacheKeys` with the standard `// vN->vM: <why>`
   trail, and fold it into `DerivationSchemaToken()` so `/api/meta`'s `derivationVersion` moves with it.
2. Bump it in the same commit as any read-model logic change.
3. `WarmStore`'s in-process file-effects LRU is keyed `store + rulesHash + filePath`; add the schema token to
   that key too, so a long-lived `serve`/`watch` process cannot serve a stale projection after a rules-neutral
   change either.

## Testing expectations

- A test asserting `DerivationSchemaToken()` changes when `FileEffectsSchema` changes (mirror whatever the
  existing schema-token test does for `TreeSchema`).
- A `WarmStore` key test: two schema values produce different keys for the same store/rules/file.

## Out of scope

Disk-caching the file-effects artifact itself in `.rig/cache.db` — that is a latency question, tracked in
[annotate pays a full cold derivation per invocation](./annotate-pays-a-full-cold-derivation-per-invocation.md).
