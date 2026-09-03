# A second `rig serve` silently overwrites the first one's resident marker

**Status:** done 2026-09-03 · **Found:** 2026-09-03, while verifying the family-list slice in a browser ·
**Triage:** ready-for-agent
**Family:** serve / resident transport

## What happens

`rig serve` publishes `.rig/serve.json` **unconditionally**. Start a second serve in the same working
directory — a local build on another port, which is the documented way to see `wwwroot` edits at all — and it
overwrites the marker the first one wrote. There is one marker per working directory and no arbitration.

The marker is not decoration: `AnnotateResidentTransport` discovers a resident host through it
(`MarkerFileName = "serve.json"`, `ReadMarker` / `IsAlive`), so `rig annotate` and anything else on that path
follows the marker to whichever serve wrote last. Kill that one and the marker is stale until something
notices the PID is dead — `TryGetAsync` does check `IsAlive` and deletes an unchanged marker, but only when
someone happens to call it.

Observed: a locally built serve on `:5077` replaced the `:5050` entry; the original had to be restored
by hand (backed up, restored byte-for-byte, `GET /api/meta` re-verified 200).

## Why it matters more than it looks

The one workflow that *forces* a second serve is the one this repo documents in `CLAUDE.md` — `rig serve`
serves `wwwroot/` from the INSTALLED tool, so a UI change is invisible until you either re-run `mini-ci` or
run the locally built `Rig.Cli.dll serve` on another port. So the collision is not an edge case; it is the
normal cost of doing web work.

## Options

- **First writer wins, with a live-PID check.** A serve that finds a marker whose PID is alive refuses to
  publish (or publishes nothing and says so), rather than overwriting. Cheapest, and matches the existing
  `IsAlive` machinery. `rig watch --no-serve` already establishes the precedent of a host that deliberately
  does not claim the endpoint.
- **Marker per port.** `serve-<port>.json`, with discovery picking the newest live one. Removes the collision
  entirely at the cost of a discovery scan and a rule for which host wins.
- **Explicit opt-out.** A `--no-marker` flag for the "I am a second, temporary host" case, mirroring
  `watch --no-serve`. Leaves the default footgun in place, so weakest.

First option recommended: it makes the failure a refusal rather than silent theft, which is the same
fail-fast-instead-of-late shape as the two guards added on 2026-09-03 (`mini-ci` tool-store lock,
`rig index` store lock).

## Acceptance

- Two serves in one working directory: the second either refuses the marker or writes its own, and in neither
  case does the first host's marker vanish while that host is alive.
- A stale marker (PID dead) is still reclaimable — the current `IsAlive` + delete path must keep working.
- `rig annotate` resolves to a host that is actually running, with both serves up.

## Verification

- `ServeMarkerLeaseTests`: 5/5 passed, including an eight-publisher concurrency race with exactly one owner.
- Full Release build: 0 warnings, 0 errors.
- Main suite: 1,416/1,416 passed; full integration matrix passed apart from its one documented skip.
