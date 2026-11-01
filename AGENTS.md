# AGENTS.md

SharpGIS: Windows desktop raster GIS (QGIS-like) — WinForms UI on GDAL, with the CA-Markov
land-change model delegated to a Python subprocess. UI text and code comments are Chinese.

## Build

```bash
dotnet build SharpGIS.csproj      # always name the csproj
```

- `dotnet build` alone may pick `SharpGIS.slnx` and produce a second output tree. Output must be
  `bin\Debug\net48\SharpGIS.exe` — check its timestamp before trusting a self-test run.
- Use the `dotnet` CLI only. Invoking MSBuild from a Visual Studio install is blocked on this machine.
- Hard constraints: `net48`, `LangVersion 8.0`, `Nullable disable`, `AllowUnsafeBlocks`,
  `PlatformTarget=x64`, `WinExe`. On net48 there is no `<UseWindowsForms>`; WinForms/Drawing are
  explicit `<Reference>`s in `SharpGIS.csproj`.
- GDAL 3.12.1 natives are placed by the custom `CopyGdalRuntime` target into `$(OutDir)gdal\x64`.
  Do not re-enable `CopyGDALInteropFiles` / `ContentGDALInteropFiles`: the package defaults flatten
  x86 natives into the output root and break `GdalBootstrap`.
- `GdalBootstrap.Ensure()` is idempotent and resolves paths relative to `AppContext.BaseDirectory`,
  so the exe must run from its output folder.

## Tests: the self-test binary, not a test framework

There is no xUnit/NUnit. `--selftest` runs steps against the built exe and needs external raster
fixtures that are **not in the repo**. Point `SHARPGIS_TESTDATA` (or `--data=<dir>`) at a folder with
`TMtest.tif`, `multi.tif`, `pan.tif`.

```bash
bin\Debug\net48\SharpGIS.exe --selftest            # all steps
bin\Debug\net48\SharpGIS.exe --selftest --step=ui --out=artifacts\ui
bin\Debug\net48\SharpGIS.exe --selftest --step=lang --lang=en
```

- Steps: `open | render | process | camarkov | ui | audit | lang`; `--data=<dir>` overrides fixtures,
  `--out=<dir>` overrides artifact dir (default `bin\...\artifacts\selftest\<timestamp>`).
- Exit codes: `0` all pass, `1` any failure, `2` GDAL unusable. Fix code, rebuild, rerun — the
  harness reports failures by name in `TestLog` output and saves PNG screenshots for eyeballing.
- `--step=lang` alone hides i18n bugs; only a full run exercises the "class already initialized"
  ordering problems. Run the full suite for anything touching `Lang`/`LangDict*`/`Theme`.
- `bin\Debug\net48\SharpGIS.exe --probe` dumps the real GDAL C# binding signatures to
  `artifacts\api_probe.log`. Consult it before writing GDAL calls; binding names differ from
  upstream docs (`GDALWarpAppOptions`/`GDALTranslateOptions`/`GDALDEMProcessingOptions`,
  `RIOResampleAlg.GRIORA_*`, `Gdal.GDALProgressFuncDelegate`).
- Python engine: `python Engine/camarkov_engine.py --selftest` (pure functions) and
  `python tools/test_engine_lang.py` (zh/en parity on synthetic data). Needs numpy/scipy/
  scikit-learn/rasterio/matplotlib.

## Wiring

- `Program.cs` is the only entrypoint. Order matters: `Lang.Load()` → `Lang.Use(--lang)` →
  `Application.EnableVisualStyles()` → `GdalBootstrap.Ensure()`. Language must be settled before any
  UI type initializes.
- `Models/` layer stack + `Viewport` (view state only, never writes files). `Rendering/` reads
  windows and composites. `Processing/` operators (GDAL wrappers + pure C#). `UI/` MainForm, dialogs,
  panels. `Services/` `JobRunner`, Python discovery/exec, `Lang`. `SelfTest/` the harness above.
  `Engine/` is the Python model, shipped as content — not compiled.
- `RasterLayer` owns a `ReadLock`; GDAL `Dataset` objects are not thread-safe. Take the lock around
  band reads.
- CA-Markov flow: C# writes `job.json` → `python -u Engine/camarkov_engine.py --job job.json` →
  stdout line protocol `@@PROGRESS/@@INFO/@@WARN/@@DONE` → `metrics.json` parsed back into
  `CaMarkovResult`. The engine forces stdout to UTF-8 (`sys.stdout.reconfigure`);
  `JavaScriptSerializer` may hand back `ArrayList` instead of `object[]`, so parse via `IEnumerable`.

## Rules that are easy to break

1. **Dialogs: UI-thread snapshot.** `ProcessingDialogBase.OnExecute` calls `CaptureParameters()` on
   the UI thread, then `RunCore` runs on a background thread. `RunCore` must never read a control.
   Every new control needs a matching field captured in `CaptureParameters()`, or the operator
   silently reads stale/locked values. The audit step enforces `CheckForIllegalCrossThreadCalls`.
2. **i18n is Chinese-as-key.** C# `Lang.T("中文")`, Python `L("中文")`; translations live in
   `Services/LangDict1-3.cs` and `Engine/lang_en.py`, both falling back to the Chinese key. Never
   call `Lang.T` in a `static readonly`/`const`/module-level initializer — it freezes the language
   before it is chosen; expose a property instead. Translate the whole format string
   (`Lang.T("驱动因子重要性（{0}）", x)`), never concatenate half-brackets or hardcode `、`/`：`.
   Details: `tools/README.md`.
3. **Never assign `ProcessStartInfo.EnvironmentVariables`.** This machine has duplicate-case env vars
   (`HTTPS_PROXY`/`https_proxy`); .NET Framework throws "item already added" and every child process
   dies.
4. **Absolute paths across the Python boundary.** The child's cwd is `Engine/`; run `Path.GetFullPath`
   on every path (`CaMarkovParams.MakePathsAbsolute`) and write job JSON as UTF-8 without BOM.
5. **Source data is read-only.** `GDAL_PAM_ENABLED=NO` is set globally; self-tests assert no
   `.aux.xml` appears in the data dir. Write outputs only to the chosen output path.
6. **Locked outputs.** A file already open as a layer cannot be overwritten on Windows. Check
   `ProcessingUtil.IsFileWritable` / `SuggestFreePath`; route GDAL failures through
   `ProcessingUtil.DescribeGdalError` so users see an actionable message instead of `Permission denied`.
7. **Palette rasters must render nearest-neighbour.** `RasterTileRenderer` forces it; bilinear makes
   GDAL warn and fall back.
8. **Dark theme is fully owner-drawn.** `TabPage.BackColor` ignores visual styles — page content is
   wrapped in a `Panel` (`WrapPageContent`). `SplitContainer.MinSize` cannot be set in a constructor;
   do it in `Load`. `DrawToBitmap` only captures the custom canvas because of `WM_PRINTCLIENT`.

## Conventions

- User-facing terminology: 影像 (not 图像), 符号化方式 (not 渲染方式).
- Public/internal members carry `///` docs with `<param>`/`<returns>`; comments explain intent or a
  non-obvious constraint, not the obvious code.
- `tools/*.py` are developer scripts, not shipped or compiled. `Engine/**` is copied to output.

## Repo state

- Source is under git; `bin/`, `obj/`, `.vs/`, local test data and the `.workbuddy/` agent logs are
  ignored. Never commit build output — `bin/` alone is several GB and contains GDAL natives whose
  license terms belong to the NuGet package, not this repo.
- If local session logs exist in `.workbuddy/memory/`, they hold verified bug postmortems
  (threading, GDAL export color, rotation math, i18n ordering); they are private and not published.
