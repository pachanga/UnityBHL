using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using bhl;
using Types = bhl.Types;
using Logger = bhl.Logger;

namespace UnityBHL
{

  //NOTE: the only place referencing the compiler front-end - Editor-only asmdef, so
  //      BHL_PARSER is never a concern here (UNITY_EDITOR always covers it)
  public static class EditorCompiler
  {
    const string TmpDir = "Library/BHL/tmp";

    public static ProjectConf LoadProjectConf()
    {
      var settings = Settings.Instance;
      if(settings == null)
        throw new Exception("No Settings asset found - create one via Assets > Create > BHL > Settings");

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

      return proj;
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
      //      the most granular progress info the compiler emits - not per-file
      conf.logger = new Logger(1, new UnityConsoleLogger());
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

    //NOTE: blocking call sites (main thread) get a simple shown/cleared bar - only
    //      ControlPanel's Recompile button runs the compile off-thread and animates one
    public static byte[] CompileAllWithProgressBar() => WithProgressBar(CompileAll);

    public static byte[] CompileWithProgressBar(ProjectConf proj) => WithProgressBar(() => Compile(proj));

    static byte[] WithProgressBar(Func<byte[]> compile)
    {
      EditorUtility.DisplayProgressBar("BHL", "Compiling...", 0f);
      try
      {
        var bytes = compile();
        BHLErrorWindow.HideIfNoErrors();
        return bytes;
      }
      finally
      {
        EditorUtility.ClearProgressBar();
      }
    }

    //NOTE: also bakes if bakedBundlePath is set (the default) - a CI/PR check that wants
    //      pure validation without touching that tracked asset can clear the path first.
    //      Also callable via `Unity -batchmode -executeMethod UnityBHL.EditorCompiler.Rebuild`
    [MenuItem("BHL/Rebuild and Bake")]
    public static void Rebuild()
    {
      var bytes = CompileOrThrow("rebuild", () => WithProgressBar(RebuildAll));
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

    static void WriteBakedBundle(byte[] bytes)
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
  //      poll loop - plain string reference assignment is atomic, no locking needed
  class UnityConsoleLogger : ILog
  {
    public static volatile string LastLine = "";

    public void Write(DateTime time, int level, string msg)
    {
      LastLine = msg;
      Debug.Log("[BHL] " + msg);
    }

    public void Error(DateTime time, string msg) => Debug.LogError("[BHL] " + msg);
  }

}
