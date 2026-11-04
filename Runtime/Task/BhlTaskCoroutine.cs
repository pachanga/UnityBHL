using System;
using bhl;
using Coroutine = bhl.Coroutine;

namespace BitGames.BHL
{

//NOTE: the BHL coroutine of a BhlTask binding, created by the generated code (bit/metagen_bhl_bind):
//        var scope = BhlTaskScope.Enter();
//        try { var task = ATFBind.ATF_WaitSeconds(__seconds); return BhlTaskCoroutine.New(exec, scope, task); }
//        finally { scope.Exit(); }
//      the binding returns a BhlTask or a BhlFinallyTask
public static class BhlTaskCoroutine
{
  public static Coroutine New(VM.ExecState exec, BhlTaskScope scope, BhlTask task)
  {
    return BhlTaskCoroutine<BhlTaskVoid>.New(exec, scope, task.AsVoidTask(), null);
  }

  //NOTE: the result is dropped when the binding returns nothing
  public static Coroutine New<T>(VM.ExecState exec, BhlTaskScope scope, BhlTask<T> task)
  {
    return BhlTaskCoroutine<T>.New(exec, scope, task, null);
  }

  public static Coroutine New<T>(VM.ExecState exec, BhlTaskScope scope, BhlTask<T> task, Action<VM.ExecState, T> returnValue)
  {
    return BhlTaskCoroutine<T>.New(exec, scope, task, returnValue);
  }

  public static Coroutine New(VM.ExecState exec, BhlTaskScope scope, BhlFinallyTask task)
  {
    return New(exec, scope, (BhlTask)task);
  }

  public static Coroutine New<T>(VM.ExecState exec, BhlTaskScope scope, BhlFinallyTask<T> task, Action<VM.ExecState, T> returnValue)
  {
    return BhlTaskCoroutine<T>.New(exec, scope, task, returnValue);
  }
}

public class BhlTaskCoroutine<T> : Coroutine
{
  BhlTaskScope scope;
  BhlTask<T> task;
  Action<VM.ExecState, T> returnValue;
  bool started;

  internal static BhlTaskCoroutine<T> New(VM.ExecState exec, BhlTaskScope scope, BhlTask<T> task, Action<VM.ExecState, T> returnValue)
  {
    var coro = CoroutinePool.New<BhlTaskCoroutine<T>>(exec.vm);
    coro.scope = scope;
    coro.task = task;
    coro.returnValue = returnValue;
    return coro;
  }

  public override void Tick(VM.ExecState exec)
  {
    //NOTE: the call itself was the first step, what it queued runs in the next tick
    if(started)
      scope.Tick();
    else
      started = true;

    scope.ThrowIfFaulted();

    if(!task.IsCompleted)
    {
      exec.status = BHS.RUNNING;
      return;
    }

    var completed = task;
    task = default;
    var result = completed.GetAwaiter().GetResult();
    returnValue?.Invoke(exec, result);
  }

  //NOTE: cancels what is left of the call: its pending functions and forgotten tasks
  public override void Destruct(VM.ExecState exec)
  {
    scope.Cancel();
    scope.Release();
    scope = null;
    task = default;
    returnValue = null;
    started = false;
  }
}

}
