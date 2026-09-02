## Web Review: the effect gutter says the wrong thing, and the delta is not rendered at all

**Status:** progress — method delta, inline/gutter/off presentation, focus mode and folder disclosure shipped locally 2026-09-02; findings delta and context follow-ups remain ·
**Family:** web review / file effect lens

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

### Reference markup from the design sketch

Verbatim from the design prototype, as the starting point for item 2 — a sketch to translate, not shipped
code. Family order is fixed and is the whole identity mechanism, so it must be declared once and shared with
the rollup sparkline:

```js
const FAM = [
  {k:'db',     m:'D', label:'database'},
  {k:'cache',  m:'C', label:'cache'},
  {k:'blob',   m:'B', label:'blob'},
  {k:'bus',    m:'Q', label:'bus'},
  {k:'echo',   m:'E', label:'echo'},
  {k:'io',     m:'I', label:'io'},
  {k:'rpc',    m:'R', label:'rpc'},
  {k:'search', m:'S', label:'search'},
];
```

The lane is an 8-column grid of fixed-width slots, one per family, empty slots included so position is
stable across every row:

```css
:root{ --lane-slot:11px; }              /* 8 slots = 88px, vs today's 12rem = 192px gutter */

.lane{
  display:grid; grid-template-columns:repeat(8,var(--lane-slot));
  gap:0; justify-content:start;
}
.slot{
  height:15px; display:grid; place-items:center;
  font:700 9px/1 "JetBrains Mono",monospace; color:var(--muted);
  border-right:1px solid var(--hair);
}
.slot:last-child{ border-right:0 }
.slot.on{ color:var(--fg) }
.slot.here{ background:var(--chip); box-shadow:inset 0 0 0 1px var(--border) }
.slot.below{ color:var(--muted) }
.slot.uncertain{ opacity:.62 }
.slot.moved{
  background:var(--reach-soft); color:var(--reach);
  box-shadow:inset 0 0 0 1px color-mix(in srgb,var(--reach) 55%,var(--border));
}
.slot.gone{ color:var(--muted); opacity:.5; text-decoration:line-through }
.slot.amp{ color:var(--amp) }
```

A column header labels the slots once per file, which is what makes position learnable:

```css
.lanehead{
  display:grid; grid-template-columns:repeat(8,var(--lane-slot));
  justify-content:start; border-bottom:1px solid var(--border);
}
.lanehead b{
  height:14px; display:grid; place-items:center;
  font:500 8.5px/1 "JetBrains Mono",monospace; color:var(--muted);
  border-right:1px solid var(--hair);
}
```

New tokens the language needs. `--reach` is the delta hue and `--amp` is amplification/anchor; note the
collision recorded above — `--reach` dark `#e3a93c` against the app's existing `--warn` `#d29922` is not a
distinguishable pair, so one of the two has to move before this ships:

```css
:root{                        /* light */
  --ok:#0f7b34;               /* the token the app already references 7x and never defines */
  --reach:#8a5300; --reach-soft:#fdf3e0;
  --amp:#6d28a8; --hair:#e6eaef;
}
@media (prefers-color-scheme:dark){ :root:not([data-theme="light"]){
  --ok:#3fb950; --reach:#e3a93c; --reach-soft:#2a2113;
  --amp:#c48bf5; --hair:#20262e;
}}
```

**The two lane renderers in the sketch contradict each other, and the split one is correct.** Unified
`renderLane()` pushes `moved` *instead of* the directness class, so a moved slot stops saying whether the
effect is in this body or five calls down — on precisely the rows that earned ink:

```js
// unified - WRONG: directness is only reached when nothing moved
if(x.state==='gone') cls.push('gone');
else if(x.state!=='same') cls.push('moved');
else cls.push(x.cur.d===0?'here':'below');

// split - RIGHT: directness always set, moved layered on top
const cls=['slot','on', x.d===0?'here':'below'];
if(!oth.has(f.k)) cls.push('moved');
```

Adopt the split form in both, and keep the depth superscript on moved slots.

Split-view row geometry, which is what closes item 6 — a lane per side in its own fixed-width cell, and the
delta in a centre column between the panes (Rider's change lane, carrying a signed number instead of `>>`).
`table-layout:fixed` with an explicit `<colgroup>`; the lane cell stays `vertical-align:top` so it pins to
row one when a line wraps:

```
| ln | lane 96px | code (base) | delta 46px | code (head) | lane 96px | ln |
```

Two things the sketch renders that are not in the card's scope yet but should not be lost: the rollup
**sparkline** — eight ticks in the same fixed family order, amber where that family moved — as the per-file
primitive for the sidebar and the right-edge overview strip; and `.dnum` for the signed delta chip. One
sketch bug to not copy: `.dnum.neg` and the sparkline's `i.off` paint lost reach with `var(--ok)` green,
while the language's own rule says removal renders struck-grey and never green.

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
   review reading. (M.) The user subsequently requested full-file reading explicitly on 2026-09-02;
   the agreed first slice is a lazy single-revision full-file view, not expansion of both diff panes.

### Delivered first production slice — 2026-09-02

- Method reach is compared across revisions by exact symbol id, with a fail-closed unique
  owner/signature/parameter-shape fallback for renames. Added and removed methods inside a comparable file are
  deltas; an added/deleted FILE remains quiet rather than painting every effect as new/gone.
- Family presence, nearest depth, dispatch-only basis and looped repetition all participate in the delta. The
  method declaration receives the change marker and its call-site rows repeat the moved family, so a rewritten
  body does not become semantic churn solely because its line numbers changed.
- The free-flowing badge cluster is replaced by one fixed eight-slot lane (`D C B Q E I R S`) in both unified
  and split views. Direct/reached remains filled/hollow, distance remains a superscript, dispatch uncertainty is
  retained, loop amplification is the violet lower edge, unchanged reach recedes, and changed reach uses a
  dedicated teal token rather than the overloaded warning amber.
- Long-line wrapping is an explicit setting and defaults on. Turning it off restores horizontal overflow; with
  it on, long split-view lines wrap without moving the lane out of its gutter.
- `--ok` is now defined for dark, light and system palettes; `--reach`, `--reach-soft`, `--amp` and `--hair` are
  defined alongside it. Measured contrast for semantic colours is 4.97:1–7.70:1 across both themes.
- Pure delta tests cover stable-id movement, body rewrites, loop/dispatch changes, added-file quietness, unique
  renames, ambiguous rename fail-closed behaviour, and a method added inside a modified file. Browser dogfood on
  CodeRig: 41 lanes in unified/split, all eight-slot, zero gutter-containment violations; added
  `AnnotateCommand.cs` carried 43 head marks and zero false moved slots; console clean.

Still open from the ranked list: path-compressed file tree with counts/rollups, hazard/finding delta and
ranged hunk expansion. Review already renders effects before its independent findings requests complete;
the ordinary File view's shared `Promise.all` still waits for findings. Unchanged reach deliberately stays
visible but muted by default; a changes-only filter must not silently discard uncomparable findings.

Low-DPI dogfood exposed a follow-up in the first slice: `react-diff-view` creates two gutter columns in unified
mode, so the renderer painted the same semantic lane twice on context rows and reserved 320px before the code.
Unified review now owns one lane (head for context/inserts, base for deletes); split retains one per pane. The
five-pixel depth superscript was not legible on a low-DPI Windows display and is removed from the lane — exact
depth stays in the tooltip/expanded row. The always-visible legend is reduced to the fixed family key plus a `?`
popover, preserving the vocabulary without competing with the diff.

Split review also suppresses the base lane on an unchanged context row when its complete visible annotation
(effect depth/basis/repetition plus findings) is byte-for-byte equivalent to head. Both lanes remain when the
semantic presentation differs; inserts/deletes keep the only side that exists. This removes baseline duplication
without hiding a difference.

### Folder disclosure — 2026-09-02

Review tree folders now have full-row disclosure buttons with Enter/Space support and `aria-expanded`.
Collapsed state uses the full directory path, scoped to the review's base/head pair, and survives file
selection, Viewed updates, filters and List/Tree switching within the session. Nested folders preserve their
own state when an ancestor closes. Folder toggles neither navigate nor request the diff/semantic APIs.

Path search uses a temporary expanded tree so matches are visible; users can collapse search results too.
Editing the query reveals matching branches again; clearing it restores the ordinary collapsed state.
Disclosure focus is retained across sidebar rebuilds. Page reload starts expanded; persistent collapse,
path compression and expand/collapse-all controls are not part of this slice.

Validation: 7 new behavioral tree tests (18 frontend tests total), TypeScript check and the 1,350-test
local release gate pass. Chrome smoke on the real 37-file CodeRig review covers mouse/keyboard disclosure,
focus retention, nested and same-name directories, List/Tree, Viewed, search restoration, queue filters and
ordered-pair isolation. Toggle-only interactions made zero API requests and left the review URL unchanged.

### Reading-first presentation and focus — 2026-09-02

- Effects now have a local presentation preference: **Inline** (default), **Gutter**, or **Off**, preserved
  across file navigation and reload. Changing presentation makes no semantic request. The source is unchanged;
  collapsed lightning markers disclose non-selectable widgets only on click; no extra row or effect list
  appears by default. Calls on the same line remain distinct in the disclosure. Gutter keeps the existing fixed lanes; Off retains source and availability
  disclosure without semantic widgets.
- Expanded inline text names the call, family, distance, dispatch uncertainty and iteration context. Tier-2 findings
  explain an effect inside iteration; tier-3 findings explain downstream reach from an iterating call, with
  candidate wording rather than a claim of runtime N+1, query count or polynomial degree. Findings-only rows
  and method-declaration-only deltas can expand. Method removals remain visible on the head summary.
- Context deduplication includes target identities and visible finding details, not only family sets. Split
  Gutter never suppresses a method-change header, including removal of a method's last effect. Inline shared
  context is placed in the head pane; changed context shows both revisions.
- Base/head each disclose loading, unavailable findings, absent/unindexed source, a completed empty result,
  or disabled cross-method analysis. CodeRig's current rules do not enable tier 3; this change does not turn it
  on or introduce another whole-store correlation request. Real API baseline: indexed `904674e12dc1`
  `Rig.Storage/Queries/Reads.cs` returns 11 amplification findings and `crossMethodAvailable: false`.
- **Focus mode** uses the full app viewport with a compact sticky toolbar and an explicit exit; Escape exits
  even from controls. **Hide/Show files** is independent and remembered. Search shortcuts reopen the queue.
  A failed/missing diff reveals the queue and exits focus so hidden controls cannot strand the reader.
- Tree links now carry their revision side and select that exact index before navigation; a base witness
  must not open whatever head/latest store the tree happened to use previously.
- Lazy renderer loading is generation-guarded: a late initial patch render cannot overwrite the completed
  findings render with stale "loading" state. This was reproduced against the real, warm CodeRig index.

Not shipped in this slice: matching/diffing findings across revisions, changes-only filtering, complete
cross-method witness paths, correlation caching, or the `rig amplify` degree model in the file interface.

Validation: 26 frontend tests and TypeScript check pass; the ordinary 1,350-test .NET release gate passed
before the final presentation-only adjustment, followed by a successful rebuild/package/install. Chrome
at DPR 1 covers the real CodeRig review, Inline/Gutter/Off persistence, zero extra widget rows when
collapsed, single disclosure, viewport focus, sidebar/search/error recovery, and no API requests from
presentation controls. Light/dark × unified/split checks at 1280 and 1920 pixels keep markers inside the
gutter. An explicitly synthetic fixture covers multiple calls per line, tier-2/3 candidate wording,
findings-only rows, declaration-only/last-effect removals, and loading/error/disabled/empty states; this
is not a MedDBase calibration or evidence that cross-method analysis is enabled in the current store.

### Full-file reading — 2026-09-02

`Full file` lazily opens one exact Git revision in the existing review surface, including lines outside
the patch. Head is the default; deleted files open Base. `File revision` switches sides, while `Back to
diff` restores the unchanged bounded patch and its Unified/Split preference. Focus, hidden file queue,
wrapping and collapsed effects remain available. Source rows have one revision-native numbered gutter,
no artificial insertion/deletion tint, and the same effects/findings and tree-link identities as the diff.

`GET /api/review-source?base=…&head=…&file=…&side=base|head` resolves membership and rename paths through
the shared immutable review inventory, then reads the Git blob at the store's source commit. It never
falls back to a dirty/current working-tree file or recalculates annotations. The ordinary diff response
still contains only 20-line-context hunks. Source loading is independent of findings updates; navigation
and revision changes invalidate outstanding responses. Failures can retry and are not persisted in the
browser cache. Empty, absent, binary/unsupported-encoding and unavailable source are distinct states.

Current full-file preview limits are explicit: UTF-8 text up to 4 MiB and 20,000 lines; larger files are
refused with an explanation, never silently truncated. Above 200,000 characters or 5,000 lines, syntax
highlighting is disabled while all permitted source lines remain readable. Ranged expansion inside the
two diff panes and virtualized reading above these limits are still follow-ups.

Checkpoint validation: all 29 frontend tests, TypeScript checking, the frontend production build, and
the .NET solution build passed. A real Chrome/Playwright smoke against the newly built backend covers
Head/Base full-file reading, collapsed effects and revision-native widgets, focus/hidden files, return
to diff, and one lazy source request per revision without additional semantic requests. Both source
payloads match the corresponding Git blobs. Expanded widgets retain the full code-column width.

The ordinary .NET suite and the additional browser resilience checks were interrupted to relieve host
resource pressure; they are **not** recorded as passing for this slice. Packaging/global-tool install
did not finish, so the existing local server still runs the previous installed version. This is a
user-requested push checkpoint, not a completed release gate; remaining checks must run before release.

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
