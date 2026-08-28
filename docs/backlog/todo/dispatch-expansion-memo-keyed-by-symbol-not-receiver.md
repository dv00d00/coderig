# A virtual hub devirtualizes only for the FIRST receiver that reaches it — the expansion memo is keyed by symbol, not by dispatch context

**Status:** OPEN · **Priority: HIGH** (silent, order-dependent loss of devirtualization in `tree`/`reaches`/`impact`;
the overrides that go missing are exactly the effect-bearing ones, so real hazards inside loops produce no
finding) · **Found:** 2026-08-28, on MedDBase, from the `WizardBase.Book` booking storm ·
**Family:** query correctness / dispatch

## The bug

Both forward traversals memoize "have I already walked this node?" on the **bare symbol**:

```csharp
// src/Rig.Domain/Functions/FactPathFinder.cs:588  (BuildTree)
var expanded = new HashSet<string>(StringComparer.Ordinal);
…
if (expanded.Contains(n.Symbol)) { n.Truncated = true; n.TruncationCause = TruncationCause.AlreadyExpanded; continue; }
expanded.Add(n.Symbol);
```

```csharp
// src/Rig.Domain/Functions/FactPathFinder.cs:410-412  (ReachesWithFanoutCore)
// The static receiver type of the (BFS-shortest) edge that reached each node …
var receiverOf = new Dictionary<string, string?>(StringComparer.Ordinal);
…
if (info.ContainsKey(s.Node)) { if (grew) queue.Enqueue(s.Node); continue; }
```

But a node's successors are **not** a function of the symbol alone. Its body edges are fixed by the graph; its
**dispatch fan is a function of the receiver it was reached with** — that is the entire point of
`NarrowByReceiver`. So the first occurrence of a virtual hub wins the expansion with *its* receiver's fan, and
every later occurrence — reached through a different receiver, resolving to a **different override** — is
discarded:

* in `tree` it becomes a `⋯elided` (`TruncationCause.AlreadyExpanded`) leaf with no children;
* in `reaches` it is dropped outright, so the other receivers' overrides are simply **not reachable**.

The asymmetry is visible in the code that was already there: `bindingOf` (the generic type-arg binding, the
*other* input to dispatch resolution) is deliberately unioned across paths and re-enqueued when it grows,
with a comment explaining that first-wins "would unsoundly drop the others". The receiver — the input that
selects the override — never got the same treatment.

## Measured evidence (MedDBase, `rig tree "…WizardBase.Book(…)" --view hazards --depth 12 --raw`)

| | |
|---|---|
| `EntityBase.Save` nodes in the forest | **62** |
| of those, actually expanded | **1** |
| what the one expansion resolved to | `CommonEntityBase.Save «override-dispatch»` (the *inherited* impl, for the first receiver — a `MedicalContextEntity`) |
| the other 61 | `EntityBase.Save ⋯elided`, no children, no effects |

Among the 61 are the sites this was chased for, in
`src/main/MedDBase.BusinessLogicTier/Appointment/Booking/WizardServices.cs`:

```csharp
foreach (var wizardPrice in wizardService.ServicePrices)
{
    var module = new AppointmentServiceModuleEntity { … };
    tm.Add(module);
    module.Save();                       // ← EntityBase.Save 🔁[wizardPrice in …] ⋯elided
}
…
foreach (var s in servicesInModule.Where(eligible))
{
    …
    var personCreditUsed = new PersonCreditUsedEntity(tm) { … };
    personCreditUsed.Save();             // ← EntityBase.Save 🔁[s in servicesInModule…] ⋯elided
}
```

The loop annotation sits on the very node that stays opaque, so a genuinely quadratic chain (the child `Save`
override → `Appointment.BuildScheduleServicesCache()` → full collection re-reads; the measured 48k-round-trip
booking storm) yielded **zero** `n_plus_1` / `looped_effect` findings from the entry point.

**The original hypothesis was wrong and worth recording.** The suspicion was that receiver static-type
resolution fails when the receiver flows through a parameter, a collection element or a LINQ binding. It does
not: those receivers are **locally-constructed locals** (`var module = new AppointmentServiceModuleEntity{…}`),
the most favourable case there is, and `CallEdge.ReceiverType` is mined correctly for them. Nothing about how
the value *flowed* matters at query time — extraction records the site's static receiver type either way. The
only thing that decided whether a site devirtualized was **which receiver happened to arrive first**, which is
why the same capability looked present in one query (`CreateServices --depth 4` shows
`AppointmentServiceModuleEntity.Save «via ORMSupportClasses.EntityBase»`) and absent in another over the same
store.

This is also why it presents as intermittent: it is sensitive to traversal order, root, and depth, not to the
code under analysis.

## Fix

Key the expansion memo by **dispatch context** rather than by symbol: resolve the node's fan for this visit and
use the (sorted, deduped) target set as the discriminator.

* A node with **no** fan — every non-virtual method, the overwhelming majority — keys to the bare symbol, so it
  is still expanded exactly once and the traversal is unchanged for it.
* A virtual hub reached under receivers that resolve to **different** overrides gets one expansion per distinct
  fan. Receivers that resolve identically still collapse.
* `reaches` additionally has to carry the context on the work item instead of storing one receiver per node,
  and re-queue a node when a new context reaches it — the same shape `bindingOf` already had.

Bounded by `MaxDispatchContexts` per node, mirroring `MaxBinding`.

Not a widening of dispatch: every fan is still the narrowed set `NarrowByReceiver` already computed. This
recovers overrides that narrowing had *correctly* resolved and the memo then threw away.

## Out of scope / residual

* **`FactPathFinder.Find` (`rig path`)** has the same first-wins `receiverOf` (`FactPathFinder.cs:100`). It
  returns a single shortest path rather than a set, so the failure mode is different (a path may route through
  a less-apt override) and it is left alone here. Worth a follow-up.
* **Receiver propagation is still one-level.** Two receivers that resolve to the *same* fan share an expansion,
  so the concrete `this`-type one of them would have propagated across self-call edges
  (`PropagateReceiver`) is not carried a second time. That can leave a downstream self-call narrowed to the
  first receiver's line. Pre-existing, second-order, and deliberately not fixed — the principled ceiling is a
  real type-flow pass, per the standing note in CLAUDE.md against hand-rolled VTA.
* **No store-schema change.** This is entirely query-side: no `FactExtractor` change, no new fact, no store
  column. Existing stores need **no reindex** — only the query caches, invalidated by the `*Schema` bumps.
