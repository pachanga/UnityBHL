# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.41.0] - 2026-11-11

### Changed
- bindgen: a generated symbol's `Origin` is its C# declaration (the `[BhlBind]` method, the `[BhlCoroutine]` class,
  the `[BhlClass]`/`[BhlInterface]` declaration and its members): with bhl keeping an explicit `Origin` (3.0.0-beta25+) the LSP's go to definition jumps there rather than to the generated registration

## [0.40.0] - 2026-11-11

### Changed
- bindgen: the result of a `[BhlCoroutine]` class with `bool Exec(out T ..)` is its `out` parameters (several make a
  tuple); `Returns = typeof(T)` is only for `void Exec(VM.ExecState exec)`, next to `out` parameters it's an error:
  drop it

## [0.39.0] - 2026-11-11

### Added
- bindgen: a return (a tuple item, a coroutine's `Returns`) may be a `RefcList<T>`, an array of `T` as `IList<T>`: the
  type tells a C# caller the list is pooled and its reference is the caller's, BHL takes it over when it's returned to
  a script; a `RefcList<T>` parameter or field is an error, BHL passes a list of its own

## [0.38.0] - 2026-11-11

### Added
- bindgen: `Types_<Class>.ToVal(v)`, the `Val` of a native value typed as the class, instead of
  `Val.NewObj(v, Types_<Class>.Value)` written by hand, and `Types_<Class>.FromVal(val)`, the native value of a `Val`
  (encoded and decoded for a `CustomRW` one)

## [0.37.0] - 2026-11-11

### Added
- bindgen: `[BhlClass(.., CustomRW = true, AutoCodec = true)]` generates the codec of a value type kept in the `Val`
  (`Encode`, `Decode`, `ToVal` in `<register_class>_Codecs`): its layout is the declaration's `{ get; set; }`
  properties, bool, numbers and enums (`long`/`ulong` as bits), at most 4; when the native type is in the sources
  each of its non-private instance fields must be in the layout

### Fixed
- bindgen: a declaration interface named as its native type no longer hides the native type's members

## [0.36.0] - 2026-11-11

### Added
- bindgen: one native type may be named by classes of different configs (as meta allowed), e.g. `Vector3` by the
  project and `M2Vector3` by a package it includes: a config uses its own name, else the included one's;
  `[BhlType("Name")]` picks the other one for a parameter or a return. Within one config it's still an error

## [0.35.0] - 2026-11-10

### Changed
- bindgen: the generated part of a `[BhlCoroutine]` class reads the args into locals of its `Init` when none of them is
  disposable (a func, a refcounted list); the `Args` field is kept only for the disposable ones, released when it ends

## [0.34.0] - 2026-11-10

### Added
- Control Panel, Settings: `bhl_bindgen.json` next to `bhl.proj` is shown as `bhl.proj` is, editable in place and validated
  as the generator loads it, with the binding sources, the included configs and the result dirs

## [0.33.0] - 2026-11-10

### Added
- bindgen: `[BhlDefault("expr")]` on a parameter, the BHL default of an argument which can't be a C# default (not a
  compile-time constant, e.g. `M3Pos.None`): the C# parameter has none, the expression is its value when BHL leaves it out

## [0.32.0] - 2026-11-10

### Fixed
- bindgen: native types of one short name in different namespaces (`Game.RewardWindow`, `Game.New.RewardWindow`)
  may be bound both, a use of such a type is qualified; before it was an error

## [0.31.0] - 2026-11-10

### Changed
- bindgen: a type which is neither a `[BhlClass]` nor declared in the `sources` (a class bound by meta must be) is an
  error rather than bound as a class: e.g. an enum of a generated source not generated yet

## [0.30.0] - 2026-11-10

### Added
- bindgen: a `[BhlCoroutine]` class may do without `exec`: `Init(..)`, `bool Exec()` (true while running) or
  `bool Exec(out T result)` with `Returns`, `Cleanup()`, so C# calls it as is, no VM; the `exec` forms stay
- `BhlTaskRun` is a step of a Unity coroutine: `yield return run` ticks it once a frame and disposes it when the
  function is over; `BhlTaskRun.Start(state, static s => ..)` overloads take a state instead of a closure
- `BhlFlow.Run` allocates nothing once the pools are warm: no run object (the run is a struct of the method), and
  `Run(state, static s => ..)` overloads take a state instead of a closure
- bindgen: a static method with a body in a `[BhlClass]` declaration is bound as a method of the class (taking the native
  type first, `self`) or a static one; a class needs no `Script.<Class>_<Method>` and `nameof(..)` of it any more
  (still supported). Its private methods and constants are for the bodies, they're not bound
- the release zip `unitybhl.zip` of a version tag (`.github/workflows/release.yml`), the package with the bindings
  generator prebuilt in `Tools~/bindgen/dist` (`Tools~/bindgen/dist.sh`, not in git): a project's composer taking it
  as the package's dist needs only the .NET 8 runtime for taskman_bhl to run the generator
- bindgen: a first `VM.ExecState` parameter gets the call's `exec` (as a first `VM` gets `exec.vm`);
  `[BhlType(typeof(Action<..>))]` on a `VM.FuncPtr` parameter binds a func argument as is (a direct function, a
  `[BhlClass]` method, the `Init` of a `[BhlCoroutine]` class): with both, a hand written class method is an
  ordinary `Script.<Class>_<Method>(self, exec, ..)`
- bindgen: `[BhlCoroutine]` on a hand written partial class `<Name>` declares the coroutine by the class itself:
  the arguments are the parameters of its `Init(VM.ExecState exec, ..)`, a result is `Returns = typeof(T)`, another
  name `[BhlCoroutine("Name")]`; no separate declaration
- `Documentation~/bindings.md`: a cheat sheet of the bindings generator; `Documentation~/bhltask.md` translated to English
- bindgen: `[BhlBind]` on a static method found in the sources binds it, `[BhlBind("Name")]` under another
  BHL name; the attribute moved to the runtime assembly (conditional on `UNITY_EDITOR`, no attribute data
  in a player) so it can be put on runtime code. The declarations file form `[BhlBind(typeof(T), ..)]` stays,
  a function bound by both or twice is an error naming both places
- bindgen: a first `VM` parameter of a bound method gets the VM running the call (`exec.vm`), it's not a BHL
  argument, after `self` of a class method - as `@bhl_pass_vm` of metagen_bhl_bind
- bindgen: a `[BhlClass]` names its native type in BHL (another class naming it otherwise is an error), `bhl.IType`
  is BHL's `Type`; `[BhlType("Name")]` names a type declared elsewhere under another name
- bindgen: static methods of a `[BhlClass]` (`FuncAttrib.Static`, as `@bhl_static` of metagen_bhl_bind): a `[BhlStatic]`
  method of the interface, called on the native type (an interface can't declare it static in C# 9), or
  `nameof(T.Method)` of a static method of a type of the sources
- bindgen: operator overloads of a `[BhlClass]`: a `[BhlOp("==")]` method of the interface is a static method bound
  under its name and as the operator (as `@bhl_bin_op` of metagen_bhl_bind), both call the C# operator of its two
  arguments; an operator BHL doesn't overload, other than two arguments or no return value is an error
- bindgen: array fields of a `[BhlClass]`, `IList<T>` or `List<T>`: read as the live native list, written by copying
  the BHL array into it (created if null), as metagen_bhl_bind does; the `List_<T>` native list types are defined
  for them too
- bindgen: interfaces, `[BhlInterface("Name", typeof(T), Inherits = new[] { .. })]` on an interface whose methods are
  called on the object as the native interface (`InterfaceSymbolNative`, set up in `RegisterEnd`); a class implements
  interfaces by `[BhlClass(.., Implements = new[] { .. })]`, declared by bindgen or elsewhere (by BHL name)
- bindgen: value type classes, as POD and `@bhl_custom_rw` of metagen_bhl_bind: `[BhlClass(.., Pod = true)]` is kept
  boxed in the BHL value, `CustomRW = true` in the value itself by the project's `Val.Encode(T)`/`Decode(ref T)` (its
  args and returns too, without `custom_rw` in the config); a field set or a method call writes the changed copy back
- bindgen: `[BhlClass(typeof(T), ..)]` and `[BhlInterface(typeof(T))]` without the name: the BHL name is the declaring
  interface's, which in a namespace of its own may be named as the native type
- bindgen: `decls_namespace` of `bhl_bindgen.json`: a declaration in a C# namespace under it is in the BHL namespace of
  the rest, the classes, interfaces and the functions of `[BhlBind(typeof(T), ..)]` interfaces; a base
  of a declaration is the one C# finds when several are named alike
- bindgen: namespaces: a BHL name of a function, a class or an interface may be in one, `LM3.Chip`, defined by
  `types.ns.Nest(..)` as metagen_bhl_bind does, a C# identifier made of it has `_` for `.` (`Types_LM3_Chip`)
- bindgen: `includes` of `bhl_bindgen.json`, the configs of other bindings (e.g. of a package): one run generates each
  to its own `result_dir` and `register_class`, their declarations are known to the including config; an output imports
  the namespaces of the included ones. A cycle, a missing config, a shared `result_dir` or a type named otherwise by
  another config is an error
- bindgen: a `sources` glob starting with `!` excludes its files, e.g. the generated code under a source dir
- bindgen: no `decls` in `bhl_bindgen.json`: the declarations files are found in the `sources`, the files with
  `[BhlClass]`, `[BhlInterface]`, `[BhlBind(typeof(T), ..)]` on a type or `[BhlCoroutine]` on a class
- bindgen: `[BhlType("Name")]` on a parameter, a return value (`[return: BhlType("IList")]`) or a `[BhlClass]` property: the BHL class `Name`
  rather than the C# type's own BHL type, e.g. a C# `IList` as a bound class of `System.Collections.IList` not `[]any`
- bindgen: the C# bases of a declaration which are declarations too: of a `[BhlClass]` the parent class and the
  implemented interfaces, of a `[BhlInterface]` the inherited ones (`interface UIPlate : UIChip`); `Parent`,
  `Implements` and `Inherits` name the types declared elsewhere (e.g. by meta), a base which isn't a declaration
  is an error

### Changed
- `[BhlClass]`, `[BhlNative]` and `[BhlCoroutine]` moved to the runtime assembly next to `[BhlBind]`
  (`Runtime/BhlBindAttribute.cs`, conditional on `UNITY_EDITOR`): a declarations file may be runtime code,
  so its `nameof(Script.<Class>_<Method>)` sees the internal `Script` and is checked by the compiler

### Removed
- bindgen: `[BhlCustom]` and its hand written `Script_<Name>` classes: a coroutine is a `BhlTask` function or a
  `[BhlCoroutine]` class, a function or a class method needing `exec` or a func argument takes `VM.ExecState` and
  `[BhlType(typeof(..))] VM.FuncPtr`; the generated part of a coroutine has no public `Args` / `ReadArgs` any more

## [0.29.0] - 2026-11-09

### Added
- `BhlTaskRun`: starts a `BhlTask` function from C# which has a loop of its own (UI, tools, tests),
  without a BHL call - the same scope and the same per tick semantics a `BhlTaskCoroutine` has, driven
  by `Tick()` instead of the VM, so one flow can be written once and called both from BHL and from C#

  ```csharp
  _run = BhlTaskRun.Start(() => Flows.ClaimReward(id));   // BhlTaskRun<T>.Start for a result
  void Update()    { if(_run != null && !_run.Tick()) { _run.Dispose(); _run = null; } }
  void OnDestroy() { _run?.Dispose(); }
  ```

  `Dispose()` cancels what's left, as the VM dropping a call does, and must be called: unlike a BHL
  call nothing owns this one. It's idempotent and safe after the run is over; there's no finalizer on
  purpose (the pools are per thread, a finalizer runs on the thread of the GC). A `BhlFinallyTask`
  function fits too, by its conversion to `BhlTask`. A run allocates 64 bytes (the `BhlTaskRun` object
  itself, deliberately not pooled: it's held by the caller across frames, where a recycled one handed
  out twice would let a stale reference drive another run), everything else stays at 0
- `BhlFlow.Run(..)` (only with UniTask, `BHL_UNITASK`): awaits such a flow from a UniTask method -
  it ticks the `BhlTaskRun` once per frame instead of the VM

  ```csharp
  bool claimed = await BhlFlow.Run(() => Flows.ClaimReward(id), this.GetCancellationTokenOnDestroy());
  ```

  The token is what the VM's cancellation is for a BHL call: when it fires the run is disposed, so the
  `BhlFinallyTask` functions unwind and the pooled objects come back. `PlayerLoopTiming` is a parameter,
  to tick the flow where the VM ticks its own calls. A function is passed, not a task: a `BhlTask`
  function has to be called with a scope current, so `await Flows.ClaimReward(id)` on its own throws
  before there's anything to await - write the flow as a `UniTask` and bind it with `ToBhlTask()`
  instead if C# is its main caller

## [0.28.0] - 2026-11-09

### Changed
- The `.bhl` change pollers (`AutoCompileController`, and `BHLAssetPostprocessor` in Play Mode) walk the sources with bhl's
  `ProjectConf.CollectSourceFiles`: the `!` excluded dirs of `src_dirs` (bhl v3.0.0-beta16+) are no longer rescanned every
  300 ms, e.g. a dir of level files beside the scripts. Needs `com.bitgames.bhl` 3.0.0-beta23 (beta15-22 don't compile in Unity)

## [0.27.0] - 2026-11-07

### Changed
- BhlTask: a call costs less to start and to end, which is what a short call mostly pays (a tick of a
  parked function was already about 7 ns over a pooled `Coroutine`, a call about 14 ns without an await
  and 31 ns with one). On .NET 10/arm64: a call without an await 37 -> 28 ns, with one yield 56 -> 50 ns
  - `Cancel()` and `Release()` took `remoteLock` once each (about 4.5 ns per uncontended acquire) to
    race with a foreign task (`ToBhlTask`) completing on another thread. A scope which never hosted a
    foreign source was never handed to another thread, so it now takes no locks at all; one which did
    keeps taking them for the rest of its life (it's pooled, and a late `PostRemote` may still come for
    an old version of it)
  - a call which ran to its end skips the cancellation walk: the scope counts the tracked objects which
    haven't pooled themselves yet instead of looking at the stale entries. Foreign sources never
    decrement that count, so they always take the full path
  - a tick of a call without a foreign source doesn't read `hasRemote` (volatile) any more, and
    `BhlTask.Yield()` takes the scope once instead of the awaiter reading the thread static again when
    it suspends and when it resumes. Both measure ~0.2 ns on .NET, they're for IL2CPP where a thread
    static and a volatile read cost more

### Fixed
- The .NET build (`Runtime/UnityBHL.csproj`, `NO_UNITY`) compiles again: `BHLFieldAttributes.cs` is excluded,
  `Task/` builds out of Unity (`BhlTaskScope` logs to `Console.Error`, `BhlTask.WaitSeconds` stays Unity-only:
  no game clock there)

## [0.26.0] - 2026-11-06

### Changed
- Control Panel: a `Force` tickbox next to `Recompile` (remembered in EditorPrefs) instead of the `Force Recompile` button: with it ticked
  `Recompile` bypasses the compile cache; the `BHL/Force Recompile` menu item is still there

## [0.25.0] - 2026-11-05

### Changed
- BhlTask: a tick of an async function costs less (about 10 ns more than a pooled `Coroutine` per tick instead
  of 12 on .NET 8): the one continuation of a tick is kept without the queue lists

### Fixed
- BhlTask is thread safe for a VM per thread, as bhl: its pools are per thread (they were shared by all the
  threads), a foreign `Task` completed on another thread (no `SynchronizationContext`, e.g. in a worker thread)
  posts its result to the call under a lock instead of touching the queue of the call while it ticks

## [0.24.0] - 2026-11-05

### Changed
- bindgen: the `Generate Bindings` button of the Control Panel (shown when the project has `bhl_bindgen.json`
  next to bhl.proj) instead of the `BHL/Generate Bindings` menu item; it says when the bindings are up to date

## [0.23.0] - 2026-11-04

### Added
- bindgen: `[BhlBind(typeof(T))]` without names binds all the public and internal static methods of `T`,
  `Except = new[] { nameof(T.M), ... }` leaves some out; a method which can't be bound (generic,
  overloaded, of an unsupported type) fails the generation

### Changed
- bindgen: the generated code converts `any`, arrays and functions by the helpers of `UnityBHL.BindConv`
  instead of having their bodies inline, plain values are read as before; regenerate the bindings
- bindgen: the native list types are kept in typed fields (`NativeListTypeSymbol<T>`), an array
  argument no longer casts it on each call; their conversions are one expression lambdas
- bindgen: a native list type is defined by `NativeListTypeSymbol.DefineOnce(..)` of bhl instead of
  a generated block, which needs a bhl version having it
- `com.bitgames.bhl` 3.0.0-beta8: the generated bindings use its `NativeListTypeSymbol.DefineOnce`
  and `SetList`

### Fixed
- bindgen: a native function returning its array argument gives BHL its own array back
  (`NativeListTypeSymbol<T>.SetList` of bhl), it used to hand over the adapter released after the call

## [0.22.0] - 2026-11-04

### Added
- `Tools~/bindgen`: generates the canonical BHL bindings registration .cs (the format the BHL
  compiler reads) from C# declarations, parsing only: `[BhlBind]` binds static methods by name,
  `[BhlCustom]` interfaces declare custom functions and coroutines with hand written `Script_*`
  classes, `[BhlClass]` interfaces declare classes (members by signature or by `nameof`). The
  output is split by declarations file and into slices of 20, configured by `bhl_bindgen.json`
  next to bhl.proj. C# is read by a hand written parser of declarations only. The generator is in
  the Editor assembly (`Editor/BindGen`); the tool runs it from the console and needs the .NET SDK,
  `--parser=roslyn` uses Roslyn instead, `--parser=both` checks one by the other
- `BindGen` editor postprocessor: regenerates the bindings in-process, without the .NET SDK, when
  a file they're made of changes; `BHL/Generate Bindings` menu item
- `BitGames.BhlTask` asmdef (moved from the `com.bitgames.bhltask` package, namespace
  `BitGames.BHL` kept): C# functions bound as BHL coroutines by returning `BhlTask` or
  `BhlFinallyTask`, with its Roslyn analyzer; the iterator forms of the package (`BhlStep`,
  `IEnumerator<BhlStep>`) are dropped, async covers them; `ToBhlTask(UniTask)` only
  when the project has UniTask. bindgen binds such functions as coroutines by their return type

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
