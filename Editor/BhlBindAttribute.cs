using System;

namespace UnityBHL
{
//NOTE: read (not compiled) by the bindings generator (Tools~/bindgen): binds the listed static methods
//      of the type as BHL functions named as the methods, e.g. [BhlBind(typeof(Script), nameof(Script.Vibrate))].
//      Editor only, so the declarations never get into a player build
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class BhlBindAttribute : Attribute
{
  public BhlBindAttribute(Type type, params string[] names) {}
}
}
