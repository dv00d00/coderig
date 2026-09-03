# Dirty-tree provenance is per-file, not per-run — and the refusal is currently disabled

**Status:** todo · **Priority: HIGH** (the guard is OFF in source right now; Review currently asserts
at-commit provenance it cannot prove) · **Found:** 2026-09-03, review UI smoke run ·
**Triage:** ready-for-agent
**Family:** review / store provenance
**Decision:** D3, 2026-09-03 — mark dirty files at index time from `git status`, one bit per file. The
content-hash alternative was considered and **rejected**; see "Why not the hash" below.

## The invariant

We review **immutable commits**, but the indexer reads **whatever is on disk**. So a store labelled
`aae396ea7e8e` can hold facts derived from source that never existed at `aae396ea7e8e`, and every annotation
the review view joins onto a git diff is an at-commit claim. The invariant is:

> no annotation is shown against a revision it was not derived from.

That is real and it is currently **unenforced**.

## What was there, and why it was the wrong shape

`FileDiffEndpoint.LoadInventoryAsync` threw on `run.SourceDirty`, killing `/api/review-files` and
`/api/file-diff` outright. It was the ONLY place in the codebase where `SourceDirty` caused a refusal — all
ten other consumers degrade and disclose (`SourceRenderer` falls back to the git blob;
`StoreAnswerDisclosure.cs:96` prints `UNVERIFIABLE: indexed from a dirty tree`; `FileEffectsEndpoint.cs:178`
and `SourceEndpoint.cs:77` pass `storeDirty` out as a flag).

Three things were wrong with it:

1. **Whole-run granularity for a per-file property.** Measured on `meddbase-main-application` 2026-09-03:
   five dirty files, and **not one is a `.cs`** — a `.slnf`, two `cluster.conf`, a `Web.config` and a
   markdown file. The entire Review surface was unreachable because of local config that cannot affect a
   single semantic fact.
2. **It discarded a sound diff.** The changed-file list comes from `git diff <base> <head>` — pure git
   between two commits, which dirt cannot touch. Only the *annotations* are in question.
3. **The caveat already has a channel.** `ReviewFileDto.SemanticReady` + `Reason` exist for exactly this and
   are already populated per file (51 of 108 files semantic-ready on the current store pair).

**The refusal is disabled** as of 2026-09-03 so the review UI/UX could be exercised — see
`TODO(dirty-provenance)` at `src/Rig.Cli/Web/FileDiffEndpoint.cs:410`, which records the invariant and the
two shapes the guard must NOT be rebuilt in. Restore a guard before this ships.

## The fix

```
INDEX START  git status --porcelain -z -uall     (in the SOURCE repo, measured ~370ms)
INDEX END    the same call again, union the two sets
             └─> source_files.Dirty = 1          [one bit on an existing row]

REVIEW TIME  read the bit. no git call, no hashing.
             dirty -> SemanticReady: false, "indexed from uncommitted source"
```

- **A bit on `source_files`, not a path set on `runs`.** That table already carries one row per indexed file
  with its absolute `FilePath`, so there is no set to serialize and no path re-normalization at read time.
- **Status at start AND end, unioned.** A cold index is 4-10 minutes. A file clean at start and edited at
  minute three would otherwise be recorded clean — the **unsound-clear** direction, which is the one failure
  class this tool exists to prevent. The union leaves only edit-and-revert inside a single run.
- **`-uall`.** An untracked `.cs` that got indexed has no blob at that commit at all, so `??` must count as
  dirty.
- **Mark only paths present in `source_files`.** Build output cannot then be marked even if it leaked into
  the status output.

### Why build output cannot pollute this

Checked 2026-09-03: `obj/` and `bin/` are gitignored (`src/main/MedDBase.Site/.gitignore:4,6`; zero tracked
files under either), and `--porcelain -uall` reports untracked but **not** ignored paths — only `--ignored`
would surface those. Source-generator output never lands on disk; rig records it as a project-relative
pseudo-path. And per the bullet above, the `source_files` join excludes it a second time. If pollution ever
did reach the call it would **over-mark**, which costs precision, never soundness.

### Why not the hash

Considered and rejected: store `SourceText.GetChecksum()` per file at index time (free — `FactExtractor.cs:33`
already holds the `SourceText`, and `GetChecksum` is called nowhere in the codebase), then compare at review
time against `SourceText.From(git cat-file blob <commit>:<path>).GetChecksum()`.

It is strictly more powerful — it catches mid-run edits without a second git call, and catches a store that
was clean but has since gone stale. It loses on the axes that matter here:

| | git-status bit | content hash |
| --- | --- | --- |
| index cost | 2 git calls, ~370ms each | 1 checksum per file, off text already in RAM |
| **review cost** | **none — read the bit** | one blob read + checksum per reviewed file |
| unknowns | path normalization (git gives repo-relative forward-slash; the store holds absolute Windows paths — reuse `RelativeFileMap`) | whether `GetChecksum()` is over the ORIGINAL bytes or re-encoded ones; needs a test against a known blob before it can be trusted |
| schema | +1 bit on `source_files` | +1 hash column |

Review-time cost decides it: the web review view is the surface being fixed, and the bit is free there.
`git status` compares the working tree to HEAD, and at index time HEAD **is** `SourceCommit`, so it answers
exactly the question asked. It does not survive a later rewrite of that commit — but then the SHA no longer
resolves and `git diff` fails loudly rather than answering wrongly.

Keep the hash on file as the upgrade if per-file staleness detection is ever wanted for its own sake.

## Owns

`src/Rig.Analysis/` index-time write path (wherever `runs.SourceDirty` is currently set),
`src/Rig.Storage/` (`source_files` schema + `SchemaVersion.Index` 8→9), `src/Rig.Cli/Web/FileDiffEndpoint.cs`
(remove the TODO, read the bit into `SemanticReady`).

## Acceptance

- A store indexed from a tree whose only dirty files are non-`.cs` yields a review with **every** file
  semantic-ready. That is today's real case and the current refusal gets it maximally wrong.
- A store indexed with a dirty `.cs` in the diff yields that file `SemanticReady: false` with a reason naming
  uncommitted source, and every other file unaffected.
- `git diff`-derived rows (`status`, `path`, `additions`, `deletions`) are identical whether or not any store
  is dirty — dirt must never change the changed-file list.
- `SchemaVersion.Index` bumped and the store re-indexed; an old store fails the gate with "re-index" rather
  than silently reading a missing column.
- The `TODO(dirty-provenance)` comment is gone, not merely edited.

## Also in the blast radius

Two UI bugs seen in the same smoke run, worth folding in since they are in the error path this card changes:

- the review error rendered **twice** — once as a banner, once in the sidebar;
- the empty state read "Choose two indexed revisions and a file" while two revisions were already selected.

## Provenance

Found by Playwright smoke run 2026-09-03 against `rig serve` on the `aae396ea7e8e-dirty` /
`409c330b99dd-dirty` pair: `/api/review-files` and `/api/file-diff` both 400, every other endpoint
(`/api/files`, `/api/search`, `/api/hotspots`, `/api/entrypoints`, `/api/providers`, `/api/runs`,
`/api/meta`) served the dirty store fine. Both stores on the machine are dirty, so Review was unreachable by
default rather than in an edge case.
