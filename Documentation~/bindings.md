# C# → BHL bindings (bindgen)

A cheat sheet. The generator is `Tools~/bindgen` (the editor runs it too), the attributes are in
`Runtime/BhlBindAttribute.cs`, details in the README, «Bindings generator».

## Config `bhl_bindgen.json` (next to `bhl.proj`)

| Field | What |
|---|---|
| `namespace`, `register_class` | the C# namespace and class of the output: the project calls `RegisterBegin(types)` / `RegisterEnd(types)` |
| `sources` | `dir/*.cs`, `dir/**/*.cs`; `!glob` excludes (the generated code) |
| `result_dir` | where it's written (gitignored); `register.cs` starts with `//dep:` lines, what it's made of |
| `imports` | the `using`s of the output |
| `custom_rw` | the types marshalled by `Val.Encode/Decode`, declared elsewhere |
| `decls_namespace` | the C# namespace of the declarations: `<it>.Geo` → the BHL namespace `Geo` |
| `includes` | the configs of other bindings (a package's): their own `result_dir`, their declarations are known here |

## Functions

```csharp
static partial class Script
{
  [BhlBind] public static int Add(int a, int b = 2) => a + b;          // Add, the defaults as in C#
  [BhlBind] public static void Move([BhlDefault("Pos.None")] Pos to) {}  // a default which isn't a C# constant
  [BhlBind("LM3.Refresh")] public static void Refresh(VM vm) {}        // a name and a namespace; a first VM gets exec.vm
  [BhlBind] public static void Later(VM.ExecState exec,                // a first VM.ExecState gets exec
    [BhlType(typeof(Action)), BhlCoroutine] VM.FuncPtr fn) {}          // a func: its BHL type, released after the call
  [BhlBind] public static async BhlTask<int> Wait(float sec) { .. }    // a coroutine (BhlTask, BhlFinallyTask)
  [BhlBind] [return: BhlType("IList")] public static IList ToIList(Val v) => ..; // the IList class, not []any
}

[BhlBind(typeof(Script), nameof(Script.Add))] interface Funcs {}        // by a declaration, no attribute on the method
[BhlBind(typeof(Helpers), Except = new[] { nameof(Helpers.X) })] interface All {}  // all the static ones
```

## Classes

```csharp
namespace Game.Bind.Decls                       // decls_namespace: the interface's name is the BHL name
{
[BhlClass(typeof(Game.Unit),                    // the native type; qualify one named as the interface
  nameof(Game.Unit.Hp),                        // a member of the type from the sources: a field, a property, a method
  New = true)]                                 // BHL may new / {..}
interface Unit : Actor, IDamageable             // the bases are declarations: the parent and the interfaces
{
  Vector3 pos { get; set; }                     // a field; { get; } is read only
  [BhlNative("transform.childCount")] int kids_count { get; }  // another native name or an expression
  [BhlNative("Kill")] void Die(bool fx = true); // a method on the object
  [BhlStatic] int Total();                      // a static one: Unit.Total()
  [BhlOp("+")] Unit Merge(Unit a, Unit b);      // a method and an operator (the C# operator)
  IList<Unit> squad { get; set; }               // an array (a field may be a List<T>)

  // a static method with a body: of the native type first, a method on it (u.FindNear(5)); else a static one
  static Game.Unit FindNear(Game.Unit self, float radius) => Game.World.Near(self, radius);
  static Game.Unit Spawn(int hp) => new Game.Unit(hp);  // Unit.Spawn(10)
  private static int Clamp(int v) => v < 0 ? 0 : v;    // a private method or a const: for the bodies, not bound
}
}
```

| `[BhlClass]` argument | What |
|---|---|
| `"Name"` first | the BHL name instead of the interface's (`"LM3.Chip"`, with a namespace) |
| `Parent = ".."`, `Implements = new[] {..}` | the parent / the interfaces declared elsewhere (meta, another config) |
| `Pod = true` | a boxed value type: a changed copy is written back |
| `CustomRW = true` | a value type kept in the `Val` itself by the project's `Encode/Decode` (`Encode` sets `dv.type`) |

An interface: `[BhlInterface(typeof(IWidget))] interface IWidget : IBase { void Update(); }`, its methods are
called on the object as on the native interface.

## Exec, funcs and hand written coroutines

```csharp
interface Unit
{
  // a first VM.ExecState (after self) gets the call's exec; a func argument as is, released after the call
  static void AddTask(Game.Unit self, VM.ExecState exec, [BhlType(typeof(Action)), BhlCoroutine] VM.FuncPtr fn)
    => self.AddTask(exec.Spawn(fn));
}
```

In the declarations' namespace a type named as a declaration is the declaration: a body qualifies it
(`UnityEngine.Transform`), `nameof(Unit)` there is the BHL name.

A coroutine which isn't a `BhlTask` is declared by its class (in the output's `namespace`), the generator adds
reading the args, `Tick` and `Destruct`:

```csharp
[BhlCoroutine(Returns = typeof(VFX))]          // or [BhlCoroutine("LM3.Name")]; no Returns: void
partial class VFXCreate
{
  public void Init(string prefab, Component parent = null) { .. }   // the BHL arguments
  public bool Exec(out VFX vfx) { .. }        // true: still running; the result of the last call, no Returns: bool Exec()
  public void Cleanup() { .. }
}
```

Without `exec` the class is called from C# as is, no VM:

```csharp
var c = new VFXCreate();
c.Init("fx/boom");
VFX vfx;
while(c.Exec(out vfx))
  yield return null;
c.Cleanup();
```

When the body needs the VM (`exec.vm`, a fail by `exec.status = BHS.FAILURE`), the methods take it:
`Init(VM.ExecState exec, ..)`, `void Exec(VM.ExecState exec)` setting `exec.status = BHS.RUNNING` and pushing the
result by `ReturnValue(exec, ..)`, `Cleanup(VM.ExecState exec)`; each one either way.

A new one is better a `BhlTask` function (`[BhlBind]`, see [bhltask.md](bhltask.md)), the class is for what
can't be done so.

## Types

| C# | BHL |
|---|---|
| `int`, `uint`, `long`, `short`, `byte` … / `float`, `double` / `string` / `bool` | `int` / `float` / `string` / `bool` |
| `Val` | `any` |
| `IList<T>` (a field also `List<T>`) / `IList` | `[]T` / `[]any` |
| `Action<..>`, `Func<..>`; `[BhlCoroutine]` on the parameter | `func(..)`, `coro func(..)` |
| `[BhlType(typeof(Action<..>))] VM.FuncPtr` | the same, the func itself (a coroutine's, `exec.Spawn`) |
| `(int, string)` returned | several values |
| an enum, a class | by name: `[BhlClass]`, or `[BhlType("Name")]` for one declared elsewhere under another name |
| `bhl.IType` | `Type` |

A type that isn't a `[BhlClass]` (bound elsewhere, e.g. by meta) must be declared in the `sources`, else it's an
error: an enum of a generated source not generated yet would otherwise be bound as a class.

Native types of one short name in different namespaces may both be bound; where such a type is used it's qualified
(`Game.New.RewardWindow`), its short name alone is an error.

One native type may be named by classes of different configs (the project's `Vector3`, a package's `M2Vector3`): a config
uses its own name, else the one of the config it includes; `[BhlType("M2Vector3")]` picks the other one for a parameter
or a return. Within one config a native type has one name.
