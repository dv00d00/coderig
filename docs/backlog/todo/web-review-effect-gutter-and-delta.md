## Web Review: the effect gutter says the wrong thing, and the delta is not rendered at all

**Status:** todo · **Family:** web review / file effect lens

**Source:** dogfooding the review surface against a real 104-file MedDBase merge request (portal courses
collapsed onto service-credits-overrides), 2026-09-02. Six reader complaints from the author, then two
independent design consultations. Every code claim below was verified against the source at the time of
writing; line numbers are `src/Rig.Cli/WebClient/src/file-diff.{tsx,css}` unless stated.

### What the reader actually hit

`AppointmentBookingBase.cs` in that MR carries 175 base-side and 180 head-side marks — 294
`.rig-diff-effect-mark` elements in the DOM at once. At that density the gutter stops being an annotation
and becomes noise. Six separate problems, all reproduced:

1. **File tree nodes are not collapsible.** `wwwroot/components.js:1547` renders each directory as a plain
   `<div class="review-tree-folder">` holding a decorative `⌄` glyph, with no click handler and no
   open/closed state. The chevron promises collapsibility that does not exist. 104 files, nested 6 deep.
2. **Badge encoding is dense and under-explained** (see the model section below).
3. **No full-file view.** `Web/FileDiffEndpoint.cs` hardcodes `Content: ""` on all three revision branches;
   the server ships `git diff -U20` (`ContextLines = 20`) and nothing more, so the client has no text to
   expand into. The DTO field exists and is never populated.
4. **No line-wrapping setting.** The settings gear (`file-diff.tsx:443`) has exactly two controls:
   unified/split, and hide-whitespace. C# here runs past 200 chars on generic signatures.
5. **No full-screen mode.** The diff shares the viewport with a 400px sidebar and ~140px of header chrome.
6. **Split view renders badges over the code.** `.rig-diff-marks` lays out 1180px wide inside the narrow
   split gutter `<td>` whose neighbouring code cell starts at x=479 — 84 of 163 mark rows physically
   overlap code text. Unified escapes only because its gutter is wider. Marks *are* present on both sides
   (79 base-gutter, 84 head-gutter), so this is containment, not missing annotation.

### The model, stated correctly

Worth writing down because two readers in a row guessed it wrong from the rendering:

- `●family` vs `○family` — `nearestDepth === 0` (the effect is in this call's body) vs reached through
  callees. `file-diff.tsx:239`.
- The trailing digit is **`nearestDepth`, call distance — not a count**. `file-diff.tsx:241`.
- `?` is `viaDispatchOnly`; `⟳` is `looped`. `file-diff.tsx:242`, `:239`.
- `byLine` collapses to **one entry per family per line**, keeping the nearest/strongest basis
  (`file-diff.tsx:209`). A line calling forty repositories shows a single `db`. Effect *volume* is not in
  the model.
- `+N` is an **overflow counter** from `insight.effects.slice(0, 2)` — "N more effect families"
  (`file-diff.tsx:248-256`). It is not a delta.

### The finding that reframes the card

`oldLines` and `newLines` are both computed (`file-diff.tsx:310-311`) and **never compared**. No per-line
base-vs-head delta is rendered anywhere; the only delta in the UI is the file-header `effect sites +N`
chip. The premise of the review surface — *this line now reaches effects it did not before* — is the one
thing the gutter does not say.

And the fix is not a line-paired diff. An inserted row has no base counterpart, so a line-level delta marks
every effect on every inserted line as new. In a 104-file MR the lines a reviewer reads are inserts, where
"new" only restates the row tint. A line-paired delta is honest on context rows and vacuous on the rows
that matter.

**`FileEffects.methods` is the unused lever.** `FileEffectMethod { id, name, signature, line, endLine,
effects }` is declared at `file-diff.tsx:32-39`, carried on both revisions, and referenced exactly once —
its own type declaration. It is never consumed. The `id` is stable across rename and reflow, so matching
methods by `id` and diffing the family sets gives a delta that survives a rewritten method body. Paint the
method header once (`+db ▲`) and let line marks show *which site* introduced the family. The same join
applies to `findings`, which today have no delta either: a hazard present in both revisions is not news.

Also missing from `delta()` reasoning generally: a family whose `viaDispatchOnly` hardens into a proven
edge, or whose `looped` turns on, is not a depth change. A db call that is *newly inside a loop* is the
amplification failure mode rig exists to catch, and any delta model keyed on depth alone reports it as
unchanged.

### Design direction

Two consultations converged on gutter geometry and split on editorial policy.

**Agreed — a fixed-width lane.** Effect marks move out of the free-flowing inline row into a fixed-slot
lane inside its own clipping `<td>`: constant width, one slot per family, family identity carried by
*position* rather than hue. Variable-width pills reflowing and reordering per row are what make 294 marks
read as noise, and position is what turns the column into a stripe down the file. It also ships item 6 for
free, and it is *cheaper* than today: `file-diff.css:311-314` pins the gutter at `width: 12rem;
min-width: 12rem` (192px per side), so an ~88px lane returns ~104px per side. The horizontal-budget
objection to a lane is inverted.

Constraint on the lane: whatever marks a slot as *moved* must not displace directness. Filled-vs-hollow
and the depth superscript are what separate "this line writes the DB" from "something five calls down
does", and dropping them on moved slots loses the distinction on exactly the rows that earned ink.

**Contested — suppressing unchanged reach.** One consultation proposes rendering rows whose reach did not
move at zero ink (~294 marks down to ~14). The other rejects it for code review: unchanged reach is the
*context* for changed reach. An inserted `Repo.Fetch` is only visibly an N+1 if the unchanged `db¹` on the
`foreach` above it is still on screen, and a missed side effect is the failure mode the tool exists to
prevent. Grey-recedes/amber-pops already buys most of the calm without hiding anything. **Open — needs a
call.**

**Open decision — the amber collision.** The proposal gives "reach" amber on the argument that the diff
already owns red and green. Inside rig amber is already spent four times: `--warn` at `file-diff.css:10`,
`:111`, `:154`, and amplification's own `#e6a84a` at `:388` (anchor violet `#d28bff` at `:382`). `--warn`
resolves to `#d29922`/`#7d5300` (`wwwroot/index.html:20,40`), which is indistinguishable from the proposed
reach amber. Either retire `--warn` from the diff chrome or give reach a different hue — four meanings on
one colour is not a system. Syntax highlighting is not itself a collision (no `--sx-*` is amber), but
`--sx-string` red and `--sx-number` green do sit on tinted rows, so "the diff owns red/green" was never as
clean as stated.

### Ranked work, impact per unit effort

1. **Method-level delta** via the unused `methods[]` — match by `id`, diff family sets, paint the method
   header plus the introducing site. Line-pair delta only for context rows. (S–M, no backend change.)
2. **Lane geometry** in a clipping `<td>`: grey unchanged, amber moved, fill and depth superscript
   preserved on moved slots. (M. Closes item 6.)
3. **Line wrapping** with hanging indent — `pre-wrap` + `overflow-wrap: anywhere`. (XS.) Ranked above the
   tree because split view is unusable without it: 1440 − 400 sidebar − 384 gutter leaves ~51 chars per
   pane, and no gutter placement rescues a 200-char generic signature.
4. **Define `--ok`**, and fix the narrow-viewport rule. `var(--ok)` is referenced 7× in hand-written source
   (`file-diff.css:101,132,171,172`; `wwwroot/index.html:2420,2554,2588`) and **defined nowhere**, while
   `--muted`/`--accent`/`--warn`/`--danger` are defined for both themes at `index.html:17-43` — so `+`
   addition counts, the `A` status pill and checked *Viewed* all render unstyled. Separately,
   `file-diff.css:472` hides `.rig-diff-effect-mark span:nth-child(2)`/`(3)`, but the `⟳` span is
   conditional and *first* (`file-diff.tsx:239`), so on a looped mark the rule hides the dot and the family
   word instead of the family word and the depth digit. (XS.)
5. **Collapsible tree** with real open/closed state, per-node file counts, path compression for
   single-child chains, and a per-node reach delta once item 1 lands. (S.)
6. **Delta on `viaDispatchOnly` and `looped`, and on hazards** — not just depth. (S.)
7. **Loading honesty** — paint effect marks as soon as they are known instead of holding the row behind
   `tiers 1–3 loading…`, which on a monolith-sized store reads as broken for seconds. (XS.)
8. **Fold-bar expansion with a ranged fetch** for surrounding context, in place of populating `Content`
   with whole-file text. Both consultations rank full-file last and one argues `-U20` already covers most
   review reading. (M.)

### Acceptance

- A method whose reached family set changed is marked on its header in both unified and split, and the
  mark survives the method being renamed or moved within the file.
- A method whose body was rewritten but whose family set did not change is not marked as changed.
- A family whose reach becomes loop-carried, or whose dispatch-only edge becomes proven, reports as
  changed.
- No mark overlaps code text at any viewport width, in either view type, with the sidebar at any width.
- An added file does not paint every mark as new.
- Wrapped long lines keep marks aligned to their logical line.
- Every colour used by the gutter resolves to a defined token in light, dark and system.

### Out of scope

- Posting to GitLab/GitHub, or any remote provider URL in the review surface (unchanged from
  `../progress/web-review-impact-deep-links.md`).
- A second effect model for review. The gutter consumes the same projection as File view and
  `rig annotate`; `Web/FileDiffEndpoint.cs` routes through `FileEffectsQueryService.BuildResidentAsync`
  deliberately and that must not fork.

### Related

- `../progress/web-review-impact-deep-links.md` — the Impact → Review pivot, and the no-dead-link rule.
- The source-generator pseudo-path guard in `Web/FileDiffEndpoint.cs` (`RelativeFileMap`,
  `LoadReviewInventoryAsync`): MedDBase stores carry project-relative `.g.cs` paths with no location on
  disk, which resolved against the serve process directory and failed the whole review inventory closed.
  Fixed alongside this card. CodeRig's own repo has no source generator, which is why dogfooding missed it.
