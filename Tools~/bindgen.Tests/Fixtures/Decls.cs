using System;
using System.Collections;
using System.Collections.Generic;
using UnityBHL;
using UnityEngine;
using bhl;

//NOTE: a declarations file of the fixtures (BhlClass/BhlInterface/BhlCoroutine/BhlBind(typeof) make it one):
//      the shapes the generator supports, including the ones a real project doesn't happen to use - the whole
//      point of checking the hand parser against Roslyn is the corners nobody writes by accident
namespace Fixtures.Decls
{

//NOTE: binds the listed static methods of a type of the sources
[BhlBind(typeof(Fixtures.Src.Script), nameof(Fixtures.Src.Script.Plain), nameof(Fixtures.Src.Script.WithDefaults))]
interface ScriptBinds
{}

//NOTE: all the public and internal static methods but the Except ones
[BhlBind(typeof(Fixtures.Src.AllBinds), Except = new[] { nameof(Fixtures.Src.AllBinds.Skipped) })]
interface AllBinds
{}

//NOTE: a renamed function, and one in a BHL namespace
[BhlBind(typeof(Fixtures.Src.Script), nameof(Fixtures.Src.Script.Renamed))]
interface RenamedBinds
{}

//NOTE: a class: properties are fields (get/set tell what BHL may do), methods are called on the object
[BhlClass(typeof(Fixtures.Src.Unit), nameof(Fixtures.Src.Unit.Hp), nameof(Fixtures.Src.Unit.Move))]
interface Unit
{
  int Level { get; }
  string Name { get; set; }
  float Speed { set; }

  void Stop();
  int Damage(int amount, float scale);
  [BhlNative("InternalHeal")] void Heal(int amount);
  [BhlStatic] Fixtures.Src.Unit Spawn(string kind);
  [BhlOp("==")] bool Equals(Fixtures.Src.Unit a, Fixtures.Src.Unit b);
  [return: BhlType("IList")] IList Items();
  //NOTE: [BhlType(typeof(T))] names the BHL func type of a VM.FuncPtr parameter
  void Aim([BhlType(typeof(Action<int>))] VM.FuncPtr onDone);
  void Place([BhlDefault("M3Pos.None")] Fixtures.Src.M3Pos pos);
}

//NOTE: New, Parent and Implements
[BhlClass(typeof(Fixtures.Src.Bag), New = true, Parent = "Container", Implements = new[] { "IStorage" })]
interface Bag
{
  int Count { get; }
}

//NOTE: a named class, the interface doesn't hide the native type
[BhlClass("Vec", typeof(Fixtures.Src.Vec), nameof(Fixtures.Src.Script.Vec_Length))]
interface VecDecl
{
  float X { get; set; }
  float Y { get; set; }
}

//NOTE: a value type kept boxed
[BhlClass(typeof(Fixtures.Src.Pod), Pod = true)]
interface Pod
{
  int A { get; set; }
}

//NOTE: an interface of a native interface
[BhlInterface(typeof(Fixtures.Src.IWidget))]
//NOTE: a [BhlInterface] declares methods only (and Inherits), unlike a [BhlClass]
interface IWidget
{
  void Refresh();
  void SetVisible(bool on);
}

[BhlInterface("INamedWidget", typeof(Fixtures.Src.INamedWidget), Inherits = new[] { "IWidget" })]
interface INamedWidget
{
  string Title();
}

}
