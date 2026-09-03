# The family list comes from the rules, not a hardcoded eight — and unknown families get their own lane

**Status:** todo · **Found:** 2026-09-03, effect-severity denominator question ·
**Triage:** ready-for-agent
**Family:** web / effect vocabulary
**Decision:** D2, 2026-09-03 — **the family list is config-defined, of arbitrary size.** No hardcoded list,
no fixed eight. The client reads it from the server, and anything outside it renders in a separate lane
rather than being silently promoted to a family of its own.

## Why

`n/8` is wrong on this store, and hardcoding is why. The eight is a **union of two rule sets**:

| rule set | families it declares |
| --- | --- |
| `src/Rig.Cli/builtin-rules.json` (ships with the tool, loaded everywhere) | blob, bus, cache, db, io, rpc, **search** |
| `C:\Git\meddbase-analysis\rig.rules.json` (this repo only) | blob, cache, db, **echo**, rpc |

`echo` exists only because MedDBase declares it; `bus` and `search` only because the builtin file ships into
every repo. `bus` = {mediatr, rabbitmq}, `search` = {azure_search, elasticsearch}, and MedDBase has **zero**
references to any of them across all 2,444,657 refs in the store — not a sample, the whole store:

```
RabbitMQ 0 · MediatR 0 · Elasticsearch 0 · Nest. 0 · Azure.Search 0 · SearchIndexClient 0 · MassTransit 0
```

Structurally unreachable, so a reader shown `6/8` is being told the site sits at 75% of a ceiling it can
never reach. (This also retires the `rig derive --only bus` / `--only search` follow-up proposed on
[the severity card](./effect-severity-mark-compute-the-distribution-first.md) — the rule sets and the ref
counts answer it outright.)

## The server already does this; the client ignores it

`ProviderCatalog.DeclaredFamilies(rules)` computes the list from the effective rule set. It is exposed twice
already — `ProvidersService.List` (`src/Rig.Cli/Services/ProvidersService.cs:16-24`) and the live
`/api/providers` endpoint, which returns `providers`, `providerOps` and `families`. Arbitrary N, no enum, no
constant.

The client keeps a hand-copy anyway:

```js
// filelens.js:30 — "Family -> providers, straight from `rig derive --list-providers`"
export const FAMILY_PROVIDERS = { blob: [...], bus: [...], cache: [...], db: [...], echo: [...], io: [...], rpc: [...], search: [...] };  // :33-42
export const FAMILY_HELP     = { ... };  // :49-58
```

Same class as the `IntrinsicProviders` hand-copy at `store.js:293-294`. The copy exists because nothing
offered the real thing at the time; `/api/providers` now does.

## Scope

1. **Serve the family→providers map from `/api/providers`.** It returns family *names* today; add the
   provider grouping so the client needs no map of its own.
2. **Delete `FAMILY_PROVIDERS` and `FAMILY_HELP` from `filelens.js`.** The legend (`filelens.js:877`) and
   the grain toggle iterate the served list instead of a literal.
3. **An `other` lane for anything outside the declared set.** Today `familyOf` (`filelens.js:47`) falls back
   to `provider => provider`, silently promoting an unknown provider to a family of its own — it appears in
   the gutter as if it were a peer of `db`. Bucket it into `other` instead, and keep the provider name in the
   tooltip. The gutter geometry already tolerates arbitrary N: it ranks slots and folds overflow into a
   popover (`filelens.js:590-616`), so no layout work is implied.
4. **Denominator disclosure.** With the list config-defined, any `n/N` mark must state which N it used,
   because N now varies by repo.

## The one thing config does not hold

`FAMILY_HELP` is prose — a one-line gloss per family, so a reader need not guess what `echo` means. The rules
carry no description field. Either add an optional `description` to the family declaration, or keep glosses
client-side keyed by family name with a generic fallback for an unrecognised one. Small, but it is a real
decision and it belongs to whoever takes this slice.

## Still open — the denominator question this card does not settle

Config-defined does **not** by itself fix the two-always-missing problem: `bus` and `search` *are* config,
they are just in `builtin-rules.json`, so `DeclaredFamilies` still returns 8 for MedDBase. Two ways out, and
the choice is the product owner's:

- **declared ∩ present-in-store** — self-correcting, `6/6` today and `7/7` the day someone adds RabbitMQ.
  Needs a store read the legend does not currently do.
- **let a repo's rules opt out of builtin families** — explicit and cheap, but someone maintains the list.

Recorded on [the severity card](./effect-severity-mark-compute-the-distribution-first.md), which owns the
mark and cannot ship until this is answered.

## Acceptance

- No family name, family list, or family→provider mapping appears as a literal in `wwwroot/`.
- A rule set declaring a family the client has never heard of renders it correctly, unprompted.
- A provider outside every declared family lands in `other`, never as its own gutter family.
- Any `n/N` breadth mark discloses N.
