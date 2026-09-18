using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using bhl;
#endif

namespace UnityBHL
{

  //NOTE: a plain name/value pair, not a Dictionary - Unity can't serialize/draw
  //      Dictionary fields in the Inspector
  [Serializable]
  public class EnvVarEntry
  {
    public string name = "";
    public string value = "";
  }

  //NOTE: BhlProj parses lazily on first access (Editor-only - bhl.ProjectConf itself
  //      can't be used outside UNITY_EDITOR/BHL_PARSER), so it's available even if
  //      nothing has triggered a compile yet. EditorCompiler.LoadProjectConf still
  //      overwrites it after every real compile, to stay fresh without re-parsing here.
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

    [Tooltip("Environment variables set before every Editor compile, for postproc_sources " +
             "code that reads them (e.g. a CLI/CI build's own GAME_ROOT-style env vars, " +
             "otherwise only set externally for that build, never inside the Editor). " +
             "A value can reference $(DATA_PATH), replaced with Application.dataPath.")]
    public List<EnvVarEntry> postprocEnvVars = new List<EnvVarEntry>();

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

#if UNITY_EDITOR
    BHLProjectConfig _bhlProj;
    public BHLProjectConfig BhlProj
    {
      get
      {
        if(_bhlProj == null)
          _bhlProj = TryParseBhlProj();
        return _bhlProj;
      }
      set => _bhlProj = value;
    }

    BHLProjectConfig TryParseBhlProj()
    {
      try
      {
        if(!File.Exists(ResolvedBhlProjPath))
          return null;

        var proj = ProjectConf.ReadFromFile(ResolvedBhlProjPath);
        return new BHLProjectConfig(proj.inc_path, proj.inc_dirs, proj.src_dirs, proj.defines, proj.result_file);
      }
      catch(Exception)
      {
        return null;
      }
    }
#else
    public BHLProjectConfig BhlProj { get => null; set {} }
#endif
  }

}
