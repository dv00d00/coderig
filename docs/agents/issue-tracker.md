# Issue tracker: CodeRig backlog

Issues and specifications live as Markdown cards under `docs/backlog/`.

## Conventions

- `docs/backlog/todo/`: proposed or not started work.
- `docs/backlog/progress/`: actively in flight. Shipped work does NOT stay here on the strength of a
  remainder — the shipped record moves to `done/` and each remaining item becomes its own card, per the
  one-file-per-issue and separate-cards rules below.
- `docs/backlog/needs-review/`: value not yet agreed — neither scheduled nor declined.
- `docs/backlog/done/`: complete, superseded, retracted, or reference work.
- `docs/backlog/wont-do/`: explicitly declined or wontfix work, with the reason recorded.
- `done/` and `wont-do/` are terminal: nothing in either is expected to be reopened. Parked work therefore never belongs there — it goes to `todo/` when it is blocked on an external precondition whose trigger is known, and to `needs-review/` when its value is not agreed.
- A card enters `needs-review/` only by an explicit decision, never by ageing.
- One file per issue. The directory listing is the index; do not maintain a second index file.
- Preserve each card's existing `**Status:**` prose. When a Matt Pocock skill assigns a triage role, add or update a separate `**Triage:** <label>` line near the top.
- Record dependencies using `**Blocked by:**` with relative links to the blocking cards.
- Append discussion under `## Comments` only when durable conversation history is useful.

## Publishing

When a skill says to publish a specification, issue, or ticket:

1. Create one descriptively named Markdown card under `docs/backlog/todo/`.
2. Include the problem, accepted decisions, testing expectations, and explicit out-of-scope items.
3. Use `**Triage:** ready-for-agent` only when the card is sufficiently specified for implementation without further product decisions.
4. Create dependent work as separate cards rather than combining multiple independently shippable tickets.

## Transitions

Move the card between lifecycle directories without changing its filename:

- `todo` → `progress` when implementation or an actionable shipped slice begins.
- `progress` → `done` when no locally actionable work remains.
- `needs-review` → `todo` when the value is agreed.
- `todo`, `progress`, or `needs-review` → `wont-do` when explicitly declined, recording the reason.
- A `wontfix` decision moves the card to `wont-do` regardless of its current active stage.

## Fetching

Read the exact card path supplied by the user. If only a title or topic is supplied, search all five lifecycle
directories and report ambiguity rather than guessing.

## Wayfinding

A wayfinder map and each child decision are normal backlog cards. Prefix related filenames with a shared effort slug and number children in dependency order. Record blocking edges with relative links.

## Pull requests

Pull requests are not an issue-request surface by default.
