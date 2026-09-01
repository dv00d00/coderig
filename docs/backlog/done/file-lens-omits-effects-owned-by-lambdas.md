# The file lens omits every effect owned by a lambda — 24% of MedDBase effects

**Status:** done · **Found:** 2026-08-31 quantifying the Rider
plugin's blind spots; re-confirmed 2026-09-01 by a probe agent on 30 files · **Family:** file lens (read model)

## Outcome

The shared read model now follows extraction-preserved `methodGroup`/handoff ownership edges from synthetic
lambdas to the outer declared method or accessor, folds the effect owner there, and keeps the lambda's physical
line. The store-backed path is covered without loading lambda symbol rows; nested and property/getter cases are
synthetic regressions. Web, CLI and Rider transport all consume this same projection.

## What happens

`FileEffectReadModelIndex` filters both its method rows and its call-site rows to the file's DECLARED methods:

```csharp
// Rig.Domain/Functions/FileEffectReadModelIndex.cs
:161  var fileMethodIds = fileMethods.Select(m => m.SymbolId).ToHashSet(...)   // SelectCanonicalMethodFacts
:284  .Where(edge => fileMethodIds.Contains(edge.Caller))
:296  .Where(effect => effect.EnclosingSymbolId is not null && fileMethodIds.Contains(effect.EnclosingSymbolId))
```

An effect inside a lambda is keyed to the lambda's synthetic id (`M:Ns.Type.Method()~λ0`), which is a call-graph
node but is NOT in `fileMethodIds`. So the effect is dropped from the file model entirely: no method badge, no
line badge — in Rider, in the browser lens, and in `rig annotate`.

Scale on the MedDBase store: **9,055 of 37,831 effects (24%)**, of which 6,676 are `llblgen`. The idiom is
pervasive in this codebase — `LinqMethods.WithDb(db => …)`, `DBApp.WithTransaction(db => …)` — so whole
repository methods whose entire body is a lambda show inflated depths or nothing at all.

Clearest confirmed example: `MailSendReceive.IO/Repositories/EmailRepository.cs:50` — `entity.Save(db)` inside
a `WithDb` lambda gets no line badge, while `rig reaches` reports it as a genuine `d1` effect of the enclosing
`Save`. Same shape in `JobTitlesRepository.Query`.

The consequence for an agent or a reader: **absence of a badge is not evidence of no I/O**, which undermines
the one question the lens is for.

## Decision: fold lambdas onto their owning declaration

- **O1 fold lambdas onto their owner** (chosen): canonicalise `…~λN` to its declaring member when building
  `fileMethodIds` and when keying rows — the precedent is `CollapseInstantiations` / `MonomorphizedNodeId.BaseOf`
  (`:198`), which already folds `~mono` ids onto their base for exactly this reason. A lambda's effects then
  belong to the method that declares it, at the lambda's own source line (which is inside that method's span,
  so the line anchor stays honest). Also fixes `P:Type.Prop~λN` rows, which are cosmetically property-keyed.
- **O2 disclose only** (rejected) — print a per-file count of lambda-owned effects that could not be anchored. Cheap,
  honest, and leaves the badges wrong.
- **O3 add lambda declarations to the file's method set** (rejected) as their own rows. Truthful but noisy: the browser and
  the CLI would list `Save~λ0` beside `Save`.

O1 matches how the depth math already treats a lambda (the methodGroup edge into the lambda is
enclosed by the declaring member, per the accessor fix in `CLAUDE.md`'s effect↔reachability section).

## Testing expectations

- Read-model test: an effect inside a lambda declared in method `M` produces a method row for `M` and a
  call-site row at the lambda's line.
- Nested lambdas (`λ0~λ1`) fold to the outermost DECLARING member, not to the parent lambda.
- A lambda in a property body folds to the ACCESSOR (`M:get_X`), never to `P:X` — `P:` is not a call-graph node.
- Real-store A/B: total file-model effect count before/after on the MedDBase store, plus the
  `EmailRepository.cs:50` and `JobTitlesRepository.Query` cases.
- Bump the file-effects cache schema
  ([no schema constant today](./file-effects-artifact-has-no-cache-schema-constant.md)).

## Out of scope

Field/auto-property initializer effects (`F:` and non-lambda `P:` owners, ~24 effects) — the correct owner is
`.ctor` and it is a separate extraction question, already recorded in `CLAUDE.md`.
