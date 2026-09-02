# Generated documents are not re-extracted per file — generator facts stay at base until a cold boot

**Status:** todo · **Family:** live index / extraction
**Extracted from:** [live-background-index](../done/live-background-index.md) ("Still undisclosed on a routed answer" #2), 2026-09-02
**Triage:** needs-triage

## The problem

The per-file re-extraction path skips the generator pass, so **generator-emitted facts stay at the base
generation until a cold boot**. Edit a file whose source-generated output would change and the live index
keeps answering from the pre-edit generated facts. Recorded twice in
[live-background-index](../done/live-background-index.md) — once in "Open, ranked" #5 and again as
"Still undisclosed on a routed answer" #2 — and undisclosed on the client either way.

The adjacent generated-document gap is already carded elsewhere and should not be duplicated here:
generated documents' **diagnostics** are not observed (`RunSourceGeneratorsAsync` builds a compilation nobody
calls `GetDiagnostics` on, and the driver's `diagnostics: out _` still discards them) — spec row 7a of
[failed-compilation-disclosure](../todo/failed-compilation-disclosure.md).

## Why it matters on the real target

MedDBase stores carry project-relative `.g.cs` paths with no location on disk; source-generated proxies are
real graph content there, not a curiosity. A stale generated fact is the same class of failure as a missing
file: the answer looks complete.

## What counts as finishing

Either arm, in this order of preference:

1. Re-run the generator pass for affected projects on a dirty-file batch, so generated facts advance with
   the generation; or
2. disclose staleness of generator-emitted facts on the answer — including a routed answer to a client, not
   only in the host log.

Acceptance: a fixture project with a generator, an edit that changes its output, and an assertion that the
live answer either reflects the new generated facts or discloses that it cannot.
