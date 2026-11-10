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

`Tools~/bindgen` (also run in-process by the editor) makes BHL bindings from C# sources without compiling
them (a cheat sheet: [Documentation~/bindings.md](Documentation~/bindings.md)), configured by `bhl_bindgen.json` next to bhl.proj (`sources`, `!` excluding, e.g. the generated code, `result_dir`, `imports`,
`custom_rw`, `includes`); the declarations (below) are found in the sources too. The tool is
prebuilt in `Tools~/bindgen/dist` of the release zip `unitybhl.zip` (a version tag makes it, `.github/workflows/release.yml`;
not in git, `Tools~/bindgen/dist.sh`): a project taking the package from it, `"dist": { "url":
"https://github.com/pachanga/UnityBHL/releases/download/v<version>/unitybhl.zip", "type": "zip" }`, needs only the .NET 8
runtime, taskman_bhl runs it. From the tag's archive or a checkout taskman_bhl builds the tool (.NET 8 SDK), as with the
env var `BHL_BINDGEN_REBUILD=1`. A function is bound in either form:

```csharp
static partial class Script
{
  [BhlBind] public static void Vibrate(float power = 1f) { .. }   // on the method, BHL name = C# name
  [BhlBind("Shake")] public static void DoShake(VM vm) { .. }      // another BHL name; a first VM parameter gets exec.vm
  [BhlBind] public static void Later(VM.ExecState exec, [BhlType(typeof(Action))] VM.FuncPtr fn) { .. } // a first VM.ExecState gets exec, a func as is
}

// or in a declarations file (any of the sources), for code you can't or don't want to annotate
[BhlBind(typeof(Script), nameof(Script.Vibrate))] interface HapticsDecls {}
```

A method returning `BhlTask`/`BhlFinallyTask` is bound as a coroutine (see BhlTask below). Types are named in
BHL as in C#, a `[BhlClass]` names its native type, `bhl.IType` is BHL's `Type`. A non-generic `IList` is
`[]any`, `IList<T>` an array of `T` (a field may be a `List<T>` too), `[BhlType("Name")]` on a parameter, a return
(`[return: BhlType("IList")]`) or a `[BhlClass]` property makes it the BHL class `Name` instead, e.g. a type declared
elsewhere under another name (`[BhlType("M3FInt")] FInt time`). Classes: `[BhlClass]` (static methods: `[BhlStatic]`, operators: `[BhlOp]`,
interfaces: `Implements`, value types: `Pod`, `CustomRW` with the project's `Val.Encode`/`Decode`), interfaces: `[BhlInterface]`,
a hand written coroutine declared by its own class: `[BhlCoroutine] partial class <Name>`, the arguments are the
parameters of its `Init(VM.ExecState exec, ..)`, the result `Returns = typeof(T)`; a first `VM.ExecState` parameter
of a function or a class method gets the call's `exec`, `[BhlType(typeof(Action))] VM.FuncPtr fn` takes a func as is,
see `Runtime/BhlBindAttribute.cs`. A class or an interface is named in BHL as its declaring interface, or by the
name given first (`[BhlClass("UnityObject", typeof(UnityEngine.Object))]`). In a namespace of their own the
declarations don't hide the native types they're named after; there `typeof(..)` of such a type is qualified.
The parent class and the interfaces are the declaring interface's bases when they're declarations too, else they're
named by `Parent`, `Implements`, `Inherits` (declared elsewhere, e.g. by meta):

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

[BhlClass(typeof(Game.BattleGame),
  nameof(Script.BattleGame_AddTask))]                 // (BattleGame self, VM.ExecState exec, [BhlType(typeof(Action))] VM.FuncPtr fn)
interface BattleGame {}

[BhlClass(typeof(Game.UIPlate), Parent = "UIChip")]   // UIChip is declared elsewhere
interface UIPlate {}

[BhlClass(typeof(Game.UIBarrier))]
interface UIBarrier : UIPlate {}                        // UIPlate is a declaration: the parent class
}
```

A BHL name may be in a namespace, `[BhlBind("LM3.UIRefresh")]`, `[BhlClass("LM3.Chip", typeof(..))]`: the symbol is
defined in the nested namespaces (`types.ns.Nest("LM3")`) and referred to by the full name; a C# identifier made of
it has `_` for `.`, as metagen_bhl_bind's: `Types_LM3_Chip`, `Script.LM3_Chip_<Method>`.
With `decls_namespace` (e.g. `"Game.Bind.Decls"`) a declaration in a C# namespace under it is in the BHL namespace of
the rest: `[BhlClass(typeof(T))] interface Chip` in `Game.Bind.Decls.LM3` is `LM3.Chip`, the functions of a
`[BhlBind(typeof(T), ..)]` interface there are `LM3.<Name>`; an explicit name is kept as is.

`includes` names the configs of other bindings it uses, e.g. of a package: `"includes": ["../Packages/M3/bhl_bindgen.json"]`.
Each is generated to its own `result_dir` with its own `register_class` (the generated halves of its `[BhlCoroutine]`
classes are in its assembly, next to the hand written ones), the project registers each; their declarations are known
to the including config (classes, value types, parents and interfaces across them), not the other way round. One run
generates them all.

The main output file `register.cs` starts with `//dep: <path>` lines, the files it's made of relative to the config
(the editor regenerates it when one of them changes). They are read by the gamectl helpers of `bit/taskman_bhl` too (its README, «Bindings generator»).

## Editor tools

- **BHL/Control Panel** - compile status, errors, settings, auto-compile toggle; **Generate Bindings**
  when the project has `bhl_bindgen.json` next to bhl.proj (they're regenerated on changes by themselves too).
- **BHL/Recompile** - clean compile and baking a bytecode bundle for
  Player builds.
- **BHL/Force Recompile** - like Recompile, but bypasses the compile cache.
- **BHL/VM Stats** - Play-Mode-only pool/exec stats for every `VMTracker`-tracked VM.
- **BHL/About** - package description and UnityBHL/BHL versions.

## License

MIT - see [LICENSE](LICENSE).
