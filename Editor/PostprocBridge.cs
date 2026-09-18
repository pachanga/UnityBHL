using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using bhl;

namespace UnityBHL
{

  //NOTE: mirrors proj.postproc_sources' .cs files into a generated, Editor-only Unity
  //      asmdef so Unity compiles them itself - only then do their IFrontPostProcessor
  //      implementations share type identity with bhl's own compiled types. A prebuilt
  //      postproc_dll loaded via reflection can never satisfy that, since Unity compiles
  //      "bhl" from source, not from an external assembly. The generated asmdef is named
  //      after postproc_dll, so bhl's AppDomainPostProcessor can find this exact assembly
  //      by name among already-loaded ones, instead of scanning all of them
  public static class PostprocBridge
  {
    const string GeneratedDir = "Assets/BHL/Generated/Postproc";

    public static void Sync(ProjectConf proj)
    {
      var sources = proj.postproc_sources.Where(f => f.EndsWith(".cs")).ToList();
      var asmName = Path.GetFileNameWithoutExtension(proj.postproc_dll);

      if(sources.Count == 0 || string.IsNullOrEmpty(asmName))
      {
        if(Directory.Exists(GeneratedDir))
        {
          Debug.Log("[BHL] postproc: no postproc_sources/postproc_dll configured - removing generated asmdef");
          Directory.Delete(GeneratedDir, recursive: true);
          if(File.Exists(GeneratedDir + ".meta"))
            File.Delete(GeneratedDir + ".meta");
          AssetDatabase.Refresh();
        }
        return;
      }

      Directory.CreateDirectory(GeneratedDir);

      var changed = false;
      var wanted = new HashSet<string>();

      foreach(var src in sources)
      {
        var file_name = Path.GetFileName(src);
        wanted.Add(file_name);

        var dst = Path.Combine(GeneratedDir, file_name);
        var content = File.ReadAllText(src);
        if(!File.Exists(dst) || File.ReadAllText(dst) != content)
        {
          File.WriteAllText(dst, content);
          changed = true;
        }
      }

      foreach(var existing in Directory.GetFiles(GeneratedDir, "*.cs"))
      {
        if(wanted.Contains(Path.GetFileName(existing)))
          continue;

        File.Delete(existing);
        if(File.Exists(existing + ".meta"))
          File.Delete(existing + ".meta");
        changed = true;
      }

      //NOTE: named after postproc_dll (not a fixed name), and any stale asmdef left
      //      over from a previous, differently-named postproc_dll is removed - only one
      //      is ever expected to exist here
      var asmdefPath = Path.Combine(GeneratedDir, asmName + ".asmdef");
      foreach(var existing in Directory.GetFiles(GeneratedDir, "*.asmdef"))
      {
        if(existing == asmdefPath)
          continue;

        File.Delete(existing);
        if(File.Exists(existing + ".meta"))
          File.Delete(existing + ".meta");
        changed = true;
      }

      if(!File.Exists(asmdefPath))
      {
        File.WriteAllText(asmdefPath, MakeAsmdefContent(asmName));
        changed = true;
      }

      //NOTE: a .asmdef has no JSON field for injecting a custom #define - csc.rsp is
      //      Unity's own mechanism for extra per-folder compiler arguments. BHL_POSTPROC
      //      lets postproc_sources' own code tell "am I compiled as part of a postproc
      //      build" apart from "am I outside Unity" - the same symbol is also defined
      //      for the CLI-built postproc_dll (see bhl's BuildPostprocDll)
      var rspPath = Path.Combine(GeneratedDir, "csc.rsp");
      const string rspContent = "-define:BHL_POSTPROC";
      if(!File.Exists(rspPath) || File.ReadAllText(rspPath) != rspContent)
      {
        File.WriteAllText(rspPath, rspContent);
        changed = true;
      }

      if(changed)
      {
        Debug.Log($"[BHL] postproc: synced {sources.Count} source(s) into '{asmdefPath}' - " +
                  "Unity needs a script recompile before AppDomainPostProcessor can see it");
        AssetDatabase.Refresh();
      }
    }

    static string MakeAsmdefContent(string name) =>
$@"{{
  ""name"": ""{name}"",
  ""references"": [""bhl""],
  ""includePlatforms"": [""Editor""]
}}";
  }

}
