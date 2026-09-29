using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEditor;

namespace UnityBHL
{

  //NOTE: [InitializeOnLoad] so this resumes after a domain reload even if the Control
  //      Panel window isn't open. Watches bhl.proj's src_dirs and triggers a plain
  //      background recompile on change, but only while NOT in Play Mode -
  //      BHLAssetPostprocessor already handles compile+live-reload during Play Mode,
  //      so running both there would double-compile
  [InitializeOnLoad]
  public static class AutoCompileController
  {
    static ConcurrentDictionary<string, bool> _pendingFiles = new ConcurrentDictionary<string, bool>();
    static Thread _pollThread;
    static CancellationTokenSource _pollCts;
    static string _watchedProjFile;
    static DateTime _watchedProjMtime;

    //NOTE: a real Settings field (Settings.recompileOnFileChanges) rather than its own
    //      EditorPrefs key - lets it be a shared/git-tracked team default, while still
    //      supporting a per-developer override via Settings.IsOverriddenLocally
    public static bool Enabled
    {
      get => Settings.Instance != null && Settings.Instance.recompileOnFileChanges;
      set
      {
        var settings = Settings.Instance;
        if(settings != null)
        {
          settings.recompileOnFileChanges = value;
          EditorUtility.SetDirty(settings);
          if(Settings.IsOverriddenLocally)
            Settings.SaveLocalOverride();
          else
            AssetDatabase.SaveAssetIfDirty(settings);
        }

        if(value && !EditorApplication.isPlaying)
          Start();
        else
          Stop();
      }
    }

    static AutoCompileController()
    {
      EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
      EditorApplication.update += OnEditorUpdate;

      if(Enabled && !EditorApplication.isPlaying)
        Start();
    }

    static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
      if(!Enabled)
        return;

      if(change == PlayModeStateChange.ExitingEditMode)
        Stop();
      else if(change == PlayModeStateChange.EnteredEditMode)
        Start();
    }

    static void Start()
    {
      if(_pollThread != null)
        return;

      List<string> dirs;
      string proj_file;
      try
      {
        var proj = EditorCompiler.LoadProjectConf();
        dirs = new List<string>(proj.src_dirs);
        proj_file = proj.proj_file;
      }
      catch(Exception)
      {
        //NOTE: no Settings/bhl.proj configured yet - nothing to watch
        return;
      }

      if(dirs.Count == 0)
        return;

      _watchedProjFile = proj_file;
      _watchedProjMtime = File.Exists(proj_file) ? File.GetLastWriteTimeUtc(proj_file) : default;

      _pendingFiles.Clear();
      _pollCts = new CancellationTokenSource();
      var token = _pollCts.Token;
      _pollThread = new Thread(() => PollLoop(dirs, token)) { IsBackground = true };
      _pollThread.Start();
    }

    static void Stop()
    {
      //NOTE: cancels this specific thread's token rather than a shared stop flag - a
      //      flag flipped back to "running" by a subsequent Start() (e.g. the bhl.proj-
      //      change restart below) could otherwise race with the old thread not having
      //      noticed the stop yet, leaving two poll threads alive at once
      _pollCts?.Cancel();
      _pollCts = null;
      _pollThread = null;
    }

    static void PollLoop(List<string> dirs, CancellationToken token)
    {
      const int poll_interval_ms = 300;
      var mtimes = new Dictionary<string, DateTime>();

      foreach(var dir in dirs)
        if(Directory.Exists(dir))
          foreach(var f in Directory.GetFiles(dir, "*.bhl", SearchOption.AllDirectories))
            mtimes[f] = File.GetLastWriteTimeUtc(f);

      while(!token.IsCancellationRequested)
      {
        Thread.Sleep(poll_interval_ms);

        foreach(var dir in dirs)
        {
          if(!Directory.Exists(dir))
            continue;

          foreach(var f in Directory.GetFiles(dir, "*.bhl", SearchOption.AllDirectories))
          {
            var mtime = File.GetLastWriteTimeUtc(f);
            if(!mtimes.TryGetValue(f, out var prev) || prev != mtime)
            {
              mtimes[f] = mtime;
              _pendingFiles[f] = true;
            }
          }
        }
      }
    }

    //NOTE: bhl.proj itself is never among the polled files (wrong extension, and the
    //      poll thread's directory list is a fixed snapshot from whenever it started) -
    //      checked here on the main thread (LoadProjectConf touches Settings.Instance/
    //      AssetDatabase, unsafe off it) so an edit (e.g. via the Settings Inspector's
    //      Save, or a bare text editor) takes effect without needing to toggle the
    //      watcher or exit/enter Play Mode
    static void CheckProjFileChanged()
    {
      if(_pollThread == null || _watchedProjFile == null || !File.Exists(_watchedProjFile))
        return;

      var mtime = File.GetLastWriteTimeUtc(_watchedProjFile);
      if(mtime == _watchedProjMtime)
        return;

      Stop();
      Start();
    }

    //NOTE: pending changes still accumulate while Unity is unfocused (e.g. you're still
    //      editing in an external IDE) - the actual recompile waits until you switch
    //      back to Unity, rather than firing the moment a file is saved
    static void OnEditorUpdate()
    {
      if(!Enabled || EditorApplication.isPlaying)
        return;

      CheckProjFileChanged();

      if(_pendingFiles.IsEmpty)
        return;

      if(!UnityEditorInternal.InternalEditorUtility.isApplicationActive)
        return;

      _pendingFiles.Clear();
      //NOTE: never hot-reloads (migrates already-running ScriptBHL instances) - always
      //      the plain SetBytecode swap, regardless of Settings.hotReloadOnRecompile
      ControlPanel.Recompile(allowHotReload: false);
    }
  }

}
