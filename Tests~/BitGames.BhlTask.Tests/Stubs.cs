using System;
using System.Collections.Generic;

//NOTE: the parts of Unity and of the bhl VM the runtime touches, so it's tested by plain dotnet

namespace UnityEngine
{

public static class Time
{
  public static float time;
}

//NOTE: only to keep the 'Coroutine' ambiguity of the real UnityEngine
public sealed class Coroutine
{}

public static class Debug
{
  public static int logged;

  public static void LogException(Exception e)
  {
    ++logged;
  }
}

}

namespace bhl
{

public enum BHS
{
  NONE,
  SUCCESS,
  RUNNING,
  FAILURE,
}

public class VM
{
  public class ExecState
  {
    public BHS status;
    public VM vm;
    public List<object> pushed = new List<object>();
  }
}

public abstract class Coroutine
{
  public abstract void Tick(VM.ExecState exec);

  public virtual void Destruct(VM.ExecState exec)
  {}
}

//NOTE: pooled as in the VM (per thread too), so allocations of the runtime are measured alone
public static class CoroutinePool
{
  static class Holder<T>
  {
    [ThreadStatic] static Stack<Coroutine> _pool;
    public static Stack<Coroutine> pool => _pool ??= new Stack<Coroutine>();
  }

  [ThreadStatic] static Dictionary<Type, Stack<Coroutine>> _pools;
  static Dictionary<Type, Stack<Coroutine>> pools => _pools ??= new Dictionary<Type, Stack<Coroutine>>();

  public static T New<T>(VM vm) where T : Coroutine, new()
  {
    var pool = Holder<T>.pool;
    pools[typeof(T)] = pool;
    return pool.Count > 0 ? (T)pool.Pop() : new T();
  }

  public static void Del(VM.ExecState exec, Coroutine coro)
  {
    coro.Destruct(exec);
    pools[coro.GetType()].Push(coro);
  }
}

}
