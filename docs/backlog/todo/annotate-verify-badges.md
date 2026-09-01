# Design `rig annotate --verify` without comparing unlike depth quantities

**Status:** todo · **Found:** 2026-09-01 while auditing the file lens ·
**Triage:** needs-info
**Family:** CLI / reachability verification

**Depends on:** [the documented file-lens depth convention](../done/file-lens-method-depth-disagrees-with-reaches.md).

## Problem

Auditing a file badge currently requires a separate `rig reaches` query per method. A one-command verification
mode would be useful, but raw depth equality is invalid: the editor-facing file lens folds method-group hops
into lambdas, while `reaches` counts physical graph edges.

## Decision required

Define what verification promises before building it. The minimum honest contract is family-membership parity
for each rendered method. If it also compares distance, specify a normalization that preserves the deliberate
lambda convention and one-hop dispatch semantics rather than subtracting a global offset.

## Testing expectations

- Synthetic ordinary-call, one-lambda, nested-lambda, dispatch and direct-effect fixtures.
- A mismatch is additive disclosure and never changes the underlying file-lens answer.
- Verification reuses the existing forward engine; it must not create a third reachability implementation.

## Out of scope

Changing the file-lens depth convention merely to make raw numbers equal.
