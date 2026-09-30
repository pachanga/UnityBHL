using System;
using System.IO;
#if !NO_UNITY
using UnityEngine;
#endif
using bhl;

namespace UnityBHL
{

  //NOTE: injectable so a game can plug in its own bindings instead of self-registered ones
  public interface IVMCreator
  {
    VM MakeVM();
  }

  //NOTE: no bindings yet - bytecode isn't known here; see BHL.AttachBytecode
  public class DefaultVMCreator : IVMCreator
  {
    public VM MakeVM() => new VM();
  }

  //NOTE: decouples the bytecode source from VM construction - point this at a CDN/patch system instead of Resources
  public struct BytecodeSource
  {
    public string Path { get; }
    public Func<string, Stream> Path2Stream { get; }
    public Stream Stream => Path2Stream(Path);

    public BytecodeSource(string path, Func<string, Stream> path2stream)
    {
      Path = path;
      Path2Stream = path2stream;
    }

#if !NO_UNITY
    public static BytecodeSource FromResources(string resource_path = "bhl")
    {
      return new BytecodeSource(resource_path, path =>
      {
        var asset = Resources.Load<TextAsset>(path);
        if(asset == null)
        {
          throw new Exception(
            $"No baked BHL bytecode found at Resources/{path}.bytes - " +
            "run 'BHL/Recompile' in the Editor before building"
          );
        }
        return new MemoryStream(asset.bytes);
      });
    }
#endif
  }

  public class VMCreator : IVMCreator
  {
    public BytecodeSource Bundle { get; }
    public IUserBindings Bindings { get; }

    //NOTE: Types is a shared, read-mostly symbol/declaration catalog once Register() has
    //      populated it - the actually-mutable per-VM state (gvars, resolved imports)
    //      lives in Module/VM instead (see bhl's Module vs ModuleDeclared split), so
    //      reusing one Types across every VM this creator makes is safe, and skips
    //      re-running potentially-slow Bindings.Register() on each MakeVM() call
    bhl.Types _types;

    public VMCreator(BytecodeSource bundle, IUserBindings bindings = null)
    {
      Bundle = bundle;
      Bindings = bindings;
    }

    public VM MakeVM()
    {
      if(Bindings == null)
        return VM.FromBytecode(Bundle.Stream);

      if(_types == null)
      {
        _types = new bhl.Types();
        Bindings.Register(_types);
      }

      return new VM(_types, new ModuleLoader(_types, Bundle.Stream));
    }
  }

}
