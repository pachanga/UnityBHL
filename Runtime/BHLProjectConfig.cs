using System.Collections.Generic;
using bhl;

namespace UnityBHL
{

  //NOTE: a resolved bhl.proj, as plain data - kept minimal (just what TryMapModuleToFile
  //      needs, plus the raw dirs/defines for BC consumers) rather than exposing the
  //      full bhl.ProjectConf, which can't be referenced outside UNITY_EDITOR/BHL_PARSER
  //      anyway (and wouldn't be NO_UNITY-safe if it were held onto here). Field names
  //      match the old BitGames.Scripting.BHLProjectConf DTO so that consumer can return
  //      this instance as-is instead of mapping onto a separate shape
  public class BHLProjectConfig
  {
    public readonly IncludePath IncludePath;
    public readonly List<string> inc_dirs;
    public readonly List<string> src_dirs;
    public readonly List<string> defines;
    public readonly string result_file;

    public BHLProjectConfig(
      IncludePath incPath,
      List<string> incDirs = null,
      List<string> srcDirs = null,
      List<string> defines = null,
      string resultFile = "")
    {
      IncludePath = incPath;
      inc_dirs = incDirs ?? new List<string>();
      src_dirs = srcDirs ?? new List<string>();
      this.defines = defines ?? new List<string>();
      result_file = resultFile ?? "";
    }

    public bool TryMapModuleToFile(string module, out string file)
    {
      file = IncludePath?.TryIncludePaths(module);
      return file != null;
    }
  }

}
