# A method's file-lens depth disagrees with `rig reaches` for the same family

**Status:** done · **Completed:** 2026-09-01 ·
**Found:** 2026-09-01 by a probe agent auditing `rig annotate` · **Family:** file lens / reachability

**Blocked by:** [folding lambda-owned effects onto their owner](../done/file-lens-omits-effects-owned-by-lambdas.md).
Re-measure after that change before deciding whether any depth behaviour remains defective.

## The two observations

| method | file lens badge | `rig reaches` shallowest | delta |
|---|---|---|---|
| `ImageEdit.Save` (`MedDBase.Pages/Document/Template/HomeComponents/ImageEdit.cs:27`) | `echo:2` | `eventbus publish` at **d3** | lens 1 shallower |
| `PathwayTreeNode.get_Task` (`MedDBase.Pathways.Views/PathwayTreeNode.cs:43`) | `cache:20` | `redis write` at **d22** | lens 2 shallower |

Repro:

```
rig annotate "…\ImageEdit.cs" --method Save
rig reaches "ImageEdit.Save" --format tsv          # compare the echo-family depths
rig annotate "…\PathwayTreeNode.cs" --summary
rig reaches "PathwayTreeNode.get_Task" --format tsv
```

For `ImageEdit.Save` every OTHER family matched the oracle exactly (`db!`, `cache:3`, `rpc:9`, `io:12`); only
`echo` disagreed. That pattern argues against a systematic off-by-one in the depth arithmetic.

## Verified 2026-09-01 — the delta is the LAMBDA hop

Both numbers reproduced first-hand. `rig annotate ImageEdit.cs --summary` → `27  Save  cache:3 db! echo:2
io:12 rpc:9`. `rig reaches "ImageEdit.Save" --format tsv` → the two shallowest echo-family effects are:

```
3  eventbus  publish  SimpleDocumentMessage  M:…TemplateEntity.Save(IPredicate,Boolean)~λ0   TemplateEntity.cs:189
3  eventbus  publish  SimpleDocumentMessage  M:…TemplateEntity.Save(IPredicate,Boolean)~λ1   TemplateEntity.cs:237
```

**Both owners are LAMBDAS.** The forward walk charges a hop to enter the lambda
(`ImageEdit.Save → … → TemplateEntity.Save → ~λ0`), so the effect sits at d3. The lens's reverse closure seeds
at the lambda and arrives at `ImageEdit.Save` one hop shorter. The `cache:20` vs `d22` case has a delta of 2,
which the same explanation predicts for an effect two lambda hops deep (verify: check whether that owner is a
lambda inside a lambda).

So this is very likely **not a routing error and not an arithmetic offset** — it is one convention counting a
methodGroup hop into a lambda and the other not. Which also means `ImageEdit.Save`'s four agreeing families
agree precisely because their nearest owners are ordinary methods.

Remaining check before closing: confirm the `cache:20` owner is doubly-lambda-nested, and confirm no
non-lambda-owned family ever disagrees.

## Why this may not be a bug

The lens's `NearestDepth` and `reaches`'s depth are computed by different machinery and are allowed to differ
where the lens legitimately finds a SHORTER route:

- `CollapseInstantiations` (`FileEffectReadModelIndex.cs:198`) folds `~mono` instantiation nodes onto their base
  id keeping the MINIMUM distance, so a route through a generic instantiation can be shorter in the lens.
- The lens closure is per-family reverse reachability; `reaches` is a forward walk with one-hop dispatch and its
  own sync-cut rules.

Both being shallower-in-the-lens is consistent with the collapse explanation. So the question to settle is not
"which number is bigger" but **whether the lens's shorter route is a real path**.

## Verification steps

1. `rig path "ImageEdit.Save" "<the eventbus publish owner>"` — does a 2-hop path exist? If yes, `reaches` is
   the one under-reporting and this card moves to the reachability side.
2. Repeat for `PathwayTreeNode.get_Task` at 20 hops (use `rig tree … --view paths` if `path` is unhelpful at
   that depth).
3. If no such path exists, instrument the family closure for that one family and look for a seed that should not
   be there — an `echo`-family provider matched on a node that does not perform it.

Do NOT "fix" either number before step 1: making the two agree by adjusting an arithmetic offset would hide
whichever of them is actually wrong.

## Decision, if the lambda-hop explanation holds

Not "make the numbers equal" — pick which one is the honest answer to "how far away is the nearest effect".
A lambda hop is not a call a reader can see in the source, so NOT charging for it (the lens's answer) is
arguably the better editor/CLI number, while `reaches` should keep charging it because it is walking a graph.
Note this decision is entangled with
[folding lambda-owned effects onto their owner](../done/file-lens-omits-effects-owned-by-lambdas.md): if lambdas fold,
the lambda hop disappears from the lens by construction and the two conventions converge on their own — so
decide that card first and re-measure before touching depth arithmetic here.

## Outcome to record

Whichever way it lands, the answer belongs in documentation as well as code: an agent comparing `annotate` to
`reaches` needs one sentence saying whether the two depths are the same quantity. The probe agent spent real
time reconciling them and asked for exactly that (see
[annotate output legibility](./annotate-output-legibility-for-agent-consumers.md), item 1).

## Outcome

The two depths intentionally answer different questions. `reaches` counts every traversed graph edge,
including a `methodGroup` hop into a physical lambda node. The file lens is an editor-facing projection: it
folds lambda-owned effects onto their declared method/accessor and counts visible invocation hops from there.
That makes its number one smaller per folded lambda hop; it is not a routing defect or a global off-by-one.

The synthetic contract `Editor_depth_counts_visible_calls_but_not_the_method_group_hop_into_a_lambda` pins a
two-edge forward shape (invocation + methodGroup) as depth 1 in the file lens. The read-model implementation
also states this convention at the fold site. MedDBase re-measurement is useful calibration, not required to
define the semantics.
