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

C# functions bound as BHL coroutines without hand written coroutine classes: an async static function returning
`BhlTask` or `BhlFinallyTask`, whose awaits run in
the ticks of the BHL call awaiting it and are cancelled with it. A separate asmdef
`BitGames.BhlTask` (namespace `BitGames.BHL`) with a Roslyn analyzer; `ToBhlTask(UniTask)` only when
the project has UniTask. Details: [Documentation~/bhltask.md](Documentation~/bhltask.md).

## Bindings generator

`Tools~/bindgen` makes BHL bindings from C# sources by parsing them, without compiling. The editor runs the same
generator in-process: on a change of a source and by **Generate Bindings** of the Control Panel. The cheat sheet of
the attributes and the types is [Documentation~/bindings.md](Documentation~/bindings.md), the attributes themselves
are in `Runtime/BhlBindAttribute.cs`.

### Config

`bhl_bindgen.json` next to `bhl.proj`:

| Field | What |
|---|---|
| `sources` | globs of the C# sources, `!` excludes (e.g. the generated code); the declarations are found in them too |
| `result_dir` | where the bindings are written |
| `namespace`, `register_class` | the namespace and the class of the output, the project registers it |
| `imports` | the `using`s of the output |
| `custom_rw` | the value types kept in `Val` by the project's `Val.Encode` / `Decode` |
| `decls_namespace` | the C# namespace of the declarations, see «Namespaces» |
| `includes` | the configs of other bindings it uses, see «Includes» |

The main output file `register.cs` starts with `//dep: <path>` lines, the files it's made of relative to the config:
the editor regenerates the bindings when one of them changes, the gamectl helpers of `bit/taskman_bhl` read them too
(its README, «Bindings generator»).

### The tool

The release zip `unitybhl.zip` of a version tag (`.github/workflows/release.yml`) has the tool prebuilt in
`Tools~/bindgen/dist` (`Tools~/bindgen/dist.sh`, not in git). A project taking the package from it needs only the
.NET 8 runtime:

```json
"dist": { "url": "https://github.com/pachanga/UnityBHL/releases/download/v<version>/unitybhl.zip", "type": "zip" }
```

From the tag's archive or a checkout taskman_bhl builds the tool from the sources (.NET 8 SDK); the env var
`BHL_BINDGEN_REBUILD=1` makes it do so with `dist/` too.

### Functions

```csharp
static partial class Script
{
  [BhlBind] public static void Vibrate(float power = 1f) { .. }   // BHL name = C# name
  [BhlBind("Shake")] public static void DoShake(VM vm) { .. }      // another BHL name; a first VM gets exec.vm
  [BhlBind] public static void Later(VM.ExecState exec,            // a first VM.ExecState gets the call's exec
    [BhlType(typeof(Action))] VM.FuncPtr fn) { .. }                // a func argument as is
}

// or by a declaration (in any of the sources), for code you can't or don't want to annotate
[BhlBind(typeof(Script), nameof(Script.Vibrate))] interface HapticsDecls {}
```

A function returning `BhlTask` / `BhlFinallyTask` is a coroutine (see BhlTask above).

### Classes

A class is declared by an interface: `[BhlClass(typeof(T))]` (static methods: `[BhlStatic]`, operators: `[BhlOp]`,
value types: `Pod`, `CustomRW`), an interface by `[BhlInterface]`. It's named in BHL as the declaring interface, or by
the name given first (`[BhlClass("UnityObject", typeof(UnityEngine.Object))]`). The parent class and the interfaces
are the declaring interface's bases when they're declarations too, else `Parent`, `Implements`, `Inherits` name them
(declared elsewhere, e.g. by meta). A static method with a body is bound as a method of the class: of the native
type first, a method on it, else a static one.

```csharp
namespace Game.Bind.Decls
{
[BhlClass(typeof(UnityEngine.PlayerPrefs))]
interface PlayerPrefs
{
  [BhlStatic] int GetInt(string key, int defaultValue = 0);   // PlayerPrefs.GetInt(..) in BHL
}

[BhlClass("UnityObject", typeof(UnityEngine.Object))]
interface UnityObjectDecl
{
  [BhlOp("==")] bool Equals(UnityEngine.Object self, UnityEngine.Object v);   // a == b, the C# operator
}

[BhlClass(typeof(Game.BattleGame))]
interface BattleGame
{
  static void AddTask(Game.BattleGame self, VM.ExecState exec, [BhlType(typeof(Action))] VM.FuncPtr fn)
    => self.AddTask(exec.Spawn(fn));                          // battle_game.AddTask(fn) in BHL
}

[BhlClass(typeof(Game.UIPlate), Parent = "UIChip")]   // UIChip is declared elsewhere
interface UIPlate {}

[BhlClass(typeof(Game.UIBarrier))]
interface UIBarrier : UIPlate {}                        // UIPlate is a declaration: the parent class
}
```

In a namespace of their own the declarations don't hide the native types they're named after; there `typeof(..)` of
such a type is qualified.

### Coroutine classes

A hand written coroutine is declared by its own class, `[BhlCoroutine] partial class <Name>`: the arguments are the
parameters of its `Init([VM.ExecState exec, ]..)`, the result is `Returns = typeof(T)`. Without `exec`
(`bool Exec([out T result])`, `Cleanup()`) it's callable from C# as is. A new coroutine is better a `BhlTask` function.

### Types

Types are named in BHL as in C#, a `[BhlClass]` names its native type, `bhl.IType` is BHL's `Type`. A non-generic
`IList` is `[]any`, `IList<T>` an array of `T` (a field may be a `List<T>` too). `[BhlType("Name")]` on a parameter, a
return (`[return: BhlType("IList")]`) or a `[BhlClass]` property makes it the BHL class `Name` instead, e.g. a type
declared elsewhere under another name (`[BhlType("M3FInt")] FInt time`).

A type which isn't a `[BhlClass]` must be declared in the `sources`, else it's an error: an enum of a generated source
not generated yet would be bound as a class otherwise.

### Namespaces

A BHL name may be in a namespace, `[BhlBind("LM3.UIRefresh")]`, `[BhlClass("LM3.Chip", typeof(..))]`: the symbol is
defined in the nested namespaces (`types.ns.Nest("LM3")`) and referred to by the full name; a C# identifier made of it
has `_` for `.`: `Types_LM3_Chip`, `Script.LM3_Chip_<Method>`.

With `decls_namespace` (e.g. `"Game.Bind.Decls"`) a declaration in a C# namespace under it is in the BHL namespace of
the rest: `[BhlClass(typeof(T))] interface Chip` in `Game.Bind.Decls.LM3` is `LM3.Chip`, the functions of a
`[BhlBind(typeof(T), ..)]` interface there are `LM3.<Name>`; an explicit name is kept as is.

### Includes

`includes` names the configs of other bindings it uses, e.g. of a package: `"includes": ["../Packages/M3/bhl_bindgen.json"]`.
Each is generated to its own `result_dir` with its own `register_class` (the generated halves of its `[BhlCoroutine]`
classes are in its assembly, next to the hand written ones), the project registers each. Their declarations are known
to the including config (classes, value types, parents and interfaces across them), not the other way round. One run
generates them all.

A native type may be named by classes of different configs, e.g. `UnityEngine.Vector3` as `Vector3` by the project and
as `M2Vector3` by a package: a config's own name is used, else the one of the config it includes. Where a signature
needs the other name, `[BhlType("M2Vector3")]` on the parameter or `[return: BhlType("M2Vector3")]`. Within one config
a native type has one name.

## Editor tools

- **BHL/Control Panel** - compile status, errors, settings (`bhl.proj` and `bhl_bindgen.json`, editable in place), auto-compile
  toggle; **Generate Bindings** when the project has `bhl_bindgen.json` next to bhl.proj (they're regenerated on changes by
  themselves too).
- **BHL/Recompile** - clean compile and baking a bytecode bundle for
  Player builds.
- **BHL/Force Recompile** - like Recompile, but bypasses the compile cache.
- **BHL/VM Stats** - Play-Mode-only pool/exec stats for every `VMTracker`-tracked VM.
- **BHL/About** - package description and UnityBHL/BHL versions.

## License

MIT - see [LICENSE](LICENSE).
