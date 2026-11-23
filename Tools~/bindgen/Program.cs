using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

//NOTE: generates the BHL bindings registration .cs from C# sources, parsing only (nothing is compiled):
//      [BhlBind(typeof(T), nameof(T.M), ...)] on a type in the sources binds static methods of T found in them,
//      [BhlBind] on a static method in the sources binds it.
//      The output is the canonical bindings format the BHL compiler reads (bhl docs/csharp-bindings.md).
//      usage: bindgen <config.json> [--watch] [--exit-on-stdin-close], paths in the config are relative to it.
//      The parser is hand written (Editor/BindGen/HandParser.cs) so the generator runs inside Unity too;
//      Tools~/bindgen.Tests checks it against Roslyn, which is a dependency of those tests alone
internal static class Program
{
  static readonly JsonSerializerOptions JsonOpts =
    new JsonSerializerOptions { IncludeFields = true, ReadCommentHandling = JsonCommentHandling.Skip };

  const int PollMs = 500;
  //NOTE: a save burst (a rename, a multi-file refactor) is one regeneration
  const int QuietMs = 500;

  public static int Main(string[] args)
  {
    string conf_path = null;
    bool watch = false;
    bool exit_on_stdin_close = false;

    foreach(var arg in args)
    {
      if(arg == "--watch")
        watch = true;
      else if(arg == "--exit-on-stdin-close")
        exit_on_stdin_close = true;
      else if(conf_path == null && !arg.StartsWith("-"))
        conf_path = arg;
      else
      {
        Console.Error.WriteLine("bindgen: unexpected argument '" + arg + "'");
        return Usage();
      }
    }

    if(conf_path == null)
      return Usage();

    return watch ? Watch(conf_path, exit_on_stdin_close) : Once(conf_path);
  }

  static int Usage()
  {
    Console.Error.WriteLine("usage: bindgen <config.json> [--watch] [--exit-on-stdin-close]");
    return 2;
  }

  static List<Config> LoadConfigs(string conf_path)
  {
    return Config.LoadAll(conf_path, json => JsonSerializer.Deserialize<Config>(json, JsonOpts));
  }

  static Generator MakeGenerator(List<Config> confs)
  {
    //NOTE: the defines of the config itself (the last one) for all the sources
    return new Generator(confs, new HandParser(confs[confs.Count - 1].defines));
  }

  static int Once(string conf_path)
  {
    try
    {
      //NOTE: the editor may be generating for the same config right now; a build step can't just skip,
      //      so it waits for its turn and says so if it never comes
      Console.WriteLine(MakeGenerator(LoadConfigs(conf_path)).Run(lock_wait_ms: 30000).summary);
      return 0;
    }
    catch(BindGenBusyException e)
    {
      Console.Error.WriteLine("bindgen: " + e.Message);
      return 1;
    }
    catch(GenException e)
    {
      Console.Error.WriteLine(e.Message);
      return 1;
    }
  }

  //NOTE: generates once and then on every change of a file the bindings are made of, so an LSP or any
  //      other reader of the generated sources keeps up without Unity. Deliberately a foreground process:
  //      its lifetime is the on/off switch, which is why there's no flag to disable it (unlike the Unity
  //      watcher, which lives inside a process you can't Ctrl+C and so needs a persistent toggle)
  static int Watch(string conf_path, bool exit_on_stdin_close)
  {
    using(var gate = BindGenLock.TryAcquireWatch(conf_path))
    {
      if(gate == null)
      {
        int pid = BindGenLock.ReadWatchHolderPid(conf_path);
        Console.Error.WriteLine(
          "bindgen: already watching " + conf_path + (pid > 0 ? " (pid " + pid + ")" : "")
        );
        return 1;
      }

      using(var stop = new ManualResetEventSlim(false))
      {
        Console.CancelKeyPress += (s, e) =>
        {
          //NOTE: so the lock and the pid in it go away with us rather than with the process
          e.Cancel = true;
          stop.Set();
        };
        Console.WriteLine(
          "bindgen: watching " + conf_path +
          " (pid " + System.Diagnostics.Process.GetCurrentProcess().Id + "), Ctrl+C to stop"
        );

        //NOTE: generate before watching - a watcher which only reacts to changes would leave a stale
        //      result stale until someone touched a file, which is the very state it's started to fix
        Generate(conf_path);

        //NOTE: after the first generation, so its summary isn't interleaved with an immediate EOF
        if(exit_on_stdin_close)
          WatchStdin(stop);

        var files = WatchedFiles(conf_path);
        var mtimes = Snapshot(files);

        while(!stop.IsSet)
        {
          if(stop.Wait(PollMs))
            break;

          if(!AnyChanged(files, mtimes))
            continue;

          //NOTE: keep sampling until nothing has moved for QuietMs, so a burst is one regeneration
          while(!stop.IsSet && AnyChanged(files, mtimes))
            if(stop.Wait(QuietMs))
              break;

          if(stop.IsSet)
            break;

          Generate(conf_path);
          //NOTE: the watched set itself moves with the sources: a new declarations file, a new include
          files = WatchedFiles(conf_path);
          mtimes = Snapshot(files);
        }
      }

      Console.WriteLine("bindgen: stopped");
      return 0;
    }
  }

  //NOTE: opt in (--exit-on-stdin-close), for an editor which launches this as a task and whose exit
  //      closes our stdin: we go with it instead of outliving the session as an orphan. Off by default
  //      because backgrounding (`&`, a task runner) gives us a closed or /dev/null stdin too, and that
  //      would have us quit the moment we start
  static void WatchStdin(ManualResetEventSlim stop)
  {
    var thread = new Thread(() =>
    {
      try
      {
        while(Console.In.Read() != -1)
        {}
      }
      catch(Exception)
      {}

      if(!stop.IsSet)
      {
        Console.WriteLine("bindgen: stdin closed");
        stop.Set();
      }
    });
    thread.IsBackground = true;
    thread.Start();
  }

  //NOTE: a failed generation doesn't stop the watching - the file being edited is routinely
  //      half written, and the next save is what fixes it
  static void Generate(string conf_path)
  {
    try
    {
      Console.WriteLine(MakeGenerator(LoadConfigs(conf_path)).Run(lock_wait_ms: 30000).summary);
    }
    catch(BindGenBusyException e)
    {
      Console.Error.WriteLine("bindgen: " + e.Message);
    }
    catch(GenException e)
    {
      Console.Error.WriteLine(e.Message);
    }
    catch(IOException e)
    {
      Console.Error.WriteLine("bindgen: " + e.Message);
    }
  }

  //NOTE: the configs (the one given and the ones it includes) and the files the outputs say they were
  //      made of. A declarations file added since isn't among them - the next regeneration picks it up
  static List<string> WatchedFiles(string conf_path)
  {
    try
    {
      var confs = LoadConfigs(conf_path);
      var files = new List<string>();
      foreach(var conf in confs)
        files.Add(conf.FilePath);

      var deps = BindGenDeps.Read(confs);
      if(deps != null)
        files.AddRange(deps);
      return files;
    }
    catch(Exception)
    {
      //NOTE: a broken config is reported by the generation, not here - but keep watching the config
      //      itself, so fixing it is what starts everything up again
      return new List<string> { Path.GetFullPath(conf_path) };
    }
  }

  static Dictionary<string, DateTime> Snapshot(List<string> files)
  {
    var mtimes = new Dictionary<string, DateTime>();
    foreach(var f in files)
      mtimes[f] = Mtime(f);
    return mtimes;
  }

  //NOTE: updates the snapshot as it goes, so the burst loop above settles instead of re-firing
  static bool AnyChanged(List<string> files, Dictionary<string, DateTime> mtimes)
  {
    bool changed = false;
    foreach(var f in files)
    {
      var mtime = Mtime(f);
      if(mtimes[f] == mtime)
        continue;
      mtimes[f] = mtime;
      changed = true;
    }
    return changed;
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
}
