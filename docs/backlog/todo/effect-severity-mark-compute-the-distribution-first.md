# Compute the effect-severity distribution before choosing a mark for it

**Status:** todo · **Found:** 2026-09-02, deciding where the approved severity signal renders ·
**Triage:** needs-info
**Family:** web review / file effect lens

## The approved signal

Two metrics are approved as the severity signal on a call site: **family breadth** (how many of the eight
families the call reaches) and **reachable-method count**. A transitive effect-site count was considered and
deferred to its own card.

Constraint carried over from `src/Rig.Cli/Web/HotspotsContracts.cs:3-4` — *"there is no blended score whose
weighting a client would have to reverse-engineer"*. So the mark rides on **one named metric**, never a
composite of the two.

## Why this card exists: compute first, then decide rendering

Nothing about the rendering can be chosen honestly until the distribution is known. Measure family breadth
across the real MedDBase store first — how many call sites sit at 1, 2, … 8 families — then choose the mark and
the threshold from that shape. If 6/8 is common, a loud mark is noise; if it is rare, a quiet one is invisible.

**The threshold is unset, pending that distribution.** 5+ families was floated as a starting guess, not a
decision.

## Rendering candidates, loud to boring

Recorded so the choice is made from the list rather than re-invented:

- a **bold method name**;
- a **squiggly underline**;
- an **exclamation mark in its own dedicated gutter lane** — the dull, clear option, and the one that does not
  compete with the existing eight-slot effect lane (see
  [web-review-effect-gutter-and-delta](../progress/web-review-effect-gutter-and-delta.md), where the lane
  geometry and its horizontal budget are already settled).

Also open: whether the mark appears in one place or several — the effect lane, a separate gutter mark, and a
column in the review file list are not mutually exclusive, and the file-list column is the only one that
answers "which file in this MR should I read first".

## Worked example, from the live payload

| line | call | families | profile |
|---|---|---|---|
| 883 | `SetPersonContractId` | **6/8** | blob 11 · cache 4 · db 3 · echo 12 · io 17 · rpc 26 |
| 884 | `SetPersonCourseId` | **6/8** | identical at every depth |
| 886 | `Services.GetAppointmentService()` | 3/8 | cache 2 · db 5 · io 16 |

A property setter reaching a remote call 26 hops down is exactly the reader-facing case the signal is for.

## Measured 2026-09-02 — the distribution

Seeded random sample of **250 of 11,966 indexed files** (2.1%), annotated warm through `rig serve` against
store `409c330b99dd-dirty` (v8; note the store is disclosed `UNVERIFIABLE: indexed from a dirty tree`).
0 errors, **1,567 call sites** and **857 method rows**, 197s. Reproduce with seed `20260902`.

| family breadth | call sites | % | method rows | % |
|---|---|---|---|---|
| 1/8 | 709 | 45.2 | 302 | 35.2 |
| 2/8 | 210 | 13.4 | 174 | 20.3 |
| 3/8 | 267 | 17.0 | 92 | 10.7 |
| 4/8 | 94 | 6.0 | 46 | 5.4 |
| 5/8 | 201 | 12.8 | 181 | 21.1 |
| 6/8 | 86 | 5.5 | 62 | 7.2 |
| 7/8 | **0** | 0.0 | **0** | 0.0 |
| 8/8 | **0** | 0.0 | **0** | 0.0 |

Cumulative call sites: `>=6/8` **5.49%**, `>=5/8` **18.32%**, `>=4/8` 24.31%, `>=3/8` 41.35%, `>=2/8` 54.75%.

**The denominator is wrong, and that is the headline.** Only six families occur at all — `db` 1890,
`io` 1546, `cache` 1212, `echo` 841, `rpc` 543, `blob` 182 rows. `bus` and `search` appear in **zero** of
1,567 sites, so `n/8` cannot exceed 6/8 in practice and a reader shown "6/8" is being told the site is at
75% of a ceiling it can never reach.

**Settled 2026-09-03 — the eight is not a fact about this store.** `bus` and `search` are declared by
`builtin-rules.json`, which ships with the tool into every repo; `echo` is declared only by MedDBase's own
`rig.rules.json`. Confirmed estate-wide rather than in-sample: MedDBase has **zero** references to mediatr,
rabbitmq, elasticsearch, azure_search, Nest or MassTransit across all 2,444,657 refs, so those two families
are structurally unreachable here. The proposed `rig derive --only bus` follow-up is unnecessary — the rule
sets plus the ref counts answer it outright.

**D2, 2026-09-03:** the family list is **config-defined and of arbitrary size** — no hardcoded eight
anywhere, and anything outside the declared set renders in its own `other` lane rather than being promoted to
a pseudo-family. That decision and its scope live on
[family-list-comes-from-rules-not-a-client-hardcode](./family-list-comes-from-rules-not-a-client-hardcode.md),
which also records that the web client currently hand-copies the list (`filelens.js:33-42`) even though
`/api/providers` already serves it.

**Still open, and this card is blocked on it.** Config-defined does not by itself fix the two-always-missing
problem, because `bus` and `search` *are* config — `DeclaredFamilies` still returns 8 for MedDBase. The mark
cannot ship until the denominator is one of:

- **declared ∩ present-in-store** — self-correcting (`6/6` today, `7/7` the day someone adds RabbitMQ),
  needs a store read the legend does not currently do;
- **rules may opt out of builtin families** — explicit and cheap, but the opt-out list needs maintaining.

Whichever is chosen, the mark must **disclose its N**, since N now varies per repo.

**Threshold, from the data rather than a guess.** The floated 5+ would mark 18.3% of call sites — roughly one
in five, which is not a severity signal. `6/8` is both the observed maximum and 5.49% of sites (~1 in 18),
which is the only bucket rare enough to read as an exception. Recommendation: threshold at the top observed
bucket, restated against whichever denominator is chosen.

Note the distribution is **non-monotone**: 5/8 (12.8%) is twice 4/8 (6.0%). That spike is a cluster of code
reaching a common multi-family core, not a smooth tail — so a threshold set at 5 captures a population, while
one set at 6 captures outliers.

**Not measured:** reachable-method count, the card's second approved metric. `annotate` does not emit it, so
it needs its own surface; this measurement covers family breadth only.

## The denominator question is wider than 8-vs-6 — measured 2026-09-03

Served live from `/api/providers` after the family grouping shipped: **67 providers, 42 in a declared family,
25 in none.**

```
alloc, app_state, async_block, async_lock, audit, bounded_retry, browser_render, clientpage_event,
clientpage_io, clientpage_nav, config, crypto_pgp, git, inproc_timer, ironpdf, lock, parallel,
permission, process, reflection, resilience, script_eval, session_state, shared_state, throw
```

So the family axis is structurally blind to 37% of the provider vocabulary — `lock`, `reflection`,
`permission`, `shared_state` and `process` among them. That is not a rounding error for a severity signal:
a call site reaching `lock:acquire` and `reflection:invoke` and nothing else scores **0/N on family breadth**,
however alarming it is.

This does not decide the open question above, it enlarges it. Three options now, not two:

- **declared ∩ present-in-store** — as before, self-correcting, `6/6` on MedDBase today;
- **rules opt out of builtin families** — as before, explicit, needs maintaining;
- **breadth is the wrong metric for a third of the vocabulary** — the 25 unmapped providers may need their own
  axis (an `other` count, or a hazard-tier signal) rather than being folded into a family denominator at all.

Recorded, not resolved. The mark still cannot ship until one is chosen.

## Acceptance

- The family-breadth (and reachable-method-count) distribution over the real store is reported before any
  rendering lands: counts per breadth bucket, so "heavy" is defined against data.
- The shipped mark carries one named metric, not a blend.
- The threshold is a stated number with the distribution that justifies it.
