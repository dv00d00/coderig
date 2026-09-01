# Rendered target names leak CLR backtick arity instead of source generic syntax

**Status:** done · **Completed:** 2026-09-01 · **Found:** 2026-09-01 by a probe agent auditing `rig annotate`
· **Family:** rendering

## What happens

`SymbolNameFormatter.ShortName` passes CLR arity markers straight through, so call-site targets render as:

| printed | source |
|---|---|
| `TypedListExtension.Fill``1` | `Fill<T>` |
| `DFSUploadHelper.DeleteOnError``1` | `DeleteOnError<T>` |
| `Process.tell``1` | `tell<T>` |
| `Router.fromConfig``2` | `fromConfig<T,U>` |
| `MemoryCacheWithInvalidation`2.GetOrCreateWithSlidingExpiration` | `MemoryCacheWithInvalidation<T,U>.GetOrCreate…` |
| `LinqMethods.WithDb``1` | `WithDb<T>` |

Seen in `rig annotate --format tsv` `site` rows across `Pathways.cs:213,365,390`,
`PersonContractsService.cs:66`, `PersonModelCacheService.cs:482`, `PersonCoursesRepository.cs:44`, and in the
Rider plugin's unanchored-row logging.

Repro: `rig annotate "…\PatientPortal\Controllers\Pathway\Pathways.cs" --format tsv | rg '\x60'`

## Why fix it

These names are read by humans and pasted into other rig commands by agents. A backtick-arity name is not what
the source says, and the CLI's own pattern matching is substring-over-DocID, so a reader who copies
`` Fill``1 `` gets a match while a reader who copies `Fill<T>` from the source may not — the rendering and the
query vocabulary disagree in opposite directions.

## Fix

- Normalise in the SHORT-NAME renderer only: `Name``N` → `Name<T…>` (or plain `Name<>` if the parameter names
  are not available), `Type`N.Member` → `Type<…>.Member`. Do not touch DocIDs, store facts, or any cache key —
  the mangled form is the correct identity, this is a display concern.
- Decide once whether to print placeholder letters (`<T,U>`) or empty brackets (`<,>`); placeholders read
  better and are what the audit expected.
- Apply it wherever `ShortName` output reaches a human surface so `annotate`, the web lens, `tree --files`,
  `reaches` and the Rider row logs all agree.

## Testing expectations

- Unit tests over `ShortName`: one type arg, two type args, generic type + generic method, a nested generic, and
  a non-generic name passing through unchanged.
- One rendering test asserting a real `site` row from pasted `rig annotate --format tsv` output.
- Check nothing parses the rendered name back into an id (grep for consumers of `ShortName` before changing it).

## Out of scope

DocID formatting and pattern-matching semantics — the mangled name stays the identity everywhere except display.

## Outcome

`ShortName` is now the single human-readable seam and delegates generic grammar to the existing
`PrettyGenericName`: open arities become `<T, U>` and constructed generics retain their concrete short names.
Exact DocIDs remain separate identity fields on every web/search/read-model row.

Tree rendering keeps a deliberately raw intermediate short name until path-specific concrete generic
arguments are substituted; its folded `via` marker uses the same binding. The compact LLM format likewise
keeps its intentional no-placeholder contract. Unit coverage includes both generic shapes and a synthetic
`FileEffectLens` site target, proving the label is shared by web, `annotate`, and Rider transport.
