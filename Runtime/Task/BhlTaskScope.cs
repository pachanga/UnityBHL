using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using UnityEngine;

namespace BitGames.BHL
{

//NOTE: a pooled object of a BHL call (a runner, a ToBhlTask source, an observer of a forgotten task):
//      the scope takes it back when the call is cancelled
interface IBhlTaskScopeItem
{
  //NOTE: faults a pending task awaited by a function resumed on cancel (a BhlFinallyTask), true if it did
  bool CancelStep(short version, BhlTaskScope scope);
  void ReleaseByScope(short version);
}

//NOTE: the target of a continuation which runs during a cancellation
interface IBhlCancelResumable
{
  bool ResumesOnCancel { get; }
}

//NOTE: one per BHL call of a BhlTask binding: the queue of the continuations of its async functions,
//      run by the call's coroutine once per tick, and the pooled objects of the call. Pooled; main
//      thread only, as the VM
public sealed class BhlTaskScope
{
  [ThreadStatic] static BhlTaskScope current;
  static readonly Stack<BhlTaskScope> pool = new Stack<BhlTaskScope>();

  internal static BhlTaskScope Current => current;

  List<Action> queue = new List<Action>();
  List<Action> running = new List<Action>();
  readonly List<(IBhlTaskScopeItem item, short version)> items = new List<(IBhlTaskScopeItem, short)>();
  BhlTaskScope prev;
  ExceptionDispatchInfo error;
  OperationCanceledException canceled;
  //NOTE: changes when the scope goes back to the pool, late callbacks check it
  short version;

  public bool IsCancelled { get; private set; }

  internal short Version => version;

  //NOTE: one per cancellation, made only when a BhlFinallyTask is resumed
  internal OperationCanceledException CanceledException => canceled ??= new OperationCanceledException();

  BhlTaskScope()
  {}

  internal static BhlTaskScope Require()
  {
    return current ?? throw new InvalidOperationException("BhlTask is used outside of a BHL call");
  }

  //NOTE: the scope is current while the binding is called, its async functions capture it
  public static BhlTaskScope Enter()
  {
    var scope = pool.Count > 0 ? pool.Pop() : new BhlTaskScope();
    scope.prev = current;
    current = scope;
    return scope;
  }

  public void Exit()
  {
    current = prev;
    prev = null;
  }

  //NOTE: makes the scope current while an iterator of the call runs
  internal BhlTaskScope Activate()
  {
    var prevScope = current;
    current = this;
    return prevScope;
  }

  internal static void Restore(BhlTaskScope prevScope)
  {
    current = prevScope;
  }

  internal void Release()
  {
    ++version;
    IsCancelled = false;
    error = null;
    canceled = null;
    queue.Clear();
    running.Clear();
    items.Clear();
    pool.Push(this);
  }

  internal void Post(Action continuation)
  {
    //NOTE: nothing is resumed after the cancellation is over
    if(IsCancelled && current != this)
      return;
    queue.Add(continuation);
  }

  //NOTE: an item back in the pool is not removed, its version won't match
  internal void Track(IBhlTaskScopeItem item, short itemVersion)
  {
    items.Add((item, itemVersion));
  }

  //NOTE: the exception of a forgotten task: thrown into the VM by the next tick of the call, logged
  //      when the call is being cancelled (nothing to throw it into)
  internal void ReportError(Exception e, short scopeVersion)
  {
    if(scopeVersion != version || IsCancelled)
    {
      if(!(e is OperationCanceledException))
        Debug.LogException(e);
      return;
    }

    if(error == null)
      error = ExceptionDispatchInfo.Capture(e);
    else
      Debug.LogException(e);
  }

  internal void ThrowIfFaulted()
  {
    if(error == null)
      return;
    var e = error;
    error = null;
    e.Throw();
  }

  //NOTE: runs the continuations queued before this tick, the ones they queue wait for the next one
  internal void Tick()
  {
    var prevScope = Activate();
    try
    {
      (queue, running) = (running, queue);
      for(int i = 0; i < running.Count; ++i)
        running[i]();
    }
    finally
    {
      running.Clear();
      Restore(prevScope);
    }
  }

  //NOTE: drops the continuations and takes the pooled objects of the call back. The BhlFinallyTask functions
  //      are resumed first with OperationCanceledException (their awaits in finally blocks resume at once too):
  //      what they await is faulted, their own continuations are run, the rest is never resumed. A call of
  //      BhlTask functions only is cancelled without exceptions
  internal void Cancel()
  {
    if(IsCancelled)
      return;
    IsCancelled = true;

    var prevScope = Activate();
    try
    {
      bool progress = true;
      while(progress)
      {
        progress = false;

        for(int i = 0; i < items.Count; ++i)
        {
          if(items[i].item.CancelStep(items[i].version, this))
            progress = true;
        }

        if(queue.Count > 0)
        {
          (queue, running) = (running, queue);
          try
          {
            for(int i = 0; i < running.Count; ++i)
            {
              var continuation = running[i];
              if(ResumesOnCancel(continuation))
              {
                continuation();
                progress = true;
              }
            }
          }
          finally
          {
            running.Clear();
          }
        }
      }

      for(int i = 0; i < items.Count; ++i)
        items[i].item.ReleaseByScope(items[i].version);
    }
    finally
    {
      items.Clear();
      queue.Clear();
      Restore(prevScope);
    }
  }

  internal static bool ResumesOnCancel(Action continuation)
  {
    return continuation?.Target is IBhlCancelResumable r && r.ResumesOnCancel;
  }
}

}
