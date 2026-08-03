## `impact` behavioral-EP count differs by one between CLI and web

**Status:** todo
**Source:** demo prep, 2026-07-31

### Repro

```powershell
cd C:\Git\meddbase-analysis
rig impact --base e8858aa90e02 --head 4cfb885a244b-dirty
curl "http://localhost:5050/api/impact?base=e8858aa90e02&head=4cfb885a244b-dirty"
```

- **CLI:** `32 entry point(s) with a changed behavior` — and internally consistent: the behavioral block
  lists exactly 32 rows, and `32 + 543 structural-only = 575 affected`, which matches its own header.
- **Web:** `impact: 33 behavioral change(s)` / `33 EP(s) with a behavioral effect change`;
  `perEp` has 33 entries, every one carrying a non-empty `added`/`removed`/hazard delta.

### The extra row

Set-differencing the two (normalising route vs FQN, and the `` `1 `` generic-arity suffix the CLI prints)
leaves exactly one EP present in the web payload and absent from the CLI list:

```
echoactor  MedDBase.Pathways.Processes.Admin.Catalogues.Inbox
```

So one surface applies a filter the other doesn't — this is not a rendering difference. Which one is correct
is **not yet established**; `ImpactMapper`/`RigApiEndpoints` and the CLI's behavioral-block selection need to
be read side by side. Note the CLI's arithmetic is self-consistent, which is weak evidence for the CLI, but
self-consistency doesn't prove the row should be excluded.

### Why it matters beyond a cosmetic count

`--expect-no-effect-change` is advertised as a CI gate. If the two surfaces disagree about whether an EP's
behaviour changed, they can disagree about whether the gate should trip — a reviewer reading the dashboard and
a pipeline reading the exit code would see different answers on the same commit pair. Fix the shared
selection, don't patch the count in one renderer.

### Also worth aligning while in here

The CLI prints `AsyncReply.Inbox``1` where the web prints `AsyncReply.Inbox`, and the CLI prints FQNs where
the web prints routes. Harmless for humans, but it makes CLI↔web diffing (exactly what caught this) need
per-surface normalisation. A shared identity for an EP row would make parity checks mechanical — and would
make this class of bug testable.
