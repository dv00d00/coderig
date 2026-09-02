# Resident host memory grows across a session — 10.8 GB at boot, ~19 GB after one edit + reconcile

**Status:** todo · **Priority: HIGH** (the parent card calls it "the biggest risk to a genuinely long-lived process") · **Family:** index performance / resident host
**Extracted from:** [live-background-index](../done/live-background-index.md) ("Open, ranked" #2 plus the interning section), 2026-09-02
**Triage:** needs-info (a GC-policy-versus-periodic-reboot decision, not a defect with one fix)

## The measurement

On the 226/227-project MedDBase solution the resident host is **10.79 GB at boot and 19.11 GB after one edit
plus one reconcile**. Slice 2 released the live set, but the working set does not return: ServerGC/DATAS
keeps the segments. So a long session needs either a GC policy decision or a periodic re-boot. Recorded as
`Open, ranked` #2 in [live-background-index](../done/live-background-index.md) and unresolved there.

This number is already load-bearing elsewhere: it is the decisive argument for why a second `rig watch` in
one directory **refuses to boot** (two by accident is 21–38 GB on a 64 GB box), a decision taken 2026-08-22.

## The open half of the interning result

String interning was measured 2026-08-22 and the **verdict is keep** (+6.5% on a once-per-session cold boot
against −14.6% of a continuously-paid live set). What is still open is the **many-edits growth curve**: an
interner's real job is that a re-extracted generation's strings ALIAS the base generation's rather than
duplicating them — a brake on GROWTH. One edit cannot see it (both arms move +0.06–0.07 GB). A 10-edit
series on both arms would quantify it. This decides how much MORE interning is worth over a session, not
whether to keep it.

## What counts as finishing

- The 10-edit growth series run on both arms, interleaved, with the machine quiet — the parent card's
  measurement lessons apply verbatim.
- A recorded decision between a GC/DATAS policy change and a documented periodic re-boot, justified by that
  curve.
- Whatever is chosen, the host's own status output discloses it, so a long-running host never looks healthy
  while heading for the box's limit.
