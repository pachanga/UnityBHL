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

### Fixed
- `EditorCompiler.Compile` now applies `bhl.proj`'s postprocessing - previously
  silently ignored, since `CompileConf.postproc` was never set and defaulted to
  `EmptyPostProcessor`. In the Editor this always uses `bhl`'s `AppDomainPostProcessor`
  (see `PostprocBridge` above), not `postproc_dll` directly - only the CLI/headless
  build uses `postproc_dll`.
