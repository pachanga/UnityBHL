using UnityEditor;
using UnityEngine;

namespace UnityBHL
{

  //NOTE: [InitializeOnLoad] so auto-start-on-play works even if the Control Panel
  //      window isn't open - an EditorWindow's own OnEnable only runs while it's open.
  //      Just the Editor-side auto start/stop + port config + progress-bar wiring;
  //      the actual multi-VM debug session bookkeeping lives in BHL.cs (Runtime), so
  //      it's also available to a Player build with its own debug-attach bootstrap.
  [InitializeOnLoad]
  public static class DebugServerController
  {
    const string DebugModePrefKey = "UnityBHL.DebugMode";
    const int DefaultPort = 7777;

    public static bool Enabled
    {
      get => EditorPrefs.GetBool(DebugModePrefKey, false);
      set
      {
        EditorPrefs.SetBool(DebugModePrefKey, value);
        if(!value)
          Stop();
        else if(EditorApplication.isPlaying)
          Start();
      }
    }

    public static bool IsRunning => BHL.TryGetVM(out var vm) && BHL.GetDebugServer(vm) != null;
    public static bool IsConnected => BHL.TryGetVM(out var vm) && (BHL.GetDebugServer(vm)?.IsConnected ?? false);
    public static bool IsPaused => BHL.TryGetVM(out var vm) && (BHL.GetDebugServer(vm)?.IsPaused ?? false);

    static int Port => Settings.Instance != null ? Settings.Instance.debugPort : DefaultPort;

    static DebugServerController()
    {
      EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

      //NOTE: blocks Play Mode entry (see BHL.AttachDebugServer) until a DAP client
      //      attaches, or the user cancels via this progress bar
      BHL.OnDebuggerWaiting = () =>
        !EditorUtility.DisplayCancelableProgressBar(
          "BHL Debugger", $"Waiting for DAP client to attach on port {Port}...", 0f);
      BHL.OnDebuggerWaitDone = EditorUtility.ClearProgressBar;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
      if(change == PlayModeStateChange.EnteredPlayMode && Enabled)
        Start();
      else if(change == PlayModeStateChange.ExitingPlayMode)
        Stop();
    }

    static void Start()
    {
      if(!BHL.TryGetVM(out var vm) || BHL.GetDebugServer(vm) != null)
        return;

      BHL.AttachDebugServer(vm, Port, waitForClient: true);
      Debug.Log($"[BHL] debug server listening on {Port}");
    }

    static void Stop() => BHL.StopAllDebugServers();
  }

}
