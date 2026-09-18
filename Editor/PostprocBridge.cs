using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using bhl;

namespace UnityBHL
{

  //NOTE: mirrors proj.postproc_sources' .cs files into a generated, Editor-only Unity
  //      asmdef so Unity compiles them itself - only then do their IFrontPostProcessor
  //      implementations share type identity with bhl's own compiled types (see
  //      AppDomainPostProcessor in bhl). A prebuilt postproc_dll loaded via reflection
  //      can never satisfy that, since Unity compiles "bhl" from source, not from an
  //      external assembly
  public static class PostprocBridge
  {
    const string GeneratedDir = "Assets/BHL/Generated/Postproc";
    const string AsmdefPath = GeneratedDir + "/BHL.Postproc.Generated.asmdef";

    const string AsmdefContent =
@"{
  ""name"": ""BHL.Postproc.Generated"",
  ""references"": [""bhl""],
  ""includePlatforms"": [""Editor""]
}";

    public static void Sync(ProjectConf proj)
    {
      var sources = proj.postproc_sources.Where(f => f.EndsWith(".cs")).ToList();

      if(sources.Count == 0)
      {
        if(Directory.Exists(GeneratedDir))
        {
          Directory.Delete(GeneratedDir, recursive: true);
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

      if(!File.Exists(AsmdefPath))
      {
        File.WriteAllText(AsmdefPath, AsmdefContent);
        changed = true;
      }

      if(changed)
        AssetDatabase.Refresh();
    }
  }

}
