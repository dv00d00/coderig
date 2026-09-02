# Fact-layer loop-nesting depth would retire the span-containment heuristic

**Status:** todo · **Priority: LOW** (deliberately deferred: it orphans every existing store) · **Family:** extraction / schema
**Extracted from:** [nonlinear-amplification-degree](../done/nonlinear-amplification-degree.md) (follow-up) and
[amplification-context-propagation](../done/amplification-context-propagation.md), 2026-09-02
**Triage:** needs-info (a schema bump is a product decision, not an implementation detail)

## The gap

The store records only the **innermost** loop per call site: `EnclosingLoopKind` is a single value, and there
is no nesting depth and no parent-loop id at the fact layer. So intra-method nesting has to be recovered by
**line-span containment** — group a method's edges by `LoopDetail`, take `[min(Line), max(Line)]` as each
loop's span, and treat B as nested in A when B's span is strictly contained in A's. That contribution is
tagged `~`, never `✔`.

Span containment earns its place: on MedDBase, 1,290 methods have ≥2 distinct loops and only **240 (18.6%)**
have a genuinely nested pair, so a naive distinct-loop count would overcount 81.4% of them. Ground truth
both ways: `InvoiceEntity.RecalculateTotal`'s two loops are siblings → degree 1;
`Appointment.BuildScheduleServicesCache` has lines 381–397 strictly inside 375–414 → degree 2.

## The residual imprecision it cannot fix

Two SIBLING loops with the *same* `LoopDetail` text separated by a third loop merge into one span that then
appears to contain the third. Rare, and it is exactly why the intra-method contribution is `~`. A second
residual: the LINQ **query fold** (union-find over `IterationContext.LoopIdentifiers`) also folds a genuine
multi-`from` cross product, because the facts cannot separate a cross-product `from` from a `join`/`let`.

## Why it is deferred, not scheduled

A parent-loop id per reference is a **write-side schema bump**, and a bump **orphans every existing store** —
including the ~2 GB MedDBase store. The parent cards defer it on that basis, twice.

## What counts as finishing

- A decision that the bump is worth an estate-wide re-index.
- If taken: parent-loop id (or nesting depth) per reference; the span-containment path retired rather than
  left beside it; the `~` tag disappears from the intra-method contribution or is redefined.
- The two ground-truth methods above keep their degrees, and the sibling-versus-nested discrimination is
  re-measured on the new facts.
