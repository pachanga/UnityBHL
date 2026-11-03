using System;
using System.Collections.Generic;
using UnityEngine;
using bhl;
using Coroutine = bhl.Coroutine;

namespace BitGames.BHL
{

//NOTE: the BHL coroutine of a BhlTask binding, created by the generated code (bit/metagen_bhl_bind):
//        var scope = BhlTaskScope.Enter();
//        try { var task = ATFBind.ATF_WaitSeconds(__seconds); return BhlTaskCoroutine.New(exec, scope, task); }
//        finally { scope.Exit(); }
//      the binding returns a BhlTask, a BhlFinallyTask, an iterator of steps or a single step
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

  public static Coroutine New(VM.ExecState exec, BhlTaskScope scope, IEnumerator<BhlStep> iterator)
  {
    var coro = BhlIteratorCoroutine<BhlTaskVoid>.New(exec, scope, null);
    coro.Push(iterator);
    return coro;
  }

  public static Coroutine New<T>(VM.ExecState exec, BhlTaskScope scope, IEnumerator<BhlStep<T>> iterator, Action<VM.ExecState, T> returnValue)
  {
    var coro = BhlIteratorCoroutine<T>.New(exec, scope, returnValue);
    coro.SetRoot(iterator);
    return coro;
  }

  //NOTE: a binding of a single step allocates nothing at all
  public static Coroutine New(VM.ExecState exec, BhlTaskScope scope, BhlStep step)
  {
    var coro = BhlIteratorCoroutine<BhlTaskVoid>.New(exec, scope, null);
    coro.SetFirst(step);
    return coro;
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

//NOTE: runs an iterator of steps with the nested ones on a stack; the iterators run with the scope of
//      the call current, so the tasks they start belong to the call
public class BhlIteratorCoroutine<T> : Coroutine
{
  BhlTaskScope scope;
  Action<VM.ExecState, T> returnValue;
  IEnumerator<BhlStep<T>> root;
  readonly Stack<IEnumerator<BhlStep>> stack = new Stack<IEnumerator<BhlStep>>();
  BhlStep first;
  bool hasFirst;
  BhlStep pending;
  bool hasPending;
  float waitEnd;
  T result;

  internal static BhlIteratorCoroutine<T> New(VM.ExecState exec, BhlTaskScope scope, Action<VM.ExecState, T> returnValue)
  {
    var coro = CoroutinePool.New<BhlIteratorCoroutine<T>>(exec.vm);
    coro.scope = scope;
    coro.returnValue = returnValue;
    return coro;
  }

  internal void Push(IEnumerator<BhlStep> iterator) => stack.Push(iterator);

  internal void SetRoot(IEnumerator<BhlStep<T>> iterator) => root = iterator;

  internal void SetFirst(BhlStep step)
  {
    first = step;
    hasFirst = true;
  }

  public override void Tick(VM.ExecState exec)
  {
    scope.Tick();
    scope.ThrowIfFaulted();

    var prevScope = scope.Activate();
    try
    {
      while(true)
      {
        if(hasPending)
        {
          if(!IsPendingDone())
          {
            exec.status = BHS.RUNNING;
            return;
          }
          hasPending = false;
        }

        if(!Next(out var step))
        {
          returnValue?.Invoke(exec, result);
          return;
        }

        switch(step.kind)
        {
          case BhlStep.Kind.Yield:
            exec.status = BHS.RUNNING;
            return;
          case BhlStep.Kind.Run:
            stack.Push(step.child);
            break;
          case BhlStep.Kind.WaitSeconds:
            waitEnd = Time.time + step.seconds;
            pending = step;
            hasPending = true;
            break;
          case BhlStep.Kind.Await:
            pending = step;
            hasPending = true;
            break;
        }
      }
    }
    finally
    {
      BhlTaskScope.Restore(prevScope);
    }
  }

  bool IsPendingDone()
  {
    if(pending.kind == BhlStep.Kind.WaitSeconds)
      return Time.time >= waitEnd;

    if(!pending.task.IsCompleted)
      return false;
    var task = pending.task;
    pending = default;
    task.GetAwaiter().GetResult();
    return true;
  }

  bool Next(out BhlStep step)
  {
    while(stack.Count > 0)
    {
      var top = stack.Peek();
      if(top.MoveNext())
      {
        step = top.Current;
        return true;
      }
      stack.Pop();
      top.Dispose();
    }

    if(hasFirst)
    {
      hasFirst = false;
      step = first;
      return true;
    }

    if(root != null)
    {
      if(root.MoveNext())
      {
        var s = root.Current;
        if(!s.isReturn)
        {
          step = s.step;
          return true;
        }
        result = s.result;
      }
      var r = root;
      root = null;
      r.Dispose();
    }

    step = default;
    return false;
  }

  //NOTE: disposes the iterators from the innermost one, their finally blocks run; then cancels the tasks
  public override void Destruct(VM.ExecState exec)
  {
    var prevScope = scope.Activate();
    try
    {
      while(stack.Count > 0)
        Dispose(stack.Pop());
      if(root != null)
        Dispose(root);
    }
    finally
    {
      BhlTaskScope.Restore(prevScope);
    }

    scope.Cancel();
    scope.Release();
    scope = null;
    returnValue = null;
    root = null;
    first = default;
    hasFirst = false;
    pending = default;
    hasPending = false;
    result = default;
  }

  //NOTE: nothing to throw an exception of a finally block into while the call is being destroyed
  static void Dispose(IDisposable iterator)
  {
    try
    {
      iterator.Dispose();
    }
    catch(Exception e)
    {
      Debug.LogException(e);
    }
  }
}

}
