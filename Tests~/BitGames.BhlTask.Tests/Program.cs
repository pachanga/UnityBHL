using System; using System.Collections.Generic; using bhl; using BitGames.BHL; using UnityEngine; using Cysharp.Threading.Tasks;
using Coroutine = bhl.Coroutine;

//NOTE: the call of a binding is made the way the generated code makes it (Call*), ticked and destroyed the
//      way the VM does it (Tick, CoroutinePool.Del); allocations are measured with the pools warm
static class P {
  static int fails;
  static void Check(bool c, string m) { Console.WriteLine((c ? "ok   " : "FAIL ") + m); if(!c) fails++; }
  static string log = "";
  static int n;
  // BhlTask
  static async BhlTask Steps() { log += "a"; await BhlTask.Yield(); log += "b"; await BhlTask.Yield(); log += "c"; }
  static async BhlTask<int> Nested() { await Steps(); await BhlTask.WaitSeconds(1f); return 42; }
  static async BhlTask CheapGuarded() { try { log += "["; await BhlTask.WaitSeconds(100f); log += "never"; } finally { log += "]"; } }
  static async BhlFinallyTask FinGuarded() { try { log += "["; await BhlTask.WaitSeconds(100f); log += "never"; } finally { log += "]"; } }
  static async BhlFinallyTask FinOverCheap() { try { log += "["; await Wait(100f); log += "never"; } finally { log += "]"; } }
  static async BhlTask CheapOverFin() { log += "<"; await FinGuarded(); log += "never"; }
  static async BhlFinallyTask FinAwaitInFinally() { try { await BhlTask.Yield(); } finally { log += "f"; try { await BhlTask.Yield(); } catch(OperationCanceledException) { log += "c"; } } }
  static async BhlTask Throws() { await BhlTask.Yield(); throw new InvalidOperationException("boom"); }
  static async BhlTask ThrowsSync() { throw new InvalidOperationException("sync boom"); }
  static async BhlTask<int> Sync() { return 7; }
  static async BhlTask Wait(float s) { await BhlTask.WaitSeconds(s); }
  static BhlTask WaitPass(float s) => BhlTask.WaitSeconds(s);
  static async BhlFinallyTask FinWait(float s) { await FinWaitInner(s); }
  static async BhlFinallyTask FinWaitInner(float s) { await BhlTask.WaitSeconds(s); }
  static async BhlTask<int> WaitInt(float s) { await Wait(s); return 1; }
  static async BhlTask Twice() { var t = Steps(); await t; await t; }
  static async BhlTask Foreign(System.Threading.Tasks.Task t) { await t; }
  static async BhlTask ForgetBoom() { Throws().Forget(); await BhlTask.WaitSeconds(10f); }
  static async BhlTask ForgetOk() { Steps().Forget(); log += "+"; await BhlTask.Yield(); await BhlTask.Yield(); await BhlTask.Yield(); log += "!"; }
  static async BhlTask ForgetPending() { Wait(100f).Forget(); }
  static async BhlTask<int> FromUni(StubSource<int> s) { log += "u"; int v = await new UniTask<int>(s).ToBhlTask(); log += "v"; await BhlTask.Yield(); log += "w"; return v; }
  static async BhlFinallyTask FromUniFin(StubSource<bool> s) { try { await new UniTask(s).ToBhlTask(); log += "never"; } finally { log += "]"; } }
  static async BhlTask FromUniCheap(StubSource<bool> s) { await new UniTask(s).ToBhlTask(); log += "never"; }
  static async BhlTask<int> FromSys(System.Threading.Tasks.Task<int> t) { int v = await t.ToBhlTask(); return v + 1; }
  // iterators

  static VM.ExecState E = new VM.ExecState();
  static int sum;
  static Coroutine Call(Func<BhlTask> f) { var s = BhlTaskScope.Enter(); try { var t = f(); return BhlTaskCoroutine.New(E, s, t); } finally { s.Exit(); } }
  static Coroutine CallF(Func<BhlFinallyTask> f) { var s = BhlTaskScope.Enter(); try { var t = f(); return BhlTaskCoroutine.New(E, s, t); } finally { s.Exit(); } }
  static Coroutine CallT<T>(Func<BhlTask<T>> f) { var s = BhlTaskScope.Enter(); try { var t = f(); return BhlTaskCoroutine.New(E, s, t, static (exec, v) => exec.pushed.Add(v)); } finally { s.Exit(); } }
  static Coroutine CallInt(Func<BhlTask<int>> f) { var s = BhlTaskScope.Enter(); try { var t = f(); return BhlTaskCoroutine.New(E, s, t, static (exec, v) => sum += v); } finally { s.Exit(); } }
  static bool Tick(Coroutine c) { E.status = BHS.SUCCESS; c.Tick(E); return E.status == BHS.RUNNING; }
  static int Run(Coroutine c, int max = 1000) { for(int i = 1; i <= max; ++i) { bool r; try { r = Tick(c); } catch { CoroutinePool.Del(E, c); throw; } if(!r) { CoroutinePool.Del(E, c); return i; } Time.time += 0.25f; } return -1; }
  static void Cancel(Coroutine c, int ticks) { for(int i = 0; i < ticks; ++i) { Tick(c); Time.time += 0.25f; } CoroutinePool.Del(E, c); }
  static string Err(Action a) { try { a(); } catch(Exception e) { return e.GetType().Name + ":" + e.Message; } return null; }

  static void Main() {
    for(int pass = 0; pass < 2; ++pass) {
      Console.WriteLine("== pass " + pass);
      log = ""; var c = Call(Steps); Check(log == "a", "call runs to the first await: " + log);
      int ticks = Run(c); Check(ticks == 3 && log == "abc", $"yield = one tick each: ticks={ticks} log={log}");
      E.pushed.Clear(); log = ""; Time.time = 0; ticks = Run(CallT(Nested));
      Check(log == "abc" && E.pushed.Count == 1 && (int)E.pushed[0] == 42, $"nested + WaitSeconds + result: ticks={ticks} log={log}");
      E.pushed.Clear(); ticks = Run(CallT(Sync)); Check(ticks == 1 && (int)E.pushed[0] == 7, "sync completion in the first tick");
      Check(Err(() => Run(Call(Throws))) == "InvalidOperationException:boom", "exception rethrown by Tick");
      Check(Err(() => Run(Call(ThrowsSync))) == "InvalidOperationException:sync boom", "sync exception");
      Check(Err(() => Run(Call(Twice)))?.Contains("after its result was taken") == true, "awaited twice throws");
      var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
      Check(Err(() => Run(Call(() => Foreign(tcs.Task))))?.Contains("ToBhlTask") == true, "foreign await rejected");
      // cancellation
      log = ""; Time.time = 0; Cancel(Call(CheapGuarded), 2); Check(log == "[", "cheap cancel: not resumed, finally skipped: " + log);
      log = ""; Time.time = 0; Cancel(CallF(FinGuarded), 2); Check(log == "[]", "BhlFinallyTask cancel: finally runs: " + log);
      log = ""; Time.time = 0; Cancel(CallF(FinOverCheap), 2); Check(log == "[]", "BhlFinallyTask over a BhlTask: the BhlTask is faulted, finally runs: " + log);
      log = ""; Time.time = 0; Cancel(Call(CheapOverFin), 2); Check(log == "<[]", "BhlTask over a BhlFinallyTask: inner finally runs, outer not resumed: " + log);
      log = ""; Cancel(CallF(FinAwaitInFinally), 1); Check(log == "fc", "await in finally of a cancelled BhlFinallyTask throws at once: " + log);
      // Forget
      Time.time = 0; Check(Err(() => Run(Call(ForgetBoom))) == "InvalidOperationException:boom", "forgotten exception fails the call");
      log = ""; ticks = Run(Call(ForgetOk)); Check(log == "a+bc!" && ticks == 4, $"forgotten runs on: ticks={ticks} log={log}");
      Debug.logged = 0; ticks = Run(Call(ForgetPending)); Check(ticks == 1 && Debug.logged == 0, $"pending forgotten dropped with the call, nothing logged: ticks={ticks}");
      // ToBhlTask
      log = ""; E.pushed.Clear(); var us = new StubSource<int>(); c = CallT(() => FromUni(us)); Tick(c); Tick(c); Check(log == "u", "unitask pending: " + log);
      us.Complete(5); Check(log == "u", "unitask completion waits for the tick: " + log);
      ticks = Run(c); Check(log == "uvw" && (int)E.pushed[0] == 5 && ticks == 2 && us.taken == 1, $"unitask result in scope: ticks={ticks} log={log} taken={us.taken}");
      us = new StubSource<int>(); us.Fail(new InvalidOperationException("uni boom"));
      Check(Err(() => Run(CallT(() => FromUni(us)))) == "InvalidOperationException:uni boom", "completed failed unitask");
      us = new StubSource<int>(); c = CallT(() => FromUni(us)); Tick(c); us.Fail(new InvalidOperationException("late boom"));
      Check(Err(() => Run(c)) == "InvalidOperationException:late boom", "failed unitask");
      log = ""; var vs = new StubSource<bool>(); c = CallF(() => FromUniFin(vs)); Tick(c); CoroutinePool.Del(E, c);
      Check(log == "]" && vs.taken == 0, "BhlFinallyTask cancel over unitask: finally runs: " + log);
      log = ""; Run(Call(Steps)); vs.Complete(true); Check(vs.taken == 1 && log == "abc", "late unitask released, scope reused untouched: " + log);
      log = ""; vs = new StubSource<bool>(); c = Call(() => FromUniCheap(vs)); Tick(c); CoroutinePool.Del(E, c); vs.Complete(true); Check(log == "" && vs.taken == 1, "BhlTask cancel over unitask: dropped, unitask released later");
      var tc = new System.Threading.Tasks.TaskCompletionSource<int>(); E.pushed.Clear(); c = CallT(() => FromSys(tc.Task)); Tick(c); tc.SetResult(3);
      ticks = Run(c); Check(E.pushed.Count == 1 && (int)E.pushed[0] == 4, $"Task.ToBhlTask: ticks={ticks} pushed={string.Join(",", E.pushed)}");
    }

    const int N = 1000;
    var u = new StubSource<int>[N]; for(int i = 0; i < N; ++i) u[i] = new StubSource<int>();
    for(int w = 0; w < 100; ++w) {
      Time.time = 0; Run(Call(static () => Wait(1f))); Run(CallInt(static () => WaitInt(1f))); Run(Call(static () => ForgetOk2())); var s = new StubSource<int>(); var cc = CallInt(() => FromUni2(s)); Tick(cc); s.Complete(1); Run(cc);
      Cancel(Call(static () => Wait(100f)), 2); Cancel(Call(static () => ForgetPending()), 2); Cancel(CallF(static () => FinWait(100f)), 2); Cancel(Call(static () => WaitPass(100f)), 2);
    }
    var r = new Dictionary<string, long>();
    void M(string k, Action a) { long b0 = GC.GetAllocatedBytesForCurrentThread(); for(int i = 0; i < N; ++i) a(); r[k] = (GC.GetAllocatedBytesForCurrentThread() - b0) / N; }
    for(int rep = 0; rep < 2; ++rep) {
      M("BhlTask run", () => { Time.time = 0; Run(Call(static () => Wait(1f))); });
      M("BhlTask<int> run", () => { Time.time = 0; Run(CallInt(static () => WaitInt(1f))); });
      M("Forget run", () => Run(Call(static () => ForgetOk2())));
      int ui = 0; M("ToBhlTask(UniTask) run", () => { cur = u[ui++]; var cc = CallInt(static () => FromUni2(cur)); Tick(cc); cur.Complete(1); Run(cc); });
      M("BhlTask cancel (wrapper)", () => { Time.time = 0; Cancel(Call(static () => Wait(100f)), 2); });
      M("BhlTask cancel (passthrough)", () => { Time.time = 0; Cancel(Call(static () => WaitPass(100f)), 2); });
      M("BhlTask cancel + forgotten", () => { Time.time = 0; Cancel(Call(static () => ForgetPending()), 2); });
      M("BhlFinallyTask cancel (2 levels)", () => { Time.time = 0; Cancel(CallF(static () => FinWait(100f)), 2); });
      for(int i = 0; i < N; ++i) u[i] = new StubSource<int>();
    }
    foreach(var kv in r) Console.WriteLine($"alloc {kv.Key}: {kv.Value}B");
#if !DEBUG
    foreach(var k in new[] { "BhlTask run", "BhlTask<int> run", "Forget run", "ToBhlTask(UniTask) run", "BhlTask cancel (wrapper)",
      "BhlTask cancel (passthrough)", "BhlTask cancel + forgotten" })
      Check(r[k] == 0, "no allocations: " + k);
#endif
    Threads();
    fails += RunTests.Run();
    Console.WriteLine(fails == 0 ? "all passed" : fails + " failed");
    Environment.Exit(fails);
  }
  static StubSource<int> cur;

  //NOTE: VMs on several threads at once, each ticks its own calls: plain, cancelled, BhlFinallyTask cancelled,
  //      and awaiting a Task completed by a thread pool thread while the call ticks or is cancelled
  static async BhlTask<int> CountYields(int n) { int k = 0; for(int i = 0; i < n; ++i) { await BhlTask.Yield(); ++k; } return k; }
  static async BhlFinallyTask<int> FinYields(int n) { try { return await CountYields(n); } finally { } }
  static async BhlTask<int> FromTask(System.Threading.Tasks.Task<int> t) { int v = await t.ToBhlTask(); await BhlTask.Yield(); return v; }
  static void Threads() {
    const int T = 8, CALLS = 3000;
    var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
    var threads = new System.Threading.Thread[T];
    for(int t = 0; t < T; ++t) {
      int seed = t;
      threads[t] = new System.Threading.Thread(() => {
        var e = new VM.ExecState();
        var rnd = new Random(seed);
        bool TickT(Coroutine c) { e.status = BHS.SUCCESS; c.Tick(e); return e.status == BHS.RUNNING; }
        try {
          for(int i = 0; i < CALLS; ++i) {
            int n = 1 + rnd.Next(5);
            int kind = rnd.Next(4);
            var tcs = new System.Threading.Tasks.TaskCompletionSource<int>();
            var s = BhlTaskScope.Enter();
            Coroutine c;
            try {
              c = kind == 0 ? BhlTaskCoroutine.New(e, s, CountYields(n), static (exec, v) => exec.pushed.Add(v)) :
                  kind == 1 ? BhlTaskCoroutine.New(e, s, FinYields(n), static (exec, v) => exec.pushed.Add(v)) :
                  BhlTaskCoroutine.New(e, s, FromTask(tcs.Task), static (exec, v) => exec.pushed.Add(v));
            } finally { s.Exit(); }
            if(kind >= 2) {
              int spin = rnd.Next(2000);
              System.Threading.Tasks.Task.Run(() => { System.Threading.Thread.SpinWait(spin); tcs.SetResult(n); });
            }
            bool cancel = rnd.Next(3) == 0;
            e.pushed.Clear();
            int ticks = 0;
            while(true) {
              if(cancel && ticks == 1) { CoroutinePool.Del(e, c); break; }
              if(!TickT(c)) { CoroutinePool.Del(e, c); break; }
              if(++ticks > 1000000) { errors.Enqueue("never completes"); CoroutinePool.Del(e, c); break; }
            }
            if(!cancel && (e.pushed.Count != 1 || (int)e.pushed[0] != n))
              errors.Enqueue($"kind {kind}: expected {n}, pushed {string.Join(",", e.pushed)}");
            if(kind >= 2)
              tcs.Task.Wait();
          }
        } catch(Exception ex) { errors.Enqueue(ex.GetType().Name + ": " + ex.Message); }
      });
    }
    foreach(var th in threads) th.Start();
    foreach(var th in threads) th.Join();
    errors.TryPeek(out var first);
    Check(errors.IsEmpty, $"threads: {T} x {CALLS} calls at once: " + (first ?? "") + (errors.Count > 1 ? $" (+{errors.Count - 1} more)" : ""));
  }
  static async BhlTask Steps2() { ++n; await BhlTask.Yield(); ++n; await BhlTask.Yield(); ++n; }
  static async BhlTask ForgetOk2() { Steps2().Forget(); await BhlTask.Yield(); await BhlTask.Yield(); await BhlTask.Yield(); }
  static async BhlTask<int> FromUni2(StubSource<int> s) { int v = await new UniTask<int>(s).ToBhlTask(); await BhlTask.Yield(); return v; }
}
