# External interface calls do not dispatch into first-party implementations

**Status:** todo · design required · **Family:** graph model / dispatch recall
**Triage:** needs-info
**Extracted from:** [external/library leaf admission](../done/external-library-code-no-graph-representation.md),
2026-09-03.

## Gap

L2 admits selected metadata call targets as external leaf nodes, but a call whose declaring member is on an
external interface still stops at that leaf. Calls such as `IMediator.Send` therefore do not reach indexed
first-party handlers even when Roslyn facts contain the implementing types. The leaf fixes boundary visibility,
not dispatch recall.

## Decision required

Choose the fact-based bridge from an external interface member to indexed implementations without pretending
to reconstruct external library internals. The design must state:

- which Roslyn-mined implementation/type-relation facts establish the candidate set;
- how receiver narrowing and one-hop dispatch apply;
- how the edge is disclosed when the external declaration itself has no indexed body;
- what admission bound prevents framework-wide fan-out.

## Testing expectations

- A fixture with an external interface declaration and two first-party implementations, only one in receiver
  scope.
- Forward `reaches`/`tree` and reverse `callers` agree on the admitted one-hop dispatch edge.
- Residual CHA fan-out remains disclosed and no external body is invented.

## Out of scope

- General assembly decompilation.
- Indexing the framework closure.
