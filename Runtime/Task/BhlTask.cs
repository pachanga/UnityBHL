using System;
using System.Runtime.CompilerServices;
#if !NO_UNITY
using UnityEngine;
#endif

namespace BitGames.BHL
{

//NOTE: an async C# function bound as a BHL coroutine returns it, see README.md of the package.
//      A cancelled call drops its BhlTask functions without resuming them: no exceptions, no allocations,
//      and no finally blocks either (the analyzer rejects the ones around awaits); BhlFinallyTask resumes
//      them with OperationCanceledException instead
[AsyncMethodBuilder(typeof(BhlTaskMethodBuilder))]
public readonly struct BhlTask
{
  readonly BhlTask<BhlTaskVoid> task;

  internal BhlTask(BhlTask<BhlTaskVoid> task)
  {
    this.task = task;
  }

  public static BhlTask CompletedTask => default;

  public static BhlTask FromException(Exception e) => new BhlTask(BhlTask<BhlTaskVoid>.FromException(e));

  public bool IsCompleted => task.IsCompleted;

  public BhlTask<BhlTaskVoid> AsVoidTask() => task;

  public Awaiter GetAwaiter() => new Awaiter(task.GetAwaiter());

  //NOTE: runs on without being awaited, its exception fails the BHL call
  public void Forget() => task.Forget();

  //NOTE: resumes in the next tick of the BHL call, as yield() in BHL
  public static YieldAwaitable Yield() => default;

#if !NO_UNITY
  //NOTE: scaled time (Time.time): Time.timeScale speeds it up as it does the game; out of Unity
  //      there's no game clock, a host there waits on its own
  public static async BhlTask WaitSeconds(float seconds)
  {
    float end = Time.time + seconds;
    while(Time.time < end)
      await Yield();
  }
#endif

  public readonly struct Awaiter : ICriticalNotifyCompletion, IBhlAwaiter
  {
    readonly BhlTask<BhlTaskVoid>.Awaiter awaiter;

    internal Awaiter(BhlTask<BhlTaskVoid>.Awaiter awaiter)
    {
      this.awaiter = awaiter;
    }

    public bool IsCompleted => awaiter.IsCompleted;

    public void GetResult() => awaiter.GetResult();

    public void OnCompleted(Action continuation) => awaiter.OnCompleted(continuation);

    public void UnsafeOnCompleted(Action continuation) => awaiter.UnsafeOnCompleted(continuation);
  }

  public readonly struct YieldAwaitable
  {
    public YieldAwaiter GetAwaiter() => default;
  }

  public readonly struct YieldAwaiter : ICriticalNotifyCompletion, IBhlAwaiter
  {
    public bool IsCompleted => false;

    //NOTE: continuations run with the scope current; only a BhlFinallyTask is resumed by a cancellation
    public void GetResult()
    {
      var scope = BhlTaskScope.Current;
      if(scope != null && scope.IsCancelled)
        throw scope.CanceledException;
    }

    public void OnCompleted(Action continuation) => UnsafeOnCompleted(continuation);

    public void UnsafeOnCompleted(Action continuation)
    {
      BhlTaskScope.Require().Post(continuation);
    }
  }
}

[AsyncMethodBuilder(typeof(BhlTaskMethodBuilder<>))]
public readonly struct BhlTask<T>
{
  readonly IBhlTaskSource<T> source;
  readonly short token;
  readonly T result;

  internal BhlTask(IBhlTaskSource<T> source, short token)
  {
    this.source = source;
    this.token = token;
    result = default;
  }

  public BhlTask(T result)
  {
    source = null;
    token = 0;
    this.result = result;
  }

  public static BhlTask<T> FromException(Exception e) => new BhlTask<T>(new BhlTaskErrorSource<T>(e), 0);

  public bool IsCompleted => source == null || source.IsCompleted(token);

  public Awaiter GetAwaiter() => new Awaiter(this);

  //NOTE: runs on without being awaited, its exception fails the BHL call
  public void Forget()
  {
    if(source != null)
      BhlTaskForget<T>.Observe(source, token);
  }

  public readonly struct Awaiter : ICriticalNotifyCompletion, IBhlAwaiter
  {
    readonly BhlTask<T> task;

    internal Awaiter(BhlTask<T> task)
    {
      this.task = task;
    }

    public bool IsCompleted => task.IsCompleted;

    public T GetResult() => task.source == null ? task.result : task.source.GetResult(task.token);

    public void OnCompleted(Action continuation) => UnsafeOnCompleted(continuation);

    public void UnsafeOnCompleted(Action continuation) => task.source.OnCompleted(continuation, task.token);
  }
}

//NOTE: a BhlTask whose function is resumed by a cancellation of the BHL call with OperationCanceledException,
//      so its finally and using blocks run; costs exceptions (~1 KB per awaiting function) when cancelled.
//      A separate type since a method can't pick its builder in C# 9 (an attribute would do in C# 10)
[AsyncMethodBuilder(typeof(BhlFinallyTaskMethodBuilder))]
public readonly struct BhlFinallyTask
{
  readonly BhlTask task;

  internal BhlFinallyTask(BhlTask task)
  {
    this.task = task;
  }

  public bool IsCompleted => task.IsCompleted;

  public BhlTask.Awaiter GetAwaiter() => task.GetAwaiter();

  public void Forget() => task.Forget();

  public static implicit operator BhlTask(BhlFinallyTask task) => task.task;
}

[AsyncMethodBuilder(typeof(BhlFinallyTaskMethodBuilder<>))]
public readonly struct BhlFinallyTask<T>
{
  readonly BhlTask<T> task;

  internal BhlFinallyTask(BhlTask<T> task)
  {
    this.task = task;
  }

  public bool IsCompleted => task.IsCompleted;

  public BhlTask<T>.Awaiter GetAwaiter() => task.GetAwaiter();

  public void Forget() => task.Forget();

  public static implicit operator BhlTask<T>(BhlFinallyTask<T> task) => task.task;
}

//NOTE: the result of a BhlTask without one
public readonly struct BhlTaskVoid
{}

//NOTE: an awaiter which resumes in the ticks of the current BhlTaskScope; only these are awaited in
//      a BhlTask function, a foreign task is converted with ToBhlTask()
interface IBhlAwaiter
{}

static class BhlAwaiter<TAwaiter>
{
  public static readonly bool IsOwn = typeof(IBhlAwaiter).IsAssignableFrom(typeof(TAwaiter));
}

}
