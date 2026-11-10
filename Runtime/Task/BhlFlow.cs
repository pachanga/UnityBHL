#if BHL_UNITASK
using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace BitGames.BHL
{

//NOTE: awaits a BhlTask function from C# which is written in UniTask (UI, tools): the BhlTaskRun is
//      ticked once per frame by this loop instead of by the VM, so one flow can be called both from
//      BHL and from C#.
//      A function is passed, not a task: a BhlTask function has to be called with a scope current
//      (BhlTask.Yield() and ToBhlTask() both Require() one), which is what BhlTaskRun.Start() does -
//      calling it outside throws before there's anything to await.
//      'ct' is what the VM's cancellation is for a BHL call: when it fires, the run is disposed, so
//      the BhlFinallyTask functions of the flow unwind and the pooled objects come back. In Unity
//      this.GetCancellationTokenOnDestroy() ties the flow to the object which started it.
//      Only compiled when the project has UniTask (BHL_UNITASK of the asmdef), as ToBhlTask(UniTask).
//      Not covered by Tests~: a stub of UniTask can only test itself here, the loop below is a plain
//      use of the run (BhlTaskCall), which is.
//      Allocates nothing once the pools are warm: the run is a struct held by the method alone, and with
//      the state overloads a static lambda captures nothing (its delegate is cached):
//      await BhlFlow.Run((agent, "Open"), static s => Script.AnimOpen(s.agent, s.Item2), ct)
public static class BhlFlow
{
  public static async UniTask Run(Func<BhlTask> fn, CancellationToken ct = default,
    PlayerLoopTiming timing = PlayerLoopTiming.Update)
  {
    //NOTE: an already cancelled token doesn't start the flow at all
    ct.ThrowIfCancellationRequested();
    //NOTE: no using: a using variable of a struct is read only, Tick() would change a copy
    var call = BhlTaskRun.Call(fn);
    try
    {
      while(call.Tick())
        await UniTask.Yield(timing, ct);
    }
    finally
    {
      call.Dispose();
    }
  }

  public static async UniTask<T> Run<T>(Func<BhlTask<T>> fn, CancellationToken ct = default,
    PlayerLoopTiming timing = PlayerLoopTiming.Update)
  {
    //NOTE: an already cancelled token doesn't start the flow at all
    ct.ThrowIfCancellationRequested();
    //NOTE: no using: a using variable of a struct is read only, Tick() would change a copy
    var call = BhlTaskRun.Call(fn);
    try
    {
      while(call.Tick())
        await UniTask.Yield(timing, ct);
      return call.Result;
    }
    finally
    {
      call.Dispose();
    }
  }

  public static async UniTask Run<TState>(TState state, Func<TState, BhlTask> fn, CancellationToken ct = default,
    PlayerLoopTiming timing = PlayerLoopTiming.Update)
  {
    //NOTE: an already cancelled token doesn't start the flow at all
    ct.ThrowIfCancellationRequested();
    //NOTE: no using: a using variable of a struct is read only, Tick() would change a copy
    var call = BhlTaskRun.Call(state, fn);
    try
    {
      while(call.Tick())
        await UniTask.Yield(timing, ct);
    }
    finally
    {
      call.Dispose();
    }
  }

  public static async UniTask<T> Run<TState, T>(TState state, Func<TState, BhlTask<T>> fn, CancellationToken ct = default,
    PlayerLoopTiming timing = PlayerLoopTiming.Update)
  {
    //NOTE: an already cancelled token doesn't start the flow at all
    ct.ThrowIfCancellationRequested();
    //NOTE: no using: a using variable of a struct is read only, Tick() would change a copy
    var call = BhlTaskRun.Call(state, fn);
    try
    {
      while(call.Tick())
        await UniTask.Yield(timing, ct);
      return call.Result;
    }
    finally
    {
      call.Dispose();
    }
  }
}

}
#endif
