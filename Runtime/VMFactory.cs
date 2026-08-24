using System.Collections.Generic;
using bhl;

namespace UnityBHL
{

  //NOTE: for code that pools many short-lived VMs (e.g. one per server session) - not used by BHL's own singleton
  public interface IVMFactory : IVMCreator
  {
    VM RentVM();
    void ReturnVM(VM vm);
    void CleanVMCache();
  }

  public class VMFactory : IVMFactory
  {
    readonly Stack<VM> _vmStack = new Stack<VM>();
    readonly IVMCreator _creator;

    public VMFactory(IVMCreator creator)
    {
      _creator = creator;
    }

    public VM MakeVM() => _creator.MakeVM();

    //NOTE: reset is lazy, on rent, not on return
    public VM RentVM()
    {
      lock(_vmStack)
      {
        if(_vmStack.Count == 0)
          return MakeVM();

        var vm = _vmStack.Pop();
        vm.Stop();
        vm.UnloadModules();
        return vm;
      }
    }

    public void ReturnVM(VM vm)
    {
      lock(_vmStack)
        _vmStack.Push(vm);
    }

    public void CleanVMCache()
    {
      lock(_vmStack)
        _vmStack.Clear();
    }
  }

}
