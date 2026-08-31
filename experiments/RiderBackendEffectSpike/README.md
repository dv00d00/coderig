# CodeRig Rider plugin

Minimal backend-only Rider plugin for the resident CodeRig file-effect read model. It targets Rider
2026.2 and renders SQL and file-system reachability at two semantic levels:

- one Code Vision row per effect family above each affected declaration, with Rider's database-query or
  folder icon: `rig: SQL · depth N` / `rig: FILE · depth N`;
- a family-specific gutter icon plus an inline hint on each proven invocation: `sql·N`, `file·N`, or both.

The plugin does not open SQLite. A visible-document daemon pass performs one non-blocking whole-file
request to `rig watch`, joins returned enclosing/target DocID pairs to Rider's current PSI invocations,
and lets Rider own the editor ranges. The wire contract deliberately contains no line/column spans.
Solution-wide daemon modes, generated files, and non-user files are no-ops.

An ambiguous direct-effect source line containing multiple different invocation targets fails closed at
the call-site layer; its method-level Code Vision summary remains. This avoids bolding a call the index did
not prove.

If no matching resident host is available, the plugin fails closed: it commits no CodeRig highlighting
and does not block editing or the build. A host failure is cached, so one daemon pass cannot spin on a
missing pipe.

## Build and install

Create an installable ZIP:

```pwsh
pwsh scripts/build-rider-plugin.ps1
```

The artifact is `artifacts/rider/CodeRig-0.3.0.zip`. Rider can install it through
**Settings | Plugins | Install Plugin from Disk**.

Build and copy it directly into the default Rider 2026.2 profile:

```pwsh
pwsh scripts/build-rider-plugin.ps1 -Install
```

Pass `-RiderProfile <path>` to target another profile. Rider must be restarted after a direct install.

## Runtime contract validated on 2026-08-31

The packaged `CodeRig 0.1.0` method-summary slice was validated in both an isolated Rider 2026.2.0.1
instance and the normal profile. It
loaded the backend DLL and queried a real resident `rig watch` for `src/Rig.Storage/Queries/Reads.cs`. The
host returned 34 exact method rows; the daemon mapped all 34 to current declarations and committed 68 UI
highlightings: one Code Vision entry plus one gutter mark per row. The normal-profile backend log contained
no CodeRig registration or rendering errors.

`CodeRig 0.2.0` keeps the declaration Code Vision entry and moves the gutter mark to the semantic invocation,
combining it with a bold font. Its read model is keyed by enclosing and target DocIDs, while all visible
ranges come from Rider PSI. The packaged plugin was installed in the normal profile and queried the same
`Reads.cs` file after a clean Rider restart: the host returned 34 methods plus 8 call sites, and the daemon
committed 42 UI highlighters with no CodeRig registration or projection error in the backend log.

`CodeRig 0.3.0` adds the built-in `io` provider as the compact `file` family. The resident host materialises
one reverse read model per family and unions only the rows for the requested file; Rider still performs no
database access or whole-file symbol resolution. SQL uses Rider's database-query glyph and file-system effects
use Rider's opened-folder glyph. A call reaching both families gets one stable `sql·N file·N` inlay. The
packaged plugin was loaded from the normal Rider profile and projected a mixed-family `CliApplication.cs`
response (four method rows plus four call-site rows) into 13 UI highlighters without registration errors.

The first implementation accidentally participated in solution analysis and issued 569 requests for 489
files at startup. The `DaemonProcessKind.VISIBLE_DOCUMENT` gate reduced the repeat run to two TTL-separated
requests for the one visible file. Do not remove that gate.

This remains intentionally small. It has no frontend/Kotlin module, settings page, click action, full witness
path UI, automatic `rig watch` process management, or project/compilation selector. A physical file in more
than one indexed compilation context fails closed as `ambiguous`.
