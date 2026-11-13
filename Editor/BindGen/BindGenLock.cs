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
    var path = Path.Combine(Path.GetTempPath(), "bindgen-" + Hash(Path.GetFullPath(config_path)) + ".lock");
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
