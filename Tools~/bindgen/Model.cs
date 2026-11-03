using System;
using System.Collections.Generic;

//NOTE: what a parser extracts from C# sources; the generator works only with this model,
//      so parsers (Roslyn, hand written) are interchangeable and comparable

public class SrcPos
{
  public string File;
  public int Line;
  public int Col;

  public override string ToString() => $"{File}({Line},{Col})";
}

public class GenException : Exception
{
  public GenException(string msg) : base(msg) {}

  public GenException(SrcPos pos, string msg) : base($"{pos}: error: {msg}") {}
}

//NOTE: a type as written in the source, not resolved yet
public class TypeDecl
{
  public string Text;
  //NOTE: a predefined type keyword (int, void, ...)
  public string Keyword;
  //NOTE: the last identifier of a simple or qualified name
  public string Name;
  public List<TypeDecl> TupleItems;
  //NOTE: the type arguments of a generic name
  public List<TypeDecl> GenericArgs;
  public SrcPos Pos;
}

public class ParamDecl
{
  public string Name;
  public TypeDecl Type;
  //NOTE: the default value expression as written, null if none
  public string Default;
  //NOTE: ref, out, in, params, this; empty if none
  public string Modifiers;
  //NOTE: [BhlCoroutine] on a func-typed param
  public bool IsCoroutine;
  public SrcPos Pos;
}

public class MethodDecl
{
  public string Name;
  //NOTE: the full name of the declaring class, null for a custom one
  public string Target;
  //NOTE: declared in a [BhlCustom] interface, bound to the hand written Script_<Name>
  public bool IsCustom;
  //NOTE: [BhlCoroutine]
  public bool IsCoroutine;
  //NOTE: a class method: [BhlNative("..")], the native member called on self instead of Name
  public string Native;
  //NOTE: a class method bound to the static SelfArgType.<Class>_<Name>(self, ..)
  public string SelfArgType;
  public bool IsStatic;
  public TypeDecl ReturnType;
  public List<ParamDecl> Params = new List<ParamDecl>();
  //NOTE: the usings of the declaring file, the signature's types are written as there
  public List<string> Usings;
  public SrcPos Pos;
}

//NOTE: a property of a [BhlClass] interface
public class FieldDecl
{
  public string Name;
  public TypeDecl Type;
  public bool Get;
  public bool Set;
  //NOTE: [BhlNative("..")], the native member accessed instead of Name
  public string Native;
  public List<string> Usings;
  public SrcPos Pos;
}

//NOTE: a member named in [BhlClass("Name", typeof(T), nameof(T.Member), nameof(Script.<Name>_Method), ..)]
public class MemberRef
{
  //NOTE: the short name of the qualifying type, null if not qualified
  public string Type;
  public string Name;
  public SrcPos Pos;
}

//NOTE: [BhlClass("Name", typeof(T), members.., Parent = "..", New = true)] interface
public class ClassDecl
{
  public string Name;
  public TypeDecl NativeType;
  public string Parent;
  public bool New;
  public List<FieldDecl> Fields = new List<FieldDecl>();
  public List<MethodDecl> Methods = new List<MethodDecl>();
  public List<MemberRef> MemberRefs = new List<MemberRef>();
  public List<string> Usings;
  public SrcPos Pos;
}

//NOTE: one name of [BhlBind(typeof(Type), nameof(Type.Name), ...)]
public class BindDecl
{
  public string Type;
  public string Name;
  public SrcPos Pos;
}

public class CsDecls
{
  public List<BindDecl> Binds = new List<BindDecl>();
  public List<MethodDecl> Customs = new List<MethodDecl>();
  public List<ClassDecl> Classes = new List<ClassDecl>();
  public HashSet<string> Enums = new HashSet<string>();
  //NOTE: the non-private methods of the bound types, by the type's short name and method name
  public Dictionary<(string type, string name), List<MethodDecl>> Methods = new Dictionary<(string, string), List<MethodDecl>>();
  //NOTE: the non-private fields and properties of the bound types, by the type's short name and name
  public Dictionary<(string type, string name), FieldDecl> Fields = new Dictionary<(string, string), FieldDecl>();
}

public interface ICsParser
{
  void ParseDecls(string file, string src, CsDecls decls);
  //NOTE: collects enums and the members of the bound types
  void ParseSource(string file, string src, HashSet<string> bound_types, CsDecls decls);
}
