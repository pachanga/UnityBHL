using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using bhl;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace UnityBHL
{

  public class ControlPanel : EditorWindow
  {
    Vector2 _scroll;
    bool _showSettings = true;
    Editor _settingsEditor;

    static Task<byte[]> _pendingCompile;
    static int _compileStep;

    [MenuItem("BHL/Control Panel", priority = 1)]
    static void Open() => GetWindow<ControlPanel>("BHL Control Panel");

    void OnInspectorUpdate() => Repaint();

    public static void RepaintIfOpen()
    {
      if(HasOpenInstances<ControlPanel>())
        GetWindow<ControlPanel>("BHL Control Panel", focus: false).Repaint();
    }

    void OnDisable()
    {
      if(_settingsEditor != null)
        DestroyImmediate(_settingsEditor);
    }

    void OnGUI()
    {
      _scroll = EditorGUILayout.BeginScrollView(_scroll);

      DrawDebugStatus();
      EditorGUILayout.Space();

      DrawSettings();
      EditorGUILayout.Space();

      EditorGUILayout.BeginHorizontal();

      using(new EditorGUI.DisabledScope(_pendingCompile != null))
      {
        var recompileContent = new GUIContent(
          _pendingCompile != null ? "Compiling..." : "Recompile",
          "Incremental compile - a no-op (\"BHL no stale files detected\") if nothing in " +
          "src_dirs/bhl.proj/self changed since the last successful compile. Unchanged " +
          "individual files are also served from cache, skipping postproc for them.");
        if(GUILayout.Button(recompileContent))
          Recompile();

        var forceContent = new GUIContent(
          _pendingCompile != null ? "Compiling..." : "Force Recompile",
          "Bypasses both cache checks above (proj.use_cache = false) - every file goes " +
          "through the full pipeline, postproc included, regardless of what changed.");
        if(GUILayout.Button(forceContent, GUILayout.Width(120)))
          Recompile(force: true);
      }

      EditorGUILayout.EndHorizontal();

      DrawErrors();

      EditorGUILayout.EndScrollView();

      DrawVersionFooter();
    }

    //NOTE: outside the scroll view so it stays pinned at the bottom regardless of
    //      scroll position. FindForAssembly reflects however the package was actually
    //      resolved (registry/local/git - and for git, the resolved tag's version if
    //      it looked like a semver, see UPM's git-dependency version resolution)
    static void DrawVersionFooter()
    {
      var version = PackageInfo.FindForAssembly(typeof(ControlPanel).Assembly)?.version;
      if(string.IsNullOrEmpty(version))
        return;

      EditorGUILayout.LabelField($"UnityBHL v{version}", EditorStyles.centeredGreyMiniLabel);
    }

    //NOTE: same per-error format as BHLErrorWindow (shared via DrawErrorsList), so an
    //      open Control Panel shows errors identically to the popup
    void DrawErrors()
    {
      if(EditorCompiler.LastErrors.Count == 0)
        return;

      EditorGUILayout.Space();
      GUILayout.Label($"=== Errors ({EditorCompiler.LastErrors.Count}) ===");
      BHLErrorWindow.DrawErrorsList();
    }

    void DrawSettings()
    {
      var settings = Settings.Instance;

      if(settings == null)
      {
        EditorGUILayout.HelpBox("No BHL Settings asset found.", MessageType.Warning);
        if(GUILayout.Button("Create Settings"))
          CreateSettings();
        return;
      }

      _showSettings = EditorGUILayout.Foldout(_showSettings, "Settings", true);
      if(!_showSettings)
        return;

      if(_settingsEditor == null || _settingsEditor.target != settings)
        _settingsEditor = Editor.CreateEditor(settings);

      EditorGUILayout.BeginVertical(EditorStyles.helpBox);

      //NOTE: interleaved with SettingsInspector's own drawing (rather than just calling
      //      its plain OnInspectorGUI()) so this toggle lands below Recompile On Play
      //      but above Result Path, as requested
      var settingsInspector = (SettingsInspector)_settingsEditor;
      settingsInspector.DrawMainFields();

      EditorGUILayout.Space();

      bool auto_compile = EditorGUILayout.Toggle(
        new GUIContent("Recompile On File Changes", "Watches bhl.proj's .bhl files and recompiles on change while not in Play Mode"),
        AutoCompileController.Enabled);
      if(auto_compile != AutoCompileController.Enabled)
        AutoCompileController.Enabled = auto_compile;

      EditorGUILayout.Space();

      settingsInspector.DrawResultPath();

      EditorGUILayout.EndVertical();
    }

    //NOTE: must live in Resources for Settings.Instance's Resources.Load(ResourceName) to find it
    static void CreateSettings()
    {
      const string dir = "Assets/Resources";
      if(!AssetDatabase.IsValidFolder(dir))
        AssetDatabase.CreateFolder("Assets", "Resources");

      var settings = ScriptableObject.CreateInstance<Settings>();
      AssetDatabase.CreateAsset(settings, dir + "/BHLSettings.asset");
      AssetDatabase.SaveAssets();

      Selection.activeObject = settings;
    }

    void DrawDebugStatus()
    {
      bool debug_mode = EditorGUILayout.Toggle("Debug Mode", DebugServerController.Enabled);
      if(debug_mode != DebugServerController.Enabled)
        DebugServerController.Enabled = debug_mode;

      if(!DebugServerController.Enabled)
        return;

      var prev = GUI.color;
      GUI.color = !DebugServerController.IsRunning ? Color.gray
                : !DebugServerController.IsConnected ? Color.yellow
                : DebugServerController.IsPaused ? new Color(1f, 0.65f, 0f)
                : Color.green;
      GUILayout.Label(!DebugServerController.IsRunning ? "○ Debug server: stopped"
                    : !DebugServerController.IsConnected ? "○ Debug server: waiting for client"
                    : DebugServerController.IsPaused ? "● Debug server: paused"
                    : "● Debug server: connected");
      GUI.color = prev;
    }

    public static void Recompile(bool force = false)
    {
      if(_pendingCompile != null)
        return;

      //NOTE: resolved on the main thread - Settings.Instance does a Resources.Load,
      //      which throws if it's first touched from the background task below
      var proj = EditorCompiler.LoadProjectConf();
      if(force)
        proj.use_cache = false;

      //NOTE: both are static and outlive a single compile - reset them first, otherwise
      //      the bar's first frame(s) show stale state left over from the last compile
      UnityConsoleLogger.LastLine = "Compiling...";
      _compileStep = 0;

      _pendingCompile = Task.Run(() => EditorCompiler.Compile(proj));
      EditorApplication.update += PollCompile;
    }

    //NOTE: same modal EditorUtility.DisplayProgressBar (BHL/Recompile menu items use it
    //      via EditorCompiler.WithProgressBar) - always shown while pending, whether or
    //      not the Control Panel window itself is open, instead of the previous
    //      inline-when-open/modal-when-closed split
    static void PollCompile()
    {
      if(!_pendingCompile.IsCompleted)
      {
        var line = UnityConsoleLogger.LastLine;
        _compileStep = EditorCompiler.NextProgressStep(line, _compileStep);
        EditorUtility.DisplayProgressBar("BHL", line, _compileStep / (float)EditorCompiler.ProgressStepCount);
        return;
      }

      EditorApplication.update -= PollCompile;
      EditorUtility.ClearProgressBar();

      var task = _pendingCompile;
      _pendingCompile = null;

      if(task.IsFaulted)
      {
        if(task.Exception.InnerException is CompileErrorsException cex)
          EditorCompiler.LogCompileErrors(cex);
        else
          Debug.LogError("[BHL] compile failed:\n" + task.Exception.InnerException?.Message);
        return;
      }

      //NOTE: always applied, even in Edit Mode with no VM yet - SetBytecode creates one
      //      if needed, so clicking this always does something observable
      BHL.SetBytecode(task.Result);

      //NOTE: matches BHL/Recompile's own "bake if bakedBundlePath is set" behavior, so
      //      the baked asset doesn't silently drift from what's now running in-memory
      if(!string.IsNullOrEmpty(Settings.Instance.bakedBundlePath))
        EditorCompiler.WriteBakedBundle(task.Result);

      BHLErrorWindow.HideIfNoErrors();
    }
  }

}
