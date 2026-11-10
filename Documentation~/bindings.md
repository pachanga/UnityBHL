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
| `type_names_file` | `<C# type> <BHL type>` of the types declared elsewhere (meta) |
| `custom_rw` | the types marshalled by `Val.Encode/Decode`, declared elsewhere |
| `decls_namespace` | the C# namespace of the declarations: `<it>.Geo` → the BHL namespace `Geo` |
| `includes` | the configs of other bindings (a package's): their own `result_dir`, their declarations are known here |

## Functions

```csharp
static partial class Script
{
  [BhlBind] public static int Add(int a, int b = 2) => a + b;          // Add, the defaults as in C#
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
  nameof(Script.Unit_FindNear),                // static Script.<Class>_<Method>(self, ..), a method
  New = true)]                                 // BHL may new / {..}
interface Unit : Actor, IDamageable             // the bases are declarations: the parent and the interfaces
{
  Vector3 pos { get; set; }                     // a field; { get; } is read only
  [BhlNative("transform.childCount")] int kids_count { get; }  // another native name or an expression
  [BhlNative("Kill")] void Die(bool fx = true); // a method on the object
  [BhlStatic] int Total();                      // a static one: Unit.Total()
  [BhlOp("+")] Unit Merge(Unit a, Unit b);      // a method and an operator (the C# operator)
  IList<Unit> squad { get; set; }               // an array (a field may be a List<T>)
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
static partial class Script
{
  // a first VM.ExecState gets the call's exec; a func argument as is, released after the call
  public static void Unit_AddTask(Game.Unit self, VM.ExecState exec, [BhlType(typeof(Action)), BhlCoroutine] VM.FuncPtr fn)
    => self.AddTask(exec.Spawn(fn));                  // the method AddTask: nameof(Script.Unit_AddTask)
}
```

A coroutine which isn't a `BhlTask` is declared by its class (in the output's `namespace`), the generator adds
reading the args, `Tick` and `Destruct`:

```csharp
[BhlCoroutine(Returns = typeof(VFX))]          // or [BhlCoroutine("LM3.Name")]; no Returns: void
partial class VFXCreate
{
  public void Init(VM.ExecState exec, string prefab, Component parent = null) { .. }  // the BHL arguments
  public void Exec(VM.ExecState exec) { .. ReturnValue(exec, vfx); }
  public void Cleanup(VM.ExecState exec) { .. }
}
```

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
| an enum, a class | by name: `[BhlClass]` or `type_names_file` |

## From meta (metagen_bhl_bind)

| Meta | bindgen |
|---|---|
| `@bhl_native_prefix:Script.%(..)` | `[BhlBind]` |
| `@bhl_coroutine`, direct | `BhlTask` |
| `@bhl_bind_type:custom` | `VM.ExecState` / `VM.FuncPtr` parameters, a `[BhlCoroutine]` class |
| `@bhl_pass_vm` | a first `VM` (or `VM.ExecState`) parameter |
| `@bhl_native_class` | `[BhlClass(typeof(T))]` |
| `extends` / `implements` | the interface's base / `Parent`, `Implements` |
| `@bhl_no_new` | no `New` |
| `@bhl_static` / `@bhl_bin_op` | `[BhlStatic]` / `[BhlOp]` |
| `@bhl_get:0`, `@bhl_set:0` | `{ get; }`, `{ set; }` |
| `@bhl_native_prefix` of a field | `[BhlNative]` |
| `@bhl_get:2`, `@bhl_set:2` | `[BhlNative("an expression")]`, else a `Script.<Class>_<Method>` method |
| `POD` / `@bhl_custom_rw` | `Pod` / `CustomRW` |
| `Ns.Name` | `"Ns.Name"` or `decls_namespace` |
