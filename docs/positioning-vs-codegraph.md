# `rig` vs CodeGraph — positioning for a mixed .NET Framework / .NET Core monorepo

**Date:** 2026-07-26
**Subject repo:** `meddbase-main-application` @ `25c5b5df3394` (12,223 `.cs` files, ~2,700 JS/TS/VB)
**Tools compared:** `rig` (this repo) vs [colbymchenry/codegraph](https://github.com/colbymchenry/codegraph) v1.5.0 (62.5k★, MIT)

---

## TL;DR

CodeGraph is an excellent **retrieval** tool and a poor **call-graph** tool for this codebase.
`rig` is the inverse. They are not competitors; they answer different questions.

On seven targets with ground truth read from source, CodeGraph **missed five real call
sites** and emitted **one confidently wrong cross-project edge**. It resolves static and
same-class calls correctly, and fails on interface dispatch, lambda bodies, and inheritance
— the three idioms that dominate MedDBase.

**Recommendation: keep `rig` as the call-graph/effect authority for .NET.** Optionally adopt
CodeGraph for cross-language source retrieval, where `rig` has nothing to offer.

---

## What each tool actually is

| | CodeGraph | `rig` |
|---|---|---|
| Front end | tree-sitter grammars (Rust kernel), **no build** | Roslyn + MSBuild design-time build |
| Resolution | name/heuristic matching over syntax | compiler-exact, frozen into facts |
| Scope | 24 languages | C# / VB.NET |
| Primary question | *"where is the code, give it to me"* | *"what does this do to the outside world"* |
| Consumption | MCP server (`explore`) + CLI | CLI + web UI + skill |
| Unique output | verbatim source + doc comments | effects, hazards, per-EP behavioural diff, deployment attribution |

The single most important structural difference: **CodeGraph never compiles anything.** That
is simultaneously its greatest strength (zero config, instant, any language, no build) and
the direct cause of every failure below.

---

## Method

- Same commit, same working tree, for both tools.
- Ground truth established by reading **source**, never from either tool's output.
- Cost test: three subagents, identical question, identical model and agent type, each
  restricted to one toolset (grep-only baseline / CodeGraph-only / rig-only).

## Index cost

| | CodeGraph | `rig` |
|---|---|---|
| First index | **31m 16s** | ~3 min (incl. MSBuild design-time build, warm dtb cache) |
| CPU utilisation | **1.01 of 32 cores** avg | `--parallelism 16` |
| Nodes / edges | 406,167 / 1,177,153 | 439,075 symbols / 2,408,506 refs |
| Store per commit | 498 MB | 3.6 GB |

CodeGraph's store is ~7× smaller — the honest price of storing less. Its documented headline
is *re-sync* speed on edits, which was not measured here.

---

## Result 1 — cost A/B (one precise-lookup question)

> *"Where is `SetInvoiceSettings` declared/implemented, what calls it, and which entry point triggers it?"*

| agent | tool calls | tokens | wall | correct? | self-rated confidence |
|---|---|---|---|---|---|
| baseline (grep/read) | 11 | 52,451 | 82s | ✅ | high |
| **`rig`** | **10** | **43,231** | 135s | ✅ | high |
| CodeGraph | 12 | **62,356** | 146s | ✅ *by workaround* | **medium** |

**CodeGraph was the most expensive and least confident — 19% more tokens than plain grep.**
Its caller index missed the call, so the agent had to reconstruct the answer by elimination
(enumerating all 12 sites that can obtain an `IHealthcodeService`). A tool failure that forces
compensating work is anti-savings. In its own words:

> `codegraph callers` and `codegraph impact` both returned *only* the interface/impl pair —
> they did **not** surface this real call site … the caller index is demonstrably incomplete
> for this symbol.

⚠️ **Caveat:** this is one *needle-lookup* question — grep's best case. CodeGraph's published
83–91% savings are measured on *exploratory* questions over TS/Rust/Python repos. This result
does not refute that headline in general; it refutes it for this repo and question class.

---

## Result 2 — call-graph fidelity (the load-bearing test)

Seven targets, ground truth from source:

| target | call idiom | truth | CodeGraph | `rig` |
|---|---|---|---|---|
| `Argument.CheckNull` | static on concrete type | many | ✅ 20 callers | — |
| `ProvideHealthcodeSettings` | same-class private | 3 | ✅ exactly 3 | — |
| `SetInvoiceSettings` | interface recv **in lambda** | `InvoiceMain.cs:706` | ❌ missed | ✅ |
| `SetSiteSettings` | interface recv `srv?.` | `EditSite.cs:251` | ❌ missed | ✅ |
| `SetCompanySettings` | interface recv `srv?.` | `Company/Edit.cs:736` | ❌ missed | ✅ |
| `SetMedicalPersonSettings` | 1 direct **+** 1 lambda | `EditLive.cs:569`, `SaveClinicians.cs:298` | ❌ missed **both** | ✅ both |
| `Save(optionalTransaction)` | inherited base method | `WorkflowMasterBase.cs:158` | ❌ **wrong target** | ✅ exact overload |

### The controlled case

`SetMedicalPersonSettings` has two call sites in different idioms — one direct
(`srv?.SetMedicalPersonSettings(...)`), one inside a lambda
(`IfSome(settings => srv?.SetMedicalPersonSettings(...))`). **CodeGraph missed both.**
So this is not a lambda bug: interface-typed receiver calls are not attributed at all.

### The worst result is a false edge, not a miss

```csharp
// Master_HealthcodeServiceImpl.cs:1604
public void SetSiteSettings(int siteId, SiteHealthcodeSettings siteSettings, ITransaction optionalTransaction = null)
{
    Argument.CheckNull(new { siteSettings });
    ProvideHealthcodeSettings(false).Sites[siteId] = siteSettings;
    Save(optionalTransaction);          // <-- this call
}
```

- **Truth:** `WorkflowMasterBase.Save(ITransaction optionalTransaction = null)` —
  `MedDBase.Application.Core.Workflow/WorkflowMasterBase.cs:158`. Inherited, exact signature match.
- **`rig`:** binds it correctly, with the invocation line — `[invocation @ …Master_HealthcodeServiceImpl.cs:1608]`.
- **CodeGraph:** binds it to `private record Save(XeroAuth XeroAuth)` in
  `src/external-auth/MedDBase.ExternalAuth.Xero/IO/XeroTokenManagerEnv.cs:59` —
  a **record type**, in an **unrelated project**, matched on the bare name `Save`.

A miss makes an agent work harder. **A confident false edge makes it wrong**, silently, in
exactly the blast-radius queries the tool is marketed for. `Save` is one of the most common
method names in .NET; this failure mode is not rare here.

---

## Where each tool shines

### CodeGraph is the better tool for

- **Source retrieval.** `codegraph node` / `explore` return verbatim source with line numbers
  and doc comments in one call. `rig` returns DocIDs and `file:line` and *cannot emit source at all* —
  our rig-restricted agent hit this wall in five minutes.
- **The non-.NET surface.** ~2,700 JS/TS/VB files here that `rig` will never index.
- **Zero-config breadth.** No build, no project model, no rules file. Works on any repo in minutes.
- **Static and same-class call resolution** — verified correct above.
- **Agent ergonomics.** MCP-first, auto-sync on file change, works in Cursor/Codex/Claude Code.

### `rig` is the better tool for

- **Correct call graphs on legacy .NET idioms.** Interface DI, lambdas, inheritance,
  delegate/event handoffs — verified above; these are pervasive in this codebase.
- **Cross-project binding that survives the build system.** Because `rig` runs a real MSBuild
  design-time build, it binds across `ProjectReference`, paket, and binary references, and picks a
  concrete TFM (`--framework`) on multi-targeted projects. CodeGraph has no project model at all:
  it sees files, not assemblies, which is why `Save` crossed a project boundary it should not have.
- **Effects.** EF Core reads/writes/commits, Redis, HTTP, object store, message bus — with
  observations like `[read_before_commit]` and `[concurrency_handled:DbUpdateConcurrencyException]`.
  CodeGraph has no concept of an effect.
- **Hazards.** race windows, dual writes, N+1, TOCTOU, cache coherence.
- **Behavioural diff.** `rig impact --per-ep` / `--expect-no-effect-change` — per-entry-point
  effect deltas between two commits. Nothing in CodeGraph is comparable; its `impact` is
  symbol reachability.
- **Deployment attribution.** *loaded-in* vs *active-in* service, via capability gates. In a
  monorepo where one solution builds FrontEnd, DataServer, and kube workers, "which host actually
  runs this entry point" is a question only `rig` answers.

---

## Why the boundary falls exactly there

CodeGraph resolves names; `rig` resolves symbols.

To bind `srv?.SetSiteSettings(...)` you must know the static type of `srv` — which requires
type inference through a generic `Option<IHealthcodeService>`, then interface→implementation
resolution. To bind `Save(optionalTransaction)` you must walk the base-class chain and pick the
overload matching `ITransaction`. Both are compiler jobs. tree-sitter produces a syntax tree; it
has no type system, so it falls back to matching identifiers — which is why it finds `Save` in a
Xero record.

`rig` mines Roslyn's own `FindImplementationForInterfaceMember` / `OverriddenMethod` at index
time and freezes them into facts. It is not reimplementing the compiler; it is *recording* the
compiler's answers. That is the whole difference, and it is not closeable by better heuristics.

CodeGraph discloses this ceiling itself: **ASP.NET 83.9%**, filed under "convention/reflection-heavy…
at their honest static-analysis ceiling." Our results are consistent with that disclosure — they
are simply much worse than 83.9% on *this* codebase's idioms, because the residual is not randomly
distributed. It is concentrated exactly where a 20-year-old FP-flavoured .NET monorepo lives.

---

## Recommendation for this monorepo

1. **Keep `rig` as the authority for .NET call graphs, effects, and impact.** No alternative
   reaches the required fidelity on our idioms, and a wrong edge is worse than no tool.
2. **Do not rely on CodeGraph for blast radius, "who calls this", or migration/refactor safety
   on C#.** It will under-report call sites and occasionally invent one.
3. **Consider CodeGraph for source retrieval and the non-.NET surface** — front-end JS/TS and the
   VB.NET remnants — where `rig` offers nothing. The two compose cleanly: `rig` points, CodeGraph fetches.
4. **Highest-value `rig` change identified by this exercise: source emission.** A `--source` flag on
   `refs` / `path` / `tree` closes the one gap where CodeGraph is strictly better, and it is small.

---

## Limits of this study

- One repo, one commit. MedDBase is unusually FP-heavy (LanguageExt `Option`/`IfSome`) and legacy.
  On an idiomatic modern ASP.NET Core service CodeGraph would perform considerably better.
- The cost A/B is a **single question** of the needle-lookup class, which favours grep. CodeGraph's
  exploratory-question benchmark (`explore`, its primary MCP tool) was **not** exercised.
- CodeGraph's re-sync/incremental path — its actual headline claim — was not measured.
- Seven fidelity targets is enough to characterise a boundary, not enough to quantify a rate.
  The two false-negative classes (interface receivers, lambda bodies) reproduced on every instance
  tried; the false-positive class was observed once.
