using System;
using System.Collections.Generic;
using System.IO;

//NOTE: the files the outputs were generated from, read back from the '//dep:' lines the generator
//      writes into its main file - the config lists globs and directories, the result lists what
//      they actually resolved to. Shared by the Unity watcher and by `bindgen --watch`
internal static class BindGenDeps
{
  //NOTE: null when a config has no result yet: nothing to watch until the first generation
  public static HashSet<string> Read(List<Config> confs)
  {
    var deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach(var conf in confs)
    {
      var result = Path.Combine(conf.result_dir, Generator.MainFile);
      if(!File.Exists(result))
        return null;

      foreach(var line in File.ReadLines(result))
      {
        //NOTE: the header is the first lines of the file, nothing below it is a dep
        if(!line.StartsWith("//"))
          break;
        if(line.StartsWith("//dep: "))
          deps.Add(Path.GetFullPath(Path.Combine(conf.Dir, line.Substring(7))));
      }
    }
    return deps;
  }
}
