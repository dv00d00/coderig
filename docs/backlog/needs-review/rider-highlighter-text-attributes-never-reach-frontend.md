# Text attributes from `RegisterHighlighter` never reach the Rider frontend

> **PARKED 2026-09-02** - the Rider plugin experiment is deprioritised in favour of the web view, by the product owner's explicit decision. Reopen if that decision reverses.

**Status:** todo — negative result worth keeping · **Found:** 2026-08-31, during the Rider plugin backend spike
· **Family:** Rider integration

## What happens

`experiments/RiderBackendEffectSpike/RigEffectHighlighting.cs:12-21` declares a
`[RegisterHighlighter]` with `EffectType.GUTTER_MARK | EffectType.TEXT | EffectType.SOLID_UNDERLINE`,
`FontStyle.Bold`, and explicit foreground/effect colours (light and dark). None of that text styling renders
in Rider. Two highlighting-group arms were tried — `HighlightingGroupIds.GutterMarks` and
`HighlightingGroupIds.IdentifierHighlightings` (`RigEffectHighlighting.cs:22-30`) — and no persisted `.icls`
scheme override exists locally that could be shadowing either one. The **gutter mark itself does render**
(confirmed both in this spike and in the shipped slice — `docs/backlog/done/rider-plugin-minimal-product.md`
reports "a true gutter mark from the same semantic row"); it is specifically the inline text/underline/bold
attribute that never shows up.

The lever that did work for inline emphasis is a different mechanism entirely: an intra-text adornment —
`experiments/RiderBackendEffectSpike/RigEffectInlayHint.cs`, `IInlayHintWithDescriptionHighlighting`
(line 31) plus `IHighlighterAdornmentProvider` (line 59) — rendered as a hint token right after the call
name rather than as a text-attribute overlay on the call itself.

## Action to record

Either strip the dead `EffectType.TEXT | EffectType.SOLID_UNDERLINE` / `FontStyle.Bold` / colour attributes
back to gutter-mark-only, or keep them with a comment pointing at this card so the next person doesn't
re-diagnose the same dead end. Open question to carry forward: whether Rider requires a frontend-side
(Kotlin/RD) text-attribute *registration* — separate from the backend `[RegisterHighlighter]` declaration —
before a backend-declared text attribute can render at all, which the backend-only spike here cannot settle.
