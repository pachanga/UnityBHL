using System;
using System.Runtime.CompilerServices;

//NOTE: the awaiter shape of UniTask, completed by hand: StubSource.Complete / Fail

namespace Cysharp.Threading.Tasks
{

public class StubSource<T>
{
  public bool done;
  public T result;
  public Exception error;
  public Action continuation;
  public int taken;

  public void Complete(T result)
  {
    done = true;
    this.result = result;
    Resume();
  }

  public void Fail(Exception e)
  {
    done = true;
    error = e;
    Resume();
  }

  void Resume()
  {
    var c = continuation;
    continuation = null;
    c?.Invoke();
  }
}

public readonly struct UniTask
{
  readonly StubSource<bool> source;

  public UniTask(StubSource<bool> source)
  {
    this.source = source;
  }

  public Awaiter GetAwaiter() => new Awaiter(source);

  public readonly struct Awaiter : ICriticalNotifyCompletion
  {
    readonly StubSource<bool> source;

    public Awaiter(StubSource<bool> source)
    {
      this.source = source;
    }

    public bool IsCompleted => source == null || source.done;

    public void GetResult()
    {
      if(source == null)
        return;
      source.taken++;
      if(source.error != null)
        throw source.error;
    }

    public void OnCompleted(Action continuation) => UnsafeOnCompleted(continuation);

    public void UnsafeOnCompleted(Action continuation) => source.continuation = continuation;
  }
}

public readonly struct UniTask<T>
{
  readonly StubSource<T> source;

  public UniTask(StubSource<T> source)
  {
    this.source = source;
  }

  public Awaiter GetAwaiter() => new Awaiter(source);

  public readonly struct Awaiter : ICriticalNotifyCompletion
  {
    readonly StubSource<T> source;

    public Awaiter(StubSource<T> source)
    {
      this.source = source;
    }

    public bool IsCompleted => source.done;

    public T GetResult()
    {
      source.taken++;
      if(source.error != null)
        throw source.error;
      return source.result;
    }

    public void OnCompleted(Action continuation) => UnsafeOnCompleted(continuation);

    public void UnsafeOnCompleted(Action continuation) => source.continuation = continuation;
  }
}

}
