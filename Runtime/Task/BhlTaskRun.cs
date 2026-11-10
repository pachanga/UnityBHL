using System;

namespace BitGames.BHL
{

//NOTE: starts a BhlTask function outside of a BHL call, for C# which has a loop of its own (UI, tools,
//      tests): what BhlTaskCoroutine is for the VM, BhlTaskRun<T> is for a plain C# caller - the same
//      scope and the same per tick semantics, driven by Tick() instead of the VM.
//      A function is passed, not a task: the scope has to be current while the function runs up to its
//      first await, which is what Enter()/Exit() around the call do here. A BhlFinallyTask function fits
//      too, it converts to a BhlTask
public static class BhlTaskRun
{
  public static BhlTaskRun<BhlTaskVoid> Start(Func<BhlTask> fn)
  {
    return new BhlTaskRun<BhlTaskVoid>(Call(fn));
  }

  public static BhlTaskRun<T> Start<T>(Func<BhlTask<T>> fn)
  {
    return new BhlTaskRun<T>(Call(fn));
  }

  //NOTE: with a static lambda: no closure, BhlTaskRun.Start((unit, 2f), static s => Move(s.unit, s.Item2))
  public static BhlTaskRun<BhlTaskVoid> Start<TState>(TState state, Func<TState, BhlTask> fn)
  {
    return new BhlTaskRun<BhlTaskVoid>(Call(state, fn));
  }

  public static BhlTaskRun<T> Start<TState, T>(TState state, Func<TState, BhlTask<T>> fn)
  {
    return new BhlTaskRun<T>(Call(state, fn));
  }

  internal static BhlTaskCall<BhlTaskVoid> Call(Func<BhlTask> fn)
  {
    if(fn == null)
      throw new ArgumentNullException(nameof(fn));
    var scope = BhlTaskScope.Enter();
    BhlTask<BhlTaskVoid> task;
    try
    {
      task = fn().AsVoidTask();
    }
    catch
    {
      Abort(scope);
      throw;
    }
    scope.Exit();
    return new BhlTaskCall<BhlTaskVoid>(scope, task);
  }

  internal static BhlTaskCall<T> Call<T>(Func<BhlTask<T>> fn)
  {
    if(fn == null)
      throw new ArgumentNullException(nameof(fn));
    var scope = BhlTaskScope.Enter();
    BhlTask<T> task;
    try
    {
      task = fn();
    }
    catch
    {
      Abort(scope);
      throw;
    }
    scope.Exit();
    return new BhlTaskCall<T>(scope, task);
  }

  //NOTE: the state is passed to fn, a static lambda then captures nothing: no closure, its delegate is cached
  internal static BhlTaskCall<BhlTaskVoid> Call<TState>(TState state, Func<TState, BhlTask> fn)
  {
    if(fn == null)
      throw new ArgumentNullException(nameof(fn));
    var scope = BhlTaskScope.Enter();
    BhlTask<BhlTaskVoid> task;
    try
    {
      task = fn(state).AsVoidTask();
    }
    catch
    {
      Abort(scope);
      throw;
    }
    scope.Exit();
    return new BhlTaskCall<BhlTaskVoid>(scope, task);
  }

  internal static BhlTaskCall<T> Call<TState, T>(TState state, Func<TState, BhlTask<T>> fn)
  {
    if(fn == null)
      throw new ArgumentNullException(nameof(fn));
    var scope = BhlTaskScope.Enter();
    BhlTask<T> task;
    try
    {
      task = fn(state);
    }
    catch
    {
      Abort(scope);
      throw;
    }
    scope.Exit();
    return new BhlTaskCall<T>(scope, task);
  }

  //NOTE: Exit() first: the scope stops being current before it's taken back
  static void Abort(BhlTaskScope scope)
  {
    scope.Exit();
    scope.Cancel();
    scope.Release();
  }
}

//NOTE: Dispose() must be called: unlike a BHL call, which the VM drops, nothing owns this one - until
//      then the pooled objects of the call (the runners holding the state machines) don't come back.
//      There's no finalizer on purpose: the pools are per thread, and a finalizer runs on the thread of
//      the GC, which would push them into the pool of the wrong one.
//      This object itself is the one thing of a call which isn't pooled (64 bytes per run, everything
//      else is 0): it's held by the caller across frames, so a pooled one handed out twice would let a
//      stale reference drive somebody else's run - the hazard the versions guard against inside, which
//      is much easier to trip over in user code (a Tick() from Update() after OnDestroy() disposed it).
//      A run is started per flow, not per frame, so the object is cheap at that rate. BhlFlow, which
//      holds a run only inside itself, keeps the BhlTaskCall without it.
//      It's a step of a Unity coroutine too: 'yield return run' ticks it once a frame (MoveNext), and the
//      run is disposed once the function is over; a coroutine stopped before (StopCoroutine, the object
//      destroyed) doesn't dispose a nested one, Dispose() it then, e.g. in OnDisable
public sealed class BhlTaskRun<T> : IDisposable, System.Collections.IEnumerator
{
  BhlTaskCall<T> call;

  internal BhlTaskRun(BhlTaskCall<T> call)
  {
    this.call = call;
  }

  //NOTE: the function is over and its result (or its exception) is taken
  public bool IsCompleted => call.IsCompleted;

  public bool IsDisposed => call.IsDisposed;

  public T Result => call.Result;

  //NOTE: one tick of the call, as the VM makes for a BhlTaskCoroutine; false when there's nothing left
  //      to tick (over or disposed). Throws what the function threw, and the exception of a forgotten
  //      task of the call
  public bool Tick() => call.Tick();

  //NOTE: cancels what's left of the call (its pending functions and forgotten tasks) and takes the
  //      pooled objects back, as the VM dropping a call does. Safe to call more than once and after
  //      the run is over
  public void Dispose() => call.Dispose();

  //NOTE: a tick per frame, Unity calls it right when the run is yielded: the first tick only marks the call
  //      started, as Tick() does. Over (or faulted, the exception goes to the coroutine) - disposed, Result stays
  public bool MoveNext()
  {
    bool more;
    try
    {
      more = call.Tick();
    }
    catch
    {
      call.Dispose();
      throw;
    }
    if(!more)
      call.Dispose();
    return more;
  }

  //NOTE: null - the next frame
  public object Current => null;

  public void Reset() => throw new NotSupportedException();
}

//NOTE: the state of a run, see BhlTaskRun<T>: a struct, held by the run or by whoever owns it alone (BhlFlow);
//      a copy isn't a run of its own, it's used in place only
internal struct BhlTaskCall<T>
{
  BhlTaskScope scope;
  BhlTask<T> task;
  bool started;
  bool done;
  T result;
  Exception error;

  internal BhlTaskCall(BhlTaskScope scope, BhlTask<T> task)
  {
    this.scope = scope;
    this.task = task;
    started = false;
    done = false;
    result = default;
    error = null;
  }

  public bool IsCompleted => done;

  public bool IsDisposed => scope == null;

  public T Result
  {
    get
    {
      if(!done)
        throw new InvalidOperationException("BhlTaskRun is not completed");
      if(error != null)
        throw new InvalidOperationException("BhlTaskRun faulted", error);
      return result;
    }
  }

  public bool Tick()
  {
    if(scope == null || done)
      return false;

    //NOTE: the call itself was the first step - it ran to its first await in Start(), as a BHL call
    //      does when it's made, so what it queued runs in the next tick
    if(started)
      scope.Tick();
    else
      started = true;

    scope.ThrowIfFaulted();

    if(!task.IsCompleted)
      return true;

    var completed = task;
    task = default;
    try
    {
      result = completed.GetAwaiter().GetResult();
    }
    catch(Exception e)
    {
      error = e;
      throw;
    }
    finally
    {
      //NOTE: a faulted function is over as well
      done = true;
    }

    return false;
  }

  public void Dispose()
  {
    var s = scope;
    if(s == null)
      return;

    scope = null;
    task = default;
    s.Cancel();
    s.Release();
  }
}

}
