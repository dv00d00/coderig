# `rig assert` — turn a confirmed review finding into a durable CI gate

**Status:** todo · **Family:** reviewer-invokable queries · CI
**Extracted from:** [reviewer-invokable-queries](../done/reviewer-invokable-queries.md) (ranked item 4), 2026-09-02
**Triage:** needs-info (the assertion language is a product decision)

## The gap it closes

The bridge from "a reviewer spotted an invariant" to "rig enforces it forever". The parent card's own
examples:

- `assert every-ep-reaching(RecallEntity.Save) also-reaches(ActivityLog.Write)` (#56 / #1271 / #831);
- `assert no-path(<EP> → object_store:write of Option<T>)` (#1646);
- `assert no-effect-set-change` — which already exists as `--expect-no-effect-change`.

## What already shipped

`--expect-no-effect-change` is the one-instance precedent and it works; `rig effects-diff` supplies the
comparison primitive with kind-labelled rows. Record:
[reviewer-invokable-queries](../done/reviewer-invokable-queries.md).

Worth knowing before designing: the `--expect-no-effect-change` gate has already had a real regression
caught only in review, so its semantics are load-bearing and should be mirrored, not re-invented.

## The decision this needs first

The assertion vocabulary. It is a small language, so it is a product decision: which predicates exist
(`every-ep-reaching`, `also-reaches`, `no-path`, `no-effect-set-change`), whether assertions live in
`rig.rules.json` or their own file, and what exit codes and output CI consumes.

## What counts as finishing

- Assertions are data, and no project vocabulary reaches core C#.
- A failing assertion names the violating path with `file:line` per hop, so the CI failure is actionable
  without re-running rig.
- Exit code contract stated and tested.
- The three corpus examples above are expressible, and at least one runs against the real store.
