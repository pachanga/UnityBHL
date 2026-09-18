using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using bhl;

namespace UnityBHL
{

  public class ControlPanel : EditorWindow
  {
    Vector2 _scroll;
    bool _showSettings = true;
    bool _showTracker = false;
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

      DrawCompileProgress();
      DrawErrors();

      if(Application.isPlaying)
      {
        _showTracker = EditorGUILayout.Foldout(_showTracker, "Tracker", true);
        if(_showTracker)
        {
          int i = 0;
          foreach(var item in VMTracker.Tracked)
          {
            if(item.VM.TryGetTarget(out var vm))
            {
              GUILayout.Label($"=== #{++i} {item.Label} ===");
              DrawVMDebugStatus(vm);
              DrawPoolStats(vm);
            }
          }
        }
      }

      EditorGUILayout.EndScrollView();
    }

    //NOTE: same step-based progress PollCompile's modal fallback shows, just inline -
    //      OnInspectorUpdate's periodic Repaint keeps it updating without extra work
    void DrawCompileProgress()
    {
      if(_pendingCompile == null)
        return;

      var line = UnityConsoleLogger.LastLine;
      _compileStep = EditorCompiler.NextProgressStep(line, _compileStep);
      var rect = GUILayoutUtility.GetRect(18, 18, GUILayout.ExpandWidth(true));
      EditorGUI.ProgressBar(rect, _compileStep / (float)EditorCompiler.ProgressStepCount, line);
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
      _settingsEditor.OnInspectorGUI();

      EditorGUILayout.Space();

      bool auto_compile = EditorGUILayout.Toggle(
        new GUIContent("Auto Recompile On File Changes", "Watches bhl.proj's .bhl files and recompiles on change while not in Play Mode"),
        AutoCompileController.Enabled);
      if(auto_compile != AutoCompileController.Enabled)
        AutoCompileController.Enabled = auto_compile;

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

    //NOTE: BHL.VM's own status is already shown by DrawDebugStatus() above - this is
    //      for any other VMTracker-tracked VM (e.g. a pooled one) with its own session
    static void DrawVMDebugStatus(VM vm)
    {
      var session = BHL.GetDebugServer(vm);
      if(session == null)
        return;

      var prev = GUI.color;
      GUI.color = !session.IsConnected ? Color.yellow
                : session.IsPaused ? new Color(1f, 0.65f, 0f)
                : Color.green;
      GUILayout.Label(!session.IsConnected ? "○ Debug: waiting for client"
                    : session.IsPaused ? "● Debug: paused"
                    : "● Debug: connected");
      GUI.color = prev;
    }

    static void DrawPoolStats(VM vm)
    {
      EditorGUILayout.Space();
      GUILayout.Label("=== Pools ===");

      DrawPool("vrefs", vm.vrefs_pool.HitCount, vm.vrefs_pool.MissCount, vm.vrefs_pool.IdleCount, vm.vrefs_pool.BusyCount);
      DrawPool("vlists", vm.vlsts_pool.HitCount, vm.vlsts_pool.MissCount, vm.vlsts_pool.IdleCount, vm.vlsts_pool.BusyCount);
      DrawPool("vmaps", vm.vmaps_pool.HitCount, vm.vmaps_pool.MissCount, vm.vmaps_pool.IdleCount, vm.vmaps_pool.BusyCount);
      DrawPool("fibers", vm.fibers_pool.HitCount, vm.fibers_pool.MissCount, vm.fibers_pool.IdleCount, vm.fibers_pool.BusyCount);
      DrawPool("fptrs", vm.fptrs_pool.HitCount, vm.fptrs_pool.MissCount, vm.fptrs_pool.IdleCount, vm.fptrs_pool.BusyCount);
      GUILayout.Label($"coros: {vm.coro_pool.NewCount - vm.coro_pool.DelCount}(busy)");

      DrawExecStats(vm);
    }

    static void DrawPool(string name, int hit, int miss, int idle, int busy)
    {
      GUILayout.Label($"{name}: {hit}(hit)/{miss}(miss)/{idle}(idle)/{busy}(busy)");
    }

    //NOTE: StackArray.Values is the pool's raw backing array - only [0, Count) are
    //      genuinely idle members; Pop() doesn't clear a slot, so anything at/beyond
    //      Count can be a stale reference to whatever was checked out from there last.
    //      vm.Fibers only lists non-detached fibers (every ScriptBHL fiber is detached,
    //      so it won't show up there) - included for completeness, not as the main source.
    static void DrawExecStats(VM vm)
    {
      var stats = new ExecStats();

      var idle_fibers = vm.fibers_pool.Stack;
      for(int i = 0; i < idle_fibers.Count; ++i)
        stats.Add(idle_fibers.Values[i].exec);

      foreach(var fiber in vm.Fibers)
        stats.Add(fiber.exec);

      foreach(var exec in vm.ScriptExecutors)
        if(exec != null)
          stats.Add(exec);

      EditorGUILayout.Space();
      GUILayout.Label("=== Exec stats ===");
      GUILayout.Label($"execs: {stats.Count}(total)");
      GUILayout.Label($"stacks: {stats.AvgStack}(avg size)/{stats.MaxStack}(max size)");
      GUILayout.Label($"frames: {stats.AvgFrames}(avg size)/{stats.MaxFrames}(max size)");
      GUILayout.Label($"regions: {stats.AvgRegions}(avg size)/{stats.MaxRegions}(max size)");
    }

    struct ExecStats
    {
      public int Count;
      int _stackSum, _frameSum, _regionSum;
      public int MaxStack, MaxFrames, MaxRegions;

      public void Add(VM.ExecState exec)
      {
        ++Count;

        _stackSum += exec.stack.vals.Length;
        MaxStack = Mathf.Max(MaxStack, exec.stack.vals.Length);

        _frameSum += exec.frames.Length;
        MaxFrames = Mathf.Max(MaxFrames, exec.frames.Length);

        _regionSum += exec.regions.Length;
        MaxRegions = Mathf.Max(MaxRegions, exec.regions.Length);
      }

      public int AvgStack => Count > 0 ? _stackSum / Count : 0;
      public int AvgFrames => Count > 0 ? _frameSum / Count : 0;
      public int AvgRegions => Count > 0 ? _regionSum / Count : 0;
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

    //NOTE: shows the same step-based progress DrawCompileProgress does inline, for when
    //      the Control Panel window isn't open. Skipped (and cleared, in case it was
    //      already showing) while it is open - it renders progress inline instead, so
    //      the modal dialog would otherwise flicker in and out alongside it every poll tick.
    static void PollCompile()
    {
      if(!_pendingCompile.IsCompleted)
      {
        if(HasOpenInstances<ControlPanel>())
          EditorUtility.ClearProgressBar();
        else
        {
          var line = UnityConsoleLogger.LastLine;
          _compileStep = EditorCompiler.NextProgressStep(line, _compileStep);
          EditorUtility.DisplayProgressBar("BHL", line, _compileStep / (float)EditorCompiler.ProgressStepCount);
        }
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
