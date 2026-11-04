using System;

namespace UnityBHL
{
//NOTE: read (not compiled) by the bindings generator (Tools~/bindgen): binds the listed static methods
//      of the type as BHL functions named as the methods, e.g. [BhlBind(typeof(Script), nameof(Script.Vibrate))];
//      without names - all its public and internal static methods but the Except ones:
//      [BhlBind(typeof(HapticsBind), Except = new[] { nameof(HapticsBind.Helper) })].
//      Editor only, so the declarations never get into a player build
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true)]
public class BhlBindAttribute : Attribute
{
  public string[] Except;

  public BhlBindAttribute(Type type, params string[] names) {}
}

//NOTE: the methods of the interface are BHL functions bound to the hand written Script_<Name> classes
//      (Exec, or Init/Exec/Cleanup of a coroutine), the generator adds their Args, ReadArgs and ReturnValue
[AttributeUsage(AttributeTargets.Interface)]
public class BhlCustomAttribute : Attribute {}

//NOTE: a coroutine BHL function, or a coroutine func-typed parameter (Action, Func<..>)
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Parameter)]
public class BhlCoroutineAttribute : Attribute {}

//NOTE: the interface declares the BHL class 'name' of the native type: its properties are fields
//      ({ get; set; } tell what BHL may do), its methods are called on the native object.
//      'members' bind members read from the sources, signatures included:
//      nameof(T.Member) - a field, property or method of the native type T itself,
//      nameof(Script.<Name>_Method) - the static method taking T self first, bound as Method
[AttributeUsage(AttributeTargets.Interface)]
public class BhlClassAttribute : Attribute
{
  //NOTE: the BHL name of the parent class
  public string Parent;
  //NOTE: BHL may create the object with new(), the native type needs a parameterless constructor
  public bool New;

  public BhlClassAttribute(string name, Type type, params string[] members) {}
}

//NOTE: the native member of a [BhlClass] property or method, if it's named differently
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method)]
public class BhlNativeAttribute : Attribute
{
  public BhlNativeAttribute(string member) {}
}
}
