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
            "run 'BHL/Rebuild and Bake' in the Editor before building"
          );
        }
        return new MemoryStream(asset.bytes);
      });
    }
#endif
  }

  //NOTE: bundle-aware, for a Player build with no Editor driver - opt in via BHL.Configure(new VMCreator(bundle))
  public class VMCreator : IVMCreator
  {
    public BytecodeSource Bundle { get; }
    public IUserBindings Bindings { get; }

    public VMCreator(BytecodeSource bundle, IUserBindings bindings = null)
    {
      Bundle = bundle;
      Bindings = bindings;
    }

    //NOTE: with no explicit Bindings, defer to VM.FromBytecode's reflection-based
    //      auto-discovery of whatever bindings the bundle declares as required. With
    //      explicit Bindings, register them directly instead - the bundle-declared
    //      bindings aren't consulted at all in that case
    public VM MakeVM()
    {
      if(Bindings == null)
        return VM.FromBytecode(Bundle.Stream);

      var types = new bhl.Types();
      Bindings.Register(types);
      return new VM(types, new ModuleLoader(types, Bundle.Stream));
    }
  }

}
