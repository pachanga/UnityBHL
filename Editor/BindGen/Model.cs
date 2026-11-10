using System;
using System.Collections.Generic;

//NOTE: what a parser extracts from C# sources; the generator works only with this model,
//      so parsers (Roslyn, hand written) are interchangeable and comparable

internal class SrcPos
{
  public string File;
  public int Line;
  public int Col;

  public override string ToString() => $"{File}({Line},{Col})";
}

internal class GenException : Exception
{
  public GenException(string msg) : base(msg) {}

  public GenException(SrcPos pos, string msg) : base($"{pos}: error: {msg}") {}
}

//NOTE: a type as written in the source, not resolved yet
internal class TypeDecl
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

internal class ParamDecl
{
  public string Name;
  public TypeDecl Type;
  //NOTE: the default value expression as written, null if none
  public string Default;
  //NOTE: ref, out, in, params, this; empty if none
  public string Modifiers;
  //NOTE: [BhlCoroutine] on a func-typed param
  public bool IsCoroutine;
  //NOTE: [BhlType("Name")], the BHL class of the native value rather than the type's own BHL type
  public string BhlType;
  //NOTE: [BhlType(typeof(T))], the BHL func type of a VM.FuncPtr parameter
  public TypeDecl BhlTypeOf;
  public SrcPos Pos;
}

internal class MethodDecl
{
  public string Name;
  //NOTE: [BhlBind("Name")], the BHL name if it differs from Name
  public string BhlName;
  //NOTE: the full name of the declaring class, null for a custom one
  public string Target;
  //NOTE: a [BhlCoroutine] class: the hand written class the coroutine is bound to, the args are passed to its
  //      Init(exec, ..), Params are the ones after exec; null if not one
  public string CoroClass;
  //NOTE: of a [BhlCoroutine] class, its C# namespace, null if none
  public string DeclNs;
  //NOTE: [BhlCoroutine]
  public bool IsCoroutine;
  //NOTE: has type parameters, such a method isn't bindable
  public bool IsGeneric;
  //NOTE: a class method: [BhlNative("..")], the native member called on self instead of Name
  public string Native;
  //NOTE: a class method bound to the static SelfArgType.<Class>_<Name>(self, ..)
  public string SelfArgType;
  //NOTE: static in the source, or a [BhlStatic] or [BhlOp] method of a [BhlClass] interface: a static method of the class
  public bool IsStatic;
  //NOTE: [BhlOp("==")] of a [BhlClass] interface method: also bound as the operator, calls the C# operator
  public string Op;
  public TypeDecl ReturnType;
  //NOTE: [return: BhlType("Name")], see ParamDecl.BhlType
  public string ReturnBhlType;
  public List<ParamDecl> Params = new List<ParamDecl>();
  //NOTE: the usings of the declaring file, the signature's types are written as there
  public List<string> Usings;
  public SrcPos Pos;

  public MethodDecl WithBhlName(string name)
  {
    var m = (MethodDecl)MemberwiseClone();
    m.BhlName = name;
    return m;
  }
}

//NOTE: a property of a [BhlClass] interface
internal class FieldDecl
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
internal class MemberRef
{
  //NOTE: the short name of the qualifying type, null if not qualified
  public string Type;
  public string Name;
  public SrcPos Pos;
}

//NOTE: [BhlClass("Name", typeof(T), members.., Parent = "..", New = true)] interface
internal class ClassDecl
{
  public string Name;
  public TypeDecl NativeType;
  public string Parent;
  public bool New;
  public List<FieldDecl> Fields = new List<FieldDecl>();
  public List<MethodDecl> Methods = new List<MethodDecl>();
  public List<MemberRef> MemberRefs = new List<MemberRef>();
  //NOTE: [BhlInterface("Name", typeof(T), Inherits = new[] { .. })] interface: a BHL interface of the native
  //      interface T, its methods are called on the object
  public bool IsInterface;
  //NOTE: the name of the declaring interface and the last identifiers of its bases: the bases which are
  //      declarations too are the parent class, the implemented or the inherited interfaces
  public string DeclName;
  public List<string> Bases = new List<string>();
  //NOTE: the C# namespace of the declaring interface, null if none; NameGiven: the BHL name is the attribute's
  public string Ns;
  public bool NameGiven;
  //NOTE: Pod = true: a value type kept boxed in Val.obj (POD in meta), CustomRW = true: kept in Val itself by
  //      the project's Val.Encode/Decode (@bhl_custom_rw); both are written back to the Val when changed
  public bool Pod;
  public bool CustomRW;
  //NOTE: the BHL names of the interfaces a class implements (Implements) or an interface inherits (Inherits)
  public List<string> Implements = new List<string>();
  public List<string> Inherits = new List<string>();
  public List<string> Usings;
  public SrcPos Pos;
}

//NOTE: one name of [BhlBind(typeof(Type), nameof(Type.Name), ...)], or [BhlBind] on the static method Type.Name
internal class BindDecl
{
  public string Type;
  public string Name;
  //NOTE: [BhlBind("Name")] on a method, null if the BHL name is the method's
  public string BhlName;
  //NOTE: of the declarations form, the C# namespace of the type it's on, null if none
  public string DeclNs;
  public SrcPos Pos;
}

//NOTE: [BhlBind(typeof(Type), Except = new[] { nameof(Type.Name), .. })], all the static methods of the type
internal class BindAllDecl
{
  public string Type;
  public string DeclNs;
  public List<BindDecl> Except = new List<BindDecl>();
  public SrcPos Pos;
}

internal class CsDecls
{
  public List<BindDecl> Binds = new List<BindDecl>();
  public List<BindAllDecl> BindAlls = new List<BindAllDecl>();
  public List<MethodDecl> Coroutines = new List<MethodDecl>();
  public List<ClassDecl> Classes = new List<ClassDecl>();
  public HashSet<string> Enums = new HashSet<string>();
  //NOTE: the non-private methods of the bound types, by the type's short name and method name
  public Dictionary<(string type, string name), List<MethodDecl>> Methods = new Dictionary<(string, string), List<MethodDecl>>();
  //NOTE: the non-private fields and properties of the bound types, by the type's short name and name
  public Dictionary<(string type, string name), FieldDecl> Fields = new Dictionary<(string, string), FieldDecl>();
}

internal interface ICsParser
{
  void ParseDecls(string file, string src, CsDecls decls);
  //NOTE: collects enums, the members of the bound types and the [BhlBind] methods (to Binds)
  void ParseSource(string file, string src, HashSet<string> bound_types, CsDecls decls);
}
