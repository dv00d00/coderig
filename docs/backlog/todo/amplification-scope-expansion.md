# Amplification display scope — widen beyond network-crossing providers?

**Status:** open decision (Dmytro's call). The tier itself SHIPPED 2026-08-03 (on by default, `--no-amplification`
opt-out); this item is only about the SCOPE of what it displays — i.e. `observations.amplification` (**tier 2**,
the intra-method looped effect).

**Related decision already taken, in the OPPOSITE direction (2026-08-04, `0fd62eb8`):** the *tier-3*
`crossMethodAmplification` witness gate went **all-IO** — empty `witnesses` = every discovered provider minus
`alloc`/`throw`/`shared_state`/`config` — because the read-only gate demonstrably hid looped SENDS
(`actor:tell`, `echo_publish:ask`, smtp, `http:POST`). That does not settle tier 2 (different grain, different
section), but it is the live precedent: the two tiers now disagree on scope by default, which is itself an
argument for widening this one.

## What shipped

`looped_effect` is now a first-class displayed finding (the "amplification" tier — a structural FACT, distinct
from the `n_plus_1` JUDGMENT; see `HazardKinds`). Its DISPLAY scope is rule data —
`observations.amplification` in `src/Rig.Cli/builtin-rules.json`, projected to `FactAmplificationRule`, matched
by `AmplificationScope.Includes`. The shipped default is **network-crossing providers only**, i.e. the ones where
×N in a loop means N round trips over a socket:

`http` (all ops) · `llblgen` (read/write/delete/bulk_write/fetch) · `db_command` · `db_connection` ·
`db_reader` · `efcore` · `object_store` (read/write) · `queue` · `actor` (tell/ask/spawn)

Widening is a ONE-LINE rules edit (the lists concatenate across the cascade, so a project overlay can append
without restating the defaults). Nothing in C# names a provider.

## Measured consequence of the narrow default (MedDBase store `2621b87fa9a5-dirty`, 2026-08-03)

1,531 looped non-intrinsic sites total. **704 are in the default scope**; **827 stay as an anonymous
`looped_effect: 827` count** in the generic "Observations on effects" block. The tier is LOSSLESS — an
out-of-scope looped effect is still counted, it just gets no section, mark, or impact row.

The out-of-scope volume, largest first:

| provider:operation | looped sites | in default scope |
|---|---|---|
| `shared_state:read` | 264 | no |
| `entity_cache:read` | 250 | no |
| `permission:assert` | 65 | no |
| `shared_state:mutate` | 53 | no |
| `io:write` | 36 | no |
| `lock:acquire` | 36 | no |
| `lock:release` | 29 | no |
| `io:read` | 29 | no |
| (+ `audit:write`, `parallel:*`, `config:read`, small tails) | | no |
| `alloc` (only under `--intrinsic`) | **12,873** | no |
| `throw` (only under `--intrinsic`) | 8 | no |

**The honest disclosure: the Home2 defect that started this whole effort is an `entity_cache:read`** — so it
would NOT appear in the Amplification section under the network-first default. It is *not* invisible: it is
covered independently by the `n_plus_1` hazard tier, verified on the same store — **189 of the 250 looped
`entity_cache:read` sites carry `n_plus_1`** (plus 18 `object_store:read` and 11 `llblgen:read`, 218 total). The
61 looped `entity_cache:read` sites WITHOUT `n_plus_1` are the ones with a constant/absent key — deliberately not
an n+1 (hoistable), and under this default they show only in the generic count.

`alloc` needs no special-casing either way: it is governed by the existing `--intrinsic` mechanism (hidden by
default store-wide), and it is additionally out of the amplification scope, so a looped allocation is reported
only when a user asks for intrinsics AND the scope is widened.

## What would have to be true to widen it

- **`entity_cache` / `inproc_cache`:** widen if the section stays readable at +250 sites and reviewers actually
  want cache-in-loop inventory that `n_plus_1` already judges. The risk is redundancy, not noise: 189/250 are
  already n_plus_1 hazards, so widening mostly re-reports findings that have a section already. Consider instead
  admitting ONLY the looped cache reads that `n_plus_1` did NOT claim (the constant-key residual) — that needs an
  engine change (a cross-tier suppression), not a rules edit, so it is a separate design question.
- **`shared_state` (264):** ×N here is contention/CPU, and `race_window`/`lazy_init_race` already own the
  correctness story for shared cells. Widening would double the section for the least actionable provider.
- **`lock` (65):** a lock acquire/release in a loop is arguably interesting (per-iteration locking), but it is a
  DIFFERENT conversation (contention) with its own triage; ~1/3 of the sites are `Echo.Process` framework code.
- **`io` (67):** genuinely a boundary (disk), so a plausible NEXT step after the network set — the reason it is
  not in v1 is only that local disk ×N is orders of magnitude cheaper than N network round trips.
- **`alloc` (12,873):** would need its own presentation (a per-method rollup, not a site list) before it could be
  displayed at all. The user's workloads ARE GC-throughput-bound, so the number matters — but 12,873 rows is a
  report, not a section.

Whatever is chosen, the change is `observations.amplification` in `builtin-rules.json` plus a line in the
"CANDIDATES FOR EXPANSION" comment right above it, and re-measuring the section size on the real store.
