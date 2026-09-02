# Encode the parity preset and its read framing in the rig skill

**Status:** todo · **Family:** reviewer-invokable queries · agent skill
**Extracted from:** [reviewer-invokable-queries](../done/reviewer-invokable-queries.md) (item 1's stated remainder), 2026-09-02
**Triage:** ready-for-agent

## What was decided, and what is left

The parity capability shipped as `rig effects-diff` with kind-labelled rows. Two decisions were taken with
it: **no `parity` rename** and **no baked-in preset** — rig stays a composable primitive, so the opinionated
preset lives in the **rig skill**, not in the command.

The remainder is exactly that: encode the preset plus the read framing in the skill so a reviewer invokes
one step instead of assembling filters. Record:
[reviewer-invokable-queries](../done/reviewer-invokable-queries.md).

## The preset, verbatim from the decision

```
--only permission --only llblgen:write/bulk_write/delete --only audit
```

Plus the read framing: symmetric difference of two EPs' reachable effects AND the guards/asserts on the path
(guards are captured as `permission:assert` effects, so `effects-diff` already diffs them). The worked
example to carry: SmartLetter `SaveLetter` vs `PrintLetter` — `SaveLetter` asserts `CanModifyDocuments` and
writes `AuditLog`; `PrintLetter` checks `CanViewDocuments`, which `SaveLetter` skips.

## Where it goes

The canonical skill is **in this repo** at `.claude/skills/rig/` (`SKILL.md` + `REFERENCE.md`). Edit the
repo copy; the installed copy at `~/.claude/skills/rig/` is disposable and is refreshed by a CLI copy that
deletes the destination first. Never hand-edit the installed copy.

## What counts as finishing

- The preset and framing are in the repo skill, phrased as a reviewer step, not as a flag list.
- The skill does not claim rig has a `parity` command — it does not.
- The provider tokens in the preset are named as MedDBase-ruleset vocabulary, not as rig behaviour (the
  skill/docs half of this was finding F8 of
  [core-purity-project-vocabulary](../done/core-purity-project-vocabulary.md)).
