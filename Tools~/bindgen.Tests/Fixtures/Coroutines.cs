using System;
using UnityBHL;
using bhl;

//NOTE: a [BhlCoroutine] class is hand written in the namespace the generator writes into (the config's
//      'namespace'): the generated partial completes it with the args, Tick and Destruct
namespace Fixtures.Bind
{

//NOTE: a hand written coroutine class: the args are Init's parameters after exec, and Returns is for a
//      void Exec(exec) which pushes the result itself
[BhlCoroutine(Returns = typeof(int))]
partial class WaitFrames
{
  public void Init(VM.ExecState exec, int frames, float scale) {}
  public void Exec(VM.ExecState exec) {}
  public void Cleanup(VM.ExecState exec) {}
}

//NOTE: a named one whose result is the out parameters of Exec
[BhlCoroutine("LoadThing")]
partial class Load
{
  public void Init(VM.ExecState exec, string path) {}
  public bool Exec(out string text, out int size) { text = null; size = 0; return true; }
  public void Cleanup(VM.ExecState exec) {}
}

}
