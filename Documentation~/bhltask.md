# BhlTask

C# functions bound as BHL coroutines: instead of a `[BhlCoroutine]` class with `Init` / `Exec` / `Cleanup` per
binding, one static function. Its awaits run in the ticks of the BHL call awaiting it and live exactly as
long: the `paral` branch lost, the fiber or the VM stopped, and the function is cancelled.

## Binding

It's `[BhlBind]` on the C# function, see [bindings.md](bindings.md).

The C# function returns one of two forms:

| Returns | For | Cancellation |
|---|---|---|
| `BhlTask`, `BhlTask<T>` | an async function, the default | not resumed: no exceptions, no allocations, `finally` doesn't run |
| `BhlFinallyTask`, `BhlFinallyTask<T>` | an async function with `finally` / `using` around an `await` | `OperationCanceledException`: `finally` runs, ~1 KB per function of the chain |

```csharp
public static BhlTask ATF_WaitSeconds(float seconds) => BhlTask.WaitSeconds(seconds);

public static async BhlTask<bool> Tap(Vector3 pos)
{
  await BhlTask.Yield();
  return await ATFBot.Input.Tap(pos).ToBhlTask();
}
```

The binding's return value is the `T` of `BhlTask<T>` or `BhlFinallyTask<T>`.

## API

| | |
|---|---|
| `await BhlTask.Yield()` | the next tick of the call, as `yield()` in BHL |
| `await BhlTask.WaitSeconds(s)` | scaled time (`Time.time`) |
| `task.Forget()` | the task runs unawaited; its exception fails the BHL call |
| `uniTask.ToBhlTask()`, `task.ToBhlTask()` | awaits a `UniTask` (when the project has `com.cysharp.unitask`: the `BHL_UNITASK` define of the asmdef's `versionDefines`) or a `System.Threading.Tasks.Task` |
| `BhlTask.CompletedTask`, `new BhlTask<T>(v)`, `FromException(e)` | completed tasks |

- **Only its own awaiters may be awaited.** A foreign one (`UniTask`, `Task`) would resume the function
  outside the call's ticks, after its end too: such an `await` fails the task with `NotSupportedException`
  and the analyzer doesn't let it compile (`BHLT001`). `ToBhlTask()` converts a foreign task: it completes
  in the first tick of the call after the foreign one does.
- A `Task` resumes through the main thread's `SynchronizationContext`, a frame of delay and the `Task`'s own
  allocations; a `UniTask` has neither.
- **A task is awaited once**, as in UniTask: its source goes back to the pool once the result is taken, a
  second `await` throws `InvalidOperationException` (analyzer: `BHLT004`).
- **The thread is the VM's**: a call is created, ticked and cancelled in the VM's thread, every thread has
  pools of its own (as bhl's pools), so VMs of different threads run at once. A foreign `Task` without a
  `SynchronizationContext` (in a worker thread) may complete in another thread, its result reaches the call
  in the next tick.

## From C# code

A `BhlTask` function is called from C# outside of BHL by a run of its own, ticked by the caller, the same semantics
as a BHL call: the function is passed, not a task, since it has to be called with the call's scope current.

```csharp
await BhlFlow.Run(() => Script.Wait(1f), this.GetCancellationTokenOnDestroy()); // from a UniTask method
await BhlFlow.Run((unit, 2f), static s => Script.Move(s.unit, s.Item2), ct);     // no closure: a state, a static lambda

var run = BhlTaskRun.Start((unit, 2f), static s => Script.Move(s.unit, s.Item2)); // in a Unity coroutine
yield return run;                                                              // ticked once a frame, disposed when over
Debug.Log(run.Result);                                                         // of a BhlTask<T>

using(var run = BhlTaskRun.Start(() => Script.Wait(1f)))                       // any other loop (a tool): Tick() it
  while(run.Tick())
    Update();
```

- `BhlFlow.Run` (only with UniTask) allocates nothing once the pools are warm, with a state and a `static` lambda;
  its token cancels the run as the VM cancels a call.
- `BhlTaskRun` is an object of 64 B, the rest of a run allocates nothing. It must be disposed: `Dispose()`, or
  `yield return run` in a Unity coroutine disposes it once the function is over; a coroutine stopped before
  (`StopCoroutine`, the object destroyed) doesn't, dispose the run then, e.g. in `OnDisable` / `OnDestroy`.

## Lifetime and cancellation

- Everything the call started dies with it: when the root task completes, the forgotten tasks not done yet
  are cancelled.
- **`BhlTask`** isn't resumed on cancellation: the continuations are dropped, the runners go back to the
  pool. No exceptions, no allocations, but `finally` / `using` around an `await` don't run either, which is
  why the analyzer forbids them in a `BhlTask` (`BHLT002`).
- **`BhlFinallyTask`** is resumed with `OperationCanceledException`, so `finally` runs; an `await` in its
  `finally` during cancellation throws the same exception at once. Mixed chains work: a `BhlTask` awaiting
  a `BhlFinallyTask` fails with the exception without being resumed itself; a `BhlFinallyTask` over a
  `BhlTask` or over `ToBhlTask` gets the exception.
- `ToBhlTask` doesn't stop the foreign task, pass it a `CancellationToken` of your own. Its result is taken
  anyway once it completes.
- An exception of a forgotten task during cancellation goes to `Debug.LogException` too;
  `OperationCanceledException` isn't logged.

A type of its own rather than an attribute: in C# 9 a method can't choose its builder (`[AsyncMethodBuilder]`
on a method is C# 10), and looking an attribute up by reflection on the state machine isn't reliable under
IL2CPP stripping.

## Allocations

Per call, once the pools are warm (Release / Debug):

| | Release | Debug |
|---|---|---|
| `BhlTask` (start, ticks, completion, `Forget`, `ToBhlTask(UniTask)`) | 0 | ~64 B per async function |
| `BhlTask` cancellation | 0 | 0 |
| `BhlFinallyTask` cancellation | ~1 KB per function of the chain | the same |

- Debug: the compiler makes the state machine a class. The editor with Code Optimization = Debug allocates,
  a Release player doesn't.
- **Every extra frame costs:** an async wrapper which only awaits another task is a runner, 64 B in Debug
  and one more exception on a `BhlFinallyTask` cancellation. Write a passthrough
  (`=> BhlTask.WaitSeconds(s)`); the analyzer suggests it (`BHLT005`).
- Also allocating: exceptions and `FromException`, `ToBhlTask(Task)`, the first calls while the pools grow,
  a task neither awaited nor forgotten by `Forget`.

## Analyzer

The Roslyn analyzer `Runtime/Task/Analyzers/BitGames.BhlTask.Analyzer.dll` is in the folder of the
`BitGames.BhlTask` asmdef: Unity applies it to the assemblies referencing `BitGames.BhlTask`.

| Code | Level | When |
|---|---|---|
| `BHLT001` | error | a `BhlTask` / `BhlFinallyTask` function awaits a foreign task, `ToBhlTask()` is needed |
| `BHLT002` | error | a `BhlTask` function has an `await` inside a `try` with `finally`, a `using` or after a `using var` |
| `BHLT003` | error | the result of a `BhlTask` is discarded |
| `BHLT004` | warning | a task is awaited or forgotten more than once |
| `BHLT005` | warning | an async function only awaits a task of the same type, an extra frame |

The rules are local to a function. The whole chain needn't be analyzed: every function of a chain is
checked in its own assembly, and the chain is safe if its links are (Roslyn doesn't see the bodies of the
methods of other assemblies anyway). Not checked: a `foreach` with an `IDisposable` enumerator inside a
`BhlTask` (the `Dispose` of the usual collections is empty) and a repeated `await` in a loop.

### Build and tests

The sources are in `Analyzer~/` (Unity doesn't import folders with `~`). Needs .NET SDK 8.

```sh
cd <UnityBHL root>
dotnet test "Analyzer~/BitGames.BhlTask.Analyzer.Tests"
# a Release build; the dll is copied to Runtime/Task/Analyzers/ after it (target CopyToUnity)
dotnet build -c Release "Analyzer~/BitGames.BhlTask.Analyzer"
```

The version of `Microsoft.CodeAnalysis.CSharp` (4.3.0) is the one of `com.bitgames.ref`: the project's
Unity supports it.

## Runtime tests

`Tests~/BitGames.BhlTask.Tests` is a console program: the BhlTask runtime is built by plain dotnet with stubs
of Unity, the VM and UniTask (`Stubs.cs`, `UniTaskStub.cs`). A binding is called as the generated code does,
ticks and destruction as the VM does. Checked: the behavior (ticks, the result, exceptions, cancellation of
both forms, mixed chains, `Forget`, `ToBhlTask`) and allocations; zero allocations in Release are among the
checks. The exit code is the number of failed checks.

```sh
cd <UnityBHL root>
dotnet run -c Release --project "Tests~/BitGames.BhlTask.Tests"
dotnet run -c Debug --project "Tests~/BitGames.BhlTask.Tests"
```

## Layout

```
Runtime/Task/BhlTask.cs           BhlTask, BhlFinallyTask, their awaiters
Runtime/Task/BhlTaskSource.cs     pooled sources: the state machine runner, the Forget observer, the builders
Runtime/Task/BhlTaskScope.cs      the call's queue of continuations and its pooled objects, cancellation
Runtime/Task/BhlTaskForeign.cs    ToBhlTask for UniTask (when the project has it) and Task
Runtime/Task/BhlTaskCoroutine.cs  the bhl coroutine of a task
Runtime/Task/BhlTaskRun.cs        a task started from C# without a VM: the caller ticks it
Runtime/Task/BhlFlow.cs           awaiting such a run from a UniTask method (only with UniTask)
Runtime/Task/Analyzers/           the analyzer's dll
Analyzer~/                        the analyzer's sources and tests
Tests~/                           the runtime tests
```
