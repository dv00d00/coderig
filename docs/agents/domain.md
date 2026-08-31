# Domain documentation

CodeRig is a single-context repository.

## Before exploring

- Read `docs/ubiquitous-language.md` for canonical product, fact, effect, and execution-model terminology.
- Read ADRs under `docs/adr/` that affect the area being changed.
- Use `docs/README.md` to locate supporting design and operational documentation.

Missing ADRs are normal; proceed silently.

## Canonical glossary

`docs/ubiquitous-language.md` is the single source of truth. Do not create a parallel `CONTEXT.md`.

Use its terms in specifications, backlog cards, code, tests, and agent output. When terminology is genuinely missing or has changed, update that document through the `domain-modeling` workflow.

## Architectural decisions

Record an ADR under `docs/adr/` only when the decision is:

1. costly to reverse;
2. surprising without its context; and
3. the result of a genuine trade-off.

Surface contradictions with existing ADRs explicitly rather than silently overriding them.
