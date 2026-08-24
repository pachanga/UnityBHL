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
    const string PrefKey = "UnityBHL.AutoCompile";

    static ConcurrentDictionary<string, bool> _pendingFiles = new ConcurrentDictionary<string, bool>();
    static Thread _pollThread;
    static volatile bool _pollStop;

    public static bool Enabled
    {
      get => EditorPrefs.GetBool(PrefKey, false);
      set
      {
        EditorPrefs.SetBool(PrefKey, value);
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
      try
      {
        dirs = new List<string>(EditorCompiler.LoadProjectConf().src_dirs);
      }
      catch(Exception)
      {
        //NOTE: no Settings/bhl.proj configured yet - nothing to watch
        return;
      }

      if(dirs.Count == 0)
        return;

      _pendingFiles.Clear();
      _pollStop = false;
      _pollThread = new Thread(() => PollLoop(dirs)) { IsBackground = true };
      _pollThread.Start();
    }

    static void Stop()
    {
      _pollStop = true;
      _pollThread = null;
    }

    static void PollLoop(List<string> dirs)
    {
      const int poll_interval_ms = 300;
      var mtimes = new Dictionary<string, DateTime>();

      foreach(var dir in dirs)
        if(Directory.Exists(dir))
          foreach(var f in Directory.GetFiles(dir, "*.bhl", SearchOption.AllDirectories))
            mtimes[f] = File.GetLastWriteTimeUtc(f);

      while(!_pollStop)
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

    //NOTE: pending changes still accumulate while Unity is unfocused (e.g. you're still
    //      editing in an external IDE) - the actual recompile waits until you switch
    //      back to Unity, rather than firing the moment a file is saved
    static void OnEditorUpdate()
    {
      if(_pendingFiles.IsEmpty || !Enabled || EditorApplication.isPlaying)
        return;

      if(!UnityEditorInternal.InternalEditorUtility.isApplicationActive)
        return;

      _pendingFiles.Clear();
      ControlPanel.Recompile();
    }
  }

}
