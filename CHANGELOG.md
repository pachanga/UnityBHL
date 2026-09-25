# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.9.0] - 2026-09-25

### Added
- `BHL/Debug Mode` menu item: checkable toggle, mirroring the Control Panel's own
  toggle.

### Changed
- "Script sources"/"Postproc sources" are now collapsible dropdowns (one path per line)
  instead of a single array-literal line.
- "Debug Mode" toggle shows a warning HelpBox while enabled: Play Mode will block until a
  DAP debugger attaches on the configured port.
- Control Panel window title shows "BHL Control Panel (Debug)" while Debug Mode is on.

### Fixed
- `DebugServerController.Start` now uses the creating `BHL.VM` accessor instead of
  `TryGetVM`, so the debug server (and its "BHL Debugger" waiting dialog) actually
  attaches on Play Mode entry even if nothing else has touched `BHL.VM` yet.

## [0.8.0] - 2026-09-24

### Added
- `BHL.VM` auto-loads `Settings.bakedBundlePath` via `Resources.Load` on device if
  nothing else attached bytecode yet, deferring to a custom `IVMCreator`'s own bytecode
  source (`VM.Loader` already set) rather than overriding it.

### Changed
- `bakedBundlePath`'s Inspector label renamed to "Result Resource Path"; empty-field
  hint now shows an example path instead of the `result_file`-ignored note.

### Fixed
- The Editor's `Library/BHL/bhl.bytes` restore always overrides a custom VM factory's
  bytecode (unlike the new device auto-load above) - it exists to carry the freshest
  recompiled bytecode across a domain reload, not to act as a deferential fallback.

## [0.7.0] - 2026-09-24

### Changed
- `Settings.hotReloadOnRecompile` defaults to off again.

### Fixed
- `Library/BHL/bhl.bytes` is now deleted right after being read, so it can't serve a
  stale compile (Recompile On Play off) or shadow a separate CLI/CI build's output.
- "Recompile On Play" now actually gates whether Play Mode compiles at all, instead of
  just toggling cache-bypass on a compile that ran unconditionally.

## [0.6.0] - 2026-09-24

### Added
- `BHL/About` menu item: package description and UnityBHL/BHL versions.

## [0.5.0] - 2026-09-24

### Added
- `Settings.hotReloadOnRecompile`: Recompile/Force Recompile can migrate already-running
  `ScriptBHL` instances in place (`BHL.ReloadModules`) instead of just swapping bytecode
  for future loads. Defaulted on (later reverted to off, see 0.7.0).

## [0.4.0] - 2026-09-24

### Added
- Control Panel footer also shows the `bhl` runtime version (`bhl.Version.Name`).

## [0.3.0] - 2026-09-24

### Fixed
- Control Panel's Recompile/Force Recompile always use the same modal progress bar as
  the `BHL/Recompile` menu items, instead of an inline bar that only fell back to modal
  when the window was closed.

## [0.2.0] - 2026-09-24

### Added
- Control Panel footer shows the resolved UnityBHL package version
  (`PackageInfo.FindForAssembly`).

## [0.1.1] - 2026-09-23

### Changed
- Tidied up `package.json`/`composer.json` `displayName` and `description`.

## [0.1.0] - 2026-09-18

Initial release.

### Added
- `ScriptBHL` MonoBehaviour: attaches a BHL script class instance to a GameObject,
  configures its fields from the Inspector, and ticks it every frame.
- `BHLRuntime`: lazy VM singleton, compiles `.bhl` sources and reloads changed modules
  while in Play Mode.
- `BHLInstanceRegistry`: tracks live `ScriptBHL` instances per module for reload
  migration.
- Editor-time hot reload via `BHLAssetPostprocessor`, watching `.bhl` asset changes.
- Basic Unity bindings module (`unity` - `Vector3`, `Quaternion`).
- `NO_UNITY` build: `Runtime/UnityBHL.csproj` compiles a Unity-independent subset as a
  plain .NET assembly, for headless/server consumers.
- `BHL.Reset()`: public alias for the internal lazy-state cleanup.
- `BHLRuntimeException`: combines a caught exception with a live `VM.Fiber`'s BHL-side
  stack trace.
- `BHLProjectConfig`: a resolved `bhl.proj`'s module->source-file mapping, plus raw
  `inc_dirs`/`src_dirs`/`defines`/`result_file`.
- `Settings` moved from `Editor/` to `Runtime/`, gained a lazily-parsed `BhlProj`
  property.
- `VMCreator` takes an optional `IUserBindings` for explicit VM construction.
- `PostprocBridge`: mirrors `bhl.proj`'s `postproc_sources` into a generated, Editor-only
  asmdef referencing `bhl`, so Unity compiles postproc code itself and `bhl`'s
  `AppDomainPostProcessor` can find it by assembly name - a prebuilt `postproc_dll`
  loaded via reflection can never satisfy `IFrontPostProcessor` here, since Unity
  compiles `bhl` from source into its own assembly. Generates a `csc.rsp` defining
  `BHL_POSTPROC`. Synced on every compile; only rewrites changed files.
- `Settings.postprocEnvVars`: name/value pairs applied via
  `Environment.SetEnvironmentVariable` before every Editor compile, for
  `postproc_sources` code that needs a CLI/CI build's own env vars. Supports
  `$(DATA_PATH)` expansion.
- Control Panel: "Force Recompile" next to "Recompile", bypassing `bhl`'s compile cache.
- `Settings.postprocAsmdefDir`: where `PostprocBridge` generates its asmdef.
- `Settings.forceRecompileOnPlay`: bypasses the compile cache on Play Mode entry too.
- `BHL/Force Recompile` menu item.
- `PostprocBridge.Clear`: deletes the generated postproc asmdef folder on demand.
- `BHL/VM Stats` window: per-VM pool/exec stats split out of the Control Panel.

### Changed
- `BHL/Recompile`/`Force Recompile` show a live-updating progress bar (derived from the
  compiler's log lines) instead of a static "Compiling..." bar.
- Explicit menu priorities so `Control Panel` < `Recompile` < `Force Recompile` always
  sort in that order.
- Settings Inspector: Debug Port moved to the top.
- Control Panel's "Hot Recompile" renamed to "Recompile"; it and "Force Recompile" now
  also bake to `bakedBundlePath` if set.
- `BHL/Rebuild and Bake` menu item renamed to `BHL/Recompile`.
- Control Panel: "Auto On File Changes" renamed to "Auto Recompile On File Changes",
  moved inside the "Settings" foldout.
- `SettingsInspector` split into `DrawMainFields`/`DrawResultPath` so the Control Panel
  can interleave its own toggle between them.

### Fixed
- `EditorCompiler.Compile` now applies `bhl.proj`'s postprocessing - previously silently
  ignored (`CompileConf.postproc` defaulted to `EmptyPostProcessor`).
- `BHL/Recompile` actually forced a full, cache-bypassing rebuild despite its name
  matching the Control Panel's plain "Recompile" button.
