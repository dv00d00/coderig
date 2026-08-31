# Rider file-effect model goes stale after a save and never self-heals

**Status:** todo · **Found:** 2026-08-31, during the Rider plugin backend spike · **Family:** live host / Rider integration

## What happens

The `rig watch` resident host answers the Rider `file-effects` request with sourceStatus `stale` right after
the user saves a file, and there is nothing in the request path that clears it — the next identical request
from the SAME file gets the SAME `stale` answer until some unrelated query happens to force a reconciliation.
The plugin's marks freeze at whatever they were before the save.

Serving code is `ServeFileEffectsAsync`
(`src/Rig.Cli/Commands/WatchCommand.cs:617`): it captures the current snapshot under `_gate` and hands it
straight to `RiderFileEffectResponder.Respond` — no demand refinement, no "a file just came in for this
project, rebuild it now." The unavailability decision is `FileEffectUnavailableReason`
(`WatchCommand.cs:645`), which fails closed on a topology change, a watcher overflow, `Dirty.PendingProjects`
being non-empty, or `GetCompilationHealth()` (`WatchCommand.cs:662`) reporting anything but clean. A save
typically dirties exactly the project the user is editing, so `PendingProjects > 0` is the common case and it
stays that way until something else in the host's normal query flow reconciles it.

## Why it matters

Fail-closed here is deliberate and correct — a wrong mark on a stale file is worse than no mark, and the
comment at `WatchCommand.cs:644` says so explicitly ("this first slice does not attempt demand refinement").
The gap is that nothing ever asks for the refinement: the file-effects request is exactly the signal that
should trigger "reconcile this file's project now," and today it doesn't. From the editor's point of view the
marks just silently stop updating after a save, with no visible reason and no recovery short of an unrelated
query landing first.

## Fix directions to record, not decide

1. **Demand-driven reconciliation triggered by the file-effects request itself** — if the requested file's
   project is in `Dirty.PendingProjects`, reconcile that project inline (or kick it off and answer `stale`
   once, not indefinitely) rather than only reporting the pending count.
2. **A background debounce that re-runs the design-time build for dirty projects** on its own schedule, so
   staleness clears without any client having to ask.

Either way, keep the fail-closed contract: the fix is to make `stale` self-correcting, not to relax when
`stale` is returned.
