using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

//NOTE: a hand written C# parser of just what the bindings need: usings, namespaces, type declarations,
//      attributes and member signatures; bodies and initializers are skipped by bracket balance. It needs
//      no Roslyn, so it can run inside Unity. It fills the model as RoslynParser does, --parser=both checks it
internal class HandParser : ICsParser
{
  readonly HashSet<string> defines;

  public HandParser(IEnumerable<string> defines)
  {
    this.defines = new HashSet<string>(defines);
  }

  public void ParseDecls(string file, string src, CsDecls decls)
  {
    var tree = new Parser(file, src, defines).Parse();

    foreach(var i in tree.Types.Where(t => t.Kind == "interface"))
    {
      var class_attr = FindAttr(i.Attrs, "BhlClass");
      if(class_attr != null)
        decls.Classes.Add(Class(i, class_attr, tree.Usings));
      var iface_attr = FindAttr(i.Attrs, "BhlInterface");
      if(iface_attr != null)
      {
        var iface = Class(i, iface_attr, tree.Usings);
        iface.IsInterface = true;
        decls.Classes.Add(iface);
      }
      if(!HasAttr(i.Attrs, "BhlCustom"))
        continue;
      foreach(var m in i.Members.Where(m => m.Kind == MemberKind.Method))
      {
        var decl = Method(m, null, tree.Usings);
        decl.IsCustom = true;
        decl.DeclNs = i.Ns;
        decls.Customs.Add(decl);
      }
    }

    //NOTE: the declarations form is on a type, [BhlBind] on a method is read from the sources
    foreach(var (attr, decl_ns) in tree.Types.SelectMany(t => t.Attrs.Select(a => (a, t.Ns))))
    {
      if(!IsAttr(attr, "BhlBind"))
        continue;

      var args = attr.Args;
      if(args == null || args.Count < 1 || args[0].Expr.TypeOf == null || args[0].NameEquals != null)
        throw new GenException(attr.Pos, "expected [BhlBind(typeof(T), nameof(T.M), ...)]");

      var type_of = args[0].Expr.TypeOf;
      if(type_of.Name == null)
        throw new GenException(type_of.Pos, $"expected a name, got '{type_of.Text}'");
      var names = args.Skip(1).Where(a => a.NameEquals == null).ToList();
      var except = args.Skip(1).Where(a => a.NameEquals != null).ToList();
      if(except.Count > 1 || except.Count == 1 && (except[0].NameEquals != "Except" || except[0].Expr.Items == null || names.Count > 0))
        throw new GenException(except.Last().Pos, "expected Except = new[] { nameof(T.M), ... } without the names to bind");
      foreach(var arg in names)
        decls.Binds.Add(new BindDecl { Type = type_of.Name, Name = MemberName(arg.Expr), DeclNs = decl_ns, Pos = arg.Pos });
      if(names.Count == 0)
      {
        decls.BindAlls.Add(new BindAllDecl {
          Type = type_of.Name,
          DeclNs = decl_ns,
          Except = except.SelectMany(a => a.Expr.Items).Select(e => new BindDecl { Type = type_of.Name, Name = MemberName(e), Pos = e.Pos }).ToList(),
          Pos = attr.Pos,
        });
      }
    }
  }

  public void ParseSource(string file, string src, HashSet<string> bound_types, CsDecls decls)
  {
    var tree = new Parser(file, src, defines).Parse();

    decls.Enums.UnionWith(tree.Enums);

    foreach(var c in tree.Types)
    {
      if(!(c.Kind == "class" || c.Kind == "struct" || c.Kind == "interface"))
        continue;
      //NOTE: a type with [BhlBind] methods is bound as a whole here, so their overloads are found too
      if(!bound_types.Contains(c.Name) && !c.Members.Any(m => m.Kind == MemberKind.Method && HasAttr(m.Attrs, "BhlBind")))
        continue;
      foreach(var member in c.Members)
      {
        var bind = member.Kind == MemberKind.Method ? FindAttr(member.Attrs, "BhlBind") : null;
        //NOTE: the generated code needs access, without a modifier only an interface member is public
        bool accessible = c.Kind == "interface" || member.Mods.Contains("public") || member.Mods.Contains("internal");
        if(bind != null && (!accessible || member.Mods.Contains("protected") || !member.Mods.Contains("static")))
          throw new GenException(bind.Pos, $"[BhlBind] is expected on a public or internal static method, '{c.Name}.{member.Name}' isn't one");
        if(!accessible || member.Mods.Contains("protected"))
          continue;
        if(member.Kind == MemberKind.Method)
        {
          var key = (c.Name, member.Name);
          if(!decls.Methods.TryGetValue(key, out var list))
            decls.Methods[key] = list = new List<MethodDecl>();
          list.Add(Method(member, c.FullName, tree.Usings));
          if(bind != null)
          {
            if(bind.Args != null && bind.Args.Count > 1)
              throw new GenException(bind.Pos, "expected [BhlBind] or [BhlBind(\"Name\")] on a method");
            decls.Binds.Add(new BindDecl {
              Type = c.Name,
              Name = member.Name,
              BhlName = bind.Args != null && bind.Args.Count == 1 ? StringArg(bind, 0) : null,
              Pos = bind.Pos,
            });
          }
        }
        else if(member.Kind == MemberKind.Field && !member.Mods.Contains("static") && !member.Mods.Contains("const"))
        {
          foreach(var v in member.Vars)
          {
            decls.Fields[(c.Name, v.name)] = new FieldDecl {
              Name = v.name,
              Type = member.Type,
              Get = true,
              Set = !member.Mods.Contains("readonly"),
              Usings = tree.Usings,
              Pos = v.pos,
            };
          }
        }
        else if(member.Kind == MemberKind.Property && !member.Mods.Contains("static"))
        {
          bool Has(string kind) => member.Accessors?.Any(a => a.kind == kind && !a.mods.Contains("private") && !a.mods.Contains("protected")) ?? false;
          decls.Fields[(c.Name, member.Name)] = new FieldDecl {
            Name = member.Name,
            Type = member.Type,
            //NOTE: an expression bodied property is get only
            Get = member.Accessors == null || Has("get"),
            Set = Has("set"),
            Usings = tree.Usings,
            Pos = member.Pos,
          };
        }
      }
    }
  }

  static bool IsAttr(Attr attr, string name)
  {
    return attr.Name == name || attr.Name == name + "Attribute";
  }

  static bool HasAttr(List<Attr> attrs, string name)
  {
    return FindAttr(attrs, name) != null;
  }

  static Attr FindAttr(List<Attr> attrs, string name)
  {
    return attrs.FirstOrDefault(a => IsAttr(a, name));
  }

  static string StringArg(Attr attr, int idx)
  {
    if(attr.Args == null || attr.Args.Count <= idx || attr.Args[idx].Expr.Str == null)
      throw new GenException(attr.Pos, $"expected a string argument #{idx + 1}");
    return attr.Args[idx].Expr.Str;
  }

  static MemberRef MemberRefOf(Expr expr)
  {
    if(expr.NameOf != null)
      return new MemberRef { Type = expr.NameOfMember ? expr.NameOf[expr.NameOf.Count - 2] : null, Name = expr.NameOf.Last(), Pos = expr.Pos };
    if(expr.Str != null)
      return new MemberRef { Name = expr.Str, Pos = expr.Pos };
    throw new GenException(expr.Pos, "expected nameof(T.Member) or a string");
  }

  static string MemberName(Expr expr)
  {
    if(expr.Str != null)
      return expr.Str;
    if(expr.NameOf != null)
      return expr.NameOf.Last();
    throw new GenException(expr.Pos, "expected nameof(T.M) or a string");
  }

  static ClassDecl Class(TypeNode i, Attr attr, List<string> usings)
  {
    var args = attr.Args;
    //NOTE: without the name first the BHL name is the interface's
    int type_idx = args != null && args.Count > 0 && args[0].Expr.TypeOf != null ? 0 : 1;
    if(args == null || args.Count <= type_idx || args[type_idx].Expr.TypeOf == null)
      throw new GenException(attr.Pos, $"expected [{attr.Name}(typeof(T), ...)] or [{attr.Name}(\"Name\", typeof(T), ...)]");
    var decl = new ClassDecl { Name = type_idx == 0 ? i.Name : StringArg(attr, 0), NameGiven = type_idx == 1, DeclName = i.Name, Ns = i.Ns, Bases = i.Bases, NativeType = args[type_idx].Expr.TypeOf, Usings = usings, Pos = i.Pos };
    foreach(var named in args.Skip(type_idx + 1))
    {
      if(named.NameEquals == null)
      {
        decl.MemberRefs.Add(MemberRefOf(named.Expr));
        continue;
      }
      if(named.NameEquals == "Parent" && named.Expr.Str != null)
        decl.Parent = named.Expr.Str;
      else if(named.NameEquals == "New" && named.Expr.Bool != null)
        decl.New = named.Expr.Bool.Value;
      else if(named.NameEquals == "Pod" && named.Expr.Bool != null)
        decl.Pod = named.Expr.Bool.Value;
      else if(named.NameEquals == "CustomRW" && named.Expr.Bool != null)
        decl.CustomRW = named.Expr.Bool.Value;
      else if((named.NameEquals == "Implements" || named.NameEquals == "Inherits") && named.Expr.Items != null && named.Expr.Items.All(e => e.Str != null))
        (named.NameEquals == "Implements" ? decl.Implements : decl.Inherits).AddRange(named.Expr.Items.Select(e => e.Str));
      else
        throw new GenException(named.Pos, "expected Parent = \"..\", New, Pod or CustomRW = true|false, Implements or Inherits = new[] { \"..\" }");
    }

    foreach(var member in i.Members)
    {
      if(member.Kind == MemberKind.Property)
      {
        if(member.Accessors == null)
          throw new GenException(member.Pos, "expected { get; set; } accessors");
        var native = FindAttr(member.Attrs, "BhlNative");
        decl.Fields.Add(new FieldDecl {
          Name = member.Name,
          Type = member.Type,
          Get = member.Accessors.Any(a => a.kind == "get"),
          Set = member.Accessors.Any(a => a.kind == "set"),
          Native = native != null ? StringArg(native, 0) : null,
          IsCustom = HasAttr(member.Attrs, "BhlCustom"),
          Usings = usings,
          Pos = member.Pos,
        });
      }
      else if(member.Kind == MemberKind.Method)
      {
        var method = Method(member, null, usings);
        var native = FindAttr(member.Attrs, "BhlNative");
        method.Native = native != null ? StringArg(native, 0) : null;
        var op = FindAttr(member.Attrs, "BhlOp");
        method.Op = op != null ? StringArg(op, 0) : null;
        method.IsStatic = HasAttr(member.Attrs, "BhlStatic") || op != null;
        method.IsCustom = HasAttr(member.Attrs, "BhlCustom");
        decl.Methods.Add(method);
      }
      else
        throw new GenException(member.Pos, "only properties and methods are expected in a [BhlClass] interface");
    }
    return decl;
  }

  static string BhlTypeArg(List<Attr> attrs)
  {
    var attr = FindAttr(attrs, "BhlType");
    return attr != null ? StringArg(attr, 0) : null;
  }

  static MethodDecl Method(Member m, string target, List<string> usings)
  {
    return new MethodDecl {
      Name = m.Name,
      Target = target,
      IsStatic = m.Mods.Contains("static"),
      IsCoroutine = HasAttr(m.Attrs, "BhlCoroutine"),
      IsGeneric = m.IsGeneric,
      ReturnType = m.Type,
      ReturnBhlType = BhlTypeArg(m.Attrs.Where(a => a.Target == "return").ToList()),
      Params = m.Params.Select(p => new ParamDecl {
        Name = p.Name,
        Type = p.Type,
        Default = p.Default,
        Modifiers = p.Modifiers,
        IsCoroutine = HasAttr(p.Attrs, "BhlCoroutine"),
        BhlType = BhlTypeArg(p.Attrs),
        Pos = p.Pos,
      }).ToList(),
      Usings = usings,
      Pos = m.Pos,
    };
  }

  enum MemberKind { Method, Field, Property, Other }

  //NOTE: an expression in an attribute, only the forms the bindings use are recognized, the rest are all null
  class Expr
  {
    //NOTE: a string literal, not an interpolated one
    public string Str;
    public bool? Bool;
    public TypeDecl TypeOf;
    //NOTE: the names of nameof(A.B.C), the last one is a member access if NameOfMember
    public List<string> NameOf;
    public bool NameOfMember;
    //NOTE: the items of new[] { .. } or new T[] { .. }
    public List<Expr> Items;
    public SrcPos Pos;
  }

  class Arg
  {
    public string NameEquals;
    public Expr Expr;
    public SrcPos Pos;
  }

  class Attr
  {
    //NOTE: the last identifier of the name
    public string Name;
    //NOTE: the target of [target: Name], null if none
    public string Target;
    //NOTE: null without parentheses
    public List<Arg> Args;
    public SrcPos Pos;
  }

  class Param
  {
    public List<Attr> Attrs;
    public string Modifiers;
    public TypeDecl Type;
    public string Name;
    public string Default;
    public SrcPos Pos;
  }

  class Member
  {
    public MemberKind Kind;
    public List<Attr> Attrs;
    public List<string> Mods;
    public TypeDecl Type;
    public string Name;
    public List<Param> Params;
    public List<(string name, SrcPos pos)> Vars;
    //NOTE: null for an expression bodied property
    public List<(string kind, List<string> mods)> Accessors;
    public bool IsGeneric;
    //NOTE: the start of the declaration, its attributes included
    public SrcPos Pos;
  }

  class TypeNode
  {
    //NOTE: class, struct, interface or record
    public string Kind;
    public string Name;
    public string FullName;
    //NOTE: the namespace it's declared in, null if none
    public string Ns;
    public List<Attr> Attrs;
    //NOTE: the last identifiers of the base types
    public List<string> Bases = new List<string>();
    public List<Member> Members = new List<Member>();
    public SrcPos Pos;
  }

  class Tree
  {
    public List<string> Usings = new List<string>();
    //NOTE: all the type declarations, nested ones included, in the order of the source
    public List<TypeNode> Types = new List<TypeNode>();
    public List<string> Enums = new List<string>();
    //NOTE: all the attributes outside of bodies, in the order of the source
    public List<Attr> Attrs = new List<Attr>();
  }

  enum Tk { Id, Num, Str, Char, Punct, Eof }

  class Token
  {
    public Tk Kind;
    //NOTE: keywords are Ids
    public string Text;
    //NOTE: the value of a string literal, null for an interpolated one
    public string Value;
    public int Start;
    public int End;
  }

  class Parser
  {
    static readonly HashSet<string> modifiers = new HashSet<string> {
      "public", "private", "protected", "internal", "static", "readonly", "const", "virtual", "override", "abstract",
      "sealed", "extern", "unsafe", "volatile", "new", "async", "partial", "fixed", "required", "file",
    };
    static readonly HashSet<string> param_modifiers = new HashSet<string> { "ref", "out", "in", "params", "this", "scoped", "readonly" };
    static readonly HashSet<string> predefined = new HashSet<string> {
      "bool", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong", "char", "float", "double", "decimal", "string", "object", "void",
    };
    static readonly string[] two_char_puncts = { "=>", "::", "==", "!=", "<=", "&&", "||" };

    readonly string file;
    readonly string src;
    readonly HashSet<string> defines;
    readonly List<int> line_starts = new List<int> { 0 };
    readonly List<Token> toks = new List<Token>();
    readonly Tree tree = new Tree();
    int i;
    int p;

    public Parser(string file, string src, HashSet<string> defines)
    {
      this.file = file;
      this.src = src;
      //NOTE: #define and #undef change them for this file only
      this.defines = new HashSet<string>(defines);
      for(int k = 0; k < src.Length; ++k)
      {
        if(src[k] == '\n' || src[k] == '\r' && (k + 1 == src.Length || src[k + 1] != '\n'))
          line_starts.Add(k + 1);
      }
    }

    public Tree Parse()
    {
      Lex();
      Namespace(null);
      if(Cur.Kind != Tk.Eof)
        throw Error(Cur, $"unexpected '{Cur.Text}'");
      return tree;
    }

    SrcPos PosAt(int offset)
    {
      int line = line_starts.BinarySearch(offset);
      if(line < 0)
        line = ~line - 1;
      return new SrcPos { File = file, Line = line + 1, Col = offset - line_starts[line] + 1 };
    }

    SrcPos Pos(Token t) => PosAt(t.Start);

    GenException Error(Token t, string msg) => new GenException(Pos(t), msg);

    ////////////////////////////////////////////////// lexer

    char Peek(int k = 0) => i + k < src.Length ? src[i + k] : '\0';

    static bool IsIdChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    void Lex()
    {
      //NOTE: the #if nesting: whether the code around is active and whether a branch was taken
      var conds = new Stack<(bool outer, bool taken)>();
      bool active = true;
      bool line_start = true;
      while(i < src.Length)
      {
        char c = src[i];
        if(c == '\n' || c == '\r')
        {
          ++i;
          line_start = true;
          continue;
        }
        //NOTE: a BOM not at the start is whitespace too, as Roslyn has it
        if(char.IsWhiteSpace(c) || c == '\uFEFF')
        {
          ++i;
          continue;
        }
        if(c == '#' && line_start)
        {
          Directive(conds, ref active);
          continue;
        }
        if(!active)
        {
          SkipLine();
          continue;
        }
        line_start = false;

        int start = i;
        if(c == '/' && Peek(1) == '/')
          SkipLine();
        else if(c == '/' && Peek(1) == '*')
        {
          int end = src.IndexOf("*/", i + 2, StringComparison.Ordinal);
          i = end < 0 ? src.Length : end + 2;
        }
        else if(c == '"' || c == '\'' || IsStringStart())
        {
          var value = String();
          toks.Add(new Token { Kind = c == '\'' ? Tk.Char : Tk.Str, Text = src.Substring(start, i - start), Value = value, Start = start, End = i });
        }
        else if(IsIdChar(c) || c == '@')
        {
          bool num = char.IsDigit(c);
          ++i;
          //NOTE: a number takes its fraction too: 1.5f
          while(IsIdChar(Peek()) || num && Peek() == '.' && char.IsDigit(Peek(1)))
            ++i;
          toks.Add(new Token { Kind = num ? Tk.Num : Tk.Id, Text = src.Substring(start, i - start), Start = start, End = i });
        }
        else
        {
          var two = i + 1 < src.Length ? src.Substring(i, 2) : null;
          i += two_char_puncts.Contains(two) ? 2 : 1;
          toks.Add(new Token { Kind = Tk.Punct, Text = src.Substring(start, i - start), Start = start, End = i });
        }
      }
      if(conds.Count > 0)
        throw new GenException(PosAt(src.Length), "#endif expected");
      toks.Add(new Token { Kind = Tk.Eof, Text = "", Start = src.Length, End = src.Length });
    }

    void SkipLine()
    {
      while(i < src.Length && src[i] != '\n' && src[i] != '\r')
        ++i;
    }

    //NOTE: @"..", $"..", $@"..", @$"..", $$"""..""" and such
    bool IsStringStart()
    {
      int k = i;
      while(k < src.Length && (src[k] == '@' || src[k] == '$'))
        ++k;
      return k > i && k < src.Length && src[k] == '"';
    }

    //NOTE: skips a string or char literal, returns the value of a string one if it's not interpolated
    string String()
    {
      int start = i;
      if(src[i] == '\'')
      {
        for(++i; i < src.Length && src[i] != '\'' && src[i] != '\n'; ++i)
        {
          if(src[i] == '\\')
            ++i;
        }
        if(Peek() != '\'')
          throw new GenException(PosAt(start), "unterminated char literal");
        ++i;
        return null;
      }

      bool verbatim = false;
      int dollars = 0;
      for(; src[i] == '@' || src[i] == '$'; ++i)
      {
        if(src[i] == '@')
          verbatim = true;
        else
          ++dollars;
      }
      int quotes = 0;
      while(Peek(quotes) == '"')
        ++quotes;
      if(quotes >= 3 && !verbatim)
        return Raw(start, quotes, dollars > 0);

      ++i;
      var sb = dollars > 0 ? null : new StringBuilder();
      while(i < src.Length)
      {
        char c = src[i];
        if(c == '"')
        {
          if(verbatim && Peek(1) == '"')
          {
            sb?.Append('"');
            i += 2;
            continue;
          }
          ++i;
          return sb?.ToString();
        }
        if(c == '\\' && !verbatim)
        {
          ++i;
          //NOTE: not sb?.Append(Escape()), it wouldn't consume the escape of an interpolated string
          var escaped = Escape();
          sb?.Append(escaped);
          continue;
        }
        if(dollars > 0 && c == '{')
        {
          if(Peek(1) == '{')
            i += 2;
          else
            SkipHole();
          continue;
        }
        if(c == '\n' && !verbatim)
          break;
        sb?.Append(c);
        ++i;
      }
      throw new GenException(PosAt(start), "unterminated string literal");
    }

    //NOTE: an interpolation hole, it may have strings of its own
    void SkipHole()
    {
      int depth = 0;
      while(i < src.Length)
      {
        char c = src[i];
        if(c == '"' || c == '\'' || IsStringStart())
        {
          String();
          continue;
        }
        if(c == '{')
          ++depth;
        else if(c == '}' && --depth == 0)
        {
          ++i;
          return;
        }
        ++i;
      }
    }

    //NOTE: shortcut: the holes of an interpolated raw string aren't parsed, a hole with a """ string breaks it
    string Raw(int start, int quotes, bool interpolated)
    {
      i += quotes;
      var end_quotes = new string('"', quotes);
      int end = src.IndexOf(end_quotes, i, StringComparison.Ordinal);
      if(end < 0)
        throw new GenException(PosAt(start), "unterminated raw string literal");
      var content = src.Substring(i, end - i);
      i = end + quotes;
      if(interpolated)
        return null;
      if(!content.Contains('\n'))
        return content;
      //NOTE: multi line: the first and the last lines are dropped, the indentation of the last one is removed
      var lines = content.Replace("\r\n", "\n").Split('\n');
      var indent = lines[lines.Length - 1];
      return string.Join("\n", lines.Skip(1).Take(lines.Length - 2).Select(l => l.StartsWith(indent) ? l.Substring(indent.Length) : l));
    }

    string Escape()
    {
      char e = Peek();
      ++i;
      switch(e)
      {
        case '0': return "\0";
        case 'a': return "\a";
        case 'b': return "\b";
        case 'f': return "\f";
        case 'n': return "\n";
        case 'r': return "\r";
        case 't': return "\t";
        case 'v': return "\v";
        //NOTE: \u and \x are UTF-16 units, a surrogate one included
        case 'u': return ((char)Hex(4, 4)).ToString();
        case 'U': return char.ConvertFromUtf32(Hex(8, 8));
        case 'x': return ((char)Hex(1, 4)).ToString();
        default: return e.ToString();
      }
    }

    int Hex(int min, int max)
    {
      int start = i;
      while(i - start < max && Uri.IsHexDigit(Peek()))
        ++i;
      if(i - start < min)
        throw new GenException(PosAt(start), "invalid escape sequence");
      return Convert.ToInt32(src.Substring(start, i - start), 16);
    }

    void Directive(Stack<(bool outer, bool taken)> conds, ref bool active)
    {
      int start = i;
      SkipLine();
      var line = src.Substring(start + 1, i - start - 1);
      int comment = line.IndexOf("//", StringComparison.Ordinal);
      if(comment >= 0)
        line = line.Substring(0, comment);
      var m = Regex.Match(line, @"^\s*(\w+)\s*(.*?)\s*$");
      var kw = m.Groups[1].Value;
      var expr = m.Groups[2].Value;
      switch(kw)
      {
        case "if":
        {
          bool taken = active && Eval(expr, start);
          conds.Push((active, taken));
          active = taken;
          break;
        }
        case "elif":
        case "else":
        case "endif":
        {
          if(conds.Count == 0)
            throw new GenException(PosAt(start), $"unexpected #{kw}");
          var (outer, taken) = conds.Pop();
          if(kw == "endif")
          {
            active = outer;
            break;
          }
          active = outer && !taken && (kw == "else" || Eval(expr, start));
          conds.Push((outer, taken || active));
          break;
        }
        case "define":
          if(active)
            defines.Add(expr);
          break;
        case "undef":
          if(active)
            defines.Remove(expr);
          break;
      }
    }

    //NOTE: symbols, true, false, !, ==, !=, &&, || and parentheses
    bool Eval(string expr, int offset)
    {
      var tokens = Regex.Matches(expr, @"\|\||&&|==|!=|[!()]|\w+").Cast<Match>().Select(t => t.Value).ToList();
      int k = 0;
      string Cur() => k < tokens.Count ? tokens[k] : null;
      bool Or()
      {
        bool v = And();
        while(Cur() == "||") { ++k; v |= And(); }
        return v;
      }
      bool And()
      {
        bool v = Eq();
        while(Cur() == "&&") { ++k; v &= Eq(); }
        return v;
      }
      bool Eq()
      {
        bool v = Unary();
        while(Cur() == "==" || Cur() == "!=") { bool eq = tokens[k++] == "=="; v = (v == Unary()) == eq; }
        return v;
      }
      bool Unary()
      {
        var t = Cur();
        ++k;
        if(t == "!")
          return !Unary();
        if(t == "(")
        {
          bool v = Or();
          if(Cur() != ")")
            throw new GenException(PosAt(offset), $"invalid preprocessor expression '{expr}'");
          ++k;
          return v;
        }
        if(t == null || !Regex.IsMatch(t, @"^\w+$"))
          throw new GenException(PosAt(offset), $"invalid preprocessor expression '{expr}'");
        return t == "true" || t != "false" && defines.Contains(t);
      }
      bool result = Or();
      if(k != tokens.Count)
        throw new GenException(PosAt(offset), $"invalid preprocessor expression '{expr}'");
      return result;
    }

    ////////////////////////////////////////////////// parser

    Token Cur => toks[p];

    bool Is(string text, int k = 0)
    {
      var t = toks[Math.Min(p + k, toks.Count - 1)];
      return (t.Kind == Tk.Id || t.Kind == Tk.Punct) && t.Text == text;
    }

    bool IsId(int k = 0) => toks[Math.Min(p + k, toks.Count - 1)].Kind == Tk.Id;

    bool AtEnd => Cur.Kind == Tk.Eof;

    void Expect(string text)
    {
      if(!Is(text))
        throw Error(Cur, $"'{text}' expected, got '{Cur.Text}'");
      ++p;
    }

    string Id()
    {
      if(!IsId())
        throw Error(Cur, $"identifier expected, got '{Cur.Text}'");
      return toks[p++].Text;
    }

    //NOTE: the source text of the tokens [from, to)
    string Text(int from, int to)
    {
      return src.Substring(toks[from].Start, toks[to - 1].End - toks[from].Start);
    }

    static bool IsOpen(Token t) => t.Kind == Tk.Punct && (t.Text == "(" || t.Text == "[" || t.Text == "{");

    static bool IsClose(Token t) => t.Kind == Tk.Punct && (t.Text == ")" || t.Text == "]" || t.Text == "}");

    //NOTE: from an opening bracket past its closing one
    void SkipBalanced()
    {
      int depth = 0;
      do
      {
        if(IsOpen(Cur))
          ++depth;
        else if(IsClose(Cur))
          --depth;
        ++p;
      }
      while(depth > 0 && !AtEnd);
    }

    //NOTE: up to one of the stops outside of brackets, the stop isn't skipped
    void SkipTo(params string[] stops)
    {
      while(!AtEnd && !stops.Any(s => Is(s)) && !IsClose(Cur))
      {
        if(IsOpen(Cur))
          SkipBalanced();
        else
          ++p;
      }
    }

    void SkipAngles()
    {
      int depth = 0;
      do
      {
        if(Is("<"))
          ++depth;
        else if(Is(">"))
          --depth;
        ++p;
      }
      while(depth > 0 && !AtEnd);
    }

    //NOTE: a block, '=> expr;' or ';'
    void SkipBody()
    {
      if(Is("{"))
        SkipBalanced();
      else if(Is("=>"))
      {
        ++p;
        SkipTo(";");
        Expect(";");
      }
      else
        Expect(";");
    }

    //NOTE: a member not needed: up to its ';' or past its body
    void SkipMember()
    {
      while(!AtEnd && !Is("}"))
      {
        if(Is(";"))
        {
          ++p;
          return;
        }
        if(Is("{") || Is("=>"))
        {
          SkipBody();
          return;
        }
        if(IsOpen(Cur))
          SkipBalanced();
        else
          ++p;
      }
    }

    void Namespace(string scope)
    {
      while(!AtEnd && !Is("}"))
      {
        if(Is(";"))
          ++p;
        else if(Is("extern") && Is("alias", 1))
        {
          SkipTo(";");
          Expect(";");
        }
        else if(Is("using") || Is("global") && Is("using", 1))
          Using();
        else if(Is("namespace"))
        {
          ++p;
          int from = p;
          while(!AtEnd && !Is("{") && !Is(";"))
            ++p;
          var name = Text(from, p);
          var inner = scope == null ? name : scope + "." + name;
          if(Is(";"))
          {
            //NOTE: file scoped, the rest of the file is in it
            ++p;
            scope = inner;
            continue;
          }
          Expect("{");
          Namespace(inner);
          Expect("}");
        }
        else
          Member(scope, null);
      }
    }

    void Using()
    {
      if(Is("global"))
        ++p;
      Expect("using");
      //NOTE: neither static nor alias usings are needed
      if(Is("static") || IsId() && Is("=", 1))
      {
        SkipTo(";");
        Expect(";");
        return;
      }
      int from = p;
      SkipTo(";");
      tree.Usings.Add(Text(from, p));
      Expect(";");
    }

    //NOTE: a member of a type or a namespace
    void Member(string scope, TypeNode owner)
    {
      int start = p;
      var attrs = AttrLists(ref start);
      //NOTE: only assembly attributes, followed by a namespace
      if(AtEnd || Is("}") || Is("namespace"))
        return;
      var mods = new List<string>();
      //NOTE: 'ref' is a modifier of a ref struct, else it's a part of the type
      while(IsId() && (modifiers.Contains(Cur.Text) || Is("ref") && (Is("struct", 1) || Is("partial", 1))))
        mods.Add(toks[p++].Text);

      var m = new Member { Kind = MemberKind.Other, Attrs = attrs, Mods = mods, Pos = Pos(toks[start]) };
      owner?.Members.Add(m);

      if(Is("class") || Is("struct") || Is("interface") || Is("record"))
      {
        TypeDecl(scope, owner, attrs, start);
        return;
      }
      if(Is("enum"))
      {
        ++p;
        tree.Enums.Add(Id());
        SkipTo("{");
        SkipBalanced();
        return;
      }
      //NOTE: outside of a type it's a top level statement
      if(owner == null || Is("delegate") || Is("event") || Is("~") || Is("implicit") || Is("explicit") || !IsId() && !Is("("))
      {
        SkipMember();
        return;
      }

      var type = Type();
      //NOTE: a constructor, an operator, an indexer
      if(!IsId() || Is("operator") || Is("this"))
      {
        SkipMember();
        return;
      }
      var name_pos = Pos(Cur);
      var name = MemberName();
      if(name == null)
      {
        SkipMember();
        return;
      }
      m.Type = type;
      m.Name = name;

      if(Is("<") || Is("("))
      {
        m.Kind = MemberKind.Method;
        m.IsGeneric = Is("<");
        if(m.IsGeneric)
          SkipAngles();
        m.Params = Params();
        //NOTE: type parameter constraints
        while(!AtEnd && !Is("{") && !Is("=>") && !Is(";"))
          ++p;
        SkipBody();
      }
      else if(Is("{") || Is("=>"))
      {
        m.Kind = MemberKind.Property;
        if(Is("=>"))
        {
          SkipBody();
          return;
        }
        m.Accessors = Accessors();
        if(Is("="))
        {
          SkipTo(";");
          Expect(";");
        }
      }
      else
      {
        m.Kind = MemberKind.Field;
        m.Vars = new List<(string, SrcPos)>();
        m.Vars.Add((name, name_pos));
        while(true)
        {
          //NOTE: a fixed size buffer
          if(Is("["))
            SkipBalanced();
          if(Is("="))
            Initializer();
          if(!Is(","))
            break;
          ++p;
          var pos = Pos(Cur);
          m.Vars.Add((Id(), pos));
        }
        Expect(";");
      }
    }

    //NOTE: the name of a member, the last one of an explicit implementation (IFoo<T>.Bar), null for an operator
    //      or an indexer
    string MemberName()
    {
      var name = Id();
      while(true)
      {
        if(Is("<"))
        {
          int save = p;
          SkipAngles();
          if(!Is("."))
          {
            //NOTE: type parameters of a method
            p = save;
            return name;
          }
        }
        if(!Is("."))
          return name;
        ++p;
        if(Is("operator") || Is("this"))
          return null;
        name = Id();
      }
    }

    //NOTE: up to the next declarator or the end of the field
    //      shortcut: '<' after a name is taken for generics, 'a < b, c = 1' loses the declarator c
    void Initializer()
    {
      Expect("=");
      int angles = 0;
      while(!AtEnd && !Is(";") && !(Is(",") && angles == 0 && IsId(1) && (Is("=", 2) || Is(",", 2) || Is(";", 2))))
      {
        if(Is("<") && IsId(-1))
          ++angles;
        else if(Is(">") && angles > 0)
          --angles;
        if(IsOpen(Cur))
          SkipBalanced();
        else
          ++p;
      }
    }

    void TypeDecl(string scope, TypeNode owner, List<Attr> attrs, int start)
    {
      var kind = Cur.Text;
      ++p;
      if(kind == "record" && (Is("class") || Is("struct")))
        ++p;
      var t = new TypeNode { Kind = kind, Attrs = attrs, Pos = Pos(toks[start]) };
      t.Name = Id();
      t.FullName = owner != null ? owner.FullName + "." + t.Name : scope != null ? scope + "." + t.Name : t.Name;
      t.Ns = owner != null ? owner.Ns : scope;
      tree.Types.Add(t);

      //NOTE: type parameters, a primary constructor, bases and constraints; of the bases the last identifier
      //      of each (not generic) is kept
      int angles = 0;
      bool in_bases = false;
      string base_name = null;
      while(!AtEnd && !Is("{") && !Is(";"))
      {
        if(Is("("))
        {
          SkipBalanced();
          continue;
        }
        if(Is("<"))
          ++angles;
        else if(Is(">"))
          --angles;
        else if(angles == 0 && Is(":") && !in_bases)
          in_bases = true;
        else if(angles == 0 && Is("where"))
          in_bases = false;
        else if(in_bases && angles == 0 && Is(","))
        {
          if(base_name != null)
            t.Bases.Add(base_name);
          base_name = null;
        }
        else if(in_bases && angles == 0 && IsId())
          base_name = Cur.Text;
        ++p;
      }
      if(in_bases && base_name != null)
        t.Bases.Add(base_name);
      if(Is(";"))
      {
        ++p;
        return;
      }
      Expect("{");
      while(!AtEnd && !Is("}"))
      {
        if(Is(";"))
          ++p;
        else
          Member(null, t);
      }
      Expect("}");
    }

    List<(string kind, List<string> mods)> Accessors()
    {
      var list = new List<(string, List<string>)>();
      Expect("{");
      while(!AtEnd && !Is("}"))
      {
        int start = p;
        AttrLists(ref start);
        var mods = new List<string>();
        while(IsId() && modifiers.Contains(Cur.Text))
          mods.Add(toks[p++].Text);
        list.Add((Id(), mods));
        SkipBody();
      }
      Expect("}");
      return list;
    }

    List<Param> Params()
    {
      var list = new List<Param>();
      Expect("(");
      while(!AtEnd && !Is(")"))
      {
        int start = p;
        var param = new Param { Attrs = AttrLists(ref start) };
        param.Pos = Pos(toks[start]);
        int from = p;
        while(IsId() && param_modifiers.Contains(Cur.Text))
          ++p;
        param.Modifiers = p > from ? Text(from, p) : "";
        param.Type = Type();
        param.Name = Id();
        if(Is("="))
        {
          ++p;
          int def = p;
          SkipTo(",", ")");
          param.Default = Text(def, p);
        }
        list.Add(param);
        if(!Is(")"))
          Expect(",");
      }
      Expect(")");
      return list;
    }

    //NOTE: start is moved to the first attribute list which belongs to the declaration, past assembly ones
    List<Attr> AttrLists(ref int start)
    {
      var list = new List<Attr>();
      int first = -1;
      while(Is("["))
      {
        int open = p;
        ++p;
        bool global = false;
        string target = null;
        if(IsId() && Is(":", 1))
        {
          target = Cur.Text;
          global = target == "assembly" || target == "module";
          p += 2;
        }
        if(!global && first < 0)
          first = open;
        while(!AtEnd && !Is("]"))
        {
          var attr = new Attr { Pos = Pos(Cur), Name = Id(), Target = target };
          while(Is(".") || Is("::"))
          {
            ++p;
            attr.Name = Id();
          }
          if(Is("<"))
            SkipAngles();
          if(Is("("))
            attr.Args = Args();
          tree.Attrs.Add(attr);
          if(!global)
            list.Add(attr);
          if(!Is("]"))
            Expect(",");
        }
        Expect("]");
      }
      start = first >= 0 ? first : p;
      return list;
    }

    List<Arg> Args()
    {
      var args = new List<Arg>();
      Expect("(");
      while(!AtEnd && !Is(")"))
      {
        var arg = new Arg { Pos = Pos(Cur) };
        if(IsId() && (Is("=", 1) || Is(":", 1)))
        {
          if(Is("=", 1))
            arg.NameEquals = Cur.Text;
          p += 2;
        }
        int from = p;
        SkipTo(",", ")");
        if(p == from)
          throw Error(Cur, "expression expected");
        arg.Expr = Expr(from, p);
        args.Add(arg);
        if(!Is(")"))
          Expect(",");
      }
      Expect(")");
      return args;
    }

    Expr Expr(int from, int to)
    {
      var e = new Expr { Pos = Pos(toks[from]) };
      var first = toks[from];
      int count = to - from;
      if(count == 1 && first.Kind == Tk.Str)
        e.Str = first.Value;
      else if(count == 1 && first.Kind == Tk.Id && (first.Text == "true" || first.Text == "false"))
        e.Bool = first.Text == "true";
      else if(count >= 4 && first.Text == "typeof" && toks[from + 1].Text == "(" && toks[to - 1].Text == ")")
      {
        int save = p;
        p = from + 2;
        var type = Type();
        if(p == to - 1)
          e.TypeOf = type;
        p = save;
      }
      else if(count >= 5 && first.Text == "new" && toks[to - 1].Text == "}")
      {
        int k = from + 1;
        if(toks[k].Kind == Tk.Id)
          ++k;
        if(toks[k].Text == "[" && toks[k + 1].Text == "]" && toks[k + 2].Text == "{")
          e.Items = Items(k + 3, to - 1);
      }
      else if(count >= 4 && first.Text == "nameof" && toks[from + 1].Text == "(" && toks[to - 1].Text == ")")
      {
        //NOTE: names separated by '.' or '::'
        var names = new List<string>();
        bool valid = true;
        for(int k = from + 2; k < to - 1; ++k)
        {
          bool sep = (k - from) % 2 == 1;
          var t = toks[k];
          if(sep ? t.Kind != Tk.Punct || (t.Text != "." && t.Text != "::") || k == to - 2 : t.Kind != Tk.Id)
            valid = false;
          else if(!sep)
            names.Add(t.Text);
        }
        if(valid)
        {
          e.NameOf = names;
          e.NameOfMember = names.Count > 1 && toks[to - 3].Text == ".";
        }
      }
      return e;
    }

    //NOTE: the comma separated expressions of [from, to), a trailing comma allowed
    List<Expr> Items(int from, int to)
    {
      var items = new List<Expr>();
      int save = p;
      p = from;
      while(p < to)
      {
        int start = p;
        SkipTo(",", "}");
        if(p == start)
          throw Error(Cur, "expression expected");
        items.Add(Expr(start, p));
        if(p < to)
          Expect(",");
      }
      p = save;
      return items;
    }

    //NOTE: Keyword, Name and GenericArgs, TupleItems are set as Roslyn's syntax has them: an array, a nullable,
    //      a pointer or a ref type has the Text only
    TypeDecl Type()
    {
      int from = p;
      if(Is("ref"))
      {
        ++p;
        if(Is("readonly"))
          ++p;
        Type();
        return new TypeDecl { Text = Text(from, p), Pos = Pos(toks[from]) };
      }

      var d = new TypeDecl { Pos = Pos(Cur) };
      if(Is("("))
      {
        ++p;
        d.TupleItems = new List<TypeDecl>();
        while(!AtEnd && !Is(")"))
        {
          d.TupleItems.Add(Type());
          //NOTE: the name of the element
          if(IsId())
            ++p;
          if(!Is(")"))
            Expect(",");
        }
        Expect(")");
      }
      else if(IsId() && predefined.Contains(Cur.Text))
        d.Keyword = toks[p++].Text;
      else
      {
        while(true)
        {
          d.Name = Id();
          d.GenericArgs = null;
          if(Is("<"))
          {
            ++p;
            d.GenericArgs = new List<TypeDecl>();
            while(!AtEnd && !Is(">"))
            {
              //NOTE: an unbound generic, typeof(List<>)
              d.GenericArgs.Add(Is(",") ? new TypeDecl { Text = "", Pos = Pos(Cur) } : Type());
              if(!Is(">"))
                Expect(",");
            }
            Expect(">");
          }
          if(!Is(".") && !Is("::"))
            break;
          ++p;
        }
      }

      bool wrapped = false;
      while(Is("?") || Is("*") || Is("[") && (Is("]", 1) || Is(",", 1)))
      {
        if(Is("["))
        {
          while(!AtEnd && !Is("]"))
            ++p;
        }
        ++p;
        wrapped = true;
      }
      if(wrapped)
        return new TypeDecl { Text = Text(from, p), Pos = Pos(toks[from]) };
      d.Text = Text(from, p);
      return d;
    }
  }
}
