using System.IO;
using bhl;
using UnityEngine;

namespace UnityBHL
{

  //NOTE: Editor-only - nothing in the Player-build-visible Runtime/ asmdef references this
  [CreateAssetMenu(fileName = "BHLSettings", menuName = "BHL/Settings")]
  public class Settings : ScriptableObject
  {
    [Tooltip("Path to bhl.proj, relative to the Unity project root (parent of Assets/). " +
             "Can point outside the project (e.g. \"../shared/BHL/bhl.proj\").")]
    public string bhlProjPath = "Assets/BHL/bhl.proj";

    [Tooltip("Where BHL/Rebuild and Bake writes baked bytecode. Must be inside a " +
             "Resources folder.")]
    public string bakedBundlePath = "Assets/Resources/bhl.bytes";

    [Tooltip("TCP port the BHL DAP debug server listens on")]
    public int debugPort = 7777;

    const string ResourceName = "BHLSettings";

    static Settings _instance;
    public static Settings Instance
    {
      get
      {
        if(_instance == null)
          _instance = Resources.Load<Settings>(ResourceName);
        return _instance;
      }
    }

    public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

    public string ResolvedBhlProjPath => Path.GetFullPath(Path.Combine(ProjectRoot, bhlProjPath));

    static IncludePath _moduleMap;

    //NOTE: module->file mapping (e.g. for a DAP client's "jump to source"), kept in
    //      sync automatically by EditorCompiler.LoadProjectConf
    public static void ConfigureModuleMap(IncludePath inc_path) => _moduleMap = inc_path;

    public static bool TryMapModuleToFile(string module, out string file)
    {
      file = _moduleMap?.TryIncludePaths(module);
      return file != null;
    }
  }

}
