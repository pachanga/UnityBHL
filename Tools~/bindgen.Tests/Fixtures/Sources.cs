using System;
using System.Collections;
using System.Collections.Generic;
using UnityBHL;
using UnityEngine;

//NOTE: the sources the declarations above refer to: the bound types with their members, the [BhlBind] methods
//      and the enums. Parsed in the second pass (ParseSource), not compiled by anything - only the shapes matter
namespace Fixtures.Src
{

enum M3Pos
{
  None = 0,
  First = 1,
  Last = 2,
}

[Flags]
enum Mask : byte
{
  A = 1 << 0,
  B = 1 << 1,
}

static class Script
{
  public static void Plain(int a, string b) {}

  public static int WithDefaults(int a, float b = 1.5f, string c = "x", bool d = true, M3Pos e = M3Pos.None) => a;

  public static void Renamed() {}

  //NOTE: bound by the attribute on the method itself, with a BHL name
  [BhlBind("vibrate")]
  public static void Vibrate(float power) {}

  //NOTE: 'out' is only for a [BhlCoroutine] Exec, a bound function returns a tuple instead
  [BhlBind]
  public static bool TryThing(int input) => true;

  //NOTE: lists and 'any': IList<T> for a parameter, RefcList<T> for a return, Val is any
  [BhlBind]
  public static RefcList<int> Numbers(IList<int> xs, IList<string> names, Val any) => null;

  //NOTE: a func parameter and a coroutine func one
  [BhlBind]
  public static void Each(Action<int> step, [BhlCoroutine] Func<int, bool> body) {}

  //NOTE: the static method of a [BhlClass]: takes the native type first, bound as Length
  public static float Vec_Length(Vec self) => 0f;
}

static class AllBinds
{
  public static void One() {}
  internal static void Two(int a) {}
  public static void Skipped() {}
  //NOTE: not bound: not static
  public void Instance() {}
  //NOTE: not bound: private
  static void Hidden() {}
}

class Unit
{
  public int Hp;
  public int Level { get; }
  public string Name { get; set; }
  public float Speed { set {} }

  public void Move(float x, float y) {}
  public void Stop() {}
  public int Damage(int amount, float scale) => 0;
  public void InternalHeal(int amount) {}
  public static Unit Spawn(string kind) => null;
  public static bool operator ==(Unit a, Unit b) => true;
  public static bool operator !=(Unit a, Unit b) => false;
  public IList Items() => null;
  public void Aim(VM.FuncPtr onDone) {}
  public void Place(M3Pos pos) {}
}

class Bag
{
  public Bag() {}
  public int Count { get; }
}

struct Vec
{
  public float X { get; set; }
  public float Y { get; set; }
}

struct Pod
{
  public int A { get; set; }
}

interface IWidget
{
  void Refresh();
  void SetVisible(bool on);
}

interface INamedWidget : IWidget
{
  string Title();
}

//NOTE: the same type name nested elsewhere, to keep the parsers honest about scope
static class Outer
{
  public class Unit
  {
    public int Hp;
  }
}

#if FIXTURE_DEFINE
static class OnlyWithDefine
{
  [BhlBind]
  public static void Defined() {}
}
#else
static class WithoutDefine
{
  [BhlBind]
  public static void NotDefined() {}
}
#endif

}
