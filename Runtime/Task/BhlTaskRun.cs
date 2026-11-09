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
      //NOTE: Exit() first: the scope stops being current before it's taken back
      scope.Exit();
      scope.Cancel();
      scope.Release();
      throw;
    }
    scope.Exit();

    return new BhlTaskRun<BhlTaskVoid>(scope, task);
  }

  public static BhlTaskRun<T> Start<T>(Func<BhlTask<T>> fn)
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
      scope.Exit();
      scope.Cancel();
      scope.Release();
      throw;
    }
    scope.Exit();

    return new BhlTaskRun<T>(scope, task);
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
//      A run is started per flow, not per frame, so the object is cheap at that rate
public sealed class BhlTaskRun<T> : IDisposable
{
  BhlTaskScope scope;
  BhlTask<T> task;
  bool started;
  bool done;
  T result;
  Exception error;

  internal BhlTaskRun(BhlTaskScope scope, BhlTask<T> task)
  {
    this.scope = scope;
    this.task = task;
  }

  //NOTE: the function is over and its result (or its exception) is taken
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

  //NOTE: one tick of the call, as the VM makes for a BhlTaskCoroutine; false when there's nothing left
  //      to tick (over or disposed). Throws what the function threw, and the exception of a forgotten
  //      task of the call
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

  //NOTE: cancels what's left of the call (its pending functions and forgotten tasks) and takes the
  //      pooled objects back, as the VM dropping a call does. Safe to call more than once and after
  //      the run is over
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
