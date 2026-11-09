using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

//NOTE: generates the BHL bindings registration .cs from C# sources, parsing only (nothing is compiled):
//      [BhlBind(typeof(T), nameof(T.M), ...)] on a type in the sources binds static methods of T found in them,
//      [BhlBind] on a static method in the sources binds it.
//      The output is the canonical bindings format the BHL compiler reads (bhl docs/csharp-bindings.md).
//      usage: bindgen [--parser=hand|roslyn|both] <config.json>, paths in the config are relative to it
internal static class Program
{
  public static int Main(string[] args)
  {
    bool has_opt = args.Length == 2 && args[0].StartsWith("--parser=");
    var parser_name = has_opt ? args[0].Substring(9) : "hand";
    if(args.Length != (has_opt ? 2 : 1) || !new[] { "hand", "roslyn", "both" }.Contains(parser_name))
    {
      Console.Error.WriteLine("usage: bindgen [--parser=hand|roslyn|both] <config.json>");
      return 2;
    }

    try
    {
      var json_opts = new JsonSerializerOptions { IncludeFields = true, ReadCommentHandling = JsonCommentHandling.Skip };
      var confs = Config.LoadAll(args[args.Length - 1], json => JsonSerializer.Deserialize<Config>(json, json_opts));
      //NOTE: the defines of the config itself (the last one) for all the sources
      var defines = confs[confs.Count - 1].defines;
      ICsParser parser = parser_name == "roslyn" ? new RoslynParser(defines) :
        parser_name == "both" ? new BothParser(new HandParser(defines), new RoslynParser(defines)) :
        new HandParser(defines);
      Console.WriteLine(new Generator(confs, parser).Run().summary);
      return 0;
    }
    catch(GenException e)
    {
      Console.Error.WriteLine(e.Message);
      return 1;
    }
  }
}

//NOTE: --parser=both checks the hand parser against Roslyn: every file is parsed by both, any difference
//      of the models is an error; the hand parser's model is used
class BothParser : ICsParser
{
  readonly ICsParser hand;
  readonly ICsParser roslyn;

  public BothParser(ICsParser hand, ICsParser roslyn)
  {
    this.hand = hand;
    this.roslyn = roslyn;
  }

  public void ParseDecls(string file, string src, CsDecls decls)
  {
    Check(file, d => hand.ParseDecls(file, src, d), d => roslyn.ParseDecls(file, src, d));
    hand.ParseDecls(file, src, decls);
  }

  public void ParseSource(string file, string src, HashSet<string> bound_types, CsDecls decls)
  {
    Check(file, d => hand.ParseSource(file, src, bound_types, d), d => roslyn.ParseSource(file, src, bound_types, d));
    hand.ParseSource(file, src, bound_types, decls);
  }

  static void Check(string file, Action<CsDecls> hand, Action<CsDecls> roslyn)
  {
    var a = Dump(hand).Split('\n');
    var b = Dump(roslyn).Split('\n');
    int diff = Enumerable.Range(0, Math.Min(a.Length, b.Length)).FirstOrDefault(k => a[k] != b[k], -1);
    if(diff < 0 && a.Length == b.Length)
      return;
    if(diff < 0)
      diff = Math.Min(a.Length, b.Length);
    int from = Math.Max(0, diff - 5);
    throw new GenException($"{file}: the hand parser differs from Roslyn at line {diff + 1} of the model dump\n" +
      "--- hand\n" + string.Join("\n", a.Skip(from).Take(10)) + "\n--- roslyn\n" + string.Join("\n", b.Skip(from).Take(10)));
  }

  static string Dump(Action<CsDecls> parse)
  {
    var decls = new CsDecls();
    try
    {
      parse(decls);
    }
    catch(GenException e)
    {
      return "error: " + e.Message;
    }
    var sb = new StringBuilder();
    Dump(sb, "", decls);
    return sb.ToString();
  }

  static void Dump(StringBuilder sb, string indent, object o)
  {
    if(o == null || o is string || o.GetType().IsValueType)
      sb.Append(indent).Append(o is string ? $"\"{o}\"" : o ?? "null").Append('\n');
    else if(o is IDictionary dict)
    {
      foreach(var k in dict.Keys.Cast<object>().OrderBy(k => k.ToString(), StringComparer.Ordinal))
      {
        sb.Append(indent).Append(k).Append(":\n");
        Dump(sb, indent + "  ", dict[k]);
      }
    }
    else if(o is IEnumerable items)
    {
      foreach(var item in o is HashSet<string> set ? set.OrderBy(s => s, StringComparer.Ordinal) : items.Cast<object>())
        Dump(sb, indent + "- ", item);
    }
    else
    {
      foreach(var f in o.GetType().GetFields())
      {
        sb.Append(indent).Append(f.Name).Append(":\n");
        Dump(sb, indent + "  ", f.GetValue(o));
      }
    }
  }
}
