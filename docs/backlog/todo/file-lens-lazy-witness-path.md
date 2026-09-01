# File lens: resolve one witness path lazily from web or Rider

**Status:** todo · **Family:** file lens / explanation
**Triage:** ready-for-agent

## Problem

The file read model cheaply says that a method or call site reaches an effect and carries family, depth, basis,
and amplification findings. It does not show the concrete route. Shipping witness paths for every row would
turn a ready projection into a much larger artifact and make editor latency depend on explanation data most
users never open.

## Accepted contract

- Keep the file read model path-free.
- On interaction, request one witness using store/generation identity, source context, enclosing symbol, selected
  family/provider, and the same sync/async/intrinsic semantics that produced the badge.
- Reuse the existing path/traversal engine; do not grow a file-lens-specific reachability implementation.
- Return source states (`exact`, `stale`, `ambiguous`, `unindexed`) for resident requests and fail closed when the
  badge snapshot no longer matches.
- Web opens the route in the existing Tree/path surface. Rider may request the same contract through the resident
  transport and navigate only after the asynchronous response arrives.

## Acceptance

- Direct, transitive, lambda, looped, and dispatch-only fixtures return a witness consistent with the badge.
- A dispatch-only explanation visibly discloses that the edge is an over-approximation.
- No witness work is performed while rendering a file that the user never expands.
- A stale save in Rider cannot navigate using coordinates from the prior generation.

## Related

- [Rider plugin minimal product](../progress/rider-plugin-minimal-product.md).
- [File lens first-derivative delivery record](../done/file-lens-shows-only-the-first-derivative.md).

## Out of scope

- Retaining every possible path or promising a unique path.
- Sending SQLite to Rider or querying it synchronously from the daemon.
