using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using bhl;
using Types = bhl.Types;
using Logger = bhl.Logger;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace UnityBHL
{

  //NOTE: the only place referencing the compiler front-end - Editor-only asmdef, so
  //      BHL_PARSER is never a concern here (UNITY_EDITOR always covers it)
  public static class EditorCompiler
  {
    const string TmpDir = "Library/BHL/tmp";

    //NOTE: "Packages/<name>/..." resolves via AssetDatabase whether the package is
    //      embedded directly under Packages/ or resolved into Library/PackageCache via
    //      git/registry - Unity's virtual package filesystem understands that path either
    //      way, unlike a hardcoded PackageCache-only path
    static Texture2D LoadPackageTexture(string relative_path)
    {
      var packageInfo = PackageInfo.FindForAssembly(typeof(EditorCompiler).Assembly);
      return packageInfo == null ? null : AssetDatabase.LoadAssetAtPath<Texture2D>($"{packageInfo.assetPath}/{relative_path}");
    }

    //NOTE: public - external consumer packages (e.g. a tool built on BHLModuleBrowser)
    //      use these too, for their own BHL-related UI
    static Texture2D _icon;
    public static Texture2D Icon => _icon != null ? _icon : (_icon = LoadPackageTexture("Editor/Icons/bhl_logo.png"));

    //NOTE: a separate, properly-downsized asset for tiny (16px) contexts like the
    //      Project window row icon - GUI.DrawTexture scaling the 256px master down that
    //      far (16x minification) produced a visible edge-bleed artifact
    static Texture2D _iconSmall;
    public static Texture2D IconSmall => _iconSmall != null ? _iconSmall : (_iconSmall = LoadPackageTexture("Editor/Icons/bhl_logo_16.png"));

    //NOTE: resolved here (main thread, via LoadProjectConf) rather than read directly by
    //      Compile/UnityConsoleLogger, which may run on a background thread where
    //      Settings.Instance's first Resources.Load would be unsafe - a plain field write
    //      strictly before the Task.Run in WithProgressBar is safely visible to it, no
    //      volatile needed (Task.Run itself provides that happens-before guarantee)
    static int _consoleVerbosity;

    public static ProjectConf LoadProjectConf()
    {
      var settings = Settings.Instance;
      if(settings == null)
        throw new Exception("No Settings asset found - create one via Assets > Create > BHL > Settings");

      _consoleVerbosity = settings.logVerbosity;

      var path = settings.ResolvedBhlProjPath;
      if(!File.Exists(path))
        throw new Exception($"bhl.proj not found at '{path}' - check BHLSettings.bhlProjPath");

      var proj = ProjectConf.ReadFromFile(path);

      //NOTE: Settings.BhlProj already lazily self-parses on first access; this just
      //      refreshes it with what was already parsed here, avoiding a second parse.
      //      Captured before result_file gets overridden below, so BC consumers of
      //      BhlProj.result_file see what bhl.proj itself says, not our own scratch path.
      settings.BhlProj = new BHLProjectConfig(proj.inc_path, proj.inc_dirs, proj.src_dirs, proj.defines, proj.result_file);

      //NOTE: Library/ is Unity's own scratch space - always ours, regardless of what a
      //      shared bhl.proj (also read by LSP/CLI/other consumers) happens to say here.
      //      result_file mirrors BHL.LastEditorCompilePath - BHL.cs reads this same file
      //      back on a domain reload, so the two must never drift apart
      proj.tmp_dir = TmpDir;
      proj.result_file = BHL.LastEditorCompilePath;

      ApplyPostprocEnvVars(settings);
      PostprocBridge.Sync(proj, settings);

      return proj;
    }

    //NOTE: set before every compile (not just once) so an edit to Settings.postprocEnvVars
    //      takes effect without needing a domain reload - env vars persist for the whole
    //      process once set, so re-setting the same value again is harmless
    static void ApplyPostprocEnvVars(Settings settings)
    {
      foreach(var entry in settings.postprocEnvVars)
      {
        if(!string.IsNullOrEmpty(entry.name))
          Environment.SetEnvironmentVariable(entry.name, ExpandTokens(entry.value));
      }
    }

    //NOTE: $(DATA_PATH) - Application.dataPath is only meaningful from inside Unity, so
    //      a value referencing it can't just be typed in literally the same way across
    //      different machines/checkouts
    static string ExpandTokens(string value)
    {
      return value?.Replace("$(DATA_PATH)", Application.dataPath);
    }

    //NOTE: no UnityEditor UI calls in here - callers may run this on a background
    //      thread (see ControlPanel's async Recompile), and those APIs are main-thread
    //      only. Debug.Log is thread-safe, so that stays. CompileProgress.LastLine is
    //      updated as the compiler logs pipeline-stage messages, for callers that want
    //      to show live-ish progress text.
    public static byte[] Compile(ProjectConf proj)
    {
      var files = new List<string>();
      foreach(var src_dir in proj.src_dirs)
        CompilationExecutor.AddFilesFromDir(src_dir, files);

      var conf = new CompileConf();
      conf.proj = proj;
      //NOTE: verbosity 1 surfaces per-pipeline-stage messages (parsing/type-checking/etc),
      //      the most granular progress info the compiler emits - not per-file. This is
      //      bhl's own Logger gate (always 1, for progress-bar tracking - see
      //      UnityConsoleLogger), separate from Settings.logVerbosity, which only gates
      //      whether those same lines also get printed to Unity's console
      conf.logger = new Logger(1, new UnityConsoleLogger(_consoleVerbosity));
      conf.self_file = BuildUtils.GetSelfFile();
      conf.files = BuildUtils.NormalizeFilePaths(files);
      //NOTE: proj.bindings is the source of truth, not "everything self-registered"
      conf.bindings = proj.LoadBindings();
      conf.postproc = proj.LoadPostprocessor();
      conf.ts = new Types();
      //NOTE: always on - every BHL compile happens in the Editor, and hot-reload
      //      relinking depends on it
      conf.indirect_calls = true;

      var executor = new CompilationExecutor();
      //NOTE: Task.Run avoids a sync-over-async deadlock on Unity's main-thread SynchronizationContext
      var result = Task.Run(() => executor.Exec(conf)).GetAwaiter().GetResult();

      //NOTE: plain Debug.LogWarning, no WithoutStackTrace here - this method must stay
      //      background-thread-safe (see note above), and stack-trace-type get/set are
      //      main-thread-only, unlike Debug.Log itself
      foreach(var warn in result.warnings)
      {
        if(string.IsNullOrEmpty(warn.file))
          Debug.LogWarning($"[BHL] {warn.text}");
        else
          Debug.LogWarning($"[BHL] {warn.file}({warn.range.start.line},{warn.range.start.column}): {warn.text}");
      }

      if(result.errors.Count > 0)
        throw new CompileErrorsException(result.errors);

      LastErrors = Array.Empty<ICompileError>();

      return File.ReadAllBytes(proj.result_file);
    }

    public static byte[] CompileAll() => Compile(LoadProjectConf());

    //NOTE: bypasses CompilationExecutor's content-hash cache and wipes the scratch dir
    //      first, unlike CompileAll - for verifying a truly clean build succeeds,
    //      not routine iteration
    public static byte[] RebuildAll()
    {
      var proj = LoadProjectConf();
      proj.use_cache = false;

      if(Directory.Exists(proj.tmp_dir))
        Directory.Delete(proj.tmp_dir, recursive: true);

      return Compile(proj);
    }

    //NOTE: resolves proj on the calling (main) thread first - see LoadProjectConf's own
    //      note on why (Settings.Instance's first Resources.Load must not happen off it)
    //      - then hands only the actual compile to WithProgressBar, which runs it via
    //      Task.Run so the progress bar can keep refreshing with live output meanwhile
    public static byte[] CompileAllWithProgressBar()
    {
      var proj = LoadProjectConf();
      return WithProgressBar(() => Compile(proj));
    }

    public static byte[] CompileWithProgressBar(ProjectConf proj) => WithProgressBar(() => Compile(proj));

    //NOTE: runs compile() via Task.Run and polls it from here (main thread) so the
    //      progress bar can keep refreshing with the compiler's latest log line
    //      (UnityConsoleLogger.LastLine) instead of freezing on "Compiling..." for the
    //      whole (possibly long) compile, the way a single blocking call would
    static byte[] WithProgressBar(Func<byte[]> compile)
    {
      //NOTE: LastLine is static and outlives this compile - reset it first, otherwise
      //      the bar's first frame(s) show a stale line left over from the last compile
      UnityConsoleLogger.LastLine = "Compiling...";

      var task = Task.Run(compile);
      int step = 0;
      try
      {
        while(!task.IsCompleted)
        {
          var line = UnityConsoleLogger.LastLine;
          step = NextProgressStep(line, step);
          EditorUtility.DisplayProgressBar("BHL", line, step / (float)ProgressStepCount);
          Thread.Sleep(50);
        }
      }
      finally
      {
        EditorUtility.ClearProgressBar();
      }

      if(task.IsFaulted)
        throw task.Exception.InnerException ?? task.Exception;

      BHLErrorWindow.HideIfNoErrors();
      return task.Result;
    }

    internal const int ProgressStepCount = 4;

    //NOTE: maps the compiler's pipeline-stage log lines (see executor.cs's Pipeline
    //      stage names, e.g. "BHL register bindings"/"BHL parse finalize"/"BHL compile
    //      write"/"BHL postproc finalize"/"BHL all done") to a coarse step number, so
    //      the progress bar can show real progress instead of just sweeping back and
    //      forth. A line matching nothing (e.g. "BHL cache blob write", "BHL write to
    //      file") keeps whatever step was last detected, rather than resetting
    internal static int NextProgressStep(string line, int currentStep)
    {
      if(string.IsNullOrEmpty(line))
        return currentStep;

      var lower = line.ToLowerInvariant();

      if(lower.Contains("all done"))
        return 4;
      if(lower.Contains("postproc"))
        return 3;
      if(lower.Contains("compile"))
        return 2;
      if(lower.Contains("parse"))
        return 1;
      if(lower.Contains("register bindings"))
        return 0;

      return currentStep;
    }

    //NOTE: matches the Control Panel's plain "Recompile" button - incremental, respects
    //      bhl's own compile cache. Also bakes if bakedBundlePath is set (the default).
    [MenuItem("BHL/Recompile", priority = 2)]
    public static void Recompile()
    {
      var bytes = CompileOrThrow("recompile", CompileAllWithProgressBar);
      if(!string.IsNullOrEmpty(Settings.Instance.bakedBundlePath))
        WriteBakedBundle(bytes);
    }

    //NOTE: matches the Control Panel's "Force Recompile" button - bypasses bhl's compile
    //      cache (use_cache = false) without wiping tmp_dir, unlike Rebuild/RebuildAll
    //      below. Also bakes if bakedBundlePath is set (the default).
    [MenuItem("BHL/Force Recompile", priority = 3)]
    public static void ForceRecompile()
    {
      var proj = LoadProjectConf();
      proj.use_cache = false;

      var bytes = CompileOrThrow("force recompile", () => WithProgressBar(() => Compile(proj)));
      if(!string.IsNullOrEmpty(Settings.Instance.bakedBundlePath))
        WriteBakedBundle(bytes);
    }

    //NOTE: full, cache-bypassing rebuild (also wipes tmp_dir) - for CI/verifying a clean
    //      build succeeds, not routine iteration, hence no menu item of its own (see
    //      Recompile above for that). Callable via
    //      `Unity -batchmode -executeMethod UnityBHL.EditorCompiler.Rebuild` - kept as
    //      its own method (rather than folded into Recompile) for that BC
    public static void Rebuild()
    {
      var proj = LoadProjectConf();
      proj.use_cache = false;

      if(Directory.Exists(proj.tmp_dir))
        Directory.Delete(proj.tmp_dir, recursive: true);

      var bytes = CompileOrThrow("rebuild", () => WithProgressBar(() => Compile(proj)));
      if(!string.IsNullOrEmpty(Settings.Instance.bakedBundlePath))
        WriteBakedBundle(bytes);
    }

    //NOTE: logs each error, then throws a short summary - a human reads the log lines
    //      above (or the Control Panel's error list), but -executeMethod callers still
    //      need the non-zero exit an uncaught exception gives; swallowing it here would
    //      report CI success even when the compile failed. Unity logs whatever we throw
    //      here itself, so keep it terse rather than rethrowing the original (whose
    //      message dumps every error's full text again).
    static byte[] CompileOrThrow(string verb, Func<byte[]> compile)
    {
      try
      {
        return compile();
      }
      catch(CompileErrorsException ex)
      {
        LogCompileErrors(ex);
        throw new Exception($"BHL {verb} failed ({ex.errors.Count} error(s)) - see above");
      }
    }

    //NOTE: read by BHLErrorWindow to render an explicit per-error "Open" button via
    //      CodeEditor.CurrentEditor.OpenProject - Unity's console can't reliably jump to
    //      .bhl files itself (double-click needs a real Unity asset, but bhl.proj's
    //      src_dirs are often outside the Unity project entirely), so we don't rely on it
    public static IReadOnlyList<ICompileError> LastErrors { get; private set; } = Array.Empty<ICompileError>();

    public static void LogCompileErrors(CompileErrorsException ex)
    {
      LastErrors = ex.errors;
      BHLErrorWindow.ShowErrors();

      WithoutStackTrace(LogType.Error, () =>
      {
        foreach(var err in ex.errors)
        {
          if(string.IsNullOrEmpty(err.file) || err.file == "?")
            Debug.LogError($"[BHL] {err.text}");
          else
            Debug.LogError($"[BHL] {err.file}({err.range.start.line},{err.range.start.column}): {err.text}");
        }
      });
    }

    //NOTE: suppresses Unity's own auto-captured call stack for the logs inside `body`,
    //      so double-clicking the console entry doesn't open EditorCompiler.cs (where
    //      LogError/LogWarning was actually called from) - restored right after so
    //      other logging in the project is unaffected
    static void WithoutStackTrace(LogType type, Action body)
    {
      var prev_trace = Application.GetStackTraceLogType(type);
      Application.SetStackTraceLogType(type, StackTraceLogType.None);
      try
      {
        body();
      }
      finally
      {
        Application.SetStackTraceLogType(type, prev_trace);
      }
    }

    internal static void WriteBakedBundle(byte[] bytes)
    {
      var bundle_path = Settings.Instance.bakedBundlePath;

      var dir = Path.GetDirectoryName(bundle_path);
      if(!Directory.Exists(dir))
        Directory.CreateDirectory(dir);

      File.WriteAllBytes(bundle_path, bytes);
      AssetDatabase.Refresh();

      Debug.Log($"[BHL] baked {bytes.Length} bytes to {bundle_path}");
    }
  }

  //NOTE: LastLine is written from whatever thread is compiling (background, for
  //      ControlPanel's async path) and read from the main thread by a progress-bar
  //      poll loop - plain string reference assignment is atomic, no locking needed.
  //      LastLine is always updated regardless of 'verbosity' - progress-bar tracking
  //      must keep working even with Settings.logVerbosity off, only the actual
  //      Debug.Log console output is gated by it
  class UnityConsoleLogger : ILog
  {
    public static volatile string LastLine = "";

    readonly int verbosity;

    public UnityConsoleLogger(int verbosity)
    {
      this.verbosity = verbosity;
    }

    public void Write(DateTime time, int level, string msg)
    {
      LastLine = msg;
      if(level <= verbosity)
        Debug.Log("[BHL] " + msg);
    }

    //NOTE: always shown regardless of verbosity - an actual error is never noise
    public void Error(DateTime time, string msg) => Debug.LogError("[BHL] " + msg);
  }

}
