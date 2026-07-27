#!/usr/bin/env bash
# Enforce the meddbase/ boundary (see ../README.md).
#
#   internal/*  -> may touch meddbase/
#   everything else -> must NOT touch meddbase/, so it is portable upstream by construction
#
# Compares against the merge-base with main, so it judges only what the branch itself changed.
# Exit 0 = portable (or an internal/* branch), exit 1 = a non-internal branch touching meddbase/.
#
# Usage:  check-portable-branch.sh [branch]     (defaults to the current branch)
# CI:     see .gitlab-ci.yml — needs full history, so set GIT_DEPTH=0 for the job.
set -euo pipefail

BRANCH="${1:-$(git rev-parse --abbrev-ref HEAD)}"
BASE_BRANCH="${BASE_BRANCH:-main}"

if [[ "$BRANCH" == internal/* ]]; then
    echo "OK: '$BRANCH' is an internal/* branch — may touch meddbase/ (and is never pushed to a GitHub remote)."
    exit 0
fi

# Prefer the merge-base so long-lived branches aren't blamed for main's own changes. Fall back to a plain
# diff against the base branch when no common ancestor is available (shallow clone with no history).
if MERGE_BASE="$(git merge-base "$BASE_BRANCH" "$BRANCH" 2>/dev/null)"; then
    DIFF_FROM="$MERGE_BASE"
else
    echo "warning: no merge-base with '$BASE_BRANCH' (shallow clone?) — diffing against '$BASE_BRANCH' directly." >&2
    DIFF_FROM="$BASE_BRANCH"
fi

OFFENDING="$(git diff --name-only "$DIFF_FROM" "$BRANCH" -- meddbase/ || true)"

if [[ -z "$OFFENDING" ]]; then
    echo "OK: '$BRANCH' touches no meddbase/ paths — portable upstream."
    exit 0
fi

cat >&2 <<EOF
FAIL: '$BRANCH' is not an internal/* branch but modifies the internal-only meddbase/ tree:

$(echo "$OFFENDING" | sed 's/^/    /')

meddbase/ is NEVER ported to the public OSS repo, and both GitHub remotes are PUBLIC. Either:
  - rename the branch to internal/<name> (it then stays on the gitlab 'meddbase' remote only), or
  - move the MedDBase-specific content out of the portable change.

Rule of thumb: the FINDING is portable (docs/backlog/), the EVIDENCE is internal (meddbase/docs/).
See meddbase/README.md.
EOF
exit 1
