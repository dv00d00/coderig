# Web Impact has no effect filter, so a large MR reads as ~9% signal

**Status:** todo · **Priority: HIGH** (91.3% of what the web Impact view renders is intrinsic noise, with no
control to remove it; the CLI has had the filter since 2026-07-27) · **Found:** 2026-09-02, CLI-vs-web audit ·
**Family:** web / disclosure-parity
**Triage:** ready-for-agent
**Decision:** D1, 2026-09-02 — match the CLI (intrinsics hidden by default, a toggle to restore), and apply
the filter CLIENT-SIDE. See "Why client-side" below; it is the whole reason this card is specifiable.

## The gap

`rig impact` gained `--only` / `--exclude` / `--intrinsic` / `--structural` on 2026-07-27
(`done/impact-usability-parity-filter-and-alloc-noise.md`). `/api/impact` accepts `base`, `head` and `async`
only — `src/Rig.Cli/Web/RigApiEndpoints.cs:130-131`. So the browser cannot hide or reveal intrinsics, cannot
scope to a provider, and shows every `alloc` and `throw` in the diff.

Measured on the MedDBase store: `alloc` (243,391) + `throw` (79,508) against ~30,619 for the other 49
providers — **91.3% intrinsic**. A web reviewer reading a large merge request is reading roughly 9% signal.

## Why client-side — and why that removes the cache-key problem

The map's §2 fork was framed as "a stateful toggle needs a cache-key story". It does not, because the filter
does not have to reach the server:

- **The CLI already filters render-side.** `ImpactCommand` calls `ImpactEngine.DiffAsync`, then applies
  `FilterPerEp(only, exclude, intrinsic)` and `FilterGuardConditions(only, exclude)` to the result
  (`ImpactCommand.cs:238-252`). The filter is not an input to the diff.
- **The server cache is therefore already filter-agnostic.** `QueryCacheKeys.ImpactCacheKey` keys on
  (base store identity, head store identity, rules fingerprint, traversal mode) — `QueryCacheKeys.cs:297` —
  and caches the UNFILTERED artifact. Nothing to add.
- **The payload already carries everything the filter needs.** `ImpactEffectDto` is
  `(Provider, Operation, Resource, Enclosing, File, Line)` and rides inside every `ImpactEpDeltaDto.Added` /
  `.Removed` (`ImpactContracts.cs:10,21`; `ImpactMapper.cs:53-70`). The claim that the web view is
  "count-only" is wrong: the per-EP effect rows are fully present.
- **So the client's IndexedDB key stays exactly as it is** — `impact|${base}|${head}|${asyncWalk}`
  (`api.js:176-179`). No filter axis, no `derivationVersion` interaction, no risk of serving a payload cached
  under the other setting, which is the acceptance constraint the fork was worried about.

Client-side is also the better interaction on the merits: the diff loads BOTH stores and derives both, which
is minutes on MedDBase. A filter that refetched would make toggling intrinsics cost minutes. This mirrors the
File Lens, whose every filter field is client-side for the same reason (`filelens.js:249-256`, ~50s cold).

## Scope

Add to the web Impact view, in the CLI's grammar and vocabulary:

1. **`intrinsic` toggle, defaulting to HIDDEN.** Matches the CLI default. When hidden, the view must state how
   many effect rows are hidden — the CLI's `intrinsic_hidden` disclosure is what keeps hiding from being a
   silent loosening (`ImpactCommand.cs:284`), and the same obligation applies here.
2. **`only` / `exclude` token filters**, same token grammar as the CLI (`provider` or `provider:operation`,
   comma-separated, case-insensitive).
3. **Unknown-token warning.** A typo'd token that filters everything out reads as "no behavioural change" —
   the silent false negative the CLI card was written about (`ImpactCommand.cs:258-267`). Derive the warning
   from the vocabulary PRESENT IN THIS DIFF's payload, not from the rule set: "`llbgen:write` matches no
   provider in this diff" is actionable, needs no new endpoint, and needs no provider list in the client.
4. **Recompute every derived count under the active filter** — the affected-EP count, the per-EP added/removed
   counts, and any header total. A filtered view showing unfiltered totals is worse than no filter.
5. **URL-addressable filter state**, so a filtered view is shareable. Follow the File Lens precedent
   (`LENS_URL_KEYS` in `store.js:145-157`), and pick param names that do not collide with the lens keys.
6. **The `FILTERED` disclosure** whenever the filter is not at its default — again the File Lens precedent
   (`isFilterActive`, `filelens.js:277-281`). Note the asymmetry: intrinsic-hidden is the DEFAULT here, so it
   must show as a hidden-count disclosure rather than tripping `FILTERED`, exactly as the CLI does.

## Out of scope

- **`structural`.** It is genuinely server-side — the per-EP reach roster is not in the payload — and
  `/api/impact/reach` already answers it per EP (`api.js:182-186`). Its own slice.
- **Guard-condition deltas** (§3 of the parity card). Already computed and already in the cache artifact; a
  separate rendering slice.
- **Rejecting unknown QUERY PARAMS** (§5 of the parity card). This card adds no query params, so it cannot
  make that worse; the general fix stays there.
- **Any server change at all.** If a change to `/api/impact`, `ImpactMapper`, `ImpactContracts`, or
  `QueryCacheKeys` looks necessary, STOP and report why — it would mean one of the four bullets above is
  wrong, and that is a decision, not an implementation detail.

## Acceptance

- With the default filter, the web Impact view hides intrinsic effects and states how many rows are hidden.
- Toggling `intrinsic` on and off does NOT issue a network request and does NOT invalidate the client cache.
- `rig impact --base X --head Y --only <tokens>` and the web view under the same tokens report the **same
  effect set and the same affected-EP count** for one store pair. Verify against real captured CLI output,
  not against an assumption about it.
- A token matching nothing in the diff produces a visible warning, never a silently empty result.
- **No `*Schema` bump.** Nothing about the derivation or the payload changes; a bump would flush every warm
  disk cache and every browser's IndexedDB for nothing.
- `impact|${base}|${head}|${asyncWalk}` is still the client cache key, unchanged.

## Provenance

Section 2.1 of [cli-web-parity-1](./cli-web-parity-1-web-api-seed-and-effect-disclosure-parity.md), extracted
2026-09-02 when D1 resolved the §2 fork. That card keeps §2's cross-endpoint intrinsic question and §7's
file-effects cache-key axis; this one owns the Impact slice only.

## Related

- **ABSORBED BY** [Impact selection moves into the engine as one view](./cli-web-collapse-1-impact-selection-into-the-engine.md)
  — the same omission seen from the web side. This card is that slice's option A (client-side, D1 as written);
  its option B applies the shared view server-side post-cache via `?only=&exclude=&intrinsic=` and is
  recommended there on the ground that A is a third implementation of the selection in JS. Option B reverses
  D1's mechanism while keeping D1's intent, so it is the product owner's call and that card is blocked on it.
  Family rationale on [the CLI/web collapse map](./cli-web-collapse-map.md).
