# `n_plus_1`: two independent defects losing 23 true findings (key capture + provider gate)

**Status:** TODO · **Found:** 2026-08-03, investigating why all 175 `n_plus_1` findings were `entity_cache:read`
· **Family:** hazard-recall / FR-3 · Distinct from
[n-plus-1-cross-method-amplification](n-plus-1-cross-method-amplification.md) — these are INTRA-method and both
have small fixes.

## The observation that started it

Every one of the 175 `n_plus_1` findings on the MedDBase store is `entity_cache:read`. Zero from `llblgen`,
`object_store`, `db_command`, `http`, despite all being in the `observations.nPlusOne` provider gate. The
initial guess — that other providers' reads are simply never lexically looped with a varying key — is FALSE.

Cross-tab over the whole store (effects only), which is what settles it:

| provider:operation | `looped_effect` + `n_plus_1` | `looped_effect` only | not looped |
|---|---|---|---|
| `entity_cache:read` | **175** | 74 | 1,658 |
| `llblgen:read` | 0 | **14** | 3,564 |
| `object_store:read` | 0 | **33** | 154 |
| `db_command:execute` | 0 | 7 | 34 |
| `http:GET` | 0 | 3 | 20 |
| `db_reader:row_read` | 0 | 2 | 3 |
| `redis:read` / `inproc_cache:read` | 0 | **0** | 16 / 0 |

`looped_effect` fires and `n_plus_1` does not ⇒ the iteration context WAS found and the key/gate stage
rejected it. The genuine zeros (redis, inproc_cache, efcore, repository, fhir, elasticsearch, azure_search)
are vacuous — 5 of the 12 gated providers have no effect rule at all in the MedDBase ruleset.

## Defect 1 — the argument surface drops keys nested in a complex expression (+11)

`FactExtractor.ArgumentListOf` (~line 1318) records an argument's name only when the argument expression is
ITSELF an identifier or member access:

```csharp
var name = expression is MemberAccessExpressionSyntax or IdentifierNameSyntax ? expression.ToString() : null;
```

All 14 looped `llblgen:read` sites are `TypedListBase<T>.Fill(long, ISortExpression, bool, IPredicate)`, where
the per-iteration key sits INSIDE a predicate expression. `InvoicesByNominalCode.cs:96`:

```csharp
foreach (string s in nomCs) {
    ncl.Fill(1, null, true, (NominalCodeFields.FkOwnerAccount == profile.FkOwnerAccount)
                          & (NominalCodeFields.Name == s.Trim()));
```

That argument is a `BinaryExpressionSyntax`, so every one of `FirstArgumentName`, `FirstArgumentTemplate`,
`ArgumentNames[3]`, `ArgumentTemplates[3]` is null and `KeyVariesWith` has nothing to match `s` against.
Provider, operation and iteration identifiers all pass; the key match alone rejects it.

**11 confirmed true N+1s lost:** `InvoicesByNominalCode.cs:96`, `Servlet\SPByNominalCode.cs:78`,
`Servlet\JDOC\SPByNominalCode.cs:78`, `Pages\Admin\Reports\Edit.cs:264`, `Pages\Collaborate\Overview.cs:167`,
`Pages\PageLoad.cs:385/393/416`, `ServiceTier\Company\CompanyToChamber.cs:514/590/604`.
Correctly excluded by design (2): `InvoiceEntity.cs:2101` (`for`), `TestBed_Legacy.cs:734` (`do`) — no bound
identifier. Needs real dataflow, out of scope (1): `InvoiceEntity.cs:2064`, where the loop variable is
`numberString` but the key is the parsed local `number`.

**Fix:** when the argument expression is not itself an identifier/member access, fall back to the joined
DESCENDANT identifier/member paths (`"NominalCodeFields.Name|s.Trim"`). Extraction change ⇒ needs a reindex.
FP risk is bounded because the match is whole-word, but it is real: a hoistable read whose predicate mentions
the loop variable in a NON-key position would become a false positive. Gate it behind a synthetic fixture that
encodes exactly that case before shipping.

## Defect 2 — `object_store` is absent from the provider gate (+12)

`observations.nPlusOne[0].providers` in `builtin-rules.json` lists `http, redis, inproc_cache, entity_cache,
db_reader, db_command, llblgen, repository, efcore, fhir, elasticsearch, azure_search` — **no `object_store`**,
even though two effect rules tag `object_store:read` and those call shapes take the key as a DIRECT first
argument, so the argument surface is already captured. Rejected purely by the provider check in
`FactObservationDeriver` at the `rules.NPlusOne` gate.

12 sites would fire immediately, key already captured and whole-word-matching the bound identifier — e.g.
`Application.Core\ObjectStore.cs:847` (`fkObjectHolder`), `MedDBase.Update\Update.cs:2793` (`id`),
`DataServer\…\CompiledQueries.cs:34` (`dto` / `dto.Identifier`), `Pages\Activity\Browser.cs:225` (`gk`),
`ServiceTier\Mp\MpHtmlService.cs:344` (`gkSignature`), `ServiceTier\Quote\QuoteService.cs:157` (`dto.Id`).
The other 20 looped `object_store:read` rows correctly would NOT fire (they key on a method parameter or an
unrelated local, not a range variable) — a useful sign the gate is the only thing wrong.

Also: `db_command`'s only tagged operation is `execute`, which is NOT in the gate's `operations` list — a real
token mismatch, but all 7 looped sites have a null argument surface (raw `ExecuteNonQuery`, key bound via
parameters), so fixing the token alone unlocks nothing on this store. Lower-value ungated read providers with
rules but no gate entry: `config`, `dapper`, `ldap`, `linq2db`, `parquet`, `queue`, `xero`.

**Fix:** add `object_store` to the provider list (query-side rules data, NO reindex). Per the repo cache rule
this needs the relevant `*Schema` constant in `QueryCacheKeys.cs` bumped so cached derivations invalidate.

## Why this matters beyond the +23

`entity_cache` fires only by SYNTACTIC ACCIDENT: `AccountCache.New(chamber.PfkCompany)` happens to be a bare
member access, so the key lands in `FirstArgumentName`. The varying-key discriminator therefore works only
when the key is a syntactically bare identifier/member path at the read's own call site — far more brittle
than 175 findings implies. Any cross-method key propagation is strictly harder than an intra-method base case
that is itself only working for one shape, so Defect 1 is plausibly a PREREQUISITE for the cross-method design
rather than an independent bug.

## Caveats

Both counts are static projections from the fact store, not observed rig output after a fix. Neither fix
addresses parse-then-use shapes (`InvoiceEntity.cs:2064`), which need intra-method dataflow. The 3,564
non-looped `llblgen:read` rows were not examined for cross-method amplification.
