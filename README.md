# BHL for Unity

Unity integration for [BHL](https://github.com/bitdotgames/BHL): attach BHL script classes to
GameObjects via `ScriptBHL`, with hot reload of edited `.bhl` files while in Play Mode.

## Requirements

- Unity 2021.3+
- The `com.bitgames.bhl` package (BHL core)

## BHLComponent (optional base class)

`unity.BHLComponent` (part of the `unity` bindings module, no extra import/config needed)
declares an optional base class with `gameObject`/`transform` fields and virtual no-op
`Awake`/`Update`/`OnDestroy` methods. Extending it isn't required - `ScriptBHL` detects these
fields/methods by name either way - but it makes them IDE/LSP-discoverable and gives the
lifecycle methods an explicit, overridable contract:

```bhl
import "unity"

class MyScript : unity.BHLComponent
{
  override func Update() { ... }
}
```

## BhlTask

C# functions bound as BHL coroutines without `Script_*` classes: a static function returning
`BhlStep`, `BhlTask`, `BhlFinallyTask` or an `IEnumerator<BhlStep>` iterator, whose awaits run in
the ticks of the BHL call awaiting it and are cancelled with it. A separate asmdef
`BitGames.BhlTask` (namespace `BitGames.BHL`) with a Roslyn analyzer; `ToBhlTask(UniTask)` only when
the project has UniTask. Details, in Russian: [Documentation~/bhltask.md](Documentation~/bhltask.md).

## Editor tools

- **BHL/Control Panel** - compile status, errors, settings, auto-compile toggle.
- **BHL/Recompile** - clean compile and baking a bytecode bundle for
  Player builds.
- **BHL/Force Recompile** - like Recompile, but bypasses the compile cache.
- **BHL/VM Stats** - Play-Mode-only pool/exec stats for every `VMTracker`-tracked VM.
- **BHL/About** - package description and UnityBHL/BHL versions.

## License

MIT - see [LICENSE](LICENSE).
