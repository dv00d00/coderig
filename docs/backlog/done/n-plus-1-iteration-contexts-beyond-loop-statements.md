# `n_plus_1`: iteration contexts beyond loop STATEMENTS (query expressions + enumerating lambdas)

**Status:** IMPLEMENTED 2026-08-03 (branch `fix/stale-store-and-seed-resolution`; 943/944 tests green, 1 pre-existing
skip) · **Found:** 2026-08-03 from a preprod trace, not from reading rig · **Family:** hazard-recall / FR-3

## The miss

Preprod trace `35bcafca0907910d3106c460f5d0afc7` (`Admin/Profile/Home2`, ~72s wall): **11,461 single-row
`SELECT [dbo].[PROFILE]` round-trips in one request**, 34.85s of DB time. The dashboard "Fat Spans — Wall + SQL
Roundtrip Triage" ranks it 3rd worst N+1 in preprod by total SQL seconds (2,347 calls/trace, 35.7s).

Source is `MedDBase.Pages/Admin/Profile/Home2.cs:294`:

```csharp
return from p in profiles.ToList().DistinctOn(p => p.PkProfile)
       let profile = ProfileCache.New(p.PkProfile)          // ← one SELECT per element
       let activeLicense = p.PkLicense > 0 ? Some(LicenseCache.New(p.PkLicense)) : None
```

`rig` reported **nothing** — not even `looped_effect`. Every gate of the FR-3 detector passed except one:
`ProfileCache.New` is tagged `entity_cache:read`, `entity_cache` is in the `nPlusOne` providers, `read` is in its
operations, and the key `p.PkProfile` genuinely varies. The blocker was that iteration context was recognised ONLY
from loop STATEMENTS — `FactExtractor.StructuralContextOf` matched `ForEach`/`For`/`While` and nothing else — so a
`let` clause, whose ancestors are `LetClauseSyntax`/`QueryExpressionSyntax`, produced `loopKind == null`. A second
blocker sat behind it: `ForeachIdentifier` returned an identifier only for `foreach`, so widening the switch alone
would not have fired.

Scale of the blind spot on MedDBase: **109,825** call sites sit in a query-expression body vs **31,740** in a
`foreach` — the missed iteration surface was 3.5× the covered one. `do` bodies (557 sites) were missed entirely too.

## What shipped

Iteration context is now any construct that ITERATES, and the n_plus_1 identifier is a SET drawn from whichever
kinds BIND a name:

| Kind | Binds | n_plus_1 candidate |
|---|---|---|
| `foreach` | iteration variable | yes |
| `query` (LINQ query expression body) | every range variable (`from`/`let`/`join`/`into`) | yes |
| `lambda` (rule-declared enumerating call) | the lambda's parameters | yes |
| `for` / `while` / `do` | nothing | no — `looped_effect` only |

Precision details that matter:

- **The primary `from` source is excluded.** `from p in profiles.ToList().DistinctOn(..)` evaluates once, so a read
  there is not amplified. Getting this wrong would be exactly backwards — that expression is the batched FIX shape,
  and reporting it as the per-element read would invert the finding. Span containment, so it holds at any nesting.
- **Every query range variable counts, not just the `from` one.** A query rebinds all of them per element, so a key
  built from a `let` is as amplified as one from the range variable. Hence a set, not a single identifier.
- **Loop and lambda identifiers UNION rather than one winning.** In `foreach (var a in xs) ys.Select(y => Fetch(y))`
  both are rebound per iteration; taking only the loop's would miss the `y`-keyed read.
- **`for`/`while`/`do` deliberately emit no n_plus_1.** They amplify, but bind no identifier, so there is nothing for
  the varying-key discriminator to match. `looped_effect` covers them; a keyless n_plus_1 would be a guess.

The enumerating-lambda set is rule DATA (`observations.enumeratingMethods` in `builtin-rules.json`), gated on the
resolved target's **DECLARING type** rather than the receiver type or the method name:

- Declaring type is receiver-shape independent — `Select` on a `List`, an array, an `IEnumerable` or an `IQueryable`
  all declare on `System.Linq.Enumerable`, one stable FQN. A receiver-type list would need every sequence type and
  would still miss custom `IEnumerable`s.
- It is the ONLY dimension that separates enumerating from SINGLE-SHOT lambda takers, and on MedDBase that separation
  carries the whole precision budget:

  | Target | Sites | Iterates? |
  |---|---|---|
  | `LanguageExt.Option<A>.Map` | **5,167** | no |
  | `LanguageExt.Seq<A>.Map` | 1,181 | yes |
  | `LanguageExt.Validation<F,S>.Map` | 301 | no |
  | `LanguageExt.Lst<A>.Map` | 210 | yes |
  | `LanguageExt.Either<L,R>.Map` | 110 | no |

  `Option.Map` alone is 4.4× more common than `Seq.Map`. A name-only gate on "Map" would bury every real finding.
- **`LanguageExt.Prelude` is deliberately EXCLUDED.** `Prelude.map` is overloaded across single-shot and sequence
  types, so the declaring type cannot disambiguate it; and `Prelude.Map`/`Map``2` are dictionary CONSTRUCTORS, not
  functor maps. Including it would be wrong on both counts. Do not "helpfully" add it.

All declaring-type FQNs were read out of a real store (`SELECT DISTINCT ReceiverType`), not guessed — `TypeDisplay`
is `OriginalDefinition.ToDisplayString()`, so type-parameter NAMES are part of the string (`Seq<A>`, not `Seq<T>`;
`Map<K, V>` with the space). A wrong string silently matches nothing, which is the worst failure mode available.

## Cost

No store schema change. Range variables ride in the existing `EnclosingLoopDetail`, whose `"{id} in {expr}"` shape
generalises to `"{id}[, {id}…] in {expr}"` — renderers and the identifier parser needed no special case, and the
tree reads better for it (`🔁[p, profile, activeLicense in profiles.ToList().DistinctOn...]`).
`EnclosingInvocation` gained `DeclaringType` + `LambdaParameter` inside its already-encoded string column; decode
accepts 3-field (legacy) or 5-field entries, so old stores still answer fanout/retry queries and simply find no
lambda parameter until re-indexed. The one added symbol resolution is gated behind the syntax-only lambda check, so
the common ancestor-walk case pays nothing.

## Trap found on the way

`EnclosingInvocation` is a record STRUCT, so `FirstOrDefault` on a miss yields `default` — where the positional
`= ""` defaults do NOT apply and every string field is **null**. That NRE'd 59 tests across unrelated detectors
(race_window, dual_write, allocation) because the deriver is on the shared field-access path. Normalized once at the
top of the block. Worth remembering for any future `FirstOrDefault` over these fact structs.

## Result

`Home2.GetProfilesAndActiveLicenses` now reports `n_plus_1(high)×2` at `Home2.cs:294` (context `p`,
`looped_read_with_varying_key`) — both the profile and licence per-element fetches, i.e. the exact defect the trace
pointed at. Store-wide the refined detector yields **175** `n_plus_1` findings out of 109,825 newly-visible query
call sites: the varying-key + read-provider gates carry the filtering, so the hazard view stays reviewable rather
than flooding. The enumerating-lambda arm contributed +118 raw findings while the declaring-type gate held against
76,266 Option-enclosing sites.

**Recall is NOT solved — 3 of 8 runtime-confirmed hotspots.** Measured against the preprod "N+1 hotspots" dashboard
tile, this fix covers `Admin/Profile/Home2`, `Account/Configuration/Main` and `Admin/CommonCatalogues2/Home`; five
others (incl. the worst, `HtmlEdit2` at 86.2s) stay unflagged. Those are a DIFFERENT gap — `HtmlEdit2`'s reachable
tree has 20 loop-marked nodes and 18 hazards but no `n_plus_1`, because the read is not lexically in a loop in its
own method. `n_plus_1` is intra-method by construction; cross-method amplification is tracked separately in
[n-plus-1-cross-method-amplification](../todo/n-plus-1-cross-method-amplification.md). Do not read this fix as
"rig now finds N+1s" — it closes the lexical query/lambda blind spot, nothing wider.
