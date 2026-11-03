using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public class RoslynParser : ICsParser
{
  readonly CSharpParseOptions parse_opts;

  public RoslynParser(IEnumerable<string> defines)
  {
    parse_opts = CSharpParseOptions.Default
      .WithLanguageVersion(LanguageVersion.Latest)
      .WithPreprocessorSymbols(defines);
  }

  CompilationUnitSyntax Parse(string file, string src)
  {
    return (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(src, parse_opts, file).GetRoot();
  }

  static SrcPos Pos(SyntaxNode node)
  {
    var span = node.GetLocation().GetLineSpan();
    return new SrcPos { File = span.Path, Line = span.StartLinePosition.Line + 1, Col = span.StartLinePosition.Character + 1 };
  }

  public void ParseDecls(string file, string src, CsDecls decls)
  {
    var root = Parse(file, src);

    foreach(var i in root.DescendantNodes().OfType<InterfaceDeclarationSyntax>())
    {
      var class_attr = FindAttr(i.AttributeLists, "BhlClass");
      if(class_attr != null)
        decls.Classes.Add(Class(i, class_attr, Usings(root)));
      if(!HasAttr(i.AttributeLists, "BhlCustom"))
        continue;
      var usings = Usings(root);
      foreach(var m in i.Members.OfType<MethodDeclarationSyntax>())
      {
        var decl = Method(m, null, usings);
        decl.IsCustom = true;
        decls.Customs.Add(decl);
      }
    }

    foreach(var attr in root.DescendantNodes().OfType<AttributeSyntax>())
    {
      if(!IsAttr(attr, "BhlBind"))
        continue;

      var args = attr.ArgumentList?.Arguments;
      if(args == null || args.Value.Count < 2 || !(args.Value[0].Expression is TypeOfExpressionSyntax type_of))
        throw new GenException(Pos(attr), "expected [BhlBind(typeof(T), nameof(T.M), ...)]");

      var type = LastName(type_of.Type);
      foreach(var arg in args.Value.Skip(1))
        decls.Binds.Add(new BindDecl { Type = type, Name = MemberName(arg.Expression), Pos = Pos(arg) });
    }
  }

  static bool IsAttr(AttributeSyntax attr, string name)
  {
    var attr_name = LastName(attr.Name);
    return attr_name == name || attr_name == name + "Attribute";
  }

  static bool HasAttr(SyntaxList<AttributeListSyntax> lists, string name)
  {
    return FindAttr(lists, name) != null;
  }

  static AttributeSyntax FindAttr(SyntaxList<AttributeListSyntax> lists, string name)
  {
    return lists.SelectMany(l => l.Attributes).FirstOrDefault(a => IsAttr(a, name));
  }

  static string StringArg(AttributeSyntax attr, int idx)
  {
    var args = attr.ArgumentList?.Arguments;
    if(args == null || args.Value.Count <= idx || !(args.Value[idx].Expression is LiteralExpressionSyntax lit) || !lit.IsKind(SyntaxKind.StringLiteralExpression))
      throw new GenException(Pos(attr), $"expected a string argument #{idx + 1}");
    return lit.Token.ValueText;
  }

  static MemberRef MemberRefOf(ExpressionSyntax expr)
  {
    if(expr is InvocationExpressionSyntax inv && inv.Expression is IdentifierNameSyntax id && id.Identifier.Text == "nameof"
        && inv.ArgumentList.Arguments.Count == 1)
    {
      var arg = inv.ArgumentList.Arguments[0].Expression;
      if(arg is MemberAccessExpressionSyntax ma)
        return new MemberRef { Type = LastName(ma.Expression), Name = ma.Name.Identifier.Text, Pos = Pos(expr) };
      return new MemberRef { Name = LastName(arg), Pos = Pos(expr) };
    }
    if(expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
      return new MemberRef { Name = lit.Token.ValueText, Pos = Pos(expr) };
    throw new GenException(Pos(expr), "expected nameof(T.Member) or a string");
  }

  static ClassDecl Class(InterfaceDeclarationSyntax i, AttributeSyntax attr, List<string> usings)
  {
    var args = attr.ArgumentList?.Arguments;
    if(args == null || args.Value.Count < 2 || !(args.Value[1].Expression is TypeOfExpressionSyntax type_of))
      throw new GenException(Pos(attr), "expected [BhlClass(\"Name\", typeof(T), ...)]");
    var decl = new ClassDecl { Name = StringArg(attr, 0), NativeType = Type(type_of.Type), Usings = usings, Pos = Pos(i) };
    foreach(var named in args.Value.Skip(2))
    {
      if(named.NameEquals == null)
      {
        decl.MemberRefs.Add(MemberRefOf(named.Expression));
        continue;
      }
      var prop = named.NameEquals.Name.Identifier.Text;
      if(prop == "Parent" && named.Expression is LiteralExpressionSyntax p && p.IsKind(SyntaxKind.StringLiteralExpression))
        decl.Parent = p.Token.ValueText;
      else if(prop == "New" && named.Expression is LiteralExpressionSyntax n && (n.IsKind(SyntaxKind.TrueLiteralExpression) || n.IsKind(SyntaxKind.FalseLiteralExpression)))
        decl.New = n.IsKind(SyntaxKind.TrueLiteralExpression);
      else
        throw new GenException(Pos(named), "expected Parent = \"..\" or New = true|false");
    }

    foreach(var member in i.Members)
    {
      if(member is PropertyDeclarationSyntax prop)
      {
        var accessors = prop.AccessorList?.Accessors;
        if(accessors == null)
          throw new GenException(Pos(prop), "expected { get; set; } accessors");
        var native = FindAttr(prop.AttributeLists, "BhlNative");
        decl.Fields.Add(new FieldDecl {
          Name = prop.Identifier.Text,
          Type = Type(prop.Type),
          Get = accessors.Value.Any(a => a.IsKind(SyntaxKind.GetAccessorDeclaration)),
          Set = accessors.Value.Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration)),
          Native = native != null ? StringArg(native, 0) : null,
          Usings = usings,
          Pos = Pos(prop),
        });
      }
      else if(member is MethodDeclarationSyntax m)
      {
        var method = Method(m, null, usings);
        var native = FindAttr(m.AttributeLists, "BhlNative");
        method.Native = native != null ? StringArg(native, 0) : null;
        decl.Methods.Add(method);
      }
      else
        throw new GenException(Pos(member), "only properties and methods are expected in a [BhlClass] interface");
    }
    return decl;
  }

  static MethodDecl Method(MethodDeclarationSyntax m, string target, List<string> usings)
  {
    return new MethodDecl {
      Name = m.Identifier.Text,
      Target = target,
      IsStatic = m.Modifiers.Any(SyntaxKind.StaticKeyword),
      IsCoroutine = HasAttr(m.AttributeLists, "BhlCoroutine"),
      ReturnType = Type(m.ReturnType),
      Params = m.ParameterList.Parameters.Select(p => new ParamDecl {
        Name = p.Identifier.Text,
        Type = Type(p.Type),
        Default = p.Default?.Value.ToString(),
        Modifiers = p.Modifiers.ToString(),
        IsCoroutine = HasAttr(p.AttributeLists, "BhlCoroutine"),
        Pos = Pos(p),
      }).ToList(),
      Usings = usings,
      Pos = Pos(m),
    };
  }

  static string MemberName(ExpressionSyntax expr)
  {
    if(expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
      return lit.Token.ValueText;
    if(expr is InvocationExpressionSyntax inv && inv.Expression is IdentifierNameSyntax id && id.Identifier.Text == "nameof"
        && inv.ArgumentList.Arguments.Count == 1)
      return LastName(inv.ArgumentList.Arguments[0].Expression);
    throw new GenException(Pos(expr), "expected nameof(T.M) or a string");
  }

  static string LastName(SyntaxNode node)
  {
    switch(node)
    {
      case IdentifierNameSyntax id:
        return id.Identifier.Text;
      case QualifiedNameSyntax q:
        return q.Right.Identifier.Text;
      case AliasQualifiedNameSyntax a:
        return a.Name.Identifier.Text;
      case MemberAccessExpressionSyntax ma:
        return ma.Name.Identifier.Text;
      default:
        throw new GenException(Pos(node), $"expected a name, got '{node}'");
    }
  }

  public void ParseSource(string file, string src, HashSet<string> bound_types, CsDecls decls)
  {
    var root = Parse(file, src);

    foreach(var e in root.DescendantNodes().OfType<EnumDeclarationSyntax>())
      decls.Enums.Add(e.Identifier.Text);

    List<string> usings = null;
    foreach(var c in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
    {
      if(!(c is ClassDeclarationSyntax || c is StructDeclarationSyntax || c is InterfaceDeclarationSyntax) || !bound_types.Contains(c.Identifier.Text))
        continue;
      usings ??= Usings(root);
      var target = FullName(c);
      var type = c.Identifier.Text;
      foreach(var member in c.Members)
      {
        //NOTE: the generated code needs access, without a modifier only an interface member is public
        bool accessible = c is InterfaceDeclarationSyntax ||
          member.Modifiers.Any(SyntaxKind.PublicKeyword) || member.Modifiers.Any(SyntaxKind.InternalKeyword);
        if(!accessible || member.Modifiers.Any(SyntaxKind.ProtectedKeyword))
          continue;
        if(member is MethodDeclarationSyntax m)
        {
          var key = (type, m.Identifier.Text);
          if(!decls.Methods.TryGetValue(key, out var list))
            decls.Methods[key] = list = new List<MethodDecl>();
          list.Add(Method(m, target, usings));
        }
        else if(member is FieldDeclarationSyntax f && !f.Modifiers.Any(SyntaxKind.StaticKeyword) && !f.Modifiers.Any(SyntaxKind.ConstKeyword))
        {
          foreach(var v in f.Declaration.Variables)
          {
            decls.Fields[(type, v.Identifier.Text)] = new FieldDecl {
              Name = v.Identifier.Text,
              Type = Type(f.Declaration.Type),
              Get = true,
              Set = !f.Modifiers.Any(SyntaxKind.ReadOnlyKeyword),
              Usings = usings,
              Pos = Pos(v),
            };
          }
        }
        else if(member is PropertyDeclarationSyntax p && !p.Modifiers.Any(SyntaxKind.StaticKeyword))
        {
          var accessors = p.AccessorList?.Accessors;
          bool Has(SyntaxKind kind) => accessors?.Any(a => a.IsKind(kind) && !a.Modifiers.Any(SyntaxKind.PrivateKeyword) && !a.Modifiers.Any(SyntaxKind.ProtectedKeyword)) ?? false;
          decls.Fields[(type, p.Identifier.Text)] = new FieldDecl {
            Name = p.Identifier.Text,
            Type = Type(p.Type),
            //NOTE: an expression bodied property is get only
            Get = p.ExpressionBody != null || Has(SyntaxKind.GetAccessorDeclaration),
            Set = Has(SyntaxKind.SetAccessorDeclaration),
            Usings = usings,
            Pos = Pos(p),
          };
        }
      }
    }
  }

  static List<string> Usings(CompilationUnitSyntax root)
  {
    return root.DescendantNodes().OfType<UsingDirectiveSyntax>()
      .Where(u => u.Alias == null && !u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) && u.Name != null)
      .Select(u => u.Name.ToString())
      .ToList();
  }

  static TypeDecl Type(TypeSyntax t)
  {
    var decl = new TypeDecl { Text = t.ToString(), Pos = Pos(t) };
    if(t is PredefinedTypeSyntax p)
      decl.Keyword = p.Keyword.Text;
    else if(t is TupleTypeSyntax tuple)
      decl.TupleItems = tuple.Elements.Select(e => Type(e.Type)).ToList();
    else
    {
      var last = t is QualifiedNameSyntax q ? q.Right : t is AliasQualifiedNameSyntax a ? a.Name : t as SimpleNameSyntax;
      if(last != null)
      {
        decl.Name = last.Identifier.Text;
        if(last is GenericNameSyntax g)
          decl.GenericArgs = g.TypeArgumentList.Arguments.Select(Type).ToList();
      }
    }
    return decl;
  }

  static string FullName(TypeDeclarationSyntax c)
  {
    var parts = new List<string>();
    for(SyntaxNode n = c; n != null; n = n.Parent)
    {
      if(n is TypeDeclarationSyntax t)
        parts.Add(t.Identifier.Text);
      else if(n is BaseNamespaceDeclarationSyntax ns)
        parts.Add(ns.Name.ToString());
    }
    parts.Reverse();
    return string.Join(".", parts);
  }
}
