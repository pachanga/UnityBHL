using UnityEditor;
using UnityEngine;
using bhl;
using bhl.dap;

namespace UnityBHL
{

  //NOTE: [InitializeOnLoad] so auto-start-on-play works even if the Control Panel
  //      window isn't open - an EditorWindow's own OnEnable only runs while it's open
  [InitializeOnLoad]
  public static class DebugServerController
  {
    const string DebugModePrefKey = "UnityBHL.DebugMode";
    const int Port = 7777;

    static BHLDebugServer _server;
    static bool _paused;

    public static bool IsPaused => _paused;

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

    public static bool IsRunning => _server != null;

    static DebugServerController()
    {
      EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
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
      if(_server != null || !BHL.TryGetVM(out var vm))
        return;

      _paused = false;
      _server = new BHLDebugServer(vm);
      _server.OnPause = () => _paused = true;
      _server.OnResume = () => _paused = false;
      _server.StartListening(Port);
      Debug.Log($"[BHL] debug server listening on {Port}");
    }

    static void Stop()
    {
      _server?.Stop();
      _server = null;
      _paused = false;
    }
  }

}
