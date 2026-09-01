# The file lens deletes a line's depth-0 effect when that line also has a targeted call

**Status:** done · **Triage:** ready-for-agent · **Found:** 2026-09-01 by a probe agent auditing `rig annotate`
over 30 MedDBase files; root-caused in code afterwards · **Family:** file lens (read model) / Rider + web + CLI

## Outcome

The lossless shared model now preserves an untargeted depth-zero row beside distinct targeted rows on the same
line, including cross-family and multi-callee cases. The text/web lens min-merges each family while Rider keeps
its target-first anchoring policy. `FileEffectsSchema` v2 invalidates pre-fix resident and browser projections.

## What happens

A source line that performs an effect DIRECTLY (a call into external library code — `File.OpenText`, a
`proxy.*` client-page call, `DbCommand.ExecuteNonQuery`) renders with **no depth-0 badge** whenever the same
line also contains an in-solution call that reaches some family transitively. The line shows only the DISTANT
badge, so the surface that exists to say "the I/O is right here" says "the I/O is 4–20 calls away".

The per-method table is built from a different join and stays correct, so the method row and its own lines
CONTRADICT each other. That is the trust-breaking part: `--summary` says `rpc!`, and the line that produces it
shows `io:8` and no `rpc` at all.

Verified examples (store `2f944e739e47-dirty`, clone `meddbase-main-application-2`):

| file:line | source | rig prints | should include |
|---|---|---|---|
| `MedDBase.Foundation/Level83/MasterPageWebFormBase.cs:170` | `File.OpenText(Map.Path(ConfigurationManager.AppSettings[…]))` | `io:4` | `io!` — `rig reaches LoadRedirectionFile` lists `d0 io read IO.File` |
| `MedDBase.Pages/Profile/Private/FriendRequests.cs:93` | `proxy.Redirect(profiles[0].PkProfile);` | `io:8` | `rpc!` — `d0 clientpage_proxy redirect` |
| `MedDBase.Pages/Accounts/HomeComponents/Main.cs:244` and `:251` | `proxy.Show("Control","InvoiceFilter",…)` | `io:7` | `rpc!` — one `d0 clientpage_proxy show` per line |
| `MedDBase.Pages/Workflows/ReferralIncomming/Stages/WriteDischargeDetail.cs:543` | `proxy.ShowDialog(…)` | `cache:9 db:9 echo:17 io! rpc:20` | `rpc!` (the `io!` survived; the co-located `rpc` depth-0 did not) |

Repro:

```
rig annotate <path> --method LoadRedirectionFile     # from c:/git/meddbase-analysis
rig reaches "MasterPageWebFormBase.LoadRedirectionFile"   # the d0 row the badge omits
```

Line 171 of the same method (`reader.ReadToEnd()`, no in-solution neighbour on the line) renders `io!`
correctly, which is why the defect reads as intermittent.

## Root cause

Two filters in `Rig.Domain/Functions/FileEffectReadModelIndex.cs` discard the depth-0 row:

1. **`MergeCallSites` (`:239`)** drops every UNTARGETED row at a `(enclosing, line)` where any family produced
   a TARGETED row. The untargeted row is exactly the "effect is right here, external callee, empty target,
   NearestDepth 0" row built at `:325`. A single in-solution call anywhere on the line therefore deletes it.
   The comment states the rationale: the Rider client anchors an untargeted row on the leftmost invocation of
   the line and could mark the wrong call.
2. **`BuildCallSiteKeys` (`:294`)** builds `directTargets` only for sites with exactly ONE distinct callee, so
   on a multi-callee line the direct (depth-0) row never gets a target and falls through to the untargeted arm
   — where filter (1) then deletes it.

Both are per-line and per-file, so this is not a derivation bug: the effect exists in `derive`, `reaches` and
the method table, and is lost only in the call-site projection.

## Decision: preserve the untargeted row

The anchoring worry belongs to the CONSUMER, not the read model, and the Rider plugin no longer needs the
protection: `MatchOnLine` resolves targets first and uses the untargeted arm only when no targeted row matches
(`experiments/RiderBackendEffectSpike/RigEffectDaemonStage.cs`, after the 2026-08-31 `AllSameTarget` removal).

Chosen fix (O1): **stop dropping.** Keep untargeted rows; let each surface decide. Text renders
`io! io:4` (the lens already merges min-per-family), the browser shows both, Rider prefers the targeted row.

Rejected alternatives (revisit only if the editor proves O1 wrong):

- O2 fold the untargeted row's depth INTO the targeted row on that line (one row per line; loses "this call
  is itself external I/O").
- O3 drop only when the SAME family already has a depth-0 row at that line (narrowest change, keeps the
  Rider invariant, still loses cross-family depth-0 like the `proxy.*` cases).

## Testing expectations

- Unit tests in `FileEffectReadModelIndex` coverage: (a) a line with one external depth-0 effect plus one
  in-solution targeted call at depth N emits BOTH families/depths; (b) a multi-callee line keeps its depth-0
  row; (c) a line whose only effect is external still emits exactly one untargeted row (no regression of the
  precedence rule's original purpose).
- `FileEffectLens` test: a line carrying depth 0 and depth 4 of the SAME family labels `io!`, not `io:4`.
- Real-store check: the four rows in the table above, via the repro commands. `MasterPageWebFormBase.cs:170`
  is the cheapest.
- Cache: the payload SHAPE does not change, but the same input now yields different output, so the file-effects
  artifact needs its schema axis handled — see
  [file-effects artifact has no schema constant](./file-effects-artifact-has-no-cache-schema-constant.md).

## Out of scope

- Mining columns so two calls on one line can be told apart — separate card
  ([call-site facts carry no column](../todo/call-site-facts-no-column-same-line-calls-collapse.md)).
- The lambda-owner gap (effects inside lambdas absent from the file model) — separate card.
