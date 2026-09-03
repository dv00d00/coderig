# Call-site facts carry no column, so two calls on one line collapse

**Status:** todo · **Found:** 2026-08-31, during the Rider plugin backend spike · **Family:** extraction / Rider integration

**Terminal note — 2026-09-03:** shipped in `b59b6aba` with store schema v8. Call-site facts now carry column
coordinates; the renderer follow-on remains
[`tree --full` exact leaf suppression](../todo/tree-full-suppresses-call-leaves-sharing-a-line-with-an-effect.md).

## What happens

Extraction mines a call edge down to a **line**, not a column, so `Use(Read(), Fetch())` — two different
projected effectful targets on one source line — cannot be told apart positionally by anything downstream.
The Rider plugin works around the ambiguity the only way it can from the client side: `MatchOnLine`
(`experiments/RiderBackendEffectSpike/RigEffectDaemonStage.cs:193`) accepts the host's line-only candidate
list, and only when more than one candidate shares the enclosing method does it fall back to resolving the
PSI reference and matching by target DocID (`RigEffectDaemonStage.cs:217-227`).

That workaround still has a hole: **two calls to the SAME target on one line** (e.g. `Use(Read(x), Read(y))`)
produce two candidate rows with identical `EnclosingSymbolDocId` and `TargetSymbolDocId`. The match loop at
`RigEffectDaemonStage.cs:220-227` returns the first row that satisfies both equalities, so both invocations on
the line resolve to the same candidate — they collapse into one highlighted row instead of two.

## Fix and why it wasn't done inline

The real fix is upstream: mine the **column** at extraction time so a call-site fact identifies one
invocation, not one line. That is not a client-side patch — it changes what a call-site fact contains, which
means a store schema bump and a full reindex of every existing store. That is exactly the kind of change this
spike was scoped to avoid (see `CLAUDE.md`'s cache-invalidation section: a payload-shape change requires a
schema bump before any warm store reflects it).

## Cost/risk to record

- Store schema bump (per `docs/backlog` convention, bump the relevant `*Schema` constant and every warm
  server/client cache goes stale on the next answer, which is correct but touches every consumer).
  - Reindexing the MedDBase store is ~3 minutes warm per `CLAUDE.md`, and the same is true for every other
    indexed store — none of them stay valid across the schema change.
- Until this lands, the PSI-resolve fallback in `MatchOnLine` is the only mitigation, and it does not cover
  the same-target-twice-on-one-line case above.

## Related

- [`tree --full` suppresses a distinct call leaf sharing a line with an effect](../todo/tree-full-suppresses-call-leaves-sharing-a-line-with-an-effect.md)
  — a second surface with the same root cause, blocked on this card. `TreeCommand.cs:693,707,708` key
  suppression and dedup on `(Enclosing, Line)` and `(Enclosing, Target, Line)`; both keys widen to include the
  column once it exists.
