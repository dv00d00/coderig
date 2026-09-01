# rig — work backlog

The backlog holds product slices, bugs, calibration work, and decisions that still affect the current product.
Historical audit punch-lists live under [`docs/archive/`](../archive/); completed and parked decisions stay in
[`done/`](done/) so their evidence remains searchable.

---

## Convention

```
docs/backlog/
  todo/       proposed / not-started, plus remaining work blocked on unavailable external corpora
  progress/   has shipped AND locally actionable open sub-items, or actively in-flight
  done/       fully shipped / superseded / retracted / parked-wontfix / reference
```

One file per independently shippable issue. **The index is `ls docs/backlog/*/`** — no maintained index file.
Lifecycle and card-writing rules are in [`docs/agents/issue-tracker.md`](../agents/issue-tracker.md); the optional
five-role triage vocabulary is in [`docs/agents/triage-labels.md`](../agents/triage-labels.md). `Status` describes
the work, while `Triage` says who can act next; they are separate fields.

`done/` also holds reference logs, parked/wontfix items, and session notes with recorded findings.
