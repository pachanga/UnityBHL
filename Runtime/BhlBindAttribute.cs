using System;
using System.Diagnostics;

namespace UnityBHL
{
//NOTE: the attributes are read (not compiled) by the bindings generator. Conditional: they may be put
//      on runtime code and declarations, but a player build carries no attribute data

//NOTE: [BhlBind], in two forms:
//      - on a static method found in the sources: binds it as the BHL function named as the method,
//        or [BhlBind("Name")] as Name: [BhlBind] public static void Vibrate(..);
//      - on a class or an interface of a declarations file: binds the listed static methods of the type,
//        [BhlBind(typeof(Script), nameof(Script.Vibrate))]; without names - all its public and internal
//        static methods but the Except ones: [BhlBind(typeof(HapticsBind), Except = new[] { nameof(HapticsBind.Helper) })].
[Conditional("UNITY_EDITOR")]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true)]
public class BhlBindAttribute : Attribute
{
  public string[] Except;

  public BhlBindAttribute() {}

  public BhlBindAttribute(string name) {}

  public BhlBindAttribute(Type type, params string[] names) {}
}

//NOTE: the methods of the interface are BHL functions bound to the hand written Script_<Name> classes
//      (Exec, or Init/Exec/Cleanup of a coroutine), the generator adds their Args, ReadArgs and ReturnValue.
//      On a method of a [BhlClass] interface: the class method bound to the hand written Script_<Class>.Method_<Name>,
//      its Args.self is the object unless the method is [BhlStatic]. On a property of a [BhlClass] interface: the field
//      read by the hand written Script_<Class>.Get_<Name>(self) and written by Set_<Name>(ref self, value)
[Conditional("UNITY_EDITOR")]
[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Property)]
public class BhlCustomAttribute : Attribute {}

//NOTE: a coroutine BHL function, or a coroutine func-typed parameter (Action, Func<..>)
[Conditional("UNITY_EDITOR")]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Parameter)]
public class BhlCoroutineAttribute : Attribute {}

//NOTE: the interface declares the BHL class of the native type, named as the interface or 'name' (in a namespace of
//      their own the interfaces don't hide the native types they're named after): its properties are fields
//      ({ get; set; } tell what BHL may do), its methods are called on the native object ([BhlStatic] ones on its type).
//      'members' bind members read from the sources, signatures included:
//      nameof(T.Member) - a field, property or method (a static one too) of the native type T itself,
//      nameof(Script.<Name>_Method) - the static method taking T self first, bound as Method
[Conditional("UNITY_EDITOR")]
[AttributeUsage(AttributeTargets.Interface)]
public class BhlClassAttribute : Attribute
{
  //NOTE: the BHL name of the parent class declared elsewhere (e.g. by meta); a [BhlClass] one is the interface's base
  public string Parent;
  //NOTE: BHL may create the object with new(), the native type needs a parameterless constructor
  public bool New;
  //NOTE: the BHL names of the interfaces the class implements declared elsewhere; [BhlInterface] ones are the bases
  public string[] Implements;
  //NOTE: a value type kept boxed in the BHL value (POD in meta): changed in a copy which is written back
  public bool Pod;
  //NOTE: a value type kept in the BHL value itself by the project's Val.Encode(T) and Val.Decode(ref T)
  //      (@bhl_custom_rw in meta), its args and returns too
  public bool CustomRW;

  public BhlClassAttribute(Type type, params string[] members) {}

  public BhlClassAttribute(string name, Type type, params string[] members) {}
}

//NOTE: the interface declares the BHL interface of the native interface type, named as the interface or 'name':
//      its methods are called on the object as the native interface, whichever class implements it:
//      [BhlInterface(typeof(UIWidgets.IUIWidget))] interface IUIWidget { void UpdateStaticData(); }
[Conditional("UNITY_EDITOR")]
[AttributeUsage(AttributeTargets.Interface)]
public class BhlInterfaceAttribute : Attribute
{
  //NOTE: the BHL names of the interfaces it inherits declared elsewhere; [BhlInterface] ones are the bases
  public string[] Inherits;

  public BhlInterfaceAttribute(Type type) {}

  public BhlInterfaceAttribute(string name, Type type) {}
}

//NOTE: the BHL class of a parameter or a return value ([return: BhlType("IList")]) rather than its C# type's own
//      BHL type, e.g. a C# IList as the IList class of System.Collections.IList rather than []any
[Conditional("UNITY_EDITOR")]
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
public class BhlTypeAttribute : Attribute
{
  public BhlTypeAttribute(string name) {}
}

//NOTE: the native member of a [BhlClass] or [BhlInterface] property or method, if it's named differently
[Conditional("UNITY_EDITOR")]
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method)]
public class BhlNativeAttribute : Attribute
{
  public BhlNativeAttribute(string member) {}
}

//NOTE: a [BhlClass] method bound as a static method of the class, called on the native type, as
//      UnityEngine.PlayerPrefs.GetInt(..): an interface can't declare it static (C# 9). A static method of
//      a type of the sources is named in the members instead, nameof(T.Method)
[Conditional("UNITY_EDITOR")]
[AttributeUsage(AttributeTargets.Method)]
public class BhlStaticAttribute : Attribute {}

//NOTE: a [BhlClass] method bound as a static method of the class and also as the BHL operator overload 'op'
//      ("==", "+", ..): both call the C# operator of its two arguments, it returns a value:
//      [BhlOp("==")] bool Equals(UnityEngine.Object a, UnityEngine.Object b);
[Conditional("UNITY_EDITOR")]
[AttributeUsage(AttributeTargets.Method)]
public class BhlOpAttribute : Attribute
{
  public BhlOpAttribute(string op) {}
}
}
