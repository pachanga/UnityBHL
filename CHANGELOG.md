# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.21.0] - 2026-10-08

### Changed
- Compile errors were shown in three places at once (Unity Console, the Control Panel's
  inline list, and `BHLErrorWindow`'s popup - the popup used to skip itself whenever the
  Control Panel happened to be open, since the Control Panel showed the same list
  inline). The Control Panel no longer shows errors inline; `BHLErrorWindow` is now the
  one popup, shown unconditionally, and still duplicates to Unity's Console as before.
- Control Panel: Recompile/Force Recompile buttons moved above the Settings section
  (previously below it), so they're reachable without the Settings foldout being open or
  scrolling past it.

## [0.20.0] - 2026-10-07

### Added
- `DrawModuleField`/`[BHLModuleField]` now red-tint the field when the typed name doesn't
  exactly match an existing module, same as `DrawFuncField`/`[BHLFuncField]` already did -
  previously a partial match showed completions below but the field itself gave no
  indication that the current value wasn't a real, committed module.

### Fixed
- `DrawFuncField`/`[BHLFuncField]` only red-tinted on an outright mismatch (`valid ==
  false`), not on `valid == null` (no module selected, or the module itself doesn't
  resolve) - so a symbol name typed in with no valid module behind it looked exactly as
  "fine" as a correct one. Now tinted whenever `valid != true` and something's actually
  been typed (still untinted while genuinely empty, same as before).

## [0.19.1] - 2026-10-07

### Changed
- Package description (`package.json`/`composer.json`, shown in Unity's Package Manager
  "About" window): "Unity SDK for BHL scripting typed language" -> "Unity SDK for BHL
  scripting language".

## [0.19.0] - 2026-10-07

### Changed
- `FindModuleCompletions`' directory-walk cache (added in 0.18.1) was a 2s TTL - it still
  re-walked in the background every 2s regardless of whether anything was actually
  typed or changed on disk. Replaced with a lazy, cache-until-invalidated approach (same
  as `ClassIntrospection`): the walk now runs once, on first use, and only re-runs if the
  search roots themselves change (e.g. `bhl.proj` repointed in Settings) or the new
  `BHLModuleBrowser.Invalidate()` is called explicitly.

### Added
- `DrawModuleField` now has a "Refresh" button next to the field, calling the new
  `Invalidate()` - the module list it draws from is cached (see above), so this is how a
  consumer recovers from a `.bhl` file added/removed outside Unity (nothing else would
  trigger a re-scan). `ScriptBHLInspector`'s fallback and `ATFWnd`'s Module field both
  get this for free.

## [0.18.1] - 2026-10-07

### Fixed
- `FindModuleCompletions` re-walked every `src_dirs`/`inc_dirs` root (recursively,
  matching `*.bhl`) on every call - cheap for a small project, but the walk visits every
  filesystem entry under each root, not just the matching ones, so a root containing tens
  of thousands of unrelated files (e.g. generated data living alongside real sources)
  made it genuinely slow. Since a consumer like `ATFWnd`'s Module field calls this on
  every OnGUI repaint (forced at ~10/sec via `Repaint()`), that was visible as general UI
  sluggishness in the whole window. The walk itself is now cached for 2s (keyed by the
  current search roots) - only the in-memory substring filter re-runs every call.

## [0.18.0] - 2026-10-07

### Added
- `ScriptBHL`'s own script icon (Inspector header, Hierarchy/Project rows, "Add
  Component" search) is now the BHL logo, assigned via `MonoImporter.SetIcon` on editor
  load (`ScriptBHLIconAssigner`) rather than a hand-edited `.meta`, and only when not
  already set.
- `BHLModuleBrowser.DrawMark` is now `public`, so a consumer with its own,
  differently-sourced picker (like `ScriptBHLInspector`'s Class field below) can still
  show the same icon.

### Changed
- `FindModuleFile`'s manual fallback search (combining the module name with each
  `GetSearchRoots()` entry) was redundant - `TryMapModuleToFile`/`IncludePath.
  TryIncludePaths` already searches those exact same roots the same way. Removed.
- `FindFuncCompletions`/`DrawFuncField` can also match class declarations within their
  (already known) module, gated by a new `BHLSymbolKind` filter (`All`/`Func`/`Class`,
  defaulting to `Func` to keep prior behavior; `func_pattern` still only constrains the
  `Func` half - class matching always uses the new `AnyClassPattern`). Dropdown rows show
  each result's kind when `All`.
- `ScriptBHLInspector`'s Class picker is now a type-to-search field with an autocomplete
  dropdown (via `BHLModuleBrowser.DrawCompletions`), instead of only a button opening a
  `GenericMenu` (still available via a "Browse..." button alongside it). Source stays
  `ClassIntrospection.GetAllClasses()` (compiled, `BHLComponent`-filtered), not a text
  scan - and since class names aren't unique across modules, the search text only
  filters the dropdown; it doesn't commit `ModuleName`/`ClassName` directly, only
  picking a specific (module, class) pair does.
- Reverted the whole-project symbol search (`FindSymbolCompletions`/`FindSymbolModule`/
  `DrawSymbolField`, added then removed within this same release) - it re-read and
  regex-scanned every `.bhl` file under `GetSearchRoots()` on every GUI call, which is
  expensive for a large project. Back to the cheaper two-step schema: pick a module first
  (`DrawModuleField`, just matches file paths - no file content read), then a symbol
  within that one already-known module (`DrawFuncField`, now also `BHLSymbolKind`-aware -
  see above). ATF's Run panel now draws Module and Function as two fields again instead
  of one combined row.

### Fixed
- `ScriptBHLInspector`'s no-compiled-classes fallback (Module/Class as plain fields)
  never actually had autocomplete - both were plain `PropertyField`s, just drawn with the
  same BHL icon mark as the real picker, which looked wired up but wasn't. Now uses
  `BHLModuleBrowser.DrawModuleField`/`DrawFuncField` (the latter with the `Class` kind),
  so Module autocompletes from file paths and Class scans the chosen module's file -
  still manually editable, now for real with working completions.
- `BHLModuleBrowser.GetSearchRoots` fell back to `bhl.proj`'s own containing folder when
  `inc_dirs` wasn't configured - a UnityBHL-specific special case that often isn't where
  scripts actually live. Now falls back to `src_dirs` instead, matching
  `ProjectConf.Setup()`'s own `inc_dirs`-falls-back-to-`src_dirs` convention.
- `FindFuncCompletions` only ever captured a symbol's bare name, ignoring any enclosing
  `namespace X { ... }` - a func/class declared inside one couldn't be found/resolved by
  its actual fully-qualified name (`Foo.Bar`). Scanning now tracks namespace nesting (via
  brace counting, with comments and string-literal contents stripped first so braces or
  keyword-looking text inside either don't corrupt it) and prefixes matches accordingly,
  same as `ClassIntrospection`'s compiled-symbol-table walk already did for the Class
  picker.

## [0.17.0] - 2026-10-06

### Added
- `BHLModuleBrowser` (Editor): reusable BHL module/function lookup and autocomplete -
  `FindModuleFile`, `FindModuleCompletions`, `FindFuncCompletions` (caller-supplied
  signature regex), `DrawCompletions` (layout and rect-based overloads), and composite
  `DrawModuleField`/`DrawFuncField` helpers (label + BHL mark + field + dropdown, and
  red-tint-on-invalid for func) for a plain `OnGUI()` caller. Extracted from ATF, which
  now just calls these instead of its own copy of this logic.
- `[BHLModuleField]`/`[BHLFuncField(moduleFieldName, funcPattern)]`: same autocomplete,
  declarative attributes for a serialized `string` field, via `PropertyDrawer`s in
  `Editor/BHLFieldDrawers.cs`.
- `EditorCompiler.Icon`/`IconSmall` are now `public` (were `internal`), so a consumer
  package in a different assembly can use the BHL logo mark too.

## [0.16.0] - 2026-09-30

### Changed
- `VMCreator.MakeVM` now caches and reuses its `Types` instance across calls (when
  explicit `Bindings` are configured) instead of running `Bindings.Register` on a fresh
  one every time - `Types` is a shared, read-mostly declaration catalog once populated,
  so this is safe, and skips redoing potentially-slow bindings registration per VM.

## [0.15.3] - 2026-09-30

### Fixed
- `CreateBHLScript`'s script-creation flow used `EndNameEditAction`/int instance IDs,
  replaced by `AssetCreationEndAction`/`EntityId` in Unity 6000.4 and turned into a
  compile error in 6000.6. Added a `CreateFileAction` shim that targets whichever API
  the running Unity version actually has.

## [0.15.2] - 2026-09-29

### Changed
- `BHL.Cleanup()` now clears `_lastBytecode` outside the Editor, so a compiled bytecode
  blob isn't held in memory past the VM that used it if nothing reloads one afterward.
  Editor-only exclusion: `EnsureVM()` there relies on `_lastBytecode` surviving `Cleanup()`
  (directly when domain reload is off, or via `Library/BHL/bhl.bytes` when it's on).

## [0.15.1] - 2026-09-29

### Changed
- Settings Inspector: "Postproc Env Vars" now shows below "Postproc sources" (was above).
- Compacted verbose toggle labels: "Recompile On File Changes" -> "Recompile On Changes",
  "Hot Reload On Recompile" -> "Hot Reload".
- Compacted verbose tooltip text across `Settings` fields and the Recompile/Force
  Recompile button tooltips - down to one short sentence each.

## [0.15.0] - 2026-09-29

### Added
- "Override Locally"/"Stop Overriding" button in the Settings Inspector/Control Panel:
  duplicates every `Settings` field into an `EditorPrefs`-backed copy
  (`Settings.IsOverriddenLocally`), which `Settings.Instance` then transparently returns
  instead of the shared `BHLSettings.asset` - lets a developer override any setting
  (e.g. `debugPort`, `postprocEnvVars`) on their own machine without touching the
  git-tracked asset. `EditorPrefs` key is namespaced per-project (via `Application.dataPath`),
  so it can't leak across other Unity projects on the same machine.

### Changed
- "Recompile On File Changes" is now a real `Settings` field
  (`recompileOnFileChanges`) instead of its own dedicated `EditorPrefs` key - it can now
  be a shared, git-tracked team default, while still supporting a per-developer override
  via the above.

## [0.14.0] - 2026-09-29

### Fixed
- `AutoCompileController` ("Recompile On File Changes") snapshotted `src_dirs` once
  when its watcher started, so editing `bhl.proj` (e.g. adding/removing a `src_dirs`
  entry) had no effect until the watcher was restarted (toggled off/on, or a Play Mode
  round-trip). It now also watches `bhl.proj` itself and restarts automatically when it
  changes. Restart also no longer risks two poll threads briefly running at once (each
  poll thread now gets its own `CancellationToken` instead of sharing one stop flag).

## [0.13.0] - 2026-09-29

### Changed
- `.bhl` files use the actual BHL logo as their Project window icon instead of a
  borrowed built-in icon.

### Added
- `EditorCompiler.IconSmall`: a flattened, opaque BHL logo variant for the `.bhl`
  list-view row icon.

### Fixed
- `.bhl`/`bhl.proj` icons let Unity's own default file icon show through at the edge;
  now drawn with a small overdraw margin to fully cover it.
- The `.bhl` icon also incorrectly applied to the `com.bitgames.bhl` package folder
  (its path happens to end in `.bhl`); folders are now excluded.

## [0.12.0] - 2026-09-29

### Added
- `EditorCompiler.Icon`: the BHL logo (`Editor/Icons/bhl_logo.png`), resolved via
  `PackageInfo.assetPath` so it works whether the package is embedded or resolved into
  `Library/PackageCache`. Shown in the `BHL/About` window next to its text, and as the
  `BHL Control Panel`/`BHL VM Stats` windows' own tab icon (`titleContent.image`, set
  every `OnGUI` frame rather than once in `OnEnable` - `GetWindow<T>(title)` replaces
  `titleContent` with a fresh title-only `GUIContent` right after creation, which
  otherwise wiped out an image set any earlier).
- `Settings.logVerbosity`: gates whether the compiler's per-pipeline-stage log lines are
  printed to Unity's console, exposed as a "Verbose Logs" toggle in the Settings
  Inspector/Control Panel. Off (0) by default. Progress-bar tracking
  (`UnityConsoleLogger.LastLine`) is unaffected either way - only console output is
  gated, so turning this off doesn't break the Recompile/Force Recompile progress bars.

## [0.11.1] - 2026-09-28

### Fixed
- `Runtime/Bindings/UnityBindings.bhl` declared its version-info function as
  `BindingsInfo` (plural) instead of the name `bhl` actually looks for,
  `ProjectConf.DefaultBindingsInfoScriptName` = `BindingInfo` - the mismatch meant
  nothing was ever discovered, so `bhl.proj`'s `unity` bindings entry always failed
  with "does not declare a version" wherever this `.bhl` mirror is used (LSP/CLI, which
  never load the Unity assembly's `UnityBindings.cs`).

## [0.11.0] - 2026-09-28

### Changed
- `bhl.proj` "Save" button no longer requires the edit to pass `ProjectConf.ReadFromFile`
  validation - only "Revert" did before. Validation can itself be wrong (e.g. the
  `TryParseProjList` swallowing bug just fixed), so it now just downgrades to a warning
  HelpBox instead of blocking the save.

### Fixed
- `CreateEmptyProj`'s generated `includes` entry, when the UnityBHL package resolves
  into `Library/PackageCache` (the normal git/registry-dependency case), was a
  directory-level wildcard with no `bhl.proj` filename appended - `ExpandIncludes`
  treats the last segment as a file match, so this always threw "did not match any
  existing file". Only affects newly-generated `bhl.proj` files going forward; an
  existing one created before this fix needs `/bhl.proj` appended to that `includes`
  entry by hand.

## [0.10.1] - 2026-09-28

### Fixed
- `SettingsInspector.TryParseProjList` silently swallowed `bhl.proj` parse/setup
  exceptions and fell back to an empty list, so a real problem showed up as the
  misleading "No src_dirs/postproc_sources configured" instead of the actual error -
  now shown in the HelpBox.

## [0.10.0] - 2026-09-28

### Changed
- Settings Inspector now saves the `BHLSettings` asset to disk right after any edit
  (`AssetDatabase.SaveAssetIfDirty`, scoped to just this asset) instead of waiting for
  Unity's own save cycle (focus loss, domain reload, Editor quit). The in-memory
  `Settings.Instance` already reflected edits immediately either way.

## [0.9.1] - 2026-09-28

### Fixed
- `SettingsInspector.DrawPathArray` NullReferenceException on `paths.Count` when
  `bhl.proj` has no `postproc_sources` (or doesn't exist yet) - `_cachedSrcDirs`/
  `_cachedPostprocSources` could be `null` rather than empty.

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
