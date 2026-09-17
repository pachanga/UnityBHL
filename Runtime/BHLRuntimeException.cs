using System;
using System.Text;
using bhl;

namespace UnityBHL
{

  //NOTE: combines a C# exception (or a plain message) with a live VM.Fiber's BHL-side
  //      stack trace into one readable trace - for wrapping an error caught while
  //      executing a fiber, so the BHL-side context isn't lost in a crash log/reporter
  public class BHLRuntimeException : Exception
  {
    string _stackTrace;
    public new readonly Exception InnerException;
    public readonly VM.Fiber Fiber;

    public BHLRuntimeException(string message, VM.Fiber fiber) : base(message)
    {
      Fiber = fiber;
      InnerException = null;
      InitStackTrace();
    }

    public BHLRuntimeException(Exception e, VM.Fiber fiber) : base(e.Message, e)
    {
      Fiber = fiber;
      InnerException = e;
      InitStackTrace();
    }

    void InitStackTrace()
    {
      var sb = new StringBuilder();

      // Exception of C# code called by bhl
      if(InnerException != null)
        sb.AppendLine(InnerException.StackTrace);

      // Stacktrace of bhl fiber itself
      if(Fiber != null)
      {
        sb.AppendLine("~~BHL Trace start~~");
        try
        {
          sb.AppendLine(Fiber.GetStackTrace()); // Not safe ^^'
        }
        catch(Exception)
        {
          sb.AppendLine("~~BHL Trace failed to generate~~");
        }

        sb.AppendLine("~~BHL Trace end~~");
      }

      // Stacktrace of this exception itself
      sb.Append(base.StackTrace);

      _stackTrace = sb.ToString();
    }

    public override string StackTrace => _stackTrace;
  }

}
