# `meddbase/` — internal-only tree

**Everything under this directory is MedDBase-specific and is NEVER ported to the public OSS repo.**
Everything OUTSIDE it is OSS-portable by default.

That one sentence is the whole contract. It exists because this repo has two jobs that pull in opposite
directions — hosting MedDBase rules/skills/profiles, and continuing to feed tool fixes upstream — and a
boundary that depends on remembering it is a boundary that leaks.

## The remotes

| remote | url | visibility | role |
|---|---|---|---|
| `meddbase` | `gitlab.meddbase.com/mms/coderig` | internal | where work happens; may contain anything |
| `fork` | `github.com/dmytro-kushnir-cority-meddbase/coderig` | **PUBLIC** | staging for upstream PRs |
| `origin` | `github.com/dv00d00/coderig` | **PUBLIC** | upstream OSS |

Both GitHub remotes are public. Assume anything pushed there is permanent.

## The rule, and how it is enforced

Branch name decides whether a branch may touch this tree:

- **`internal/*`** — may touch `meddbase/`. Never pushed to a GitHub remote.
- **anything else** — must NOT touch `meddbase/`, and is therefore portable upstream by construction.

`meddbase/ci/check-portable-branch.sh` enforces it by diffing against the merge-base with `main`. Run it
locally, or as a CI job (see `.gitlab-ci.yml`). It is the mechanism that makes the next fix portable without
anyone having to think about it.

```bash
meddbase/ci/check-portable-branch.sh              # checks the current branch
meddbase/ci/check-portable-branch.sh some-branch  # checks a named branch
```

## What belongs here

| path | contents |
|---|---|
| `meddbase/rules/` | MedDBase `rig.rules.json` mirrors — see the caveat below |
| `meddbase/skills/` | org-specific skills and profiles (the generic `rig` skill stays in `.claude/skills/`) |
| `meddbase/docs/` | RCA corpus, MR-derived findings, bug-triage scan, real-store calibration numbers |
| `meddbase/ci/` | internal CI helpers |

### Rules caveat — deliberately NOT moved yet

The live `rig.rules.json` still lives in `c:/git/meddbase-analysis/`, because `rig` resolves rules from
**cwd** and every day-to-day query runs from that directory. Moving it here would break those queries for no
near-term gain. If it is ever mirrored into `meddbase/rules/`, `meddbase-analysis` must keep a working copy
(or a symlink) — decide the direction of truth before duplicating, not after.

## What does NOT belong here

Tool code, tests, playgrounds, and generic backlog items stay outside — even when a MedDBase MR is what
surfaced them. The pattern to follow: the **finding** is portable and goes in `docs/backlog/`; the
**evidence** (MR numbers, internal SHAs, entity/table names, source paths) is internal and comes here.

The 2026-07-27 guard-polarity fix is the worked example — extraction fix and tests are fully portable, while
the validation numbers that proved it are MedDBase-specific.

Existing backlog docs are **not** being retro-split; that is churn with no payoff. New ones follow the
boundary from here on.
