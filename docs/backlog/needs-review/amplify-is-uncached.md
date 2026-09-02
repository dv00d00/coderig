# `rig amplify` is uncached — a 1m06s estate sweep is re-paid every invocation

**Status:** todo · **Family:** amplification / query cache
**Extracted from:** [nonlinear-amplification-degree](../done/nonlinear-amplification-degree.md) ("No caching" follow-up), 2026-09-02
**Triage:** needs-triage
**Related:** [Caching and live derivation — wayfinder](../todo/derivation-cache-map.md)

## The problem

`AmplifyCommand` shipped with `QueryCacheKeys.cs` untouched: no key, no `*Schema` constant, no disk entry.
The estate sweep is **1m06s over 10,109 entry points** on MedDBase, re-paid in full on every invocation, and
the web slice would re-pay it per request.

## What already shipped

The whole CLI feature and its calibration —
[nonlinear-amplification-degree](../done/nonlinear-amplification-degree.md). No store-schema change was made
and no cache key was touched, which is exactly why this is open.

## What the fix has to respect

- Per CLAUDE.md, the hedge is **store identity + rules fingerprint + a per-artifact `*Schema` constant**.
  `amplify`'s output is a function of the rules (`AmplificationScope` and
  `observations.amplificationCategories` both come from rules data), so the rules fingerprint axis is
  load-bearing here, not optional.
- `QueryCacheKeys.DerivationSchemaToken()` folds in all the `*Schema` constants and feeds `/api/meta`, so a
  new constant automatically moves the client's `derivationVersion`. Only the one constant is edited.
- Do **not** fold a build timestamp, app version or assembly MVID into the key — removed 2026-07-06 for
  destroying expensive artifacts on unrelated recompiles.
- Leave the `// vN->vM: <why>` trail on the constant.

## What counts as finishing

- An `AmplifySchema`-style constant plus a key, wired the way the existing artifacts are.
- A warm second sweep on the same store returns byte-identical output, measurably faster.
- A rules edit invalidates it without a re-index; a re-index invalidates it via store identity.
