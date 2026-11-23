using System;
using System.IO;
using System.Threading;

//NOTE: one generation at a time per config: the editor (the BindGen postprocessor or its Auto watcher) and
//      the console tool write the same result_dirs, and a run deletes the generated files which aren't in
//      its own set. Two runs started around one change hold different sets - the later one's new file is
//      deleted by the earlier one's sweep, and the project then misses a registration with nothing in the
//      log pointing at why. The writes aren't atomic either (File.WriteAllText truncates first), so a
//      concurrent reader - the other run, or Unity importing after a Refresh - can see half a file.
//      An advisory lock (FileShare.None on a file in the temp dir, named by a hash of the config's path):
//      the OS drops it when the process ends, so a crash leaves nothing stale behind
internal sealed class BindGenLock : IDisposable
{
  readonly FileStream stream;

  BindGenLock(FileStream stream)
  {
    this.stream = stream;
  }

  //NOTE: null when another run holds it for longer than wait_ms (0 = don't wait at all)
  public static BindGenLock TryAcquire(string config_path, int wait_ms)
  {
    return Lock(LockPath(Path.GetFullPath(config_path), ".lock"), wait_ms);
  }

  static BindGenLock Lock(string path, int wait_ms)
  {
    var until = DateTime.UtcNow.AddMilliseconds(wait_ms);
    while(true)
    {
      try
      {
        return new BindGenLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
      }
      catch(IOException)
      {
        if(DateTime.UtcNow >= until)
          return null;
        Thread.Sleep(50);
      }
      catch(UnauthorizedAccessException)
      {
        //NOTE: the temp dir isn't writable - don't make that stop a generation
        return new BindGenLock(null);
      }
    }
  }

  //NOTE: held for the whole run of a `bindgen --watch`, unlike the generation lock above which is held
  //      per generation - a second watcher of the same config refuses to start instead of doubling every
  //      regeneration. Its own file, so holding it doesn't block generations.
  //      FileShare.None, as above: on Unix .NET maps None to flock(LOCK_EX) and every other value to
  //      LOCK_SH, so anything more permissive lets two watchers hold it at once. That leaves no way to
  //      read the holder out of the locked file itself, hence the pid in a sidecar next to it
  public static BindGenLock TryAcquireWatch(string config_path)
  {
    var gate = Lock(WatchLockPath(config_path), 0);
    if(gate != null)
      WritePid(config_path);
    return gate;
  }

  //NOTE: -1 when there's nobody to name: the holder hasn't written it yet, or it isn't a pid
  public static int ReadWatchHolderPid(string config_path)
  {
    try
    {
      return int.TryParse(File.ReadAllText(PidPath(config_path)).Trim(), out int pid) ? pid : -1;
    }
    catch(Exception)
    {
      return -1;
    }
  }

  //NOTE: left behind by a watcher which was killed - harmless, it's only read while the lock is held
  //      and whoever holds it rewrote this first
  static void WritePid(string config_path)
  {
    try
    {
      //NOTE: not Environment.ProcessId - it's .NET 5+, this file compiles into Unity's editor assembly too
      File.WriteAllText(PidPath(config_path), System.Diagnostics.Process.GetCurrentProcess().Id.ToString());
    }
    catch(Exception)
    {}
  }

  //NOTE: the suffix is what keeps the watcher's run-length lock off the per generation one - the hash
  //      is of the config path alone, so it can't depend on the cwd a watcher was started from
  static string LockPath(string full_config_path, string suffix)
  {
    return Path.Combine(Path.GetTempPath(), "bindgen-" + Hash(full_config_path) + suffix);
  }

  static string WatchLockPath(string config_path) => LockPath(Path.GetFullPath(config_path), "-watch.lock");

  static string PidPath(string config_path) => LockPath(Path.GetFullPath(config_path), "-watch.pid");

  public void Dispose()
  {
    stream?.Dispose();
  }

  //NOTE: FNV-1a, only to make a short stable file name of a path
  static string Hash(string s)
  {
    ulong h = 14695981039346656037;
    foreach(char c in s)
    {
      h ^= c;
      h *= 1099511628211;
    }
    return h.ToString("x16");
  }
}

//NOTE: another bindgen is generating for this config; what to do about it is the caller's to decide - the
//      editor skips (the other run makes the same files), the console tool says so and fails
internal class BindGenBusyException : Exception
{
  public BindGenBusyException(string message) : base(message)
  {}
}
