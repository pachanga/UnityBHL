using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace BitGames.BhlTask.Analyzer
{

//NOTE: the dangerous uses of BhlTask (com.bitgames.bhltask). The rules are local to a function: every
//      function of a chain is checked in its own assembly, so the chain is safe when each link is, no
//      interprocedural analysis is needed (Roslyn sees the bodies of one compilation only anyway)
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BhlTaskAnalyzer : DiagnosticAnalyzer
{
  public const string ForeignAwaitId = "BHLT001";
  public const string FinallyInBhlTaskId = "BHLT002";
  public const string DiscardedTaskId = "BHLT003";
  public const string ConsumedTwiceId = "BHLT004";
  public const string NeedlessFrameId = "BHLT005";

  const string Category = "Usage";
  const string Ns = "BitGames.BHL";

  static readonly DiagnosticDescriptor ForeignAwaitRule = new DiagnosticDescriptor(
    ForeignAwaitId,
    "Foreign await in a BhlTask function",
    "'{0}' is awaited in a BhlTask function: it would resume the function outside of the BHL call, convert it with ToBhlTask()",
    Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

  static readonly DiagnosticDescriptor FinallyInBhlTaskRule = new DiagnosticDescriptor(
    FinallyInBhlTaskId,
    "Await inside finally or using of a BhlTask function",
    "The {0} around this await won't run if the BHL call is cancelled: a cancelled BhlTask is not resumed; return BhlFinallyTask or IEnumerator<BhlStep> instead",
    Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

  static readonly DiagnosticDescriptor DiscardedTaskRule = new DiagnosticDescriptor(
    DiscardedTaskId,
    "Discarded BhlTask",
    "The {0} returned here is discarded: {1}",
    Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

  static readonly DiagnosticDescriptor ConsumedTwiceRule = new DiagnosticDescriptor(
    ConsumedTwiceId,
    "BhlTask consumed twice",
    "'{0}' is awaited or forgotten more than once: a BhlTask goes back to the pool when its result is taken",
    Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

  static readonly DiagnosticDescriptor NeedlessFrameRule = new DiagnosticDescriptor(
    NeedlessFrameId,
    "Needless async frame",
    "'{0}' only awaits another task: return it directly (=> without async), one state machine less to allocate and to cancel",
    Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

  public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
    ImmutableArray.Create(ForeignAwaitRule, FinallyInBhlTaskRule, DiscardedTaskRule, ConsumedTwiceRule, NeedlessFrameRule);

  public override void Initialize(AnalysisContext context)
  {
    context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
    context.EnableConcurrentExecution();
    context.RegisterSyntaxNodeAction(AnalyzeAwait, SyntaxKind.AwaitExpression);
    context.RegisterSyntaxNodeAction(AnalyzeExpressionStatement, SyntaxKind.ExpressionStatement);
    context.RegisterSyntaxNodeAction(AnalyzeFunction,
      SyntaxKind.MethodDeclaration, SyntaxKind.LocalFunctionStatement,
      SyntaxKind.ParenthesizedLambdaExpression, SyntaxKind.SimpleLambdaExpression, SyntaxKind.AnonymousMethodExpression);
  }

  enum TaskKind
  {
    None,
    BhlTask,
    BhlFinallyTask,
  }

  static TaskKind GetTaskKind(ITypeSymbol type)
  {
    if(!(type is INamedTypeSymbol named) || named.ContainingNamespace?.ToDisplayString() != Ns)
      return TaskKind.None;
    switch(named.OriginalDefinition.MetadataName)
    {
      case "BhlTask":
      case "BhlTask`1":
        return TaskKind.BhlTask;
      case "BhlFinallyTask":
      case "BhlFinallyTask`1":
        return TaskKind.BhlFinallyTask;
      default:
        return TaskKind.None;
    }
  }

  static bool IsStep(ITypeSymbol type)
  {
    return type is INamedTypeSymbol named &&
      named.ContainingNamespace?.ToDisplayString() == Ns &&
      (named.OriginalDefinition.MetadataName == "BhlStep" || named.OriginalDefinition.MetadataName == "BhlStep`1");
  }

  static bool IsStepIterator(ITypeSymbol type)
  {
    return type is INamedTypeSymbol named &&
      named.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerator<T>" &&
      IsStep(named.TypeArguments[0]);
  }

  static bool IsOwnAwaiter(ITypeSymbol awaiter)
  {
    return awaiter.AllInterfaces.Any(i => i.Name == "IBhlAwaiter" && i.ContainingNamespace?.ToDisplayString() == Ns);
  }

  static SyntaxNode GetEnclosingFunction(SyntaxNode node)
  {
    return node.Ancestors().FirstOrDefault(a =>
      a is MethodDeclarationSyntax || a is LocalFunctionStatementSyntax || a is AnonymousFunctionExpressionSyntax ||
      a is AccessorDeclarationSyntax || a is ConstructorDeclarationSyntax || a is OperatorDeclarationSyntax);
  }

  static IMethodSymbol GetFunctionSymbol(SyntaxNode function, SemanticModel model)
  {
    if(function is AnonymousFunctionExpressionSyntax lambda)
      return model.GetSymbolInfo(lambda).Symbol as IMethodSymbol;
    return model.GetDeclaredSymbol(function) as IMethodSymbol;
  }

  static TaskKind GetAsyncFunctionKind(SyntaxNode function, SemanticModel model)
  {
    var symbol = function == null ? null : GetFunctionSymbol(function, model);
    if(symbol == null || !symbol.IsAsync)
      return TaskKind.None;
    return GetTaskKind(symbol.ReturnType);
  }

  void AnalyzeAwait(SyntaxNodeAnalysisContext context)
  {
    var awaitExpr = (AwaitExpressionSyntax)context.Node;
    var function = GetEnclosingFunction(awaitExpr);
    var kind = GetAsyncFunctionKind(function, context.SemanticModel);
    if(kind == TaskKind.None)
      return;

    var awaiter = context.SemanticModel.GetAwaitExpressionInfo(awaitExpr).GetAwaiterMethod?.ReturnType;
    if(awaiter != null && !IsOwnAwaiter(awaiter))
    {
      var awaited = context.SemanticModel.GetTypeInfo(awaitExpr.Expression).Type;
      context.ReportDiagnostic(Diagnostic.Create(ForeignAwaitRule, awaitExpr.Expression.GetLocation(),
        awaited?.ToDisplayString() ?? awaitExpr.Expression.ToString()));
    }

    if(kind == TaskKind.BhlTask)
    {
      var guard = FindCleanupAround(awaitExpr, function);
      if(guard != null)
        context.ReportDiagnostic(Diagnostic.Create(FinallyInBhlTaskRule, awaitExpr.GetLocation(), guard));
    }
  }

  //NOTE: a finally block, a using statement or a using declaration the await is inside of; a foreach is not
  //      checked (the Dispose of its enumerator is a no-op for the usual collections)
  static string FindCleanupAround(SyntaxNode node, SyntaxNode function)
  {
    SyntaxNode child = node;
    foreach(var ancestor in node.Ancestors())
    {
      if(ancestor == function)
        break;

      switch(ancestor)
      {
        case TryStatementSyntax tryStmt when tryStmt.Finally != null && child != tryStmt.Finally:
          return "finally block";
        case UsingStatementSyntax usingStmt when child == usingStmt.Statement:
          return "using statement";
        case BlockSyntax block:
          foreach(var stmt in block.Statements)
          {
            if(stmt == child)
              break;
            if(stmt is LocalDeclarationStatementSyntax decl && decl.UsingKeyword.IsKind(SyntaxKind.UsingKeyword))
              return "using declaration";
          }
          break;
      }
      child = ancestor;
    }
    return null;
  }

  void AnalyzeExpressionStatement(SyntaxNodeAnalysisContext context)
  {
    var stmt = (ExpressionStatementSyntax)context.Node;
    if(!(stmt.Expression is InvocationExpressionSyntax invocation))
      return;

    //NOTE: the declared return type: a collection of tasks (Stack<IEnumerator<BhlStep>>.Pop) creates nothing
    var method = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
    var type = method?.OriginalDefinition.ReturnType;
    if(type == null || type is ITypeParameterSymbol)
      return;

    if(GetTaskKind(type) != TaskKind.None)
      context.ReportDiagnostic(Diagnostic.Create(DiscardedTaskRule, invocation.GetLocation(), type.Name, "await it or call Forget()"));
    else if(IsStepIterator(type))
      context.ReportDiagnostic(Diagnostic.Create(DiscardedTaskRule, invocation.GetLocation(), "iterator", "an iterator does nothing until it runs: yield return BhlStep.Run(...)"));
    else if(IsStep(type))
      context.ReportDiagnostic(Diagnostic.Create(DiscardedTaskRule, invocation.GetLocation(), type.Name, "yield return it"));
  }

  void AnalyzeFunction(SyntaxNodeAnalysisContext context)
  {
    var function = context.Node;
    var model = context.SemanticModel;
    var symbol = GetFunctionSymbol(function, model);
    if(symbol == null)
      return;

    var body = GetBody(function);
    if(body == null)
      return;

    if(GetTaskKind(symbol.ReturnType) != TaskKind.None || IsStepIterator(symbol.ReturnType))
      CheckConsumedTwice(context, function, body);

    if(symbol.IsAsync && GetTaskKind(symbol.ReturnType) != TaskKind.None && !(function is AnonymousFunctionExpressionSyntax))
      CheckNeedlessFrame(context, function, body, symbol);
  }

  static SyntaxNode GetBody(SyntaxNode function)
  {
    switch(function)
    {
      case BaseMethodDeclarationSyntax method:
        return (SyntaxNode)method.Body ?? method.ExpressionBody?.Expression;
      case LocalFunctionStatementSyntax local:
        return (SyntaxNode)local.Body ?? local.ExpressionBody?.Expression;
      case AnonymousFunctionExpressionSyntax lambda:
        return lambda.Body;
      default:
        return null;
    }
  }

  //NOTE: an await, a Forget() or a yield return of the same local more than once (a loop isn't counted)
  static void CheckConsumedTwice(SyntaxNodeAnalysisContext context, SyntaxNode function, SyntaxNode body)
  {
    var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
    foreach(var id in body.DescendantNodes().OfType<IdentifierNameSyntax>())
    {
      if(GetEnclosingFunction(id) != function || !IsConsumed(id))
        continue;
      var local = context.SemanticModel.GetSymbolInfo(id).Symbol;
      if(!(local is ILocalSymbol || local is IParameterSymbol))
        continue;
      var type = local is ILocalSymbol l ? l.Type : ((IParameterSymbol)local).Type;
      if(GetTaskKind(type) == TaskKind.None)
        continue;
      if(!seen.Add(local))
        context.ReportDiagnostic(Diagnostic.Create(ConsumedTwiceRule, id.GetLocation(), id.Identifier.Text));
    }
  }

  static bool IsConsumed(IdentifierNameSyntax id)
  {
    var parent = id.Parent;
    if(parent is AwaitExpressionSyntax || parent is YieldStatementSyntax)
      return true;
    return parent is MemberAccessExpressionSyntax member && member.Expression == id &&
      member.Name.Identifier.Text == "Forget" && member.Parent is InvocationExpressionSyntax;
  }

  //NOTE: 'await X;' or 'return await X;' as the whole body, X of the very return type
  static void CheckNeedlessFrame(SyntaxNodeAnalysisContext context, SyntaxNode function, SyntaxNode body, IMethodSymbol symbol)
  {
    ExpressionSyntax awaited = null;
    if(body is BlockSyntax block)
    {
      if(block.Statements.Count != 1)
        return;
      if(block.Statements[0] is ExpressionStatementSyntax exprStmt && exprStmt.Expression is AwaitExpressionSyntax a1)
        awaited = a1.Expression;
      else if(block.Statements[0] is ReturnStatementSyntax ret && ret.Expression is AwaitExpressionSyntax a2)
        awaited = a2.Expression;
    }
    else if(body is AwaitExpressionSyntax a3)
      awaited = a3.Expression;

    if(awaited == null)
      return;

    var type = context.SemanticModel.GetTypeInfo(awaited).Type;
    if(type == null || !SymbolEqualityComparer.Default.Equals(type, symbol.ReturnType))
      return;

    var location = function is MethodDeclarationSyntax m ? m.Identifier.GetLocation() :
      function is LocalFunctionStatementSyntax lf ? lf.Identifier.GetLocation() : function.GetLocation();
    context.ReportDiagnostic(Diagnostic.Create(NeedlessFrameRule, location, symbol.Name));
  }
}

}
