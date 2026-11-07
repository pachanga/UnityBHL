using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace BitGames.BHL
{

//NOTE: a source is pooled: a task holds the token of the version it was made by, a source taken back
//      by the pool (its result is taken) rejects the old token
interface IBhlTaskSource<T>
{
  bool IsCompleted(short token);
  T GetResult(short token);
  void OnCompleted(Action continuation, short token);
}

//NOTE: the completion state of a pooled source
struct BhlTaskCore<T>
{
  Action continuation;
  bool isCompleted;
  T result;
  ExceptionDispatchInfo error;
  short version;

  public short Version => version;
  public bool IsDone => isCompleted;
  public Action Continuation => continuation;
  public T Result => result;
  public ExceptionDispatchInfo Error => error;

  public bool IsCompleted(short token)
  {
    Validate(token);
    return isCompleted;
  }

  public void ValidateCompleted(short token)
  {
    Validate(token);
    if(!isCompleted)
      throw new InvalidOperationException("BhlTask is not completed");
  }

  public void OnCompleted(Action continuation, short token)
  {
    Validate(token);
    if(isCompleted)
    {
      continuation();
      return;
    }
    if(this.continuation != null)
      throw new InvalidOperationException("BhlTask is awaited twice");
    this.continuation = continuation;
  }

  public void SetResult(T result)
  {
    this.result = result;
    Complete();
  }

  public void SetException(Exception e)
  {
    error = ExceptionDispatchInfo.Capture(e);
    Complete();
  }

  public void Reset()
  {
    ++version;
    continuation = null;
    isCompleted = false;
    result = default;
    error = null;
  }

  void Validate(short token)
  {
    if(token != version)
      throw new InvalidOperationException("BhlTask is awaited after its result was taken");
  }

  void Complete()
  {
    isCompleted = true;
    var c = continuation;
    continuation = null;
    c?.Invoke();
  }
}

interface IBhlTaskRunner<T> : IBhlTaskSource<T>
{
  Action MoveNext { get; }
  short Version { get; }
  void SetResult(T result);
  void SetException(Exception e);
}

//NOTE: the boxed state machine of an async function and the source of its task, back to the pool
//      when the result is taken or the call is cancelled
sealed class BhlTaskRunner<TStateMachine, T> : IBhlTaskRunner<T>, IBhlTaskScopeItem, IBhlCancelResumable
  where TStateMachine : IAsyncStateMachine
{
  [ThreadStatic] static Stack<BhlTaskRunner<TStateMachine, T>> pool;

  public TStateMachine stateMachine;
  BhlTaskCore<T> core;
  BhlTaskScope scope;
  bool resumesOnCancel;

  public Action MoveNext { get; }
  public short Version => core.Version;
  public bool ResumesOnCancel => resumesOnCancel;

  BhlTaskRunner()
  {
    MoveNext = Run;
  }

  public static BhlTaskRunner<TStateMachine, T> Rent(bool resumesOnCancel)
  {
    var p = pool ??= new Stack<BhlTaskRunner<TStateMachine, T>>();
    var runner = p.Count > 0 ? p.Pop() : new BhlTaskRunner<TStateMachine, T>();
    runner.resumesOnCancel = resumesOnCancel;
    runner.scope = BhlTaskScope.Current;
    runner.scope?.Track(runner, runner.core.Version);
    return runner;
  }

  //NOTE: a BhlTask function is not resumed by a cancellation, even when what it awaits completes then
  void Run()
  {
    if(!resumesOnCancel && scope != null && scope.IsCancelled)
      return;
    stateMachine.MoveNext();
  }

  public bool IsCompleted(short token) => core.IsCompleted(token);

  public void OnCompleted(Action continuation, short token) => core.OnCompleted(continuation, token);

  //NOTE: called by the awaiter in the parent's MoveNext, the own MoveNext of the runner may still be
  //      on the stack below it: it touches nothing after SetResult
  public T GetResult(short token)
  {
    core.ValidateCompleted(token);
    var result = core.Result;
    var error = core.Error;
    Return();

    error?.Throw();
    return result;
  }

  public void SetResult(T result) => core.SetResult(result);

  public void SetException(Exception e) => core.SetException(e);

  //NOTE: a BhlTask function never resumes itself; when what awaits it is resumed, it is faulted instead
  public bool CancelStep(short version, BhlTaskScope scope)
  {
    if(version != core.Version || core.IsDone || resumesOnCancel)
      return false;
    if(!BhlTaskScope.ResumesOnCancel(core.Continuation))
      return false;
    core.SetException(scope.CanceledException);
    return true;
  }

  public void ReleaseByScope(short version)
  {
    if(version == core.Version)
      Return();
  }

  void Return()
  {
    core.Reset();
    stateMachine = default;
    scope?.Untrack();
    scope = null;
    (pool ??= new Stack<BhlTaskRunner<TStateMachine, T>>()).Push(this);
  }
}

//NOTE: a task failed at once (an async function which threw before its first await, FromException)
sealed class BhlTaskErrorSource<T> : IBhlTaskSource<T>
{
  readonly ExceptionDispatchInfo error;

  public BhlTaskErrorSource(Exception e)
  {
    error = ExceptionDispatchInfo.Capture(e);
  }

  public bool IsCompleted(short token) => true;

  public T GetResult(short token)
  {
    error.Throw();
    return default;
  }

  public void OnCompleted(Action continuation, short token) => continuation();
}

//NOTE: observes a forgotten task, so its exception fails the BHL call and its source gets back to the pool
sealed class BhlTaskForget<T> : IBhlTaskScopeItem
{
  [ThreadStatic] static Stack<BhlTaskForget<T>> pool;

  IBhlTaskSource<T> source;
  short token;
  BhlTaskScope scope;
  short scopeVersion;
  short version;
  readonly Action onCompleted;

  BhlTaskForget()
  {
    onCompleted = OnCompleted;
  }

  public static void Observe(IBhlTaskSource<T> source, short token)
  {
    var scope = BhlTaskScope.Require();
    var p = pool ??= new Stack<BhlTaskForget<T>>();
    var observer = p.Count > 0 ? p.Pop() : new BhlTaskForget<T>();
    observer.source = source;
    observer.token = token;
    observer.scope = scope;
    observer.scopeVersion = scope.Version;
    scope.Track(observer, observer.version);
    source.OnCompleted(observer.onCompleted, token);
  }

  void OnCompleted()
  {
    var source = this.source;
    var token = this.token;
    var scope = this.scope;
    var scopeVersion = this.scopeVersion;
    Return();

    try
    {
      source.GetResult(token);
    }
    catch(Exception e)
    {
      scope.ReportError(e, scopeVersion);
    }
  }

  public bool CancelStep(short version, BhlTaskScope scope) => false;

  public void ReleaseByScope(short version)
  {
    if(version == this.version)
      Return();
  }

  void Return()
  {
    ++version;
    source = null;
    scope?.Untrack();
    scope = null;
    (pool ??= new Stack<BhlTaskForget<T>>()).Push(this);
  }
}

//NOTE: the builders of BhlTask and BhlFinallyTask differ only in what a cancellation does
struct BhlTaskBuilderCore<T>
{
  IBhlTaskRunner<T> runner;
  T result;
  Exception error;

  //NOTE: no runner when the function finished without awaiting
  public BhlTask<T> Task
  {
    get
    {
      if(runner != null)
        return new BhlTask<T>(runner, runner.Version);
      if(error != null)
        return BhlTask<T>.FromException(error);
      return new BhlTask<T>(result);
    }
  }

  public void SetResult(T result)
  {
    if(runner == null)
      this.result = result;
    else
      runner.SetResult(result);
  }

  public void SetException(Exception e)
  {
    if(runner == null)
      error = e;
    else
      runner.SetException(e);
  }

  public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine, bool resumesOnCancel)
    where TAwaiter : INotifyCompletion
    where TStateMachine : IAsyncStateMachine
  {
    CheckOwn<TAwaiter>();
    awaiter.OnCompleted(GetMoveNext(ref stateMachine, resumesOnCancel));
  }

  public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine, bool resumesOnCancel)
    where TAwaiter : ICriticalNotifyCompletion
    where TStateMachine : IAsyncStateMachine
  {
    CheckOwn<TAwaiter>();
    awaiter.UnsafeOnCompleted(GetMoveNext(ref stateMachine, resumesOnCancel));
  }

  //NOTE: a foreign awaiter would resume the function outside of the ticks of the BHL call, after the call
  //      is gone too; the exception fails the task (the analyzer reports it at compile time, BHLT001)
  static void CheckOwn<TAwaiter>()
  {
    if(!BhlAwaiter<TAwaiter>.IsOwn)
      throw new NotSupportedException($"'{typeof(TAwaiter).FullName}' is awaited in a BhlTask function, convert the task with ToBhlTask()");
  }

  //NOTE: called on the builder inside the state machine: the runner is set before the state machine
  //      is copied into it, so the copy shares it
  Action GetMoveNext<TStateMachine>(ref TStateMachine stateMachine, bool resumesOnCancel) where TStateMachine : IAsyncStateMachine
  {
    if(runner == null)
    {
      var r = BhlTaskRunner<TStateMachine, T>.Rent(resumesOnCancel);
      runner = r;
      r.stateMachine = stateMachine;
    }
    return runner.MoveNext;
  }
}

public struct BhlTaskMethodBuilder<T>
{
  BhlTaskBuilderCore<T> core;

  public static BhlTaskMethodBuilder<T> Create() => default;

  public BhlTask<T> Task => core.Task;

  public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine => stateMachine.MoveNext();

  public void SetStateMachine(IAsyncStateMachine stateMachine)
  {}

  public void SetResult(T result) => core.SetResult(result);

  public void SetException(Exception e) => core.SetException(e);

  public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
    where TAwaiter : INotifyCompletion
    where TStateMachine : IAsyncStateMachine
  {
    core.AwaitOnCompleted(ref awaiter, ref stateMachine, false);
  }

  public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
    where TAwaiter : ICriticalNotifyCompletion
    where TStateMachine : IAsyncStateMachine
  {
    core.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine, false);
  }
}

public struct BhlTaskMethodBuilder
{
  BhlTaskBuilderCore<BhlTaskVoid> core;

  public static BhlTaskMethodBuilder Create() => default;

  public BhlTask Task => new BhlTask(core.Task);

  public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine => stateMachine.MoveNext();

  public void SetStateMachine(IAsyncStateMachine stateMachine)
  {}

  public void SetResult() => core.SetResult(default);

  public void SetException(Exception e) => core.SetException(e);

  public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
    where TAwaiter : INotifyCompletion
    where TStateMachine : IAsyncStateMachine
  {
    core.AwaitOnCompleted(ref awaiter, ref stateMachine, false);
  }

  public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
    where TAwaiter : ICriticalNotifyCompletion
    where TStateMachine : IAsyncStateMachine
  {
    core.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine, false);
  }
}

public struct BhlFinallyTaskMethodBuilder<T>
{
  BhlTaskBuilderCore<T> core;

  public static BhlFinallyTaskMethodBuilder<T> Create() => default;

  public BhlFinallyTask<T> Task => new BhlFinallyTask<T>(core.Task);

  public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine => stateMachine.MoveNext();

  public void SetStateMachine(IAsyncStateMachine stateMachine)
  {}

  public void SetResult(T result) => core.SetResult(result);

  public void SetException(Exception e) => core.SetException(e);

  public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
    where TAwaiter : INotifyCompletion
    where TStateMachine : IAsyncStateMachine
  {
    core.AwaitOnCompleted(ref awaiter, ref stateMachine, true);
  }

  public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
    where TAwaiter : ICriticalNotifyCompletion
    where TStateMachine : IAsyncStateMachine
  {
    core.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine, true);
  }
}

public struct BhlFinallyTaskMethodBuilder
{
  BhlTaskBuilderCore<BhlTaskVoid> core;

  public static BhlFinallyTaskMethodBuilder Create() => default;

  public BhlFinallyTask Task => new BhlFinallyTask(new BhlTask(core.Task));

  public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine => stateMachine.MoveNext();

  public void SetStateMachine(IAsyncStateMachine stateMachine)
  {}

  public void SetResult() => core.SetResult(default);

  public void SetException(Exception e) => core.SetException(e);

  public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
    where TAwaiter : INotifyCompletion
    where TStateMachine : IAsyncStateMachine
  {
    core.AwaitOnCompleted(ref awaiter, ref stateMachine, true);
  }

  public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
    where TAwaiter : ICriticalNotifyCompletion
    where TStateMachine : IAsyncStateMachine
  {
    core.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine, true);
  }
}

}
