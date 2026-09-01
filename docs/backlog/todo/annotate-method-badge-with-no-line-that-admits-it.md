# A method badge whose own lines carry no such family

**Status:** todo (largely explained) · **Found:** 2026-09-01, re-audit of the file lens ·
**Triage:** needs-triage
**Family:** file lens

## What happens

```
rig annotate "MedDBase.Pages\Document\Template\HomeComponents\ImageEdit.cs" --method Save
    @ Save  … echo:1 …
             30              Template.Save();        # no echo badge on this line
```

`Save`'s body contains exactly one call, `Template.Save()` on line 30. The method claims `echo:1`, i.e. "the
nearest echo effect is one call away", but no line in the method carries an echo badge — so the badge names a
distance through a call the lens does not mark. `rig reaches "ImageEdit.Save" --only echo` puts the nearest real
echo effect at **d3** (an `eventbus publish` inside `TemplateEntity.Save`), and `rig path` shows a 2-edge chain
to that method, so three surfaces report three numbers (1 / 2 / 3) for one effect chain.

Before the lambda fold this method read `echo:2`, so the fold moved the number without making the line agree.

## Cause 1 CONFIRMED 2026-09-01

The badge was a dispatch-derived reach. After
[the dispatch-disclosure fix](../done/file-lens-counts-dispatch-fan-out-as-a-real-badge.md), the method reads:

```
method 27 42  Save  cache:3? db! echo:2? io:12 rpc:9?
```

`echo` is now disclosed as dispatch-only, which is why no line carried it: the reach never came from a call the
lens could mark. The remaining gap to `reaches`' d3 is the lambda methodGroup hop, i.e. the documented depth
convention, not a defect.

What is still worth doing:

- Assert the INVARIANT below, so a future regression of this shape fails a test instead of an audit.
- Decide whether a dispatch-only method badge should also emit a marker on the line whose call carries the
  dispatch, rather than leaving the line silent. Silent is honest (no call is proven) but leaves a reader
  hunting for which call it was.

## Original candidate causes, kept for the record

1. **Dispatch-derived reach**, the same root cause as
   [the lens counting dispatch fan-out as a real badge](../done/file-lens-counts-dispatch-fan-out-as-a-real-badge.md):
   if `echo:1` comes from a reverse-dispatch edge attributed to `ImageEdit.Save`, the method row gets a depth
   while the line's own target (`Template.Save`) never enters the family closure, so no line badge is emitted.
   That would explain the missing line badge exactly. **Verify this first** — if true, this card is a
   disclosure problem, not an arithmetic one, and fixing the other card fixes the visible symptom.
2. **The lambda fold's depth**: the fold rewrites an effect's owner from `TemplateEntity.Save~λ0` to
   `TemplateEntity.Save`, which correctly removes the invisible lambda hop. If the rewrite is applied to the
   effect owner but the closure still walks from the lambda node, a method could end up one hop short of what
   its own edges support.

## Invariant worth asserting either way

A method's badge families should be a subset of the union of (its own lines' families) ∪ (families reachable
through calls the lens marks). Today nothing enforces that, and it is the cheap structural check that catches
this whole class — the probe agents used it by hand to find both this and
[the marked line with no owning method row](./file-lens-emits-a-marked-line-with-no-owning-method-row.md).

Add it as a read-model test over a synthetic graph AND as an optional `--strict` self-check on real files, so a
future regression surfaces without a second audit.

## Repro

```
rig annotate "…\HomeComponents\ImageEdit.cs" --method Save
rig reaches "ImageEdit.Save" --only echo
rig path "ImageEdit.Save" "TemplateEntity.Save"
```
