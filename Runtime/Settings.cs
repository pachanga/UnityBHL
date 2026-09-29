using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using bhl;
using UnityEditor;
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
    [Tooltip("Path to bhl.proj, relative to the project root. Can point outside the project.")]
    public string bhlProjPath = "Assets/BHL/bhl.proj";

    [Tooltip("Where baked, Player-facing bytecode is written. Must be inside a Resources " +
             "folder; auto-loaded from there on device. Unrelated to bhl.proj's own " +
             "result_file, which the Editor never uses.")]
    public string bakedBundlePath = "Assets/Resources/bhl.bytes";

    [Tooltip("TCP port the BHL DAP debug server listens on")]
    public int debugPort = 7777;

    [Tooltip("Environment variables set before every Editor compile, for postproc_sources " +
             "code. Supports $(DATA_PATH), expanded to Application.dataPath.")]
    public List<EnvVarEntry> postprocEnvVars = new List<EnvVarEntry>();

    [Tooltip("Where the generated postproc asmdef is placed. Must be under Assets/.")]
    public string postprocAsmdefDir = "Assets/BHL/Generated/Postproc";

    [Tooltip("Compile when entering Play Mode. Off reuses whatever bytecode is already " +
             "loaded instead. On by default.")]
    public bool recompileOnPlay = true;

    [Tooltip("Migrate already-running ScriptBHL instances on Recompile/Force Recompile, " +
             "instead of just swapping bytecode for future loads. Off by default.")]
    public bool hotReloadOnRecompile = false;

    [Tooltip("Print the compiler's log lines to Unity's console during a compile. Off by " +
             "default; doesn't affect progress bars either way.")]
    public int logVerbosity = 0;

    [Tooltip("Recompile on .bhl file changes while not in Play Mode. Off by default.")]
    public bool recompileOnFileChanges = false;

    const string ResourceName = "BHLSettings";

    static Settings _instance;
    public static Settings Instance
    {
      get
      {
#if UNITY_EDITOR
        if(IsOverriddenLocally)
          return LocalOverride;
#endif
        if(_instance == null)
          _instance = Resources.Load<Settings>(ResourceName);
        return _instance;
      }
    }

#if UNITY_EDITOR
    //NOTE: EditorPrefs is global across every Unity project on the machine, not just
    //      this one - namespaced by the project's own path so one project's override
    //      can't leak into another's
    static string OverrideKey => "UnityBHL.LocalOverride." + Application.dataPath.GetHashCode();

    public static bool IsOverriddenLocally
    {
      get => EditorPrefs.HasKey(OverrideKey);
      set
      {
        if(value == IsOverriddenLocally)
          return;

        if(value)
        {
          //NOTE: seed the override from whatever the shared asset currently says, so
          //      overriding starts as a plain copy rather than Settings' own field
          //      defaults - _localOverride is created fresh here, not reused, in case a
          //      stale in-memory copy from a previous override session is lying around
          var shared = Resources.Load<Settings>(ResourceName);
          _localOverride = CreateInstance<Settings>();
          if(shared != null)
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(shared), _localOverride);
          SaveLocalOverride();
        }
        else
        {
          EditorPrefs.DeleteKey(OverrideKey);
          _localOverride = null;
        }
      }
    }

    static Settings _localOverride;
    static Settings LocalOverride
    {
      get
      {
        if(_localOverride == null)
        {
          _localOverride = CreateInstance<Settings>();
          var json = EditorPrefs.GetString(OverrideKey, "");
          if(!string.IsNullOrEmpty(json))
            JsonUtility.FromJsonOverwrite(json, _localOverride);
        }
        return _localOverride;
      }
    }

    //NOTE: called by SettingsInspector after every edit, while IsOverriddenLocally -
    //      persists to EditorPrefs (survives domain reloads/Editor restarts) instead of
    //      AssetDatabase.SaveAssetIfDirty, since this Settings instance isn't a real asset
    public static void SaveLocalOverride()
    {
      EditorPrefs.SetString(OverrideKey, JsonUtility.ToJson(_localOverride));
    }
#endif

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
