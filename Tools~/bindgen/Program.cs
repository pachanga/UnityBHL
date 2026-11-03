using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

//NOTE: generates the BHL bindings registration .cs from C# sources, parsing only (nothing is compiled):
//      [BhlBind(typeof(T), nameof(T.M), ...)] in the decls files binds static methods of T found in the sources.
//      The output is the canonical bindings format the BHL compiler reads (bhl docs/csharp-bindings.md).
//      usage: bindgen <config.json>, paths in the config are relative to it
public static class Program
{
  public static int Main(string[] args)
  {
    if(args.Length != 1)
    {
      Console.Error.WriteLine("usage: bindgen <config.json>");
      return 2;
    }

    try
    {
      var conf = Config.Load(args[0]);
      var gen = new Generator(conf);
      gen.Collect();
      var code = gen.Emit();
      bool written = WriteIfChanged(conf.ResultFile, code);
      Console.WriteLine($"bindgen: {gen.FuncsCount} funcs -> {conf.ResultFile} ({(written ? "written" : "unchanged")})");
      return 0;
    }
    catch(GenException e)
    {
      Console.Error.WriteLine(e.Message);
      return 1;
    }
  }

  //NOTE: an unchanged output isn't touched, so neither Unity nor the BHL compiler rebuild anything
  static bool WriteIfChanged(string path, string content)
  {
    if(File.Exists(path) && File.ReadAllText(path) == content)
      return false;
    Directory.CreateDirectory(Path.GetDirectoryName(path));
    File.WriteAllText(path, content);
    return true;
  }
}

public class GenException : Exception
{
  public GenException(string msg) : base(msg) {}

  public GenException(SyntaxNode node, string msg)
    : base(At(node) + ": error: " + msg) {}

  static string At(SyntaxNode node)
  {
    var span = node.GetLocation().GetLineSpan();
    return $"{span.Path}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1})";
  }
}

public class Config
{
  public string Namespace { get; set; }
  public string RegisterClass { get; set; }
  public List<string> Decls { get; set; } = new List<string>();
  public List<string> Sources { get; set; } = new List<string>();
  public string ResultFile { get; set; }
  public List<string> Imports { get; set; } = new List<string>();
  public List<string> Defines { get; set; } = new List<string>();
  //NOTE: types marshalled with Val.Encode/Decode (@bhl_custom_rw in meta)
  public List<string> CustomRw { get; set; } = new List<string>();

  public List<string> DeclFiles = new List<string>();
  public List<string> SourceFiles = new List<string>();

  public static Config Load(string path)
  {
    var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, ReadCommentHandling = JsonCommentHandling.Skip };
    var conf = JsonSerializer.Deserialize<Config>(File.ReadAllText(path), opts);
    var dir = Path.GetDirectoryName(Path.GetFullPath(path));
    if(string.IsNullOrEmpty(conf.Namespace) || string.IsNullOrEmpty(conf.RegisterClass) || string.IsNullOrEmpty(conf.ResultFile))
      throw new GenException($"{path}: 'namespace', 'register_class' and 'result_file' are required");
    conf.ResultFile = Norm(Path.GetFullPath(Path.Combine(dir, conf.ResultFile)));
    conf.DeclFiles = conf.Decls.SelectMany(p => Glob(dir, p)).Distinct().ToList();
    conf.SourceFiles = conf.Sources.SelectMany(p => Glob(dir, p)).Distinct().ToList();
    return conf;
  }

  //NOTE: supports 'dir/file.cs', 'dir/*.cs' and 'dir/**/*.cs'
  static IEnumerable<string> Glob(string base_dir, string pattern)
  {
    var full = Norm(Path.GetFullPath(Path.Combine(base_dir, pattern)));
    int star = full.IndexOf('*');
    if(star < 0)
      return File.Exists(full) ? new[] { full } : new string[0];

    var root = full.Substring(0, full.LastIndexOf('/', star));
    var mask = full.Substring(root.Length + 1);
    bool rec = mask.StartsWith("**/");
    if(rec)
      mask = mask.Substring(3);
    if(mask.Contains('/') || mask.Contains("**"))
      throw new GenException($"unsupported glob '{pattern}'");
    if(!Directory.Exists(root))
      return new string[0];
    return Directory.GetFiles(root, mask, rec ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
      .Select(Norm).OrderBy(f => f, StringComparer.Ordinal);
  }

  public static string Norm(string path) => path.Replace('\\', '/');
}

public class Generator
{
  class Bind
  {
    public string Type;
    public string Name;
    public SyntaxNode Node;
  }

  class Func
  {
    public string Name;
    public string Target;
    public MethodDeclarationSyntax Method;
  }

  readonly Config conf;
  readonly CSharpParseOptions parse_opts;
  readonly List<Bind> binds = new List<Bind>();
  readonly HashSet<string> enums = new HashSet<string>();
  readonly List<Func> funcs = new List<Func>();
  readonly List<string> imports = new List<string>();

  static readonly string[] header_usings = { "System", "System.Collections", "System.Collections.Generic", "bhl" };

  public int FuncsCount => funcs.Count;

  public Generator(Config conf)
  {
    this.conf = conf;
    parse_opts = CSharpParseOptions.Default
      .WithLanguageVersion(LanguageVersion.Latest)
      .WithPreprocessorSymbols(conf.Defines);
  }

  CompilationUnitSyntax Parse(string file, string src)
  {
    return (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(src, parse_opts, file).GetRoot();
  }

  public void Collect()
  {
    foreach(var file in conf.DeclFiles)
      CollectBinds(Parse(file, File.ReadAllText(file)));

    var types = new HashSet<string>(binds.Select(b => b.Type));
    //NOTE: methods by the bound type and name, with the full name of the type
    var methods = new Dictionary<(string, string), List<(MethodDeclarationSyntax m, string target)>>();
    foreach(var file in conf.SourceFiles)
    {
      var src = File.ReadAllText(file);
      //NOTE: parsing only the files which may declare enums or bound types
      if(!src.Contains("enum ") && !types.Any(t => src.Contains("class " + t)))
        continue;
      CollectSource(Parse(file, src), types, methods);
    }

    var seen = new HashSet<string>();
    foreach(var b in binds)
    {
      if(!seen.Add(b.Name))
        throw new GenException(b.Node, $"'{b.Name}' is bound twice");
      if(!methods.TryGetValue((b.Type, b.Name), out var found))
        throw new GenException(b.Node, $"static method '{b.Type}.{b.Name}' is not found in the sources");
      if(found.Count > 1)
        throw new GenException(found[1].m, $"'{b.Type}.{b.Name}' is overloaded, BHL has no overloads");
      funcs.Add(new Func { Name = b.Name, Target = found[0].target, Method = found[0].m });
      CollectImports(found[0].m);
    }
  }

  void CollectBinds(CompilationUnitSyntax root)
  {
    foreach(var attr in root.DescendantNodes().OfType<AttributeSyntax>())
    {
      var attr_name = LastName(attr.Name);
      if(attr_name != "BhlBind" && attr_name != "BhlBindAttribute")
        continue;

      var args = attr.ArgumentList?.Arguments;
      if(args == null || args.Value.Count < 2 || !(args.Value[0].Expression is TypeOfExpressionSyntax type_of))
        throw new GenException(attr, "expected [BhlBind(typeof(T), nameof(T.M), ...)]");

      var type = LastName(type_of.Type);
      foreach(var arg in args.Value.Skip(1))
        binds.Add(new Bind { Type = type, Name = MemberName(arg.Expression), Node = arg });
    }
  }

  static string MemberName(ExpressionSyntax expr)
  {
    if(expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
      return lit.Token.ValueText;
    if(expr is InvocationExpressionSyntax inv && inv.Expression is IdentifierNameSyntax id && id.Identifier.Text == "nameof"
        && inv.ArgumentList.Arguments.Count == 1)
      return LastName(inv.ArgumentList.Arguments[0].Expression);
    throw new GenException(expr, "expected nameof(T.M) or a string");
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
        throw new GenException(node, $"expected a name, got '{node}'");
    }
  }

  void CollectSource(CompilationUnitSyntax root, HashSet<string> types, Dictionary<(string, string), List<(MethodDeclarationSyntax, string)>> methods)
  {
    foreach(var e in root.DescendantNodes().OfType<EnumDeclarationSyntax>())
      enums.Add(e.Identifier.Text);

    foreach(var c in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
    {
      if(!types.Contains(c.Identifier.Text))
        continue;
      var target = FullName(c);
      foreach(var m in c.Members.OfType<MethodDeclarationSyntax>())
      {
        if(!m.Modifiers.Any(SyntaxKind.StaticKeyword) || m.Modifiers.Any(SyntaxKind.PrivateKeyword) || m.Modifiers.Any(SyntaxKind.ProtectedKeyword))
          continue;
        var key = (c.Identifier.Text, m.Identifier.Text);
        if(!methods.TryGetValue(key, out var list))
          methods[key] = list = new List<(MethodDeclarationSyntax, string)>();
        list.Add((m, target));
      }
    }
  }

  //NOTE: the bound methods' signatures are written as in their files, so the output imports their usings
  void CollectImports(MethodDeclarationSyntax m)
  {
    foreach(var u in m.SyntaxTree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>())
    {
      if(u.Alias != null || u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) || u.Name == null)
        continue;
      var name = u.Name.ToString();
      if(!header_usings.Contains(name) && !imports.Contains(name))
        imports.Add(name);
    }
  }

  static string FullName(ClassDeclarationSyntax c)
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

  enum Kind
  {
    Void,
    Builtin,
    Any,
    Enum,
    Obj,
    CustomRw,
    Tuple,
  }

  class TypeRef
  {
    public Kind Kind;
    //NOTE: the C# type as written in the source (a keyword for builtins)
    public string Cs;
    public string Bhl;
    public List<TypeRef> Items;

    public bool IsFloat => Cs == "float" || Cs == "double";
  }

  static readonly Dictionary<string, string> builtins = new Dictionary<string, string>
  {
    {"float", "float"}, {"double", "float"},
    {"int", "int"}, {"uint", "int"}, {"short", "int"}, {"ushort", "int"}, {"sbyte", "int"}, {"byte", "int"},
    {"long", "int"}, {"ulong", "int"},
    {"string", "string"}, {"bool", "bool"},
  };

  TypeRef Resolve(TypeSyntax t, bool is_return)
  {
    if(t is PredefinedTypeSyntax p)
    {
      var kw = p.Keyword.Text;
      if(kw == "void" && is_return)
        return new TypeRef { Kind = Kind.Void, Cs = kw, Bhl = "void" };
      if(builtins.TryGetValue(kw, out var bhl))
        return new TypeRef { Kind = Kind.Builtin, Cs = kw, Bhl = bhl };
    }
    else if(t is TupleTypeSyntax tuple && is_return)
    {
      return new TypeRef { Kind = Kind.Tuple, Cs = t.ToString(), Items = tuple.Elements.Select(e => Resolve(e.Type, false)).ToList() };
    }
    else if(t is IdentifierNameSyntax || t is QualifiedNameSyntax || t is AliasQualifiedNameSyntax)
    {
      var name = LastName(t);
      var kind = name == "Val" ? Kind.Any : conf.CustomRw.Contains(name) ? Kind.CustomRw : enums.Contains(name) ? Kind.Enum : Kind.Obj;
      return new TypeRef { Kind = kind, Cs = t.ToString(), Bhl = kind == Kind.Any ? "any" : name };
    }
    throw new GenException(t, $"type '{t}' is not supported");
  }

  static string TypeRefCode(TypeRef t)
  {
    if(t.Kind == Kind.Tuple)
      return "types.TTuple(" + string.Join(",", t.Items.Select(TypeRefCode)) + ")";
    return $"types.T(\"{t.Bhl}\")";
  }

  static string Val2Native(TypeRef t, string val, string native)
  {
    switch(t.Kind)
    {
      case Kind.Builtin:
        switch(t.Cs)
        {
          case "double": return $"{native} = {val}.num;";
          case "string": return $"{native} = {val}.str;";
          case "bool": return $"{native} = {val}.bval;";
          default: return $"{native} = ({t.Cs}){val}.num;";
        }
      case Kind.Any:
        return $"{native} = {val}; {native}._refc?.Retain();";
      case Kind.Enum:
        return $"{native} = ({t.Cs})((int){val}.num);";
      case Kind.CustomRw:
        return $"{val}.Decode(ref {native});";
      default:
        return $"{native} = ({t.Cs}){val}.obj;";
    }
  }

  static string Native2Val(TypeRef t, string native, string val)
  {
    switch(t.Kind)
    {
      case Kind.Builtin:
        if(t.IsFloat)
          return $"{val}.SetFlt({native});";
        if(t.Cs == "string")
          return $"{val}.SetStr({native});";
        if(t.Cs == "bool")
          return $"{val}.SetBool({native});";
        return $"{val}.SetInt({native});";
      case Kind.Any:
        return $"{val}.SetObj({native}, Types.Any);";
      case Kind.Enum:
        return $"{val}.SetInt((int){native});";
      case Kind.CustomRw:
        return $"{val}.Encode({native});";
      default:
        return $"{val}.SetObjNoRefc({native}, Types_{t.Bhl}.Value);";
    }
  }

  static void ReturnVal(StringBuilder sb, TypeRef t)
  {
    if(t.Kind == Kind.Tuple)
    {
      //NOTE: items are pushed from the last one
      for(int i = t.Items.Count; i-- > 0;)
        sb.Append($"      {{ var dv{i} = new Val(); {Native2Val(t.Items[i], "return_val.Item" + (i + 1), "dv" + i)} exec.stack.Push(dv{i}); }}\n");
    }
    else if(t.Kind == Kind.Any)
      sb.Append("      exec.stack.Push(return_val);\n");
    else if(t.Kind == Kind.Builtin)
    {
      if(t.IsFloat)
        sb.Append("      exec.stack.Push((double)return_val);\n");
      else if(t.Cs == "string" || t.Cs == "bool")
        sb.Append($"      exec.stack.Push(({t.Cs})return_val);\n");
      else
        sb.Append("      exec.stack.Push(Val.NewInt((double)return_val));\n");
    }
    else
      sb.Append($"      {{ var dv = new Val(); {Native2Val(t, "return_val", "dv")} exec.stack.Push(dv); }}\n");
  }

  public string Emit()
  {
    var sb = new StringBuilder();
    sb.Append("//THIS FILE IS GENERATED by UnityBHL bindgen, do not edit\n");
    sb.Append("#pragma warning disable CS0162\n");
    sb.Append("#pragma warning disable UDR0001\n\n");
    foreach(var u in header_usings)
      sb.Append($"using {u};\n");
    sb.Append("#if !BHL_FRONT\n");
    foreach(var imp in conf.Imports.Concat(imports).Except(header_usings).Distinct())
      sb.Append($"using {imp};\n");
    sb.Append("#endif\n");
    sb.Append("using Types = bhl.Types;\n\n");
    sb.Append($"namespace {conf.Namespace} {{\n\n");
    sb.Append($"static public partial class {conf.RegisterClass} {{\n\n");
    sb.Append("public static void Register(Types types)\n{\n");
    foreach(var f in funcs)
      EmitFunc(sb, f);
    sb.Append("}\n\n}\n\n}\n");
    return sb.ToString();
  }

  void EmitFunc(StringBuilder sb, Func f)
  {
    var ps = f.Method.ParameterList.Parameters;
    foreach(var p in ps)
    {
      if(p.Modifiers.Count > 0)
        throw new GenException(p, $"parameter modifier '{p.Modifiers}' is not supported");
    }
    var args = ps.Select(p => (name: p.Identifier.Text, type: Resolve(p.Type, false), def: p.Default?.Value.ToString())).ToList();
    var ret = Resolve(f.Method.ReturnType, true);
    int defaults = args.Count(a => a.def != null);
    var local_fn = "fn_" + f.Name;

    sb.Append("  {\n");
    sb.Append("    #if !BHL_FRONT\n");
    sb.Append($"    static bhl.Coroutine {local_fn}(VM.ExecState exec, FuncArgsInfo args_info)\n    {{\n");
    foreach(var a in args)
      sb.Append($"      {(a.type.Kind == Kind.Any ? "bhl.Val" : a.type.Cs)} __{a.name} = default;\n");

    //NOTE: args are popped in reverse order, a default one only when it was passed
    int default_counter = 0;
    for(int i = args.Count; i-- > 0;)
    {
      var a = args[i];
      if(a.def != null)
      {
        ++default_counter;
        sb.Append($"      if(!args_info.IsDefaultArgUsed({defaults - default_counter}))\n");
      }
      sb.Append($"      {{ var dv = exec.stack.Pop(); {Val2Native(a.type, "dv", "__" + a.name)} dv._refc?.Release(); }}\n");
      if(a.def != null)
        sb.Append($"      else\n        __{a.name} = {a.def};\n");
    }

    sb.Append("      ");
    if(ret.Kind != Kind.Void)
      sb.Append("var return_val = ");
    sb.Append($"{f.Target}.{f.Name}({string.Join(", ", args.Select(a => "__" + a.name))});\n");
    if(ret.Kind != Kind.Void)
      ReturnVal(sb, ret);
    foreach(var a in args.Where(a => a.type.Kind == Kind.Any))
      sb.Append($"      __{a.name}._refc?.Release();\n");
    sb.Append("      return null;\n    }\n");
    sb.Append("    #endif\n\n");

    sb.Append($"    var fn = new FuncSymbolNative(new Origin(), \"{f.Name}\", FuncAttrib.None, {TypeRefCode(ret)}, {defaults},\n");
    sb.Append("      #if !BHL_FRONT\n");
    sb.Append($"      {local_fn}\n");
    sb.Append("      #else\n      null\n      #endif\n");
    foreach(var a in args)
      sb.Append($"      , new FuncArgSymbol(\"{a.name}\", {TypeRefCode(a.type)}, false)\n");
    sb.Append("    );\n");
    sb.Append("    types.ns.Define(fn);\n");
    sb.Append("  }\n");
  }
}
