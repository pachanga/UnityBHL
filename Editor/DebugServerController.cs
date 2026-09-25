using UnityEditor;
using UnityEngine;

namespace UnityBHL
{

  //NOTE: [InitializeOnLoad] so auto-start-on-play works even without the Control Panel
  //      open. Just Editor-side wiring - the actual session tracking lives in BHL.cs.
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

    internal static int Port => Settings.Instance != null ? Settings.Instance.debugPort : DefaultPort;

    [MenuItem("BHL/Debug Mode", priority = 21)]
    static void ToggleEnabled() => Enabled = !Enabled;

    [MenuItem("BHL/Debug Mode", true)]
    static bool ToggleEnabledValidate()
    {
      Menu.SetChecked("BHL/Debug Mode", Enabled);
      return true;
    }

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
      //NOTE: the creating accessor, not TryGetVM - Debug Mode must attach (and block
      //      Play Mode on) a debug server even if nothing else has touched BHL.VM yet,
      //      e.g. a game that only lazily creates it on first script instantiation
      var vm = BHL.VM;
      if(BHL.GetDebugServer(vm) != null)
        return;

      BHL.AttachDebugServer(vm, Port, waitForClient: true);
      Debug.Log($"[BHL] debug server listening on {Port}");
    }

    static void Stop() => BHL.StopAllDebugServers();
  }

}
