# Rider backend effect spike

**Verdict:** validated against a real isolated Rider 2026.2.0.1 instance on
2026-08-30. This is still throwaway code, not a product plugin.

Throwaway compile spike for the smallest useful Rider integration:

1. one daemon pass receives one `ICSharpFile`;
2. a cache miss schedules one asynchronous file request and returns no highlights;
3. a fake host returns two method XML documentation IDs;
4. the next daemon pass maps those IDs to the current PSI declarations;
5. the declaration name ranges become two `IHighlighting` instances.

The fake response matches this fixture:

```csharp
namespace Demo;

public sealed class OrderService
{
    public void Load() { }

    public void Save(int orderId) { }
}
```

This deliberately contains no rig/SQLite access, Kotlin frontend, settings UI, or
production error handling. The experiment answers whether the backend daemon seam,
async invalidation, install layout, and symbol-ID-to-current-range projection work
against the exact SDK used by Rider 2026.2.0.1.

## Observed runtime contract

Rider loaded `plugin/META-INF/plugin.xml` plus the DLL under `dotnet/` as a
"simplified Rider.Backend plugin". Opening `fixture/OrderService.cs` produced:

```text
[rig-spike] daemon stage constructed
[rig-spike] cache miss: .../fixture/OrderService.cs
[rig-spike] fake response ready: .../fixture/OrderService.cs
[rig-spike] PSI method: M:Demo.OrderService.Load
[rig-spike] PSI method: M:Demo.OrderService.Save(System.Int32)
[rig-spike] PSI method: M:Demo.OrderService.NoEffect
[rig-spike] committed 2 highlightings for .../fixture/OrderService.cs
```

So the runtime path is exactly one file request, then a daemon invalidation, one
cheap pass over declarations in the current file, and two PSI-owned highlights.
No per-method host requests occur.

The run also showed that daemon stages receive generated C# files from `obj/`.
The spike now rejects `IsGeneratedFile` and `IsNonUserFile` before touching the
host. Product code should retain that supported PSI-property filter.

The public 2021-era examples are stale in two small but relevant ways for 2026.2:

- `CSharpDaemonStageBase` lives in
  `JetBrains.ReSharper.Feature.Services.CSharp.Daemon`;
- `IDaemonProcess.InterruptFlag` is a `bool`, not an object with
  `ThrowIfInterrupted()`.

Build gently with:

```bash
nice -n 15 dotnet build experiments/RiderBackendEffectSpike/RiderBackendEffectSpike.csproj \
  -m:1 /p:UseSharedCompilation=false --no-restore
```

For a manual runtime run, create an isolated Rider profile, copy `plugin/META-INF`
and the built DLL into `<profile>/plugins/rig-effect-spike/{META-INF,dotnet}`, and
launch Rider with that profile's `idea.config.path`, `idea.system.path`,
`idea.plugins.path`, and `idea.log.path`. The normal Rider profile is not needed.
