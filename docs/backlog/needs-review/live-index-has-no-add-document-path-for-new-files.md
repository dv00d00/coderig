# A NEW file is silently omitted from a routed live answer — `ResidentIndex` has no add-document path

**Status:** todo · **Priority: HIGH** (a silent omission in an answer that reads as complete) · **Family:** live index / disclosure
**Extracted from:** [live-background-index](../done/live-background-index.md) ("Still undisclosed on a routed answer" #1), 2026-09-02
**Triage:** needs-triage

## The problem

`ResidentIndex` has no add-document path, so a file created after the host booted is not in the retained
workspace. `rig watch` discloses this rather than silently skipping — but the "not a workspace document (new
file?)" notice goes to the **HOST's console**. Once one-shot `rig` commands route to the resident index over
the pipe, a client querying after adding a file gets an answer that silently omits it. The transport widened
this from a terminal-watcher problem into a client problem.

That is exactly the failure shape the whole program treats as unacceptable: not an error, a confident wrong
answer. Compare the shipped fix for the broken-compilation case
([live-index-serves-confident-answers-from-a-broken-compilation-shipped](../done/live-index-serves-confident-answers-from-a-broken-compilation-shipped.md)),
where the live path stopped answering with a clean bill of health.

## What already shipped around it

Everything else in [live-background-index](../done/live-background-index.md), including the routed transport
and the compilation-health disclosure it carries. The "two hosts, one directory" item beside this one was
decided 2026-08-22 (refuse to boot). This one was left explicitly undisclosed on the client.

## What counts as finishing

Either arm is acceptable, in this order of preference:

1. An add-document path so a new file joins the retained workspace and is extracted like any dirty file; or
2. the omission travels **to the client** as a disclosure on the answer, not only to the host log — so a
   routed answer can never read as complete while a known file is missing from it.

Acceptance: a test that adds a file to a running host, queries through the transport, and asserts either the
new file's facts are present or the answer carries the disclosure.
