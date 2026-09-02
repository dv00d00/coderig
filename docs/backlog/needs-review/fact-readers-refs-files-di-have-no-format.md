# The raw fact readers `refs` / `files` / `di` are text-only

**Status:** todo · **Family:** CLI surface
**Extracted from:** [cli-tier1-global-flags](../done/cli-tier1-global-flags.md) (its own "NEXT SLICE"), 2026-09-02
**Triage:** needs-info (the parent card gates this on a concrete machine workflow needing it)

## The gap

`--format text|tsv` is on every query command via `CommonOptions.Format()`, and `symbols --format tsv|json`
shipped 2026-08-23 so exact agent/script discovery no longer parses human output. The remaining raw fact
readers — `refs`, `files`, `di` — are still text-only. The full per-command matrix lives in the rig skill's
`REFERENCE.md` § "Which commands accept which global options".

## What already shipped

`--format`, `--limit`, the `--max-depth` spelling with `--maxdepth` / `--depth` compatibility, and
`impact --time`. Record: [cli-tier1-global-flags](../done/cli-tier1-global-flags.md).

## The condition on this work

The parent card is explicit: **add formats only when a concrete machine workflow needs them.** So this card
is not a uniformity chore. It also records the adjacent decision so a later "make Tier-1 flags uniform" pass
does not treat it as an oversight: **`--rules` is deliberately NOT going global** — only commands whose
output is a function of the rules accept it, and a no-op `--rules` on a fact reader would be worse than
today's `Unrecognized command or argument`, because it would imply rules shaped a result they never touched.

## What counts as finishing

- A named workflow that needs one of the three in TSV/JSON, or a decision to leave them text-only.
- If built: emitters follow the existing `CommonOptions.Format()` shape, with no new flag vocabulary.
- The skill's REFERENCE.md matrix updated in the same change.
