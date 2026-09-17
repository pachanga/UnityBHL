using System.Collections.Generic;
using bhl;

namespace UnityBHL
{

  //NOTE: a resolved bhl.proj, as plain data - kept minimal (just what TryMapModuleToFile
  //      needs, plus the raw dirs/defines for BC consumers) rather than exposing the
  //      full bhl.ProjectConf, which can't be referenced outside UNITY_EDITOR/BHL_PARSER
  //      anyway (and wouldn't be NO_UNITY-safe if it were held onto here)
  public class BHLProjectConfig
  {
    public readonly IncludePath IncludePath;
    public readonly List<string> IncDirs;
    public readonly List<string> SrcDirs;
    public readonly List<string> Defines;
    public readonly string ResultFile;

    public BHLProjectConfig(
      IncludePath incPath,
      List<string> incDirs = null,
      List<string> srcDirs = null,
      List<string> defines = null,
      string resultFile = "")
    {
      IncludePath = incPath;
      IncDirs = incDirs ?? new List<string>();
      SrcDirs = srcDirs ?? new List<string>();
      Defines = defines ?? new List<string>();
      ResultFile = resultFile ?? "";
    }

    public bool TryMapModuleToFile(string module, out string file)
    {
      file = IncludePath?.TryIncludePaths(module);
      return file != null;
    }
  }

}
