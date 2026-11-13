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
  //      [BhlBind]/[BhlClass]/[BhlCoroutine] declarations take effect without the console. The generator is
  //      configured by bhl_bindgen.json next to bhl.proj, without it nothing happens. It runs in-process,
  //      gamectl runs the same code as a dotnet tool (Tools~/bindgen)
  public class BindGen : AssetPostprocessor
  {
    static readonly Regex decl_attr = new Regex(@"^\s*\[(BhlBind|BhlClass|BhlInterface|BhlCoroutine)\b", RegexOptions.Multiline);

    internal const string ConfName = "bhl_bindgen.json";

    //NOTE: the project generates bindings: it has bhl_bindgen.json next to bhl.proj (the Control Panel shows its button then)
    internal static bool IsConfigured => ConfPath() != null;

    //NOTE: the Generate Bindings button of the Control Panel: unlike a change of a file, it says so when nothing has changed
    internal static void GenerateNow()
    {
      if(ConfPath() == null)
        Debug.LogError($"[BHL] bindgen: no {ConfName} next to bhl.proj");
      else if(!Generate())
        Debug.Log("[BHL] bindgen: the bindings are up to date");
    }

    //NOTE: the config and the ones it includes, as the generator loads them (a missing field or include throws)
    internal static List<Config> LoadConfigs(string path) => Config.LoadAll(path, JsonUtility.FromJson<Config>);

    //NOTE: for BindGenWatcher: the config next to bhl.proj, null when the project generates no bindings
    internal static string ConfigPath() => ConfPath();

    //NOTE: for BindGenWatcher: the files the generated bindings were made of, by the //dep: headers of the
    //      outputs; null when an output isn't made yet
    internal static HashSet<string> Deps(List<Config> confs) => ReadDeps(confs);

    static string ConfPath()
    {
      var settings = Settings.Instance;
      if(settings == null)
        return null;
      var path = Path.Combine(Path.GetDirectoryName(settings.ResolvedBhlProjPath), ConfName);
      return File.Exists(path) ? path : null;
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

      HashSet<string> deps;
      try
      {
        deps = ReadDeps(Config.LoadAll(conf_path, JsonUtility.FromJson<Config>));
      }
      catch(GenException e)
      {
        Debug.LogError("[BHL] bindgen: " + e.Message);
        return;
      }
      bool need = deps == null || changed.Any(p => deps.Contains(p) || IsDecl(p));
      if(need)
        Generate();
    }

    //NOTE: the files listed in the headers of the main output files of the configs (the one next to bhl.proj and
    //      the ones it includes), null if an output isn't made yet
    static HashSet<string> ReadDeps(List<Config> confs)
    {
      var deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach(var conf in confs)
      {
        var result = Path.Combine(conf.result_dir, Generator.MainFile);
        if(!File.Exists(result))
          return null;
        foreach(var line in File.ReadLines(result))
        {
          if(!line.StartsWith("//"))
            break;
          if(line.StartsWith("//dep: "))
            deps.Add(Path.GetFullPath(Path.Combine(conf.Dir, line.Substring(7))));
        }
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

    //NOTE: true if the bindings have changed (or the generation failed: it's said). Internal for
    //      BindGenWatcher, which regenerates without the "up to date" line GenerateNow() prints
    internal static bool Generate()
    {
      try
      {
        var confs = Config.LoadAll(ConfPath(), JsonUtility.FromJson<Config>);
        var (summary, changed) = new Generator(confs, new HandParser(confs[confs.Count - 1].defines)).Run();
        if(!changed)
          return false;
        Debug.Log("[BHL] " + summary);
        AssetDatabase.Refresh();
        return true;
      }
      catch(GenException e)
      {
        Debug.LogError("[BHL] bindgen: " + e.Message);
        return true;
      }
    }
  }
}
