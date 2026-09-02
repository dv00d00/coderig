# The File Lens `intrinsic` and `async` toggles are dead controls that claim to work

**Status:** todo · **Priority: HIGH** (a control that lies is worse than a missing one; it reads as a working
filter and returns the identical payload) · **Found:** 2026-09-02, CLI-vs-web audit · **Family:** web / disclosure-parity
**Triage:** ready-for-agent

## The defect, with anchors

- Both segmented controls are labelled **"CHANGES THE QUERY, refetches"** —
  `src/Rig.Cli/wwwroot/filelens.js:805-806`.
- The filter comment at `src/Rig.Cli/wwwroot/filelens.js:254` asserts: *"Only `intrinsic` and `async` change
  what the SERVER computes, and the UI says so."*
- `/api/file-effects` accepts `file`, `store`, `only`, `exclude`, `minDepth`, `maxDepth`, `direct`, `looped`,
  `noDispatch` — `src/Rig.Cli/Web/FileEffectsEndpoint.cs:77-87`. **Neither `intrinsic` nor `async`.** That
  endpoint's own comment says both would need their own cache key first.
- `src/Rig.Cli/wwwroot/api.js:207-211` sends only `store` and `file`, and its cache key is
  `file-effects|${storeId}|${file}` — omitting both.

So `setLensFilter` (`src/Rig.Cli/wwwroot/main.js:896-904`) fires *"lens: server-side flag changed —
refetching…"*, the refetch hits the client cache, the **identical** payload returns instantly, and the toggle
appears to have worked.

## The ask

Remove or disable both controls so the UI cannot assert a filter it does not apply.

**Correct the comment at `filelens.js:254` in the same change.** Prose stating that the UI tells the truth is
how this survived an audit; leaving it in place invites the next reader to trust the controls again.

## Scope boundary

This card owns the **stopgap only**. The real fix — a cache-key axis for `intrinsic`/`async` on the
file-effects projection, which the endpoint's own comment anticipates — stays as §7 of
[cli-web-parity-1](./cli-web-parity-1-web-api-seed-and-effect-disclosure-parity.md), and it shares the
toggle-versus-cache-key answer that card's §2 fork needs. Do not build the axis here.

## Acceptance

- No control in the File Lens claims a server-side effect the endpoint cannot honour.
- The `:254` comment matches the code.
- §7 of `cli-web-parity-1` retains the cache-key work as the follow-on, and this card links to it.
- No `*Schema` bump: nothing about the derivation or the payload changes.
