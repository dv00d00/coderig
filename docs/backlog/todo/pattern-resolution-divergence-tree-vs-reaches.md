# Seed pattern resolution DIVERGES between `tree` and `reaches` — same pattern, one resolves, the other says "No symbol matches"

**Status:** todo · **Priority: HIGH** (makes `tree` unusable for symbols `reaches` handles; the user cannot tell "not indexed" from "wrong resolver") · **Found:** 2026-07-27 (reviewing MedDBase MR !11025, seeding on `DocumentPreviewBuilder.Get`) · **Family:** CLI / seed resolution
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
