using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;
using bhl;

namespace UnityBHL
{

  //NOTE: detects .bhl changes and feeds a recompile into BHL, Play Mode only. Two
  //      sources: Unity's own asset pipeline for files under Assets/, and a background
  //      mtime-poll for src_dirs living outside it (Unity's AssetPostprocessor never
  //      sees those)
  [InitializeOnLoad]
  public class BHLAssetPostprocessor : AssetPostprocessor
  {
    static ConcurrentDictionary<string, bool> _pendingModules = new ConcurrentDictionary<string, bool>();
    static ProjectConf _proj;

    static Thread _pollThread;
    static volatile bool _pollStop;

    static BHLAssetPostprocessor()
    {
      EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
      EditorApplication.update += OnEditorUpdate;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
      if(change == PlayModeStateChange.ExitingEditMode)
        CompileAndLoad();
      else if(change == PlayModeStateChange.EnteredEditMode)
        StopPolling();
    }

    //NOTE: compiled here so ScriptBHL's first OnEnable already has bytecode to load
    static void CompileAndLoad()
    {
      try
      {
        _proj = EditorCompiler.LoadProjectConf();
        BHL.SetBytecode(EditorCompiler.CompileWithProgressBar(_proj));
      }
      catch(CompileErrorsException ex)
      {
        EditorCompiler.LogCompileErrors(ex);
        return;
      }

      StartPolling(_proj);
    }

    static void StartPolling(ProjectConf proj)
    {
      var external_dirs = proj.src_dirs.Where(d => !IsInsideAssets(d)).ToList();
      if(external_dirs.Count == 0)
        return;

      _pollStop = false;
      _pollThread = new Thread(() => PollLoop(proj, external_dirs)) { IsBackground = true };
      _pollThread.Start();
    }

    static void StopPolling()
    {
      _pollStop = true;
      _pollThread = null;
      _proj = null;
    }

    static bool IsInsideAssets(string dir)
    {
      var full = Path.GetFullPath(dir);
      var assets = Path.GetFullPath(Application.dataPath);
      return full.Equals(assets, StringComparison.OrdinalIgnoreCase) ||
             full.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    static void PollLoop(ProjectConf proj, List<string> dirs)
    {
      const int poll_interval_ms = 300;
      var mtimes = new Dictionary<string, DateTime>();

      foreach(var dir in dirs)
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
              SignalModule(proj, f);
            }
          }
        }
      }
    }

    static void SignalModule(ProjectConf proj, string file_path)
    {
      try
      {
        _pendingModules[proj.inc_path.FilePath2ModuleName(file_path)] = true;
      }
      catch(Exception)
      {
        //NOTE: outside proj.inc_path - not ours to reload
      }
    }

    static void OnEditorUpdate()
    {
      if(_pendingModules.IsEmpty || !EditorApplication.isPlaying || EnsureProj() == null)
        return;

      DrainPendingReload();
    }

    static void OnPostprocessAllAssets(
      string[] importedAssets,
      string[] deletedAssets,
      string[] movedAssets,
      string[] movedFromAssetPaths)
    {
      if(!EditorApplication.isPlaying || EnsureProj() == null)
        return;

      CollectChangedModules(importedAssets);
      CollectChangedModules(deletedAssets);
      CollectChangedModules(movedAssets);
      CollectChangedModules(movedFromAssetPaths);
    }

    //NOTE: a domain reload (e.g. Reload Domain on entering Play Mode) wipes _proj same
    //      as BHL.cs's bytecode - rebuild it lazily since it's cheap (just parses bhl.proj)
    static ProjectConf EnsureProj()
    {
      if(_proj != null || !EditorApplication.isPlaying)
        return _proj;

      try
      {
        _proj = EditorCompiler.LoadProjectConf();
        StartPolling(_proj);
      }
      catch(Exception)
      {
        //NOTE: Settings/bhl.proj missing or broken - leave null
      }

      return _proj;
    }

    static void CollectChangedModules(string[] asset_paths)
    {
      foreach(var path in asset_paths)
      {
        if(path.EndsWith(".bhl"))
          SignalModule(_proj, Path.GetFullPath(path));
      }
    }

    static void DrainPendingReload()
    {
      var drained = Interlocked.Exchange(ref _pendingModules, new ConcurrentDictionary<string, bool>());
      var module_names = new List<string>(drained.Keys);

      byte[] bytes;
      try
      {
        bytes = EditorCompiler.CompileWithProgressBar(_proj);
      }
      catch(CompileErrorsException ex)
      {
        EditorCompiler.LogCompileErrors(ex);
        return;
      }

      BHL.ReloadModules(module_names, bytes);

      Debug.Log($"[BHL] reloaded {string.Join(", ", module_names)}");
    }
  }

}
