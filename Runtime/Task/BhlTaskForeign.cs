using System;
using System.Collections.Generic;
using System.Threading.Tasks;
#if BHL_UNITASK
using Cysharp.Threading.Tasks;
#endif

namespace BitGames.BHL
{

//NOTE: a foreign task completes outside of the BHL call (the player loop, the synchronization context),
//      its BhlTask completes in the first tick of the call after that. When the call is cancelled the
//      BhlTask is dropped (a BhlFinallyTask awaiting it gets OperationCanceledException) while the foreign
//      task runs on: pass it a CancellationToken of your own to stop it
public static class BhlTaskForeignExtensions
{
//NOTE: only when the project has UniTask (versionDefines of the asmdef)
#if BHL_UNITASK
  public static BhlTask ToBhlTask(this UniTask task)
  {
    var awaiter = task.GetAwaiter();
    if(awaiter.IsCompleted)
    {
      try
      {
        awaiter.GetResult();
        return BhlTask.CompletedTask;
      }
      catch(Exception e)
      {
        return BhlTask.FromException(e);
      }
    }
    return new BhlTask(BhlUniTaskSource.Create(awaiter));
  }

  public static BhlTask<T> ToBhlTask<T>(this UniTask<T> task)
  {
    var awaiter = task.GetAwaiter();
    if(awaiter.IsCompleted)
    {
      try
      {
        return new BhlTask<T>(awaiter.GetResult());
      }
      catch(Exception e)
      {
        return BhlTask<T>.FromException(e);
      }
    }
    return BhlUniTaskSource<T>.Create(awaiter);
  }
#endif

  //NOTE: the continuation of a Task allocates and goes through the synchronization context of the
  //      main thread (UnitySynchronizationContext): the BhlTask completes a frame later than the Task
  public static BhlTask ToBhlTask(this Task task)
  {
    if(task.IsCompleted)
    {
      try
      {
        task.GetAwaiter().GetResult();
        return BhlTask.CompletedTask;
      }
      catch(Exception e)
      {
        return BhlTask.FromException(e);
      }
    }
    return new BhlTask(BhlSystemTaskSource.Create(task));
  }

  public static BhlTask<T> ToBhlTask<T>(this Task<T> task)
  {
    if(task.IsCompleted)
    {
      try
      {
        return new BhlTask<T>(task.GetAwaiter().GetResult());
      }
      catch(Exception e)
      {
        return BhlTask<T>.FromException(e);
      }
    }
    return BhlSystemTaskSource<T>.Create(task);
  }
}

//NOTE: a pooled source completed by a foreign task: back to the pool once its result is taken (or the
//      call is cancelled) and the foreign task is done, the foreign result is always taken
abstract class BhlForeignSource<T> : IBhlTaskSource<T>, IBhlTaskScopeItem, IBhlCancelResumable
{
  BhlTaskCore<T> core;
  BhlTaskScope scope;
  short scopeVersion;
  bool isForeignPending;
  bool isTaken;
  protected readonly Action onForeignCompleted;
  readonly Action onScopeTick;

  public bool ResumesOnCancel => true;

  protected BhlForeignSource()
  {
    onForeignCompleted = OnForeignCompleted;
    onScopeTick = OnScopeTick;
  }

  protected abstract void Subscribe();
  protected abstract T TakeForeignResult();
  protected abstract void ReturnToPool();

  protected short Start(BhlTaskScope scope)
  {
    this.scope = scope;
    scopeVersion = scope.Version;
    isForeignPending = true;
    scope.Track(this, core.Version);
    Subscribe();
    return core.Version;
  }

  public bool IsCompleted(short token) => core.IsCompleted(token);

  public void OnCompleted(Action continuation, short token) => core.OnCompleted(continuation, token);

  public T GetResult(short token)
  {
    core.ValidateCompleted(token);
    var result = core.Result;
    var error = core.Error;
    isTaken = true;
    TryReturn();

    error?.Throw();
    return result;
  }

  public bool CancelStep(short version, BhlTaskScope scope)
  {
    if(version != core.Version || core.IsDone || !BhlTaskScope.ResumesOnCancel(core.Continuation))
      return false;
    core.SetException(scope.CanceledException);
    return true;
  }

  public void ReleaseByScope(short version)
  {
    if(version != core.Version)
      return;
    isTaken = true;
    TryReturn();
  }

  //NOTE: outside of the call
  void OnForeignCompleted()
  {
    if(scope.Version == scopeVersion && !scope.IsCancelled)
    {
      scope.Post(onScopeTick);
      return;
    }
    ReleaseForeign();
  }

  //NOTE: in the tick of the call, or in its cancellation when it came in between
  void OnScopeTick()
  {
    if(scope.IsCancelled || core.IsDone)
    {
      ReleaseForeign();
      return;
    }

    isForeignPending = false;
    T result;
    try
    {
      result = TakeForeignResult();
    }
    catch(Exception e)
    {
      core.SetException(e);
      return;
    }
    core.SetResult(result);
  }

  void ReleaseForeign()
  {
    isForeignPending = false;
    try
    {
      TakeForeignResult();
    }
    catch
    {
      //NOTE: nobody waits for the result anymore
    }
    TryReturn();
  }

  void TryReturn()
  {
    if(!isTaken || isForeignPending)
      return;
    core.Reset();
    scope = null;
    isTaken = false;
    ReturnToPool();
  }
}

#if BHL_UNITASK
sealed class BhlUniTaskSource : BhlForeignSource<BhlTaskVoid>
{
  static readonly Stack<BhlUniTaskSource> pool = new Stack<BhlUniTaskSource>();

  UniTask.Awaiter awaiter;

  public static BhlTask<BhlTaskVoid> Create(UniTask.Awaiter awaiter)
  {
    var scope = BhlTaskScope.Require();
    var source = pool.Count > 0 ? pool.Pop() : new BhlUniTaskSource();
    source.awaiter = awaiter;
    return new BhlTask<BhlTaskVoid>(source, source.Start(scope));
  }

  protected override void Subscribe() => awaiter.UnsafeOnCompleted(onForeignCompleted);

  protected override BhlTaskVoid TakeForeignResult()
  {
    var a = awaiter;
    awaiter = default;
    a.GetResult();
    return default;
  }

  protected override void ReturnToPool() => pool.Push(this);
}

sealed class BhlUniTaskSource<T> : BhlForeignSource<T>
{
  static readonly Stack<BhlUniTaskSource<T>> pool = new Stack<BhlUniTaskSource<T>>();

  UniTask<T>.Awaiter awaiter;

  public static BhlTask<T> Create(UniTask<T>.Awaiter awaiter)
  {
    var scope = BhlTaskScope.Require();
    var source = pool.Count > 0 ? pool.Pop() : new BhlUniTaskSource<T>();
    source.awaiter = awaiter;
    return new BhlTask<T>(source, source.Start(scope));
  }

  protected override void Subscribe() => awaiter.UnsafeOnCompleted(onForeignCompleted);

  protected override T TakeForeignResult()
  {
    var a = awaiter;
    awaiter = default;
    return a.GetResult();
  }

  protected override void ReturnToPool() => pool.Push(this);
}
#endif

sealed class BhlSystemTaskSource : BhlForeignSource<BhlTaskVoid>
{
  static readonly Stack<BhlSystemTaskSource> pool = new Stack<BhlSystemTaskSource>();

  Task task;

  public static BhlTask<BhlTaskVoid> Create(Task task)
  {
    var scope = BhlTaskScope.Require();
    var source = pool.Count > 0 ? pool.Pop() : new BhlSystemTaskSource();
    source.task = task;
    return new BhlTask<BhlTaskVoid>(source, source.Start(scope));
  }

  protected override void Subscribe() => task.GetAwaiter().UnsafeOnCompleted(onForeignCompleted);

  protected override BhlTaskVoid TakeForeignResult()
  {
    var t = task;
    task = null;
    t.GetAwaiter().GetResult();
    return default;
  }

  protected override void ReturnToPool() => pool.Push(this);
}

sealed class BhlSystemTaskSource<T> : BhlForeignSource<T>
{
  static readonly Stack<BhlSystemTaskSource<T>> pool = new Stack<BhlSystemTaskSource<T>>();

  Task<T> task;

  public static BhlTask<T> Create(Task<T> task)
  {
    var scope = BhlTaskScope.Require();
    var source = pool.Count > 0 ? pool.Pop() : new BhlSystemTaskSource<T>();
    source.task = task;
    return new BhlTask<T>(source, source.Start(scope));
  }

  protected override void Subscribe() => task.GetAwaiter().UnsafeOnCompleted(onForeignCompleted);

  protected override T TakeForeignResult()
  {
    var t = task;
    task = null;
    return t.GetAwaiter().GetResult();
  }

  protected override void ReturnToPool() => pool.Push(this);
}

}
