## `impact` behavioral-EP count differs by one between CLI and web

**Status:** todo
**Source:** demo prep, 2026-07-31

**Terminal note — 2026-09-03:** the count divergence shipped as part of
[CLI/web collapse child 1](./cli-web-collapse-1-impact-selection-into-the-engine.md) in `cacb5d92`.
The unavailable historical row's attribution was deliberately left unresolved on 2026-09-03: its store pair
is gone and no product behavior depends on the answer.

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

So one surface applies a filter the other doesn't — this is not a rendering difference.

### Settled 2026-09-02 by reading the code: the CLI's 32 is correct

The web's 33 is not a behavioral count under any definition the CLI uses. It is `|PerEp|`:

| step | anchor |
| --- | --- |
| the web renders `d.perEp.length` | `src/Rig.Cli/wwwroot/main.js:622` |
| which maps `art.Diff.PerEp` UNFILTERED | `ImpactMapper.cs:45` |

So the web LABEL is wrong, not the data — it prints a row count under a behavioral heading.

Root cause: `ImpactEngine`'s selection helpers are OPTIONAL statics that the web simply never calls —
`FilterPerEpEffects` (`ImpactEngine.cs:530`), `EffectChangedEpCount` (`:545`), `ClassifyStructuralCause`
(`:1464`). Nothing forces a consumer through them, so a new consumer gets the raw diff by default.

### Still open: which mechanism produced the extra row

Whether `echoactor MedDBase.Pathways.Processes.Admin.Catalogues.Inbox` is an intrinsic-only delta or a
hazard-only delta is **unsettled**. It cannot be settled from this card's repro: the store pair
(`e8858aa90e02` / `4cfb885a244b-dirty`) no longer exists in `.rig`.

The procedure on any live pair:

1. `rig impact --base A --head B --format tsv`, count `^ep_delta` rows.
2. The same with `--intrinsic`, count again.
3. Compare both against `/api/impact?base=A&head=B` → `perEp | length`.
4. If the `--intrinsic` count equals the web count, the row is intrinsic-only. If the default count already
   equals it, the difference is hazard-only.

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

## Related

- [`rig impact` reports two different behavioral-EP counts](../done/impact-reports-two-different-behavioral-ep-counts.md)
  — the CLI disagrees with ITSELF on the same vocabulary: `impact_summary behavioral_eps` prints
  `diff.PerEp.Count` while the human header prints `EffectChangedEpCount`. Choosing one definition there
  settles what the web label should say here.
- **ABSORBED BY** [Impact selection moves into the engine as one view](../done/cli-web-collapse-1-impact-selection-into-the-engine.md)
  — this card's root cause IS that card's optional-statics problem, and the fix lands there as one
  `ImpactEngine.Select` consumed by every surface. Family rationale on
  [the CLI/web collapse map](../todo/cli-web-collapse-map.md). What stays here: the attribution of the extra
  `echoactor MedDBase.Pathways.Processes.Admin.Catalogues.Inbox` row, intrinsic-only versus hazard-only, which
  is still unsettled, along with the procedure for settling it on a live store pair.
