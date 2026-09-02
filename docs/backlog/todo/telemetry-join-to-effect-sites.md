# Join runtime telemetry to effect sites

**Status:** PARKED WITH A TRIGGER — filed in `todo/` for the trigger's sake, NOT because it is pickable:
this is blocked on an EXTERNAL PRECONDITION, not awaiting a decision from us, and there is no work to
schedule until that precondition lands. Recorded 2026-09-02. Not "not useful": **not yet**.
**Reopen when source-generated proxies emitting a span per method call are in MedDBase prod** — that is when
the join key this recon could not find (method-level runtime identity) exists, and the product option (real
call count and p99 against an effect site) becomes available directly, without the entity/table heuristic and
without the exception-path-only limit of the Graylog option. · **Family:** telemetry / effect attribution
**Triage:** needs-info

## Why there is no join key today

Settled by data, not preference. rig has **no effects table** — effects are derived at query time. Every
`llblgen` rule sets `resource: receiver_type`, which resolves to the entity *class* name. No SQL text and no
table name is captured anywhere; the table name exists only as a PascalCase→SCREAMING_SNAKE convention that rig
never validates, because LLBLGen resolves the real mapping at runtime.

Cardinality, measured on store `409c330b99dd`:

| entity resource | distinct effect sites |
|---|---|
| `AppointmentEntity` + `AppointmentCollection` → `APPOINTMENT` | **377** |
| `AccountEntity` | 161 |
| `PersonEntity` | 135 |
| `ServiceModuleEntity` | 13 |

And **3,108 of 18,768 llblgen effects (16.6%) carry no entity resource at all** (`LinqMetaData` 2,195,
`CommonEntityBase` 554, `int` 359) — tracked separately as
[quoted-query-resource-attribution](./quoted-query-resource-attribution.md), also postponed.

## The options, and where each stands

- **Failure provenance via Graylog — the fallback if this is wanted before the proxies ship.** Graylog
  `StackTrace` carries method + file + line, verified live: the same grain as an effect site. An effect site
  links to "has this line thrown in prod, and when" — exact, no convention guesswork, no 377-site dilution.
  It can never give call count or p99: exception path only. Feasible and correct; it does not currently hit a
  nerve, which is why it is not being built now.
- **Table-level SQL link — rejected.** Service grain wearing site-grain clothing: 377 effect sites behind one
  `APPOINTMENT` link. Also not novel — the table-set join has already been done by hand
  (`../done/amplification-context-propagation.md:87-89`, 71/79 → 66/79 against an OTel oracle).
- **Full-statement-text matching — parked separately.** Revisit only if prod `db.query.text` turns out to be
  matchable on full statement text at interactive speed; a prior note recorded it timing out at 30s over
  7 days.

## Caveats on the recon itself

Recorded so a reopen does not inherit them as facts:

- The ClickStack half was reconstructed from a prior session's recorded recipe, **not tested live** (VPN down).
- Local Graylog is errors-only, 90-day retention, with two exception types in the sample — the mechanism was
  never exercised on a real LLBLGen failure.
- Prod PDB availability, which file:line in a stack trace depends on, is unknown.
