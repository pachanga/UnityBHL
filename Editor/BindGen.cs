using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UnityBHL
{
  //NOTE: runs the bindings generator (BindGen/) when a file the bindings are made of changes, so
  //      [BhlBind]/[BhlClass]/[BhlCustom] declarations take effect without the console. The generator is
  //      configured by bhl_bindgen.json next to bhl.proj, without it nothing happens. It runs in-process,
  //      gamectl runs the same code as a dotnet tool (Tools~/bindgen)
  public class BindGen : AssetPostprocessor
  {
    static readonly Regex decl_attr = new Regex(@"^\s*\[(BhlBind|BhlClass|BhlCustom)\b", RegexOptions.Multiline);

    [MenuItem("BHL/Generate Bindings")]
    static void GenerateMenu()
    {
      if(ConfPath() == null)
        Debug.LogError($"[BHL] bindgen: no {ConfName} next to bhl.proj");
      else
        Generate();
    }

    const string ConfName = "bhl_bindgen.json";

    static string ConfPath()
    {
      var settings = Settings.Instance;
      if(settings == null)
        return null;
      var path = Path.Combine(Path.GetDirectoryName(settings.ResolvedBhlProjPath), ConfName);
      return File.Exists(path) ? path : null;
    }

    //NOTE: the main output file, it lists the files the output is made of
    static string ResultPath(string conf_path)
    {
      var conf = JsonUtility.FromJson<Config>(File.ReadAllText(conf_path));
      return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(conf_path), conf.result_dir, Generator.MainFile));
    }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] moved_from)
    {
      var changed = imported.Concat(deleted).Concat(moved).Concat(moved_from)
        .Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        .Select(Path.GetFullPath)
        .ToList();
      if(changed.Count == 0)
        return;

      var conf_path = ConfPath();
      if(conf_path == null)
        return;

      var result = ResultPath(conf_path);
      var deps = ReadDeps(conf_path, result);
      bool need = deps == null || changed.Any(p => deps.Contains(p) || IsDecl(p));
      if(need)
        Generate();
    }

    //NOTE: the files listed in the result's header, null if there's no result yet
    static HashSet<string> ReadDeps(string conf_path, string result)
    {
      if(!File.Exists(result))
        return null;
      var dir = Path.GetDirectoryName(conf_path);
      var deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach(var line in File.ReadLines(result))
      {
        if(!line.StartsWith("//"))
          break;
        if(line.StartsWith("//dep: "))
          deps.Add(Path.GetFullPath(Path.Combine(dir, line.Substring(7))));
      }
      return deps;
    }

    //NOTE: a new declarations file isn't listed in the result yet
    static bool IsDecl(string path)
    {
      if(!File.Exists(path))
        return false;
      return decl_attr.IsMatch(File.ReadAllText(path));
    }

    static void Generate()
    {
      try
      {
        var conf = Config.Load(ConfPath(), JsonUtility.FromJson<Config>);
        var (summary, changed) = new Generator(conf, new HandParser(conf.defines)).Run();
        if(!changed)
          return;
        Debug.Log("[BHL] " + summary);
        AssetDatabase.Refresh();
      }
      catch(GenException e)
      {
        Debug.LogError("[BHL] bindgen: " + e.Message);
      }
    }
  }
}
