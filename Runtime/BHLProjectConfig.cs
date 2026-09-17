using bhl;

namespace UnityBHL
{

  //NOTE: a resolved bhl.proj's module->file mapping - kept minimal (just what
  //      TryMapModuleToFile needs) rather than exposing the full bhl.ProjectConf, which
  //      can't be referenced outside UNITY_EDITOR/BHL_PARSER anyway
  public class BHLProjectConfig
  {
    public readonly IncludePath IncludePath;

    public BHLProjectConfig(IncludePath incPath)
    {
      IncludePath = incPath;
    }

    public bool TryMapModuleToFile(string module, out string file)
    {
      file = IncludePath?.TryIncludePaths(module);
      return file != null;
    }
  }

}
