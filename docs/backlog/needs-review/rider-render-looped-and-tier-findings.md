# Rider plugin — render `Looped` plus tier-1/2/3 findings in the designed budget

> **PARKED 2026-09-02** - the Rider plugin experiment is deprioritised in favour of the web view, by the product owner's explicit decision. Reopen if that decision reverses.

**Status:** todo · **Family:** rider plugin / read model
**Extracted from:** [rider-plugin-minimal-product](../done/rider-plugin-minimal-product.md) (open boundary bullet), 2026-09-02
**Triage:** needs-triage

## The gap

The plugin renders effect families today; it does not render `Looped` or the tier-1/2/3 findings, even though
the gutter / Code Vision / inline **budget for them is already designed**. So the amplification signal that
the web file lens shows is invisible in the editor.

## What already shipped

One visible-file request renders Code Vision, a true gutter mark and an inline call-site hint from the same
semantic rows, with SQL and file-system effects as separate read-model families (a method can render both,
using Rider's database-query and folder glyphs). Record:
[rider-plugin-minimal-product](../done/rider-plugin-minimal-product.md).

## Prior art to mirror, not re-derive

The web review surface already settled this presentation problem at density: a fixed eight-slot lane, direct
versus reached as filled versus hollow, dispatch uncertainty retained, loop amplification as a lower edge,
and tier-2 findings worded as *an effect inside iteration* while tier-3 findings are worded as *downstream
reach from an iterating call* — **candidate wording, never a claim of runtime N+1, query count or polynomial
degree**. See [web-review-effect-gutter-and-delta](../done/web-review-effect-gutter-and-delta.md).

Also carried from that work: CodeRig's own rules do not enable tier 3, so a store can legitimately answer
`crossMethodAvailable: false`. The editor must render that as "not enabled", not as "none found".

## What counts as finishing

- `Looped` plus tier-1/2/3 findings render within the already-designed budget, with the same candidate
  wording as the web surface.
- A disabled or unavailable cross-method tier is disclosed, not silently empty.
- No second effect model: the plugin keeps consuming the same projection as the file lens and `rig annotate`.
- Density is bounded — the web surface hit 294 marks in one DOM before it needed the fixed lane; the editor
  budget must be stated, not discovered.
