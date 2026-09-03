# A running `rig watch` is undiscoverable from the checkout it is watching

**Status:** open bug. Found 2026-08-24 by an agent reviewing an MR in `meddbase-main-application-2` with a
live host serving that solution. It recovered — by reading rig's own source — and reported the recovery cost
as the finding.
**Triage:** needs-triage — the card proposes three independently shippable fixes and changes the endpoint
identity contract without an accepted migration/compatibility plan.

## What happens

The endpoint is keyed on the **launcher's working directory**, not the watched solution:

```csharp
// Rig.Cli/Live/LiveQueryTransport.cs
// `rig-live-<16 hex of sha256(normalised working directory)>`
internal static string PipeNameFor(string workingDirectory)
```

So a host booted as `cd C:\Git\meddbase-analysis; rig watch C:\Git\meddbase-main-application-2\MedDBase.slnx`
serves the pipe for `meddbase-analysis`, while every query issued from the checkout it is actually watching
gets:

```
No .rig store found in 'C:\Git\meddbase-main-application-2'.
Run `rig index <solution>` to create one, or cd to the directory that contains .rig/.
```

That message is a dead end. It names the one recovery (index a store) that is both expensive and unnecessary,
and says nothing about the live host already holding exactly the facts asked for.

## Why it matters more than a misconfiguration

The reviewer had been told a host was running, and the tool still told it there was nothing here. Recovering
took: enumerate processes to find `rig.exe watch`, read `LiveQueryTransport.cs` from the **rig source repo**,
learn the pipe-naming scheme, enumerate `\\.\pipe\`, then hash candidate directory strings until one matched.

Nobody without the rig checkout open can do that. For everyone else the tool is simply absent — and "absent"
is indistinguishable from "not useful", which is how a tool stops being reached for at all.

Note the adjacent evidence: an earlier agent on the same task, *not* told a host existed, checked `.rig/`,
found only `dtb-cache`, concluded rig was unavailable, and completed a whole review without it — hedging the
one finding rig would have settled. The availability signal people check is not the signal that determines
availability.

## Fix, in value order

1. **On "no store found", probe for a host before giving up.** If a resident index is serving the solution in
   this directory, say so and where from: *"a resident index for this solution is serving from `<dir>`; run
   from there, or pass `--no-live`."* Turning a dead end into a next step is most of the value.
2. **Key the endpoint on the watched SOLUTION path**, not the launcher's cwd. The solution is what identifies
   the facts; cwd is an accident of how someone started the process. This is the real fix.
3. **`rig watch` should print its endpoint directory at startup** — it currently prints the pipe name, which
   is a hash nobody can invert.

(1) and (3) are cheap and independent. (2) changes the transport contract, so client and host must move
together; a host and client that disagree about the key would silently fail to find each other — which is
exactly today's bug in a new costume.
