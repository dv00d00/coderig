# `rig annotate <file>` renders ONE line unless `--to` is passed explicitly

**Status:** done · **Completed:** 2026-09-01 · **Found:** 2026-09-01 by a probe agent auditing `rig annotate`
over 30 MedDBase files; cause confirmed by reading the option declaration · **Family:** CLI · **Severity:** the
command's primary documented use is broken

## What happens

```
rig annotate "…\MedDBase.Messages\Patient\WipePatientsMsg.cs"
→        1  using LanguageExt;
(nothing else — a 61-line file)

rig annotate "…\WipePatientsMsg.cs" --to 9999      # renders the whole file correctly
rig annotate "…\WipePatientsMsg.cs" --from 50 --to 10   # renders line 50 only, no error
```

`--help` and REFERENCE.md both say `--to` defaults to "to the end, subject to `--limit`".

## Root cause

```csharp
// Rig.Cli/Commands/AnnotateCommand.cs
:39   var to = new Option<int>("--to") { … };     // int, NOT int?
:73   To: pr.GetValue(to),                        // absent option → default(int) == 0
:192  return [(Math.Max(1, opts.From), opts.To ?? int.MaxValue)];   // 0 is not null, so ?? never fires
```

`Options.To` is typed `int?`, which made the `?? int.MaxValue` fallback look correct, but the value arriving
from the parser is `0`, never `null`. The window becomes `(1, 0)`, and `SourceRenderer.Resolve` with
`endLine < startLine` yields the single start line. `--from 50 --to 10` is the same collapse with a different
start, which is why the two symptoms looked like one bug to the auditor.

## Fix

- Declare `new Option<int?>("--to")` so absence is genuinely `null` (mirror whatever `CommonOptions` does for
  other optional numerics; if none exists, keep it local and comment why the nullable matters).
- Refuse `--to < --from` with one line naming both values instead of collapsing — see
  [`--method` silently renders the whole file](./annotate-method-filter-silently-renders-the-whole-file.md),
  which carries the same refusal item.

## Testing expectations

- A test that a bare `annotate <file>` renders more than one line for a multi-line fixture and reaches the last
  line (or the `--limit` truncation footer). This is the regression that would have caught it: every existing
  test passes `--from`/`--to` or `--summary`.
- A test that `--from 50 --to 10` exits non-zero.
- Assertions written against real pasted `rig annotate` output.

## Note for whoever picks this up

The audit that found it ran 30 files and mostly used explicit windows, so the defect survived a full parity
review of the renderer. Any new option whose absence must mean "unbounded" needs the nullable type, not a
sentinel — worth a glance at the other commands' numeric options while here.

## Outcome

`--to` is now nullable, so omission means EOF and `--limit` remains the only safety cap. Contradictory
ranges fail before store lookup and name both bounds. A synthetic immutable-store contract covers bare EOF
rendering and the early range refusal through the public CLI entry point.
