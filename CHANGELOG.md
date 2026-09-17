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
