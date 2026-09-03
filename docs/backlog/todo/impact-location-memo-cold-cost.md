# The impact location memo trades a cold fast path for the warm hit

**Status:** todo · **Found:** 2026-09-03, shipping
[cli-web-collapse-1](../done/cli-web-collapse-1-impact-selection-into-the-engine.md) · **Triage:** needs-info
**Family:** impact / web cache

## What changed and why

Server-side selection (D1-rev = B) makes a filter toggle a warm request, so `ImpactMapper.LoadUniqueLocationsAsync`
— the only per-request work left after an `ImpactCacheKey` hit — had to be memoized or every toggle would
re-scan both stores. It now loads the **full per-store stem→site map** and caches it in `WarmStore` under
`impact-locations|{StoreIdentity(storeDir)}` (a 2-entry LRU, process lifetime).

Measured on the MedDBase pair (`RIG_WARM_LOG=1`, `rig serve`):

```
MISS impact locations — loaded in 985 ms
MISS impact locations — loaded in 953 ms
HIT  impact locations   (x8, every later request)
first  /api/impact  2.13 s
second /api/impact  0.087 s   (identical request)
```

## The two costs it bought that with

1. **The cold fast path is gone.** The old call took a stem set and could skip the scan entirely when no stems
   were relevant. The map is now whole-store (~222k method rows per store) precisely so it is
   filter-independent, so the FIRST `/api/impact` per store always pays ~1 s even when the diff needs nothing
   from it.
2. **Two cold loads now serialize** behind the `WarmStore` gate (985 ms + 953 ms sequential) where
   `ImpactMapper` previously started both tasks before awaiting either (`ImpactMapper.cs:34-35`), so base and
   head overlapped. The warm path is unaffected.

Both follow the existing `WarmStore` idiom, which is what the brief asked for. Neither is obviously wrong —
a ~1 s once-per-store cost against a 0.087 s toggle is the trade the decision wanted — but neither was
measured against the alternative.

## The question

Is per-key concurrency worth adding to `WarmStore` so two cold loads for different stores overlap again?
That is the only one of the two costs that is pure loss: it makes the cold path ~1 s slower than it needs to
be with no compensating benefit, whereas losing the no-stems fast path is the direct price of
filter-independence.

`verify:` whether any other `WarmStore` consumer would benefit from the same per-key concurrency, or whether
this is the only contended pair — if it is the only one, a local `Task`-per-key memo in `ImpactMapper` is
simpler than widening the shared gate.

## Acceptance

- A decision recorded either way, with the cold-path number that justifies it.
- If per-key concurrency lands, two cold loads for distinct stores overlap and the warm hit stays ~0.09 s.
