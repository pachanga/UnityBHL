using System;
using BitGames.BHL;
using UnityEngine;

//NOTE: BhlTaskRun: the same semantics a BHL call has, without the VM
static class RunTests
{
  static int fails;
  static string log = "";

  static void Check(bool c, string m)
  {
    Console.WriteLine((c ? "ok   " : "FAIL ") + m);
    if(!c) ++fails;
  }

  static async BhlTask Steps() { log += "a"; await BhlTask.Yield(); log += "b"; await BhlTask.Yield(); log += "c"; }
  static async BhlTask<int> Sum(int n) { int s = 0; for(int i = 0; i < n; ++i) { s += i; await BhlTask.Yield(); } return s; }
  static async BhlTask Sync() { log += "s"; }
  static async BhlTask Throws() { await BhlTask.Yield(); throw new InvalidOperationException("boom"); }
  static async BhlTask ThrowsSync() { throw new InvalidOperationException("sync"); }
  static async BhlTask Cheap() { try { log += "["; await BhlTask.WaitSeconds(100f); } finally { log += "]"; } }
  static async BhlFinallyTask Fin() { try { log += "["; await BhlTask.WaitSeconds(100f); } finally { log += "]"; } }
  static async BhlFinallyTask<int> FinInt() { await BhlTask.Yield(); return 7; }
  static async BhlTask ForgetBoom() { Throws().Forget(); await BhlTask.WaitSeconds(10f); }
  static BhlTask Passthrough() => Steps();
  static async BhlTask Quiet() { await BhlTask.Yield(); await BhlTask.Yield(); }
  static readonly Func<BhlTask> quiet = Quiet;

  static int RunToEnd(BhlTaskRun<BhlTaskVoid> r, int max = 100)
  {
    int ticks = 0;
    while(r.Tick())
    {
      Time.time += 0.25f;
      if(++ticks > max) throw new Exception("never ends");
    }
    return ticks + 1;
  }

  static string Err(Action a)
  {
    try { a(); } catch(Exception e) { return e.GetType().Name + ":" + e.Message; }
    return null;
  }

  public static int Run()
  {
    Console.WriteLine("== BhlTaskRun");

    //NOTE: the function runs to its first await in Start(), as a BHL call does
    log = ""; var r = BhlTaskRun.Start(Steps);
    Check(log == "a", "Start runs to the first await: " + log);
    int ticks = RunToEnd(r);
    Check(ticks == 3 && log == "abc", $"a yield is a tick: ticks={ticks} log={log}");
    Check(r.IsCompleted, "IsCompleted after the end");
    Check(!r.Tick(), "Tick after the end is false");
    r.Dispose();
    r.Dispose();
    Check(true, "Dispose is idempotent");

    //NOTE: a result
    var ri = BhlTaskRun.Start(() => Sum(4));
    while(ri.Tick()) {}
    Check(ri.IsCompleted && ri.Result == 6, "result: " + ri.Result);
    ri.Dispose();

    //NOTE: nothing awaited at all
    log = ""; var rs = BhlTaskRun.Start(Sync);
    Check(!rs.Tick() && rs.IsCompleted && log == "s", "sync function is over in the first tick: " + log);
    rs.Dispose();

    //NOTE: exceptions
    var rt = BhlTaskRun.Start(Throws);
    Check(Err(() => { while(rt.Tick()) {} }) == "InvalidOperationException:boom", "the exception of the function");
    Check(rt.IsCompleted, "a faulted run is over");
    Check(Err(() => { var _ = rt.Result; })?.Contains("faulted") == true, "Result of a faulted run throws");
    rt.Dispose();

    var rts = BhlTaskRun.Start(ThrowsSync);
    Check(Err(() => { while(rts.Tick()) {} }) == "InvalidOperationException:sync", "an exception before any await");
    rts.Dispose();

    //NOTE: the exception of a forgotten task fails the run, as it fails a BHL call
    Time.time = 0;
    var rf = BhlTaskRun.Start(ForgetBoom);
    Check(Err(() => { while(rf.Tick()) { Time.time += 0.25f; } }) == "InvalidOperationException:boom",
      "a forgotten exception fails the run");
    rf.Dispose();

    //NOTE: Dispose cancels, as the VM dropping the call does
    log = ""; Time.time = 0;
    var rc = BhlTaskRun.Start(Cheap);
    rc.Tick(); rc.Tick();
    rc.Dispose();
    Check(log == "[", "BhlTask cancel: not resumed, finally skipped: " + log);

    log = ""; Time.time = 0;
    var rfin = BhlTaskRun.Start(() => (BhlTask)Fin());
    rfin.Tick(); rfin.Tick();
    rfin.Dispose();
    Check(log == "[]", "BhlFinallyTask cancel: finally runs: " + log);

    //NOTE: a BhlFinallyTask<T> function goes in by its conversion to BhlTask<T>
    var rfi = BhlTaskRun.Start(() => (BhlTask<int>)FinInt());
    while(rfi.Tick()) {}
    Check(rfi.Result == 7, "BhlFinallyTask<int>: " + rfi.Result);
    rfi.Dispose();

    //NOTE: a passthrough (no state machine of its own) works the same
    log = ""; var rp = BhlTaskRun.Start(Passthrough);
    ticks = RunToEnd(rp);
    Check(ticks == 3 && log == "abc", $"passthrough: ticks={ticks} log={log}");
    rp.Dispose();

    //NOTE: dropped without ticking at all
    log = ""; var rd = BhlTaskRun.Start(Steps);
    rd.Dispose();
    Check(log == "a", "dropped before the first tick: " + log);

    //NOTE: a step of a Unity coroutine: MoveNext ticks, the run is disposed once over, Result stays
    log = ""; var re = BhlTaskRun.Start(Steps);
    System.Collections.IEnumerator en = re;
    int steps = 0;
    while(en.MoveNext()) { Check(en.Current == null, "Current is null - the next frame"); ++steps; }
    Check(steps == 2 && log == "abc" && re.IsCompleted && re.IsDisposed, $"MoveNext ticks and disposes at the end: steps={steps} log={log}");
    var rei = BhlTaskRun.Start(4, static n => Sum(n));
    while(((System.Collections.IEnumerator)rei).MoveNext()) {}
    Check(rei.IsDisposed && rei.Result == 6, "Result after MoveNext is over, a state overload: " + rei.Result);
    var ret = BhlTaskRun.Start(Throws);
    Check(Err(() => { while(((System.Collections.IEnumerator)ret).MoveNext()) {} }) == "InvalidOperationException:boom" && ret.IsDisposed,
      "a faulted run is disposed by MoveNext, the exception goes to the coroutine");
    log = ""; Time.time = 0;
    var res = BhlTaskRun.Start(Cheap);
    ((System.Collections.IEnumerator)res).MoveNext(); ((System.Collections.IEnumerator)res).MoveNext();
    res.Dispose();
    Check(log == "[" && res.IsDisposed, "a stopped coroutine: Dispose cancels: " + log);

    //NOTE: the scopes come back to the pool - a run after all of the above still behaves
    log = ""; var r2 = BhlTaskRun.Start(Steps);
    ticks = RunToEnd(r2);
    Check(ticks == 3 && log == "abc", $"scopes reused: ticks={ticks} log={log}");
    r2.Dispose();

    //NOTE: a run allocates the BhlTaskRun object and nothing else once the pools are warm (Quiet
    //      doesn't touch the log: += on a string would allocate by itself)
    for(int i = 0; i < 50; ++i) { var w = BhlTaskRun.Start(quiet); RunToEnd(w); w.Dispose(); }
    long before = GC.GetAllocatedBytesForCurrentThread();
    const int N = 200;
    for(int i = 0; i < N; ++i) { var w = BhlTaskRun.Start(quiet); RunToEnd(w); w.Dispose(); }
    long per = (GC.GetAllocatedBytesForCurrentThread() - before) / N;
    Console.WriteLine($"alloc BhlTaskRun run: {per}B");
#if !DEBUG
    Check(per <= 64, "only the BhlTaskRun object is allocated: " + per + "B");
#endif

    //NOTE: as a coroutine step with a state and a static lambda: the same, no closure
    for(int i = 0; i < 50; ++i) { System.Collections.IEnumerator w = BhlTaskRun.Start(2, static n => Sum(n)); while(w.MoveNext()) {} }
    before = GC.GetAllocatedBytesForCurrentThread();
    for(int i = 0; i < N; ++i) { System.Collections.IEnumerator w = BhlTaskRun.Start(2, static n => Sum(n)); while(w.MoveNext()) {} }
    per = (GC.GetAllocatedBytesForCurrentThread() - before) / N;
    Console.WriteLine($"alloc BhlTaskRun coroutine step: {per}B");
#if !DEBUG
    Check(per <= 64, "a coroutine step with a state allocates only the BhlTaskRun object: " + per + "B");
#endif

    return fails;
  }
}
