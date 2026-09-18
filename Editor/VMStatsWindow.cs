using UnityEditor;
using UnityEngine;
using bhl;

namespace UnityBHL
{

  //NOTE: split out of ControlPanel - this is Play-Mode-only runtime introspection
  //      (VMTracker-tracked VMs' pool/exec stats), a different concern from
  //      ControlPanel's Editor-time settings/compiling
  public class VMStatsWindow : EditorWindow
  {
    Vector2 _scroll;

    [MenuItem("BHL/VM Stats", priority = 20)]
    static void Open() => GetWindow<VMStatsWindow>("BHL VM Stats");

    void OnInspectorUpdate() => Repaint();

    void OnGUI()
    {
      _scroll = EditorGUILayout.BeginScrollView(_scroll);

      if(!Application.isPlaying)
      {
        EditorGUILayout.HelpBox("VM stats are only available in Play Mode.", MessageType.Info);
      }
      else
      {
        int i = 0;
        foreach(var item in VMTracker.Tracked)
        {
          if(item.VM.TryGetTarget(out var vm))
          {
            GUILayout.Label($"=== #{++i} {item.Label} ===");
            DrawVMDebugStatus(vm);
            DrawPoolStats(vm);
          }
        }

        if(i == 0)
          EditorGUILayout.HelpBox("No tracked VMs.", MessageType.Info);
      }

      EditorGUILayout.EndScrollView();
    }

    //NOTE: BHL.VM's own status is shown in the Control Panel instead - this is for any
    //      other VMTracker-tracked VM (e.g. a pooled one) with its own debug session
    static void DrawVMDebugStatus(VM vm)
    {
      var session = BHL.GetDebugServer(vm);
      if(session == null)
        return;

      var prev = GUI.color;
      GUI.color = !session.IsConnected ? Color.yellow
                : session.IsPaused ? new Color(1f, 0.65f, 0f)
                : Color.green;
      GUILayout.Label(!session.IsConnected ? "○ Debug: waiting for client"
                    : session.IsPaused ? "● Debug: paused"
                    : "● Debug: connected");
      GUI.color = prev;
    }

    static void DrawPoolStats(VM vm)
    {
      EditorGUILayout.Space();
      GUILayout.Label("=== Pools ===");

      DrawPool("vrefs", vm.vrefs_pool.HitCount, vm.vrefs_pool.MissCount, vm.vrefs_pool.IdleCount, vm.vrefs_pool.BusyCount);
      DrawPool("vlists", vm.vlsts_pool.HitCount, vm.vlsts_pool.MissCount, vm.vlsts_pool.IdleCount, vm.vlsts_pool.BusyCount);
      DrawPool("vmaps", vm.vmaps_pool.HitCount, vm.vmaps_pool.MissCount, vm.vmaps_pool.IdleCount, vm.vmaps_pool.BusyCount);
      DrawPool("fibers", vm.fibers_pool.HitCount, vm.fibers_pool.MissCount, vm.fibers_pool.IdleCount, vm.fibers_pool.BusyCount);
      DrawPool("fptrs", vm.fptrs_pool.HitCount, vm.fptrs_pool.MissCount, vm.fptrs_pool.IdleCount, vm.fptrs_pool.BusyCount);
      GUILayout.Label($"coros: {vm.coro_pool.NewCount - vm.coro_pool.DelCount}(busy)");

      DrawExecStats(vm);
    }

    static void DrawPool(string name, int hit, int miss, int idle, int busy)
    {
      GUILayout.Label($"{name}: {hit}(hit)/{miss}(miss)/{idle}(idle)/{busy}(busy)");
    }

    //NOTE: StackArray.Values is the pool's raw backing array - only [0, Count) are
    //      genuinely idle members; Pop() doesn't clear a slot, so anything at/beyond
    //      Count can be a stale reference to whatever was checked out from there last.
    //      vm.Fibers only lists non-detached fibers (every ScriptBHL fiber is detached,
    //      so it won't show up there) - included for completeness, not as the main source.
    static void DrawExecStats(VM vm)
    {
      var stats = new ExecStats();

      var idle_fibers = vm.fibers_pool.Stack;
      for(int i = 0; i < idle_fibers.Count; ++i)
        stats.Add(idle_fibers.Values[i].exec);

      foreach(var fiber in vm.Fibers)
        stats.Add(fiber.exec);

      foreach(var exec in vm.ScriptExecutors)
        if(exec != null)
          stats.Add(exec);

      EditorGUILayout.Space();
      GUILayout.Label("=== Exec stats ===");
      GUILayout.Label($"execs: {stats.Count}(total)");
      GUILayout.Label($"stacks: {stats.AvgStack}(avg size)/{stats.MaxStack}(max size)");
      GUILayout.Label($"frames: {stats.AvgFrames}(avg size)/{stats.MaxFrames}(max size)");
      GUILayout.Label($"regions: {stats.AvgRegions}(avg size)/{stats.MaxRegions}(max size)");
    }

    struct ExecStats
    {
      public int Count;
      int _stackSum, _frameSum, _regionSum;
      public int MaxStack, MaxFrames, MaxRegions;

      public void Add(VM.ExecState exec)
      {
        ++Count;

        _stackSum += exec.stack.vals.Length;
        MaxStack = Mathf.Max(MaxStack, exec.stack.vals.Length);

        _frameSum += exec.frames.Length;
        MaxFrames = Mathf.Max(MaxFrames, exec.frames.Length);

        _regionSum += exec.regions.Length;
        MaxRegions = Mathf.Max(MaxRegions, exec.regions.Length);
      }

      public int AvgStack => Count > 0 ? _stackSum / Count : 0;
      public int AvgFrames => Count > 0 ? _frameSum / Count : 0;
      public int AvgRegions => Count > 0 ? _regionSum / Count : 0;
    }
  }

}
