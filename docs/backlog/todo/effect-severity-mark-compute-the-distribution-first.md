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

## Acceptance

- The family-breadth (and reachable-method-count) distribution over the real store is reported before any
  rendering lands: counts per breadth bucket, so "heavy" is defined against data.
- The shipped mark carries one named metric, not a blend.
- The threshold is a stated number with the distribution that justifies it.
