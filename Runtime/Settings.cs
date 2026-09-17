using System.IO;
using UnityEngine;

namespace UnityBHL
{

  //NOTE: BhlProj is only ever populated by EditorCompiler (Editor-only) - in an actual
  //      Player build nothing configures it, so it stays null there
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

    //NOTE: pushed in by EditorCompiler.LoadProjectConf, kept in sync with the resolved bhl.proj
    public BHLProjectConfig BhlProj { get; set; }
  }

}
