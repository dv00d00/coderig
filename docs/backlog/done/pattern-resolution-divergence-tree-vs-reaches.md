# Seed resolution disclosure — `reaches`/`path` could not distinguish "no such symbol" from "reaches nothing"

**Status:** ✅ FIXED 2026-07-27. **RETITLED** — the reported `tree`-vs-`reaches` divergence does NOT exist;
the resolvers were always shared. The real defect was the opposite of the report: `tree` was correct and
`reaches` fabricated a plausible zero. See *Investigation outcome*.

**Original status:** todo · **Priority: HIGH** (makes `tree` unusable for symbols `reaches` handles; the user cannot tell "not indexed" from "wrong resolver") · **Found:** 2026-07-27 (reviewing MedDBase MR !11025, seeding on `DocumentPreviewBuilder.Get`) · **Family:** CLI / seed resolution
**Related:** [[cli-ux-file-paths-and-boundaries]] (UX cluster)

## The observation

Store `de69fd2ffc6b-dirty`. Three commands, one symbol set, three inconsistent outcomes.

```bash
# (1) reaches + SHORT pattern → resolves, warns about multiplicity, returns a full answer
rig reaches "DocumentPreviewBuilder.Get"
#   note: pattern 'DocumentPreviewBuilder.Get' matched 3 distinct symbols
#         (…DocumentPreviewBuilder.Get, …GetImageHtml, …GetUnsafe) — results span ALL of them
#   Reachable methods: 668 · Direct effects: 925

# (2) tree + the SAME short pattern → hard failure
rig tree "DocumentPreviewBuilder.Get" --view effects
#   No symbol matches 'DocumentPreviewBuilder.Get'.

# (3) tree + FULL FQN → hard failure
rig tree "MedDBase.ServiceLayer.Document.DocumentPreviewBuilder.Get" --view effects
#   No symbol matches 'MedDBase.ServiceLayer.Document.DocumentPreviewBuilder.Get'.

# (4) reaches + FULL FQN → resolves but returns an EMPTY answer
rig reaches "MedDBase.ServiceLayer.Document.DocumentPreviewBuilder.Get"
#   From: MedDBase.ServiceLayer.Document.DocumentPreviewBuilder.Get
#   Reachable methods: 0 · Direct effects: 0
```

Control: `callers` accepts a full FQN fine —
`rig callers "MedDBase.ServiceLayer.Document.DocumentPreviewBuilder.GetUnsafe"` → 18 methods, correct chain.

The symbol is real and indexed: it is `src/main/MedDBase.ServiceTier/Document/DocumentPreview.cs:29`, a
`public static string Get(int, string, bool?, string, Guid?, ITransaction = null)` plus a one-arg overload
`Get(DocumentEntity)` at `:56`.

## Two problems

1. **`tree` and `reaches` use different seed resolvers.** A pattern good enough for `reaches` must be good
   enough for `tree`; today the user has to guess per command. Suspect `tree` requires a unique match and
   reports "No symbol matches" when it actually means "ambiguous / >1 match" — a misleading message either
   way.
2. **Exact-FQN match on an OVERLOADED method silently selects an empty node.** (4) resolves the param-free
   FQN but yields 0 reachable / 0 effects, while the substring form yields 925 effects. Per SKILL.md,
   "EXACT MATCH WINS" for a `M:`-stripped param-free FQN — but with two overloads, that key is ambiguous and
   appears to bind to a node with no out-edges. A silent empty result is the worst outcome: it reads as
   "this method does nothing."

## Acceptance

- `tree`, `reaches`, `callers`, `path` share ONE seed resolver with identical accept/reject behaviour.
- Ambiguity is reported as ambiguity ("matched N symbols: …; pass a fuller pattern or `--pick`"), never as
  "No symbol matches".
- Exact param-free FQN with multiple overloads either spans all overloads (like the substring path) or
  errors listing them — never silently binds one and returns 0.
- A resolved seed with genuinely 0 out-edges should say so explicitly ("resolved to X; no call edges — check
  it is a `M:`/accessor/lambda/ctor node") to distinguish it from a bad pattern.

---

## Investigation outcome (2026-07-27)

### The reported divergence is NOT REAL — third false report from a stale `.rig/LATEST`

`tree` and `reaches` have ALWAYS shared one resolver: `FactPathFinder.MatchNodes`, documented in-source as
"shared by every seed site (tree/reaches/callers/path roots + path target)", with EXACT-MATCH-WINS and
overloads collapsing by param-free FQN.

All four reported cases succeed against the HEAD store, and all four reported FAILURES reproduce exactly
against the BASE store — where `DocumentPreviewBuilder` **does not exist at all** (`rig symbols … → 0 shown`);
it is new in the MR branch:

| case | head store | base store |
|---|---|---|
| `reaches "DocumentPreviewBuilder.Get"` | 668 reachable, 33 effects | 0 / 0 |
| `tree "DocumentPreviewBuilder.Get"` | 31 effectful methods | `No symbol matches` |
| `tree "<full FQN>"` | 31 effectful methods | `No symbol matches` |
| `reaches "<full FQN>"` | 668 reachable, 33 effects | 0 / 0 |

Same root cause as [[guard-condition-renderer-divergence-tsv-llm]] and the same lesson: **pin `--store`**.
Now partly mitigated — `rig runs` no longer aborts on stale stores, so the store set is inspectable.

### The REAL defect, inverted from the report: `tree` was right, `reaches` fabricated a zero

`tree` and `callers` check `roots.Count == 0` and report `No symbol matches`. `reaches` and `path` had **no
such check** — they passed the raw pattern into the traversal, so an unmatched pattern produced an empty reach
set indistinguishable from a resolved leaf. For a symbol that does not exist, `reaches` printed a confident:

```
From: MedDBase.ServiceLayer.Document.DocumentPreviewBuilder.Get
Reachable methods: 0 · Direct effects: 0
```

This item's own acceptance called that out — *"A silent empty result is the worst outcome: it reads as **this
method does nothing**"* — and it is what let a store mix-up masquerade as a resolver bug for a whole session.

### Fix — FOUR distinguishable outcomes (`SeedResolutionNotice`, sibling of `AmbiguityNotice`)

1. **No match** → `No symbol matches '<pattern>'.`, exit 1 (tree's exact wording and code). `path` names WHICH
   endpoint failed (`(the 'from' endpoint)`) since both patterns can be the same text.
2. **Matched, zero out-edges** → exit 0, unchanged stdout, plus a stderr note
   `note: resolved to <id>; it makes no in-solution calls (0 call edges).` A leaf returning 0 is the CORRECT
   answer and must stay distinct from (1). Suppressed under `--depth 0`, where depth-0-only is the bound asked
   for, not a property of the symbol.
3. **Real symbol that can NEVER be a node** (found in review, not in the original report) → names what it found
   and points at the accessor. Nodes are methods / bodied accessors / lambdas / ctors — all `M:`; `P:`
   properties, `F:` fields and `E:` events never are. Evidence: `reaches "PerformanceLogger.Factory"` matched
   no node while `PerformanceLogger.get_Factory` reaches 16 methods, and a flat denial for a real property is
   misleading in exactly the way the old empty result was.
4. **Ambiguous** → `AmbiguityNotice`, untouched (it fired correctly throughout).

`MatchNodes` semantics were NOT touched. Exact-match-wins and overload-collapsing both tested correct, and the
overload acceptance bullet was already satisfied — the param-free FQN spans both overloads and returns 668.

### Two places the implementation deviates from the original plan, both correctly

- **`path`'s TO endpoint needs a STORE-WIDE probe, not a graph check.** `graph` is the FROM-side forward slice,
  so a `to` that exists but is simply UNREACHABLE is absent from it — a graph-only check would libel a real
  symbol as nonexistent. Verified: `GetImageHtml → PersonEventEntity.Save` still correctly reports
  `No path from …`. The graph miss only TRIGGERS the store probe, and only on the already-failed path, so the
  success path pays nothing.
- **`reaches` keys off `reachable.Count == 0`**, verified as exactly the no-match signal: seeds are added at
  depth 0 regardless of `maxDepth`, so a matched leaf yields 1 and `--maxdepth 0` on a real symbol yields 2
  (checked on the real store — a false `No symbol matches` here would be worse than the original bug).

### Verification

- Real store: the original bug now reports no-match + exit 1; a real symbol is byte-identical at 668 reachable;
  `--maxdepth 0`, `path` bad-from, bad-to, and real-but-unreachable-to all behave correctly.
- `tests/Rig.Tests/Cli/SeedResolutionDisclosureTests.cs` — 6 tests. Verified red→green: all 4 CLI-level tests
  fail with the command changes reverted; the 2 pure-unit helper tests pass either way by construction.
- Full suite 914 tests green.

### Follow-on (not done)

`ReachesQueryService` / `PathQueryService` and their `/api/*` endpoints carry the same ambiguity.
`PathQueryResult` already exposes `Matched`/`FromMatches`/`ToMatches`, so a web slice is cheap — worth an item.
