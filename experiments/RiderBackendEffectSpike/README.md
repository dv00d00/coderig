# Rider backend effect spike

**Verdict:** validated end to end against a real isolated Rider 2026.2.0.1 instance and a resident
`rig watch` on 2026-08-31. This is still throwaway code, not a product plugin.

Throwaway compile spike for the smallest useful Rider integration:

1. a visible-document daemon pass receives one `ICSharpFile`;
2. a cache miss schedules one asynchronous whole-file named-pipe request and returns no highlights;
3. resident `rig watch` returns method DocIDs plus SQL family/depth rows from its generation-owned read model;
4. the next daemon pass maps those DocIDs to current PSI declarations;
5. declaration name ranges become `IHighlighting` instances with a semantic tooltip.

The experiment contains no SQLite access in Rider, Kotlin frontend, settings UI, witness-path interaction,
or product error handling. It answers whether the resident transport, backend daemon lifecycle, async
invalidation, install layout, and DocID-to-current-range projection work against the exact Rider SDK.

## Observed runtime contract

Rider loaded `plugin/META-INF/plugin.xml` plus the DLL under `dotnet/` as a
"simplified Rider.Backend plugin". With `rig watch RuntimeIntelligenceGraph.slnx` serving the repository,
opening `src/Rig.Storage/Queries/Reads.cs` produced:

```text
[rig-spike] daemon stage constructed
[rig-spike] pipe=rig-live-e77c4441e7e35b54 request=... file=.../Reads.cs snapshot=...
[rig-spike] request=... status=ok/exact generation=0 methods=34 cacheTtl=10s reason=
[rig-spike] PSI method: M:Rig.Storage.Queries.Reads.SearchSymbolsAsync(...)
...
[rig-spike] committed 34 highlightings for .../Reads.cs
```

The Rider UI showed the resulting markers in `Reads.cs`. The backend made one request for the file snapshot,
then invalidated the daemon and did one cheap pass over current declarations. There are no per-method host
requests. Exact answers are cached for 10 seconds; non-exact answers and transport failures have short TTLs,
so a temporary stale host or an effect introduced through another file does not poison the editor forever.

The first real run exposed a more serious lifecycle trap: an ungated daemon stage participated in Rider's
solution analysis and issued **569 requests for 489 distinct files** during startup. Restricting the stage to
`DaemonProcessKind.VISIBLE_DOCUMENT` reduced the repeated run to **2 TTL-separated requests for one distinct
file**, while still committing all 34 highlights. Product code must retain this process-kind gate as well as
the `IsGeneratedFile` / `IsNonUserFile` filters.

The public 2021-era examples are stale in two small but relevant ways for 2026.2:

- `CSharpDaemonStageBase` lives in
  `JetBrains.ReSharper.Feature.Services.CSharp.Daemon`;
- `IDaemonProcess.InterruptFlag` is a `bool`, not an object with
  `ThrowIfInterrupted()`.

## Known spike limits

- The client finds the resident host by walking to the nearest `.git` or `.rig` parent. Product packaging
  needs an explicit project/host association rather than this convention.
- A physical file present in more than one project context fails closed as `ambiguous`; the request does not
  yet carry the accepted project/compilation identity.
- The snapshot token sees unsaved Rider-buffer changes, but `rig watch` indexes saved source. An `exact`
  answer therefore describes the resident disk generation, not unsaved editor text.
- `INFO` highlighting is only the smallest visible proof. Product UX still needs a deliberate marker,
  inspection severity, interaction, and witness-path design.

Build gently with:

```bash
nice -n 15 dotnet build experiments/RiderBackendEffectSpike/RiderBackendEffectSpike.csproj \
  -m:1 /p:UseSharedCompilation=false --no-restore
```

For a manual runtime run, create an isolated Rider profile, copy `plugin/META-INF`
and the built DLL into `<profile>/plugins/rig-effect-spike/{META-INF,dotnet}`, and
launch Rider with that profile's `idea.config.path`, `idea.system.path`,
`idea.plugins.path`, and `idea.log.path`. The normal Rider profile is not needed.
