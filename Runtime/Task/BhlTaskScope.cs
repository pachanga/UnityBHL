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
//      run by the call's coroutine once per tick, and the pooled objects of the call. Used by the thread
//      of its VM; the pools are per thread, as the ones of bhl, so VMs of different threads don't share
//      them. Only a foreign task (ToBhlTask) may complete on another thread, it posts by PostRemote
public sealed class BhlTaskScope
{
  [ThreadStatic] static BhlTaskScope current;
  [ThreadStatic] static Stack<BhlTaskScope> pool;

  internal static BhlTaskScope Current => current;

  //NOTE: the first queued continuation, the rest (several async functions awaiting at once) go to queue
  Action first;
  List<Action> queue = new List<Action>();
  List<Action> running = new List<Action>();
  readonly List<(IBhlTaskScopeItem item, short version)> items = new List<(IBhlTaskScopeItem, short)>();
  BhlTaskScope prev;
  ExceptionDispatchInfo error;
  OperationCanceledException canceled;
  //NOTE: changes when the scope goes back to the pool, late callbacks check it
  short version;
  //NOTE: the continuations posted by other threads, taken by the next tick
  readonly object remoteLock = new object();
  readonly List<Action> remote = new List<Action>();
  volatile bool hasRemote;

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
    var p = pool ??= new Stack<BhlTaskScope>();
    var scope = p.Count > 0 ? p.Pop() : new BhlTaskScope();
    scope.prev = current;
    current = scope;
    return scope;
  }

  public void Exit()
  {
    current = prev;
    prev = null;
  }

  //NOTE: makes the scope current while the continuations of the call run
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
    lock(remoteLock)
    {
      ++version;
      IsCancelled = false;
      remote.Clear();
      hasRemote = false;
    }
    error = null;
    canceled = null;
    first = null;
    queue.Clear();
    running.Clear();
    items.Clear();
    (pool ??= new Stack<BhlTaskScope>()).Push(this);
  }

  internal void Post(Action continuation)
  {
    //NOTE: nothing is resumed after the cancellation is over
    if(IsCancelled && current != this)
      return;
    Enqueue(continuation);
  }

  //NOTE: from any thread (a foreign task completed outside of the call); false if the call it was posted
  //      for is cancelled or over
  internal bool PostRemote(Action continuation, short scopeVersion)
  {
    lock(remoteLock)
    {
      if(scopeVersion != version || IsCancelled)
        return false;
      remote.Add(continuation);
      hasRemote = true;
      return true;
    }
  }

  void Enqueue(Action continuation)
  {
    if(first == null && queue.Count == 0)
      first = continuation;
    else
      queue.Add(continuation);
  }

  //NOTE: under remoteLock
  void TakeRemote()
  {
    for(int i = 0; i < remote.Count; ++i)
      Enqueue(remote[i]);
    remote.Clear();
    hasRemote = false;
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
    if(hasRemote)
    {
      lock(remoteLock)
        TakeRemote();
    }

    if(first == null)
      return;

    var prevScope = Activate();
    try
    {
      var continuation = first;
      first = null;
      if(queue.Count == 0)
      {
        continuation();
        return;
      }

      (queue, running) = (running, queue);
      continuation();
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
    lock(remoteLock)
    {
      if(IsCancelled)
        return;
      IsCancelled = true;
      //NOTE: what came from other threads before is cancelled as the rest, nothing comes after
      TakeRemote();
    }

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

        if(first != null)
        {
          queue.Insert(0, first);
          first = null;
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
      first = null;
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
