# `rig annotate` output legibility for agent consumers

**Status:** done · **Completed:** 2026-09-01 · **Found:** 2026-09-01, probe agent's "hard to read as an agent"
notes after 30 files · **Family:** CLI UX / docs

The bundled audit has been implemented where it was one rendering concern and split where it was not.

## Items

1. **The site badge and the method badge are different quantities.** A call-site badge is the CALLEE's own
   distance to the family; the enclosing method's badge is that plus one for an in-solution call. Both are
   correct and they look like the same number, so a reader compares `db:1` on a line with `db:2` on its method
   and suspects a bug. Add one footer line stating the convention.

2. **`families:` lists every family the query ASKED about**, so on the MedDBase store all 30 audited files
   printed `blob bus cache db echo io rpc search` while `bus` and `search` never produced a single badge. The
   line is honest (documented in the rig skill) but invites re-verification of two providers that are simply
   rarely populated here. Consider marking which families actually appear in this file, e.g.
   `families: db io rpc  (asked: + blob bus cache echo search)`.

3. **`bus` and `echo` both appear** in the family list because the local `meddbase-analysis/rig.rules.json`
   renamed its bus providers to `echo` while `builtin-rules.json` still ships `bus`. Decide whether `bus` is a
   legacy alias to retire or a distinct family, then make the two rule sets agree.

4. **"N effectful method(s), M marked line(s)" are two different metrics** (method-table rows vs annotated call
   lines) and their mismatch reads as a bug. Both probe agents initially flagged normal files over it; one
   withdrew several findings once it understood the header. One-line legend.

5. **No cheap way to cross-check a badge from inside `annotate`.** Both audits reconciled badges against
   `rig reaches` by hand. A `--verify` flag (re-derive the badge families for the rendered methods through the
   forward walk and mark any disagreement) would make that a single command — and would have settled
   [the depth disagreement](./file-lens-method-depth-disagrees-with-reaches.md) without manual work.
   Follow-up: [`annotate --verify`](../todo/annotate-verify-badges.md).

6. **Target names print CLR backtick arity** (`` Fill``1 ``) — split out into
   [its own completed card](./short-names-leak-clr-backtick-arity.md).

7. **The cold-latency floor makes it unusable interactively** (~35–50 s per call) — tracked separately in
   [annotate pays a full cold derivation per invocation](./annotate-pays-a-full-cold-derivation-per-invocation.md).
   Until that lands, the footer should say how to get a warm call.

## Testing expectations

Rendering assertions written against real pasted output. Item 2 changes the header line, so update whatever
asserts it; keep the full asked-set visible somewhere, since "cache was asked about and found nothing" is the
statement that makes an absent badge meaningful.

## Out of scope

The line-precision disclosure and the lambda blind spot, both already stated in output and in the rig skill;
  the lambda gap itself is [its own card](./file-lens-omits-effects-owned-by-lambdas.md).

## Outcome

The shared `FileEffectLens` projection now computes requested, present, and requested-but-absent family sets
once for every surface. Human `annotate` output leads with families actually present, names its two counts as
effectful declarations and distinct marked source lines, and explains the method-vs-line distance grains.
TSV remains byte-for-byte the same shape. Synthetic projection and rendering contracts pin the result.

The unrelated decisions were separated rather than hidden in this completed rendering card:

- [`bus` versus `echo` family policy](../todo/effect-family-bus-echo-policy.md)
- [`annotate --verify`](../todo/annotate-verify-badges.md)
- [resident-host latency](./annotate-pays-a-full-cold-derivation-per-invocation.md)
