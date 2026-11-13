using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEditor;

namespace UnityBHL
{

  //NOTE: regenerates the bindings when a file they're made of changes, without waiting for Unity to be
  //      focused: the point is editing a declarations file in an IDE and having the bindings follow.
  //      BindGen (the AssetPostprocessor) covers the .cs files Unity imports, but only once Unity has
  //      focus and only for what's inside the project - bhl_bindgen.json itself usually isn't (it lives
  //      next to bhl.proj), and neither are sources outside Assets/Packages.
  //      The poll is a background thread, as AutoCompileController's: mtimes only, no Unity API. The
  //      generation is on the main thread (JsonUtility, AssetDatabase.Refresh and the compile are
  //      main-thread-only), from EditorApplication.update, which keeps ticking unfocused - unlike
  //      AutoCompileController this one deliberately doesn't wait for isApplicationActive.
  //      Off by default: it writes .cs, which makes Unity recompile and reload the domain while you may
  //      still be typing somewhere else
  [InitializeOnLoad]
  public static class BindGenWatcher
  {
    const string EnabledPrefKey = "UnityBHL.BindGen.AutoRegenerate";
    const int PollIntervalMs = 300;
    //NOTE: a save burst (an IDE writing several files, or 'format on save' touching one twice) is one
    //      regeneration, not one per file
    const double QuietSeconds = 0.5;
    const double StartRetrySeconds = 2;

    static Thread _pollThread;
    static CancellationTokenSource _pollCts;
    //NOTE: bumped by the poll thread, read by the main one: a counter rather than a flag so the quiet
    //      window restarts on every new change instead of expiring in the middle of a burst
    static volatile int _changes;
    static int _seen;
    static int _generated;
    static double _seenAt;
    //NOTE: EditorPrefs is read once, not in every update tick
    static bool _enabled;
    //NOTE: when there's no config yet (or it's broken) the retry is occasional, not every tick:
    //      WatchedFiles() reads and parses it
    static double _nextStartTry;

    //NOTE: per developer (EditorPrefs), not a Settings field like AutoCompileController's: whether a
    //      background domain reload is welcome depends on how you work, not on the project
    public static bool Enabled
    {
      get => _enabled;
      set
      {
        _enabled = value;
        EditorPrefs.SetBool(EnabledPrefKey, value);
        if(value)
          Start();
        else
          Stop();
      }
    }

    static BindGenWatcher()
    {
      _enabled = EditorPrefs.GetBool(EnabledPrefKey, false);
      EditorApplication.update += OnEditorUpdate;
      EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

      if(Enabled && !EditorApplication.isPlaying)
        Start();
    }

    //NOTE: generating in Play Mode would reload the domain and drop the play session
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

      var files = WatchedFiles();
      if(files == null)
        return;

      _seen = _generated = _changes;
      _pollCts = new CancellationTokenSource();
      var token = _pollCts.Token;
      _pollThread = new Thread(() => PollLoop(files, token)) { IsBackground = true };
      _pollThread.Start();
    }

    static void Stop()
    {
      //NOTE: as AutoCompileController: this thread's own token, so a Start() right after a Stop() can't
      //      leave two poll threads alive
      _pollCts?.Cancel();
      _pollCts = null;
      _pollThread = null;
    }

    //NOTE: on the main thread (JsonUtility): the configs (the one next to bhl.proj and the ones it
    //      includes) and the files the outputs say they were made of. A declarations file added since
    //      isn't among them - inside the project BindGen's postprocessor catches it, outside it the
    //      next regeneration does
    static List<string> WatchedFiles()
    {
      var conf_path = BindGen.ConfigPath();
      if(conf_path == null)
        return null;

      try
      {
        var confs = BindGen.LoadConfigs(conf_path);
        var files = new List<string>();
        foreach(var conf in confs)
          files.Add(conf.FilePath);

        var deps = BindGen.Deps(confs);
        if(deps != null)
          files.AddRange(deps);
        return files;
      }
      catch(Exception)
      {
        //NOTE: a broken config is reported by the generation itself, not by the watcher
        return null;
      }
    }

    static void PollLoop(List<string> files, CancellationToken token)
    {
      var mtimes = new Dictionary<string, DateTime>();
      foreach(var f in files)
        mtimes[f] = Mtime(f);

      while(!token.IsCancellationRequested)
      {
        Thread.Sleep(PollIntervalMs);

        foreach(var f in files)
        {
          var mtime = Mtime(f);
          if(mtimes[f] == mtime)
            continue;
          mtimes[f] = mtime;
          ++_changes;
        }
      }
    }

    //NOTE: default for a file which isn't there (yet): its appearance is a change too
    static DateTime Mtime(string file)
    {
      try
      {
        return File.Exists(file) ? File.GetLastWriteTimeUtc(file) : default;
      }
      catch(Exception)
      {
        return default;
      }
    }

    static void OnEditorUpdate()
    {
      if(!Enabled || EditorApplication.isPlaying)
        return;

      //NOTE: the watcher may have been enabled before the project had a config, or bhl.proj repointed
      if(_pollThread == null)
      {
        double now = EditorApplication.timeSinceStartup;
        if(now < _nextStartTry)
          return;
        _nextStartTry = now + StartRetrySeconds;
        Start();
        return;
      }

      //NOTE: something new since the last tick: the quiet window starts again
      int changes = _changes;
      if(changes != _seen)
      {
        _seen = changes;
        _seenAt = EditorApplication.timeSinceStartup;
        return;
      }

      if(_seen == _generated)
        return;

      //NOTE: never while Unity is busy with the previous round
      if(EditorApplication.isCompiling || EditorApplication.isUpdating)
        return;

      if(EditorApplication.timeSinceStartup - _seenAt < QuietSeconds)
        return;

      _generated = _seen;

      //NOTE: writes nothing and says nothing when the bindings are already what they should be, so a
      //      change which doesn't affect them costs no reload
      BindGen.Generate();

      //NOTE: the set itself changes with the config (new sources, a new include). The new mtimes are
      //      taken here, so a file written while the generation ran is baselined as current and its
      //      change is missed - a sub-second window, the next save catches it
      Stop();
      Start();
    }
  }

}
