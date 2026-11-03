using System.Text;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;

namespace BitGames.BhlTask.Analyzer.Tests;

public class BhlTaskAnalyzerTests
{
  //NOTE: the shape of the runtime the analyzer looks at: names, the own awaiter interface
  const string RuntimeStub = @"
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace System.Runtime.CompilerServices { sealed class AsyncMethodBuilderAttribute : Attribute { public AsyncMethodBuilderAttribute(Type t) {} } }

namespace BitGames.BHL
{
  interface IBhlAwaiter {}

  public struct Builder
  {
    public static Builder Create() => default;
    public BhlTask Task => default;
    public void Start<TSM>(ref TSM sm) where TSM : IAsyncStateMachine {}
    public void SetStateMachine(IAsyncStateMachine sm) {}
    public void SetResult() {}
    public void SetException(Exception e) {}
    public void AwaitOnCompleted<TA, TSM>(ref TA a, ref TSM sm) where TA : INotifyCompletion where TSM : IAsyncStateMachine {}
    public void AwaitUnsafeOnCompleted<TA, TSM>(ref TA a, ref TSM sm) where TA : ICriticalNotifyCompletion where TSM : IAsyncStateMachine {}
  }

  public struct FinallyBuilder
  {
    public static FinallyBuilder Create() => default;
    public BhlFinallyTask Task => default;
    public void Start<TSM>(ref TSM sm) where TSM : IAsyncStateMachine {}
    public void SetStateMachine(IAsyncStateMachine sm) {}
    public void SetResult() {}
    public void SetException(Exception e) {}
    public void AwaitOnCompleted<TA, TSM>(ref TA a, ref TSM sm) where TA : INotifyCompletion where TSM : IAsyncStateMachine {}
    public void AwaitUnsafeOnCompleted<TA, TSM>(ref TA a, ref TSM sm) where TA : ICriticalNotifyCompletion where TSM : IAsyncStateMachine {}
  }

  public struct Awaiter : ICriticalNotifyCompletion, IBhlAwaiter
  {
    public bool IsCompleted => true;
    public void GetResult() {}
    public void OnCompleted(Action c) {}
    public void UnsafeOnCompleted(Action c) {}
  }

  [AsyncMethodBuilder(typeof(Builder))]
  public struct BhlTask
  {
    public Awaiter GetAwaiter() => default;
    public void Forget() {}
    public static BhlTask WaitSeconds(float s) => default;
  }

  [AsyncMethodBuilder(typeof(FinallyBuilder))]
  public struct BhlFinallyTask
  {
    public Awaiter GetAwaiter() => default;
  }

  public struct BhlStep
  {
    public static BhlStep Yield => default;
    public static BhlStep Run(IEnumerator<BhlStep> child) => default;
  }

  public static class Ext
  {
    public static BhlTask ToBhlTask(this Task t) => default;
  }
}
";

  static async Task RunAsync(string source, params DiagnosticResult[] expected)
  {
    var test = new CSharpAnalyzerTest<BhlTaskAnalyzer, DefaultVerifier>();
    test.TestState.Sources.Add(("Test.cs", SourceText.From(source, Encoding.UTF8)));
    test.TestState.Sources.Add(("Runtime.cs", SourceText.From(RuntimeStub, Encoding.UTF8)));
    test.ExpectedDiagnostics.AddRange(expected);
    await test.RunAsync();
  }

  static DiagnosticResult Diag(string id) => new DiagnosticResult(id, id == BhlTaskAnalyzer.ConsumedTwiceId || id == BhlTaskAnalyzer.NeedlessFrameId
    ? Microsoft.CodeAnalysis.DiagnosticSeverity.Warning : Microsoft.CodeAnalysis.DiagnosticSeverity.Error);

  const string Usings = "using System; using System.Collections.Generic; using System.Threading.Tasks; using BitGames.BHL;\n";

  [Test]
  public async Task OwnAwaitsAreFine()
  {
    await RunAsync(Usings + @"
static class C
{
  static async BhlTask A(Task t) { await BhlTask.WaitSeconds(1); await t.ToBhlTask(); }
  static BhlTask B() => BhlTask.WaitSeconds(1);
  static async BhlTask Two() { await BhlTask.WaitSeconds(1); await B(); }
}");
  }

  [Test]
  public async Task ForeignAwait()
  {
    await RunAsync(Usings + @"
static class C
{
  static async BhlTask A(Task t) { await {|#0:t|}; await BhlTask.WaitSeconds(1); }
  static async BhlFinallyTask F(Task t) { await {|#1:t|}; }
  static async Task NotOurs(Task t) { await t; }
}", Diag(BhlTaskAnalyzer.ForeignAwaitId).WithLocation(0).WithArguments("System.Threading.Tasks.Task"),
    Diag(BhlTaskAnalyzer.ForeignAwaitId).WithLocation(1).WithArguments("System.Threading.Tasks.Task"));
  }

  [Test]
  public async Task FinallyAndUsingInBhlTask()
  {
    await RunAsync(Usings + @"
static class C
{
  static async BhlTask Fin() { try { {|#0:await BhlTask.WaitSeconds(1)|}; } finally { Console.WriteLine(); } }
  static async BhlTask Using(IDisposable d) { using(d) { {|#1:await BhlTask.WaitSeconds(1)|}; } }
  static async BhlTask UsingDecl(IDisposable d) { await BhlTask.WaitSeconds(1); using var x = d; {|#2:await BhlTask.WaitSeconds(1)|}; }
  static async BhlTask Catch() { try { await BhlTask.WaitSeconds(1); } catch(Exception) {} }
  static async BhlTask AwaitInFinally() { try { Console.WriteLine(); } finally { await BhlTask.WaitSeconds(1); } }
  static async BhlFinallyTask Allowed(IDisposable d) { try { using(d) { await BhlTask.WaitSeconds(1); } } finally { Console.WriteLine(); } }
  static async BhlTask Lambda() { Func<BhlFinallyTask> f = async () => { try { await BhlTask.WaitSeconds(1); } finally {} }; await f(); }
}", Diag(BhlTaskAnalyzer.FinallyInBhlTaskId).WithLocation(0).WithArguments("finally block"),
    Diag(BhlTaskAnalyzer.FinallyInBhlTaskId).WithLocation(1).WithArguments("using statement"),
    Diag(BhlTaskAnalyzer.FinallyInBhlTaskId).WithLocation(2).WithArguments("using declaration"));
  }

  [Test]
  public async Task Discarded()
  {
    await RunAsync(Usings + @"
static class C
{
  static IEnumerator<BhlStep> It() { yield return BhlStep.Yield; }
  static async BhlTask A()
  {
    {|#0:BhlTask.WaitSeconds(1)|};
    BhlTask.WaitSeconds(1).Forget();
    {|#1:It()|};
    var stack = new Stack<IEnumerator<BhlStep>>();
    stack.Push(It());
    stack.Pop();
    await BhlTask.WaitSeconds(1);
  }
}", Diag(BhlTaskAnalyzer.DiscardedTaskId).WithLocation(0).WithArguments("BhlTask", "await it or call Forget()"),
    Diag(BhlTaskAnalyzer.DiscardedTaskId).WithLocation(1).WithArguments("iterator", "an iterator does nothing until it runs: yield return BhlStep.Run(...)"));
  }

  [Test]
  public async Task ConsumedTwice()
  {
    await RunAsync(Usings + @"
static class C
{
  static async BhlTask A()
  {
    var t = BhlTask.WaitSeconds(1);
    await t;
    await {|#0:t|};
    var u = BhlTask.WaitSeconds(1);
    u.Forget();
  }
}", Diag(BhlTaskAnalyzer.ConsumedTwiceId).WithLocation(0).WithArguments("t"));
  }

  [Test]
  public async Task NeedlessFrame()
  {
    await RunAsync(Usings + @"
static class C
{
  static async BhlTask {|#0:Wrap|}() { await BhlTask.WaitSeconds(1); }
  static async BhlTask {|#1:WrapExpr|}() => await BhlTask.WaitSeconds(1);
  static async BhlFinallyTask KeepsCancelSemantics() { await BhlTask.WaitSeconds(1); }
  static async BhlTask TwoAwaits() { await BhlTask.WaitSeconds(1); await BhlTask.WaitSeconds(1); }
}", Diag(BhlTaskAnalyzer.NeedlessFrameId).WithLocation(0).WithArguments("Wrap"),
    Diag(BhlTaskAnalyzer.NeedlessFrameId).WithLocation(1).WithArguments("WrapExpr"));
  }
}
