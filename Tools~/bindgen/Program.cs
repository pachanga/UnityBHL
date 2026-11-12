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
//      usage: bindgen <config.json>, paths in the config are relative to it.
//      The parser is hand written (Editor/BindGen/HandParser.cs) so the generator runs inside Unity too;
//      Tools~/bindgen.Tests checks it against Roslyn, which is a dependency of those tests alone
internal static class Program
{
  public static int Main(string[] args)
  {
    if(args.Length != 1)
    {
      Console.Error.WriteLine("usage: bindgen <config.json>");
      return 2;
    }

    try
    {
      var json_opts = new JsonSerializerOptions { IncludeFields = true, ReadCommentHandling = JsonCommentHandling.Skip };
      var confs = Config.LoadAll(args[0], json => JsonSerializer.Deserialize<Config>(json, json_opts));
      //NOTE: the defines of the config itself (the last one) for all the sources
      var defines = confs[confs.Count - 1].defines;
      Console.WriteLine(new Generator(confs, new HandParser(defines)).Run().summary);
      return 0;
    }
    catch(GenException e)
    {
      Console.Error.WriteLine(e.Message);
      return 1;
    }
  }
}
