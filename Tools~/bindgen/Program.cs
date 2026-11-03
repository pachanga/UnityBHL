using System;
using System.IO;
using System.Linq;

//NOTE: generates the BHL bindings registration .cs from C# sources, parsing only (nothing is compiled):
//      [BhlBind(typeof(T), nameof(T.M), ...)] in the decls files binds static methods of T found in the sources.
//      The output is the canonical bindings format the BHL compiler reads (bhl docs/csharp-bindings.md).
//      usage: bindgen <config.json>, paths in the config are relative to it
public static class Program
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
      var conf = Config.Load(args[0]);
      var gen = new Generator(conf, new RoslynParser(conf.Defines));
      gen.Collect();
      var files = gen.Emit();

      Directory.CreateDirectory(conf.ResultDir);
      int written = files.Count(kv => WriteIfChanged(Path.Combine(conf.ResultDir, kv.Key), kv.Value));
      //NOTE: only the generated files, recognized by the header, anything else in the dir is left as is
      int deleted = 0;
      foreach(var file in Directory.GetFiles(conf.ResultDir, "*.cs"))
      {
        if(files.ContainsKey(Path.GetFileName(file)) || File.ReadLines(file).FirstOrDefault() != Generator.Header)
          continue;
        File.Delete(file);
        ++deleted;
      }
      Console.WriteLine($"bindgen: {gen.FuncsCount} funcs, {gen.ClassesCount} classes -> {conf.ResultDir} ({files.Count} files, {written} written, {deleted} deleted)");
      return 0;
    }
    catch(GenException e)
    {
      Console.Error.WriteLine(e.Message);
      return 1;
    }
  }

  //NOTE: an unchanged output isn't touched, so neither Unity nor the BHL compiler rebuild anything
  static bool WriteIfChanged(string path, string content)
  {
    if(File.Exists(path) && File.ReadAllText(path) == content)
      return false;
    File.WriteAllText(path, content);
    return true;
  }
}
