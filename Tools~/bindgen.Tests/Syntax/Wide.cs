using System;
using System.Collections;
using System.Collections.Generic;
using UnityBHL;
using UnityEngine;
using bhl;
using Alias = Fixtures.Src.Unit;

//NOTE: fed straight to the parsers, not to the generator: the two only have to agree on the model, so this
//      may hold shapes the generator itself rejects (arrays, tuples, nullables) and shapes nobody writes by
//      accident. A difference in what they throw is caught too - BothParser dumps an error as the model
namespace Wide.Syntax
{

[BhlBind(typeof(Thing), nameof(Thing.A), nameof(Thing.B))]
interface ThingBinds
{}

//NOTE: attributes stacked, with named arguments in both orders and a trailing comma in the array
[BhlBind(typeof(Other), Except = new[] { nameof(Other.Skip), nameof(Other.Skip2), })]
[BhlBind(typeof(Third))]
interface Stacked
{}

//NOTE: the declaration broken across lines, comments in the middle of it
[BhlClass(
  typeof(Thing),          // the native type
  nameof(Thing.A),        /* a member */
  nameof(Thing.B))]
interface Multiline
{
  /* a property with the accessors on separate lines */
  int Value
  {
    get;
    set;
  }

  //NOTE: default values of every shape the parser may meet
  void Defaults(int a = -1, float b = 1e-3f, double c = .5, string d = "a\"b", char e = '\'',
    bool f = false, object g = null, M3 h = M3.None, int i = default, long j = 1_000L, uint k = 0xFFu);

  //NOTE: generics nested several deep, and a tuple
  IList<IList<int>> Nested(Dictionary<string, List<int>> map);
  (int a, string b) Tuple((float x, float y) p);

  //NOTE: arrays and nullables: the generator says no, the parsers still have to read them the same
  int[] Arr(int[,] grid, int[][] jagged, int? maybe, string? s2);

  //NOTE: a verbatim and an interpolated string with braces inside, which must not confuse brace counting
  [BhlDefault(@"new Thing { A = 1 }")] void Verbatim(Thing t);
  [BhlDefault($"{nameof(Thing)}.A")] void Interpolated(Thing t);
}

//NOTE: a generic method with constraints
[BhlClass(typeof(Thing))]
interface Generic
{
  T Pick<T>(IList<T> items) where T : class, new();
}

//NOTE: nested namespaces and types, same names at different depths
namespace Inner
{
  [BhlInterface(typeof(IThing))]
  interface IThing
  {
    void Do();
  }
}

enum M3 : byte
{
  None,
  //NOTE: an explicit value, a shifted one and a trailing comma
  First = 1,
  Shifted = 1 << 3,
  Last = Shifted,
}

static class Thing
{
  public static int A;
  public static string B { get; set; }
}

static class Other
{
  public static void Skip() {}
  public static void Skip2() {}
  public static void Kept() {}
}

static class Third
{
  public static void X() {}
}

//NOTE: conditional compilation nested in a type, and an #else the other parser must skip identically
static class Conditional
{
#if FIXTURE_DEFINE
  [BhlBind]
  public static void Yes() {}
  #if NESTED_DEFINE
  [BhlBind]
  public static void DeepYes() {}
  #endif
#else
  [BhlBind]
  public static void No() {}
#endif
}

}
