using System;
using System.Collections.Generic;
using bhl;

namespace UnityBHL
{

  //NOTE: wraps another IVMCreator, recording every VM it produces for the Control Panel's pool-stats display
  public class VMTracker : IVMCreator
  {
    public struct Item
    {
      public string Label;
      public WeakReference<VM> VM;
    }

    static readonly List<Item> _tracked = new List<Item>();

    public static IReadOnlyList<Item> Tracked
    {
      get
      {
        lock(_tracked)
          return new List<Item>(_tracked);
      }
    }

    public static void Clear()
    {
      lock(_tracked)
        _tracked.Clear();
    }

    readonly IVMCreator _creator;
    readonly string _label;

    public VMTracker(IVMCreator creator, string label = "")
    {
      _creator = creator;
      _label = label;
    }

    public VM MakeVM()
    {
      var vm = _creator.MakeVM();

      lock(_tracked)
      {
        for(int i = _tracked.Count - 1; i >= 0; --i)
          if(!_tracked[i].VM.TryGetTarget(out _))
            _tracked.RemoveAt(i);

        _tracked.Add(new Item { Label = _label, VM = new WeakReference<VM>(vm) });
      }

      return vm;
    }
  }

}
