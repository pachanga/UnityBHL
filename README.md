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

C# functions bound as BHL coroutines without `Script_*` classes: an async static function returning
`BhlTask` or `BhlFinallyTask`, whose awaits run in
the ticks of the BHL call awaiting it and are cancelled with it. A separate asmdef
`BitGames.BhlTask` (namespace `BitGames.BHL`) with a Roslyn analyzer; `ToBhlTask(UniTask)` only when
the project has UniTask. Details, in Russian: [Documentation~/bhltask.md](Documentation~/bhltask.md).

## Bindings generator

`Tools~/bindgen` (also run in-process by the editor) makes BHL bindings from C# sources without compiling
them, configured by `bhl_bindgen.json` next to bhl.proj (`sources`, `!` excluding, e.g. the generated code, `result_dir`, `imports`,
`custom_rw`, `type_names_file`, `includes`); the declarations (below) are found in the sources too. A function is bound in
either form:

```csharp
static partial class Script
{
  [BhlBind] public static void Vibrate(float power = 1f) { .. }   // on the method, BHL name = C# name
  [BhlBind("Shake")] public static void DoShake(VM vm) { .. }      // another BHL name; a first VM parameter gets exec.vm
}

// or in a declarations file (any of the sources), for code you can't or don't want to annotate
[BhlBind(typeof(Script), nameof(Script.Vibrate))] interface HapticsDecls {}
```

A method returning `BhlTask`/`BhlFinallyTask` is bound as a coroutine (see BhlTask below). Types are named in
BHL as in C#, a `[BhlClass]` names its native type, `type_names_file` renames the ones declared elsewhere
(e.g. by meta). A non-generic `IList` is
`[]any`, `IList<T>` an array of `T` (a field may be a `List<T>` too), `[BhlType("Name")]` on a parameter or a return
(`[return: BhlType("IList")]`) makes it the BHL class `Name` instead. Classes: `[BhlClass]` (static methods: `[BhlStatic]`, operators: `[BhlOp]`,
interfaces: `Implements`, value types: `Pod`, `CustomRW` with the project's `Val.Encode`/`Decode`, hand written field accessors: `[BhlCustom]`
on a property), interfaces: `[BhlInterface]`,
hand written functions and coroutines: `[BhlCustom]` (on a class method too, `Script_<Class>.Method_<Name>`),
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

[BhlClass(typeof(Game.BattleGame))]
interface BattleGame
{
  [BhlCustom] void AddTask([BhlCoroutine] Action fn);   // Script_BattleGame.Method_AddTask.Exec(exec, args)
}

[BhlClass(typeof(Game.UIPlate), Parent = "UIChip")]   // UIChip is declared elsewhere
interface UIPlate {}

[BhlClass(typeof(Game.UIBarrier))]
interface UIBarrier : UIPlate {}                        // UIPlate is a declaration: the parent class
}
```

`includes` names the configs of other bindings it uses, e.g. of a package: `"includes": ["../Packages/M3/bhl_bindgen.json"]`.
Each is generated to its own `result_dir` with its own `register_class` (the generated halves of its `[BhlCustom]`
bindings are in its assembly, next to the hand written ones), the project registers each; their declarations are known
to the including config (classes, value types, parents and interfaces across them), not the other way round. One run
generates them all.

The main output file `register.cs` starts with `//dep: <path>` lines, the files it's made of relative to the config
(the editor regenerates it when one of them changes); `type_names_file` has `<C# type> <BHL type>` lines, `#` comments.
Both are read by the gamectl helpers of `bit/taskman_bhl` too (its README, «Bindings generator»).

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
