# CodeRig Rider plugin

Minimal backend-only Rider plugin for the resident CodeRig file-effect read model. It targets Rider
2026.2 and renders every C# method that can reach a SQL effect in two places:

- Code Vision above the declaration: `rig: SQL · depth N`;
- a gutter icon beside the declaration, with the same effect/depth tooltip.

The plugin does not open SQLite. A visible-document daemon pass performs one non-blocking whole-file
request to `rig watch`, joins returned method DocIDs to Rider's current PSI declarations, and lets Rider
own the editor ranges. Solution-wide daemon modes, generated files, and non-user files are no-ops.

If no matching resident host is available, the plugin fails closed: it commits no CodeRig highlighting
and does not block editing or the build. A host failure is cached, so one daemon pass cannot spin on a
missing pipe.

## Build and install

Create an installable ZIP:

```pwsh
pwsh scripts/build-rider-plugin.ps1
```

The artifact is `artifacts/rider/CodeRig-0.1.0.zip`. Rider can install it through
**Settings | Plugins | Install Plugin from Disk**.

Build and copy it directly into the default Rider 2026.2 profile:

```pwsh
pwsh scripts/build-rider-plugin.ps1 -Install
```

Pass `-RiderProfile <path>` to target another profile. Rider must be restarted after a direct install.

## Runtime contract validated on 2026-08-31

Both an isolated Rider 2026.2.0.1 instance and the packaged `CodeRig 0.1.0` installed in the normal profile
loaded the backend DLL and queried a real resident `rig watch` for `src/Rig.Storage/Queries/Reads.cs`. The
host returned 34 exact method rows; the daemon mapped all 34 to current declarations and committed 68 UI
highlightings: one Code Vision entry plus one gutter mark per row. The normal-profile backend log contained
no CodeRig registration or rendering errors.

The first implementation accidentally participated in solution analysis and issued 569 requests for 489
files at startup. The `DaemonProcessKind.VISIBLE_DOCUMENT` gate reduced the repeat run to two TTL-separated
requests for the one visible file. Do not remove that gate.

This remains intentionally small. It has no frontend/Kotlin module, settings page, click action, witness
path UI, automatic `rig watch` process management, or project/compilation selector. A physical file in more
than one indexed compilation context fails closed as `ambiguous`.
