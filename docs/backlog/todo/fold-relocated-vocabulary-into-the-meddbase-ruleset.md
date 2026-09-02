# Fold the relocated project vocabulary into the MedDBase ruleset (order trap)

**Status:** todo · **Family:** core purity / rules data
**Extracted from:** [core-purity-project-vocabulary](../done/core-purity-project-vocabulary.md), 2026-09-02
**Triage:** ready-for-human (edits a file in `c:/git/meddbase-analysis`, outside this repo)

## What already shipped

Findings F1–F6 and F8 of [core-purity-project-vocabulary](../done/core-purity-project-vocabulary.md): the
LLBLGen/Echo/entity-cache vocabulary is out of rig core and out of the shipped `builtin-rules.json`, each
section degrades neutrally when absent, and real-store parity was proven byte-identical
(`rig derive --format tsv`, 18.5 MB, plus `rig amplify`) against the `cb780b68` baseline **with the relocated
rules in place**.

## What remains

The relocated content is still accumulated in **one overlay file** and layered with a second `--rules`. It
has to be folded into `c:/git/meddbase-analysis/rig.rules.json` so the real store stops depending on an extra
flag.

## The trap that makes this more than a paste

Effect matching is **first rule wins in cascade order**, and `--rules` extras always load LAST. The
relocated `actor:*` effect rules used to load from `builtin-rules.json`, i.e. ahead of everything
project-side, so they **must be spliced at the TOP of that file's `effects` list — before its `echo_publish`
rules**. Appended at the end, measured on the real store: **718 effect rows + 219
`cross_method_amplification` rows reclassify and 17 actor amplification findings vanish.**

The `amplify` categories overlay (`amplify-categories.rules.json`, from the 2026-08-28 sweep scratchpad) is
in the same position and folds in the same way — core ships no default categories, so absent config means one
implicit group, no weighting, no separate sections.

## What counts as finishing

- The overlay content lives in `c:/git/meddbase-analysis/rig.rules.json`, `actor:*` effect rules first.
- `rig derive --format tsv` and `rig amplify` from `c:/git/meddbase-analysis` with **no extra `--rules`** are
  byte-identical (SHA-256) to the current layered output.
- Neither `rig` nor its builtin rules change; this is a store-side config change only.
