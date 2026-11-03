using System.Collections.Generic;

namespace BitGames.BHL
{

//NOTE: a step of an iterator bound as a BHL coroutine (IEnumerator<BhlStep>), see README.md of the package.
//      The other way of BhlTask: an iterator allocates itself on every call, its steps don't, and a cancelled
//      call disposes it, so its finally and using blocks run without exceptions
public readonly struct BhlStep
{
  internal enum Kind : byte
  {
    Yield,
    Run,
    WaitSeconds,
    Await,
  }

  internal readonly Kind kind;
  internal readonly float seconds;
  internal readonly IEnumerator<BhlStep> child;
  internal readonly BhlTask<BhlTaskVoid> task;

  BhlStep(Kind kind, float seconds, IEnumerator<BhlStep> child, BhlTask<BhlTaskVoid> task)
  {
    this.kind = kind;
    this.seconds = seconds;
    this.child = child;
    this.task = task;
  }

  //NOTE: resumes in the next tick of the BHL call, as yield() in BHL; the same as 'yield return default'
  public static BhlStep Yield => default;

  //NOTE: runs a nested iterator to its end, in the same tick as far as it doesn't wait
  public static BhlStep Run(IEnumerator<BhlStep> child) => new BhlStep(Kind.Run, 0, child, default);

  //NOTE: scaled time (Time.time), as BhlTask.WaitSeconds
  public static BhlStep WaitSeconds(float seconds) => new BhlStep(Kind.WaitSeconds, seconds, null, default);

  //NOTE: waits for a task (its result is not available here), the task's exception fails the call
  public static BhlStep Await(BhlTask task) => new BhlStep(Kind.Await, 0, null, task.AsVoidTask());

  public static implicit operator BhlStep(BhlTask task) => Await(task);

  public static implicit operator BhlStep(BhlFinallyTask task) => Await(task);
}

//NOTE: a step of an iterator returning a value: 'yield return BhlStep<T>.Return(v)' ends it with the value
public readonly struct BhlStep<T>
{
  internal readonly BhlStep step;
  internal readonly bool isReturn;
  internal readonly T result;

  BhlStep(BhlStep step, bool isReturn, T result)
  {
    this.step = step;
    this.isReturn = isReturn;
    this.result = result;
  }

  public static BhlStep<T> Return(T result) => new BhlStep<T>(default, true, result);

  public static implicit operator BhlStep<T>(BhlStep step) => new BhlStep<T>(step, false, default);

  public static implicit operator BhlStep<T>(BhlTask task) => BhlStep.Await(task);

  public static implicit operator BhlStep<T>(BhlFinallyTask task) => BhlStep.Await(task);
}

}
