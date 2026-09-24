# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - Unreleased

### Added
- `ScriptBHL` MonoBehaviour: attaches a BHL script class instance to a GameObject,
  configures its fields from the Inspector, and ticks it every frame.
- `BHLRuntime`: lazy VM singleton, compiles `.bhl` sources from `BHLSettings.sourceDirs`
  and reloads changed modules while in Play Mode.
- `BHLInstanceRegistry`: tracks all live `ScriptBHL` instances per module, so a module
  reload can migrate every affected instance.
- Editor-time hot reload via `BHLAssetPostprocessor`, watching `.bhl` asset changes.
- Basic Unity bindings module (`unity` — `Vector3`, `Quaternion`) for BHL scripts.
- `NO_UNITY` build support: `Runtime/UnityBHL.csproj` compiles a Unity-independent
  subset (`BHL`, `VMCreator`, `VMFactory`, `VMTracker`, `IVMProvider`) as a plain .NET
  assembly, for headless/server consumers (e.g. `BitGames.Scripting.csproj`) that can't
  reference `UnityEngine`.
- `BHL.Reset()`: public alias for the internal lazy-state cleanup, so an external
  consumer sharing `BHL.VM` as a common singleton can force a fresh VM on next access.
- `BHLRuntimeException`: combines a caught C# exception (or plain message) with a live
  `VM.Fiber`'s BHL-side stack trace into one readable trace. No Unity/scripting-specific
  dependency, so it's part of the `NO_UNITY` build too. Matches (rather than being
  subclassed by) `BitGames.Scripting.BHLRuntimeException`, which keeps its own identical
  standalone implementation to stay usable by a consumer that doesn't reference UnityBHL.
- `BHLProjectConfig`: a resolved `bhl.proj`'s module->source-file mapping (e.g. for a
  DAP client's "jump to source") via `TryMapModuleToFile`, plus the raw
  `inc_dirs`/`src_dirs`/`defines`/`result_file` (named to match the old
  `BitGames.Scripting.BHLProjectConf` DTO, so that consumer can return this instance
  as-is instead of mapping onto a separate shape).
- `Settings` moved from `Editor/` to `Runtime/` (still excluded from the `NO_UNITY`
  build - it's a `ScriptableObject`) and gained a `BhlProj` property, so a
  Runtime-visible consumer (e.g. scripting's `BHLConfig`) can reference it directly
  instead of needing an Editor-only bridge. `BhlProj` parses `bhl.proj` lazily on first
  access (Editor-only - `bhl.ProjectConf` can't be used outside `UNITY_EDITOR`/
  `BHL_PARSER`), so it's available even before anything else has triggered a compile;
  `EditorCompiler.LoadProjectConf` still refreshes it after every real compile to stay
  maximally fresh. In an actual Player build it's always `null`.
- `VMCreator` now takes an optional `IUserBindings` - matches the more advanced VM
  construction `BitGames.Scripting.VMCreator` already had. With no bindings passed, it
  keeps using `VM.FromBytecode` (bundle-declared bindings auto-discovered via
  reflection); with bindings passed, it registers them directly on a fresh `Types`
  instead (`new VM(types, new ModuleLoader(types, bundle))`), and the bundle-declared
  bindings aren't consulted.
- `PostprocBridge`: mirrors `bhl.proj`'s `postproc_sources` `.cs` files into a
  generated, Editor-only asmdef (`Assets/BHL/Generated/Postproc`) referencing `bhl`, so
  Unity compiles them itself. A prebuilt `postproc_dll` loaded via reflection can never
  work here - Unity compiles `bhl` from source into its own assembly, so any externally
  `dotnet build`-built dll's `IFrontPostProcessor` is a different, unrelated type,
  regardless of target framework. The generated asmdef is named after `postproc_dll`
  (e.g. `bhl_postprocess_client.dll` -> asmdef `bhl_postprocess_client`), so `bhl`'s
  `AppDomainPostProcessor` can look up that exact assembly by name instead of scanning
  every loaded one. Synced on every `EditorCompiler.LoadProjectConf` call; a synced file
  only rewrites when its content actually changed, so it doesn't force a needless
  recompile. Also generates a `csc.rsp` defining `BHL_POSTPROC` (a .asmdef has no JSON
  field for a custom `#define`) - the same symbol bhl's CLI build defines for
  `postproc_dll`, so `postproc_sources`' own code can tell "am I part of a postproc
  build" apart from "am I outside Unity".
- `Settings.postprocEnvVars`: a list of name/value pairs, set via
  `Environment.SetEnvironmentVariable` before every Editor compile - for
  `postproc_sources` code that reads environment variables a CLI/CI build sets
  externally (e.g. a project root path), which otherwise wouldn't exist inside the
  Editor process. Editable from the Settings Inspector and the Control Panel (which
  embeds it), re-applied on every compile so an edit takes effect without a domain
  reload. A value can reference `$(DATA_PATH)`, expanded to `Application.dataPath` -
  useful since that path differs across machines/checkouts.
- `PostprocBridge` now logs when it syncs sources into the generated asmdef (and when
  it removes it, if postproc isn't configured), and `AppDomainPostProcessor` logs which
  `IFrontPostProcessor` implementation(s) it found (or a warning if none), plus how many
  times `Patch()` was actually called each compile (via `Tally()`) - a count of 0 with
  unchanged `.bhl` files usually means every file hit the compile cache
  (`ProjectConf.use_cache`), since `Patch()` only runs for files actually recompiled.
- Control Panel: "Force Recompile" next to "Hot Recompile" - the latter is a silent
  no-op ("BHL no stale files detected") if nothing in `src_dirs`/`bhl.proj`/self changed
  since the last successful compile, since `bhl`'s own top-level cache check (and the
  per-file one) short-circuits before the pipeline (postproc included) ever runs. Force
  Recompile sets `proj.use_cache = false` first, bypassing both.
- `Settings.postprocAsmdefDir`: where `PostprocBridge` generates the postproc asmdef,
  previously hardcoded to `Assets/BHL/Generated/Postproc` (still the default). Editable
  from the Settings Inspector/Control Panel.
- `Settings.forceRecompileOnPlay`: bypasses `bhl`'s compile cache on entering Play Mode
  too (same effect as the Control Panel's Force Recompile), for when the default
  incremental compile there needs to guarantee postproc runs on every entry. Off by
  default - unlike a manual Force Recompile click, this cost would otherwise be paid on
  every single Play Mode entry.
- `BHL/Force Recompile` menu item, matching the Control Panel's "Force Recompile" button
  (bypasses `bhl`'s compile cache without wiping `tmp_dir`, unlike `BHL/Recompile`'s
  underlying `Rebuild`/`RebuildAll`).
- `PostprocBridge.Clear`: deletes the generated postproc asmdef folder outright, factored
  out of `Sync`'s own "nothing configured" cleanup so a "Clear" button next to "Postproc
  Asmdef Dir" in the Settings Inspector can trigger the same thing manually (e.g. to
  force a full re-sync from scratch instead of the usual per-file content diff).
- `BHL/VM Stats` window: the Play-Mode-only per-VM pool/exec stats ("Tracker" section)
  split out of the Control Panel, which was otherwise entirely about Editor-time
  settings/compiling. `DrawDebugStatus` (the main `BHL.VM`'s debug toggle/status) stays
  in the Control Panel; `DrawVMDebugStatus`/`DrawPoolStats`/`DrawExecStats` (for every
  other `VMTracker`-tracked VM) moved to the new window.
- Control Panel footer showing the resolved UnityBHL package version
  (`PackageInfo.FindForAssembly`, so it reflects however the package was actually
  resolved - registry/local/git, including a git tag's semver if that's how it was
  pinned), pinned below the scroll view. Now also shows the `bhl` runtime's own version
  (`bhl.Version.Name`, the same string the CLI's `"BHL(vX.Y.Z) ..."` banner prints) -
  read directly rather than via `PackageInfo`, since it's not tied to how (or whether)
  the `bhl` package itself is resolved via UPM.
- `Settings.hotReloadOnRecompile`: when on, the Control Panel's Recompile/Force
  Recompile buttons migrate already-running `ScriptBHL` instances in place
  (`BHL.ReloadModules`, over every module `ScriptBHL.RegisteredModules` reports as
  having a live instance) instead of just swapping in the new bytecode for future loads
  (`BHL.SetBytecode`) - falls back to `SetBytecode` if no VM exists yet, since
  `ReloadModules` is a no-op in that case. Off by default. Never applies to Recompile On
  File Changes (`ControlPanel.Recompile`'s new `allowHotReload` parameter, `false` for
  `AutoCompileController`'s call), which always uses the plain swap regardless of this
  setting.
- `BHL/About` menu item: a small fixed-size utility window with the package description
  and the UnityBHL/BHL versions (same sources as the Control Panel's footer -
  `PackageInfo.FindForAssembly` and `bhl.Version.Name`). Given a low menu priority so it
  sits at the bottom of the `BHL` menu, past a separator.
- `Settings.bakedBundlePath` ("Result Resource Path") is now auto-loaded by `BHL.VM` on device: if
  `_lastBytecode` is empty outside the Editor, `EnsureVM` tries `Resources.Load` at the
  path relative to `bakedBundlePath`'s `Resources/` folder, silently no-oping if nothing's
  there. Previously a Player build had to call `BHL.LoadBakedBundle()` explicitly, whose
  own default source is hardcoded to `Resources/bhl` regardless of `bakedBundlePath`. This
  auto-load also checks `VM.Loader` first, so a custom `IVMCreator` (e.g. `VMCreator`
  wrapping its own `BytecodeSource`) that already returns a fully-attached VM wins
  deliberately and is left alone. The Editor's own restore
  (`TryRestoreLastEditorCompile`) does NOT get this same deference - it always overrides
  whatever the factory attached, since it exists specifically to carry the freshest
  hot-reloaded/recompiled bytecode across a domain reload, not to act as a fallback.

### Changed
- `Settings.forceRecompileOnPlay` replaced by `Settings.recompileOnPlay` (on by
  default), which now actually gates whether `BHLAssetPostprocessor.CompileAndLoad`
  runs at all on entering Play Mode, rather than just toggling `use_cache` on a compile
  that always ran regardless - turning it off skips `EditorCompiler.Compile` (and its
  postproc setup/logging) entirely, reusing whatever bytecode is already loaded.
- `BHL/Recompile` and `BHL/Force Recompile` now show a live-updating progress bar (the
  compiler's latest log line, `UnityConsoleLogger.LastLine`) instead of a static
  "Compiling..." bar for the whole compile - `EditorCompiler.WithProgressBar` now runs
  the compile via `Task.Run` and polls it from the calling thread instead of blocking on
  it directly. `CompileWithProgressBar` (used by Play Mode entry) benefits too, since it
  shares the same helper. `UnityConsoleLogger.LastLine` is reset before each compile
  starts, so the bar's first frame(s) don't show a stale line left over from the
  previous one (it's a static field). The fill percentage now reflects real progress
  (`NextProgressStep` maps the compiler's pipeline-stage log lines to a coarse step:
  "register bindings" -> 0, "parse" -> 1, "compile" -> 2, "postproc" -> 3, "all done" ->
  4, out of `ProgressStepCount` = 4), replacing the earlier back-and-forth sweep - a line
  matching none of those (e.g. "BHL cache blob write") keeps the last detected step.
- Control Panel's own inline compile-progress bar, and its modal fallback for when the
  window isn't open, now use the same step-based progress (`EditorCompiler.
  NextProgressStep`/`ProgressStepCount`, both now `internal`) instead of a
  `Mathf.PingPong` sweep - `_compileStep` and `UnityConsoleLogger.LastLine` are reset
  when a new Recompile/Force Recompile starts, for the same reason as above.
- Explicit menu priorities (`BHL/Control Panel` < `BHL/Recompile` < `BHL/Force
  Recompile`) so they always appear in that order - Unity's default alphabetical
  sort put "Force Recompile" above "Recompile".
- Settings Inspector: Debug Port moved to the top, ahead of `bhl.proj` path.
- Control Panel's "Hot Recompile" renamed to "Recompile", and it (and "Force
  Recompile") now also bake to `bakedBundlePath` if it's set, matching `BHL/Recompile`'s
  own behavior - the baked asset no longer silently drifts from what's running
  in-memory. `EditorCompiler.WriteBakedBundle` is now `internal` so the Control Panel
  can call it.
- `BHL/Rebuild and Bake` menu item renamed to `BHL/Recompile` (later moved onto a new
  `EditorCompiler.Recompile` method - see Fixed below - since the rename alone left the
  menu item's actual behavior mismatched with its new name).
- Control Panel: "Auto On File Changes" renamed to "Auto Recompile On File Changes" and
  moved inside the "Settings" foldout.
- Recompile/Force Recompile button tooltips now spell out the incremental-vs-always-full
  distinction between them, previously only in a code comment.
- Settings Inspector: `bakedBundlePath`'s label is now "Result Resource Path" (was
  "Result Path", before that "Result Bundle Path", briefly "Result Asset Path"), with a
  tooltip clarifying it's unrelated to `bhl.proj`'s own `result_file` - that's never used
  in the Editor (compiles always go to `Library/BHL`, so other tools sharing the same
  `bhl.proj`, e.g. a CLI/CI build, aren't affected by Editor compiles). When empty, a
  faint inline hint now shows the expected format and consequence directly inside the
  field ("e.g. Assets/Resources/bhl.bytes - not baked if left empty"), replacing the
  earlier "not set - bhl.proj's result_file is ignored in the Editor" wording (still
  covered by the tooltip).
- Settings Inspector: "Script sources" now lists `src_dirs` on one line (`, `-separated)
  instead of one per line, still color-coded per entry (green exists / red missing),
  drawn as an array literal (`[a, b, ...]`). A new "Postproc sources" list shows
  `postproc_sources` the same way (color-coded by `File.Exists` instead of
  `Directory.Exists`) - both now share a `DrawPathArray` helper.
- `SettingsInspector.OnInspectorGUI` split into `DrawMainFields`/`DrawResultPath`, so the
  Control Panel's "Recompile On File Changes" toggle can be interleaved between them -
  it now shows below "Recompile On Play" but above "Result Resource Path".

### Fixed
- `EditorCompiler.Compile` now applies `bhl.proj`'s postprocessing - previously
  silently ignored, since `CompileConf.postproc` was never set and defaulted to
  `EmptyPostProcessor`. In the Editor this always uses `bhl`'s `AppDomainPostProcessor`
  (see `PostprocBridge` above), not `postproc_dll` directly - only the CLI/headless
  build uses `postproc_dll`.
- `BHL/Recompile` actually forced a full, cache-bypassing rebuild (`RebuildAll`, wiping
  `tmp_dir` too) despite its name now matching the Control Panel's plain, incremental
  "Recompile" button - a leftover from the earlier menu-label-only rename. It now calls
  a new `EditorCompiler.Recompile` method mirroring the Control Panel button exactly.
  The old forced-rebuild behavior is preserved as `EditorCompiler.Rebuild` (no menu item
  of its own now), still reachable via
  `Unity -batchmode -executeMethod UnityBHL.EditorCompiler.Rebuild` for CI.
- Control Panel's Recompile/Force Recompile now always show the same modal
  `EditorUtility.DisplayProgressBar` the `BHL/Recompile`/`Force Recompile` menu items
  use, instead of an inline bar drawn inside the window (only falling back to a modal
  one when the window was closed). `DrawCompileProgress` removed; `PollCompile` no
  longer special-cases whether the window is open. Still non-blocking under the hood
  (`Task.Run` + `EditorApplication.update` polling, not `EditorCompiler.WithProgressBar`'s
  blocking loop) - `AutoCompileController`'s auto-compile-on-file-change still calls this
  same method and must not freeze the Editor while doing so.
- `BHL.LastEditorCompilePath` (`Library/BHL/bhl.bytes`) behaved as a persistent cache
  rather than a one-time domain-reload bridge: with "Recompile On Play" off, Play Mode
  could reattach a stale compile from a previous session, and since
  `EditorCompiler.LoadProjectConf` always forces `result_file` to this same path, it
  could also shadow bytecode a separate CLI/CI build had just produced.
  `TryRestoreLastEditorCompile` now deletes the file right after reading it, so each
  write is consumed exactly once.
