# `rig amplify` — interior hops are their own findings, so the top of the list is near-duplicates

**Status:** todo · **Family:** amplification / finding grain
**Extracted from:** [nonlinear-amplification-degree](../done/nonlinear-amplification-degree.md) (follow-up), 2026-09-02
**Triage:** needs-triage

## The problem

A degree-3 chain also yields its degree-2 tail, because the degree DP emits per anchor call site and every
interior hop of a long chain is itself an anchor. So the top of the ranked list carries near-duplicates
differing only in hop 1 — the reader re-reads the same quadratic path two or three times before reaching a
distinct finding.

Measured shape on `2f944e739e47-dirty`: degree 2: 509 · 3: 82 · 4: 33 · 5: 28 · 6: 7 · 7: 4. The higher the
degree, the more tails it contributes.

## What already shipped

The whole `rig amplify` command and its calibration, including the deliberate per-CALL-SITE grain (rig's
existing rollups dedupe per method, which would have collapsed two distinct looped call sites in one method
into one finding). Record: [nonlinear-amplification-degree](../done/nonlinear-amplification-degree.md).

## The fix the parent card names

**Dedupe by chain tail** would compress it. Note the constraint that per-call-site grain exists to protect:
the dedupe must not collapse two genuinely distinct anchors that happen to share a tail into one finding —
it must suppress a chain that is a strict suffix of a reported chain, and say so in the output rather than
dropping it silently.

## What counts as finishing

- A suffix-suppression rule with the retained finding disclosing how many tails it absorbed.
- Before/after counts on the same store, and the four explicit degree≥2 ground-truth families still
  rediscovered.
- Ranking still comes from configured categories, not from C# (the core-purity correction on that card).
