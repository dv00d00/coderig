# `rig annotate --method` silently renders the whole file when nothing matches

**Status:** done · **Completed:** 2026-09-01 · **Found:** 2026-09-01 by a probe agent auditing `rig annotate`
· **Family:** CLI UX

## What happens

```csharp
// Rig.Cli/Commands/AnnotateCommand.cs
:204  return matched.Length == 0 ? [(Math.Max(1, opts.From), opts.To ?? int.MaxValue)] : matched;
```

`--method <pattern>` with no match falls back to the DEFAULT window — line 1 to end of file. The caller asked
for one declaration and gets a whole-file dump with no message saying the filter was ignored. An agent
consuming the output cannot tell the difference between "this method spans the file" and "your pattern
matched nothing".

Two ways to hit it, both common:

1. A pattern that is not a substring (a regex like `^Save$`, or a typo).
2. A method that HAS no effects. `Windows` filters `lens.Methods`, which contains only effectful methods, so
   `--method Save` on a clean `Save` behaves exactly like a failed match — even though the method exists and
   `rig annotate --summary` will happily show it is absent for a legitimate reason.

Related, same command, lower severity: `--to` less than `--from` is silently clamped to a single line
(`rig annotate <file> --from 20 --to 5` renders line 20 only) rather than refused.

## Fix

- No match → **refuse** with exit 1 and the shape the ambiguous-path arm already uses (`:104-110`): name the
  pattern, then list the candidate method names in the file (from the lens, plus a hint that a method with no
  effects is not in that list).
- Distinguish the two causes: if the pattern matches a method DECLARED in the file but with no effects, say so
  — "`Save` has no effects in this store; use `--from/--to` to render it anyway" — rather than reporting no
  match. This needs the file's declared-method list, which `FileEffectsQueryService` already loads as
  `Artifact.Methods` (all canonical methods, not just effectful ones), so the information is in hand.
- `--to < --from` → refuse with one line naming both values.

## Testing expectations

- New test file (not the shared `CliApplicationTests.cs`): unmatched `--method` exits non-zero and prints no
  `src` rows; matched-but-effectless `--method` prints the distinct message; `--to < --from` refuses.
- Assertions written against real `rig annotate` output pasted from a run, not from imagination.

## Out of scope

`--method` matching semantics (substring today, consistent with the rest of the CLI's pattern rules).

## Outcome

Window selection now joins the shared artifact's complete declared-method map with the shared lens's
effectful-method projection. A missing pattern fails with declared candidates; a declared but effectless
method gets a distinct diagnostic and the explicit-range escape hatch. Synthetic end-to-end contracts cover
both refusals and prove that an effectful method renders only its declaration window.
