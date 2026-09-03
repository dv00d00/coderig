# `symbols`/`show`/`refs` answer from the STORE while `callers` answers LIVE, one command apart

**Status:** open bug. Found 2026-08-24 by an agent reviewing an MR, in the same shell, one command apart.
**Triage:** ready-for-agent

## What happens

Only `reaches` / `path` / `callers` / `tree` are routable (`LiveQueryVerbs.Routable`). Everything else falls
through to the store. With a live host serving the directory, two adjacent commands answer about **two
different commits**:

```
$ rig symbols PersonCourseService
store: 7c7a43dde8cb-dirty (LATEST) @ 7c7a43dde8cb — UNVERIFIABLE: indexed from a dirty tree

$ rig callers PersonCourseService.GetPersonCourse
live: facts from resident index — 0 file(s) applied | 3 of 11956 indexed file(s) had compile errors
```

Different checkout, different commit, no warning that the previous answer came from somewhere else.

## Why it is a trap rather than a disclosure gap

Each line is individually honest — `store:` names its sha, `live:` says resident. What is missing is the
**contrast**: nothing tells the user that the verb they just ran could not be served live *while a live source
was available*, so a reader who has calibrated on "rig answers about my working tree" is silently handed an
answer about a different tree.

The reporting agent used `symbols` only to orient and took every line number from reading files directly, so
its review was not contaminated — and said so plainly: *"that was luck plus habit, not a guarantee the tool
gave me."* The next user does not have that habit. Line numbers from a stale store, pasted into a review of a
different commit, are wrong in the most expensive way: specific, plausible, and unverifiable without redoing
the work.

## Fix

When a host is serving this directory and the verb is not routable, say so on the answer:

```
symbols: a live index is serving this directory but cannot route `symbols`; this answer is from the store at 7c7a43dde8cb.
```

That is one line and it removes the trap without needing the verb to become routable.

Then decide separately whether these verbs SHOULD route. `symbols`/`refs` are keyed lookups over the fact
tables, which the resident index already holds — `IFactGraphView` exposes `SymbolsById` /
`SymbolsByContainingSymbol` / `ReferencesTo`. Routing them looks closer to plumbing than to new capability,
and it would remove the class of bug rather than disclosing it.

## Related

- `live-host-endpoint-is-undiscoverable-from-the-checkout.md` — the other half of the live/store seam: when
  the host cannot be found at all, and the message sends the user to build a store instead.
- `callers-property-pattern-false-negative.md` — the third: a query that answers "No symbol matches" for a
  member that exists. All three share a shape — the tool is confident and the user cannot tell it is wrong.
