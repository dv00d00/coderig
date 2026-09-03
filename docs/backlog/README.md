# rig — work backlog

The backlog holds product slices, bugs, calibration work, and decisions that still affect the current product.
Historical audit punch-lists live under [`docs/archive/`](../archive/). Completed work stays in [`done/`](done/);
explicitly declined work stays in [`wont-do/`](wont-do/) so both kinds of evidence remain searchable without
mixing shipped behavior with rejected scope.

---

## Convention

```
docs/backlog/
  todo/          proposed / not-started, plus remaining work blocked on unavailable external corpora
                 or on an external precondition whose trigger is known
  progress/      has shipped AND locally actionable open sub-items, or actively in-flight
  needs-review/  value not yet agreed — neither scheduled nor declined
  done/          fully shipped / superseded / retracted / reference — terminal
  wont-do/       explicitly declined / wontfix — terminal
```

One file per independently shippable issue. **The index is `ls docs/backlog/*/`** — no maintained index file.
Lifecycle and card-writing rules are in [`docs/agents/issue-tracker.md`](../agents/issue-tracker.md); the optional
five-role triage vocabulary is in [`docs/agents/triage-labels.md`](../agents/triage-labels.md). `Status` describes
the work, while `Triage` says who can act next; they are separate fields.

`done/` also holds reference logs and session notes with recorded findings. `wont-do/` holds rejected work
with the reason for rejection. Both are terminal. Neither holds parked work: a park is something you will come
back to, so it goes to `todo/` when the reopen trigger is an external precondition, or to `needs-review/` when
its value is not agreed.
