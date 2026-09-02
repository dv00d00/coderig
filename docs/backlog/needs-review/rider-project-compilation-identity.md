# Rider plugin — carry project/compilation identity so linked and multi-target files stop failing as ambiguous

> **PARKED 2026-09-02** - the Rider plugin experiment is deprioritised in favour of the web view, by the product owner's explicit decision. Reopen if that decision reverses.

**Status:** todo · **Family:** rider plugin / read model
**Extracted from:** [rider-plugin-minimal-product](../done/rider-plugin-minimal-product.md) (open boundary bullet), 2026-09-02
**Triage:** needs-triage

## The problem

The file-effects request is keyed by file path alone, so a file that belongs to more than one compilation —
a **linked** file shared between projects, or a file in a **multi-target** project — is ambiguous, and the
plugin correctly fails closed. Failing closed is right; being unable to answer at all is the gap.

## What already shipped

The whole minimal product, including the fail-closed contract this card widens: missing, stale, unindexed and
ambiguous host answers all fail closed rather than guessing. Record:
[rider-plugin-minimal-product](../done/rider-plugin-minimal-product.md).

## The store-side constraint

Multi-TFM extraction is currently **single-compilation**: `SolutionSourceLoader.PreferredResult` picks the
first result with sources (deterministically the first declared TFM), so members and call sites behind
`#if NET8_0` exist only in the non-chosen compilation and are absent from the store. So carrying compilation
identity in the request can disambiguate *which* answer is meant, but the store may only hold one of them.
That half is held for review as
[multi-tfm-union-extraction](../needs-review/multi-tfm-union-extraction.md) — link, do not duplicate.

## What counts as finishing

- The request carries project/compilation identity alongside the file path, and the host answers per
  compilation.
- A linked file open in the context of project A returns A's rows.
- A multi-target file returns the rows of the compilation the store actually holds, and **discloses** that
  the other target framework is not represented — not silence, and not a fabricated answer.
- The existing ambiguous-fails-closed tests still hold for the cases that remain genuinely ambiguous.
