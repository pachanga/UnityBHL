using System;
using System.IO;
using UnityEngine;
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
  }

  //NOTE: bundle-aware, for a Player build with no Editor driver - opt in via BHL.Configure(new VMCreator(bundle))
  public class VMCreator : IVMCreator
  {
    public BytecodeSource Bundle { get; }

    public VMCreator(BytecodeSource bundle)
    {
      Bundle = bundle;
    }

    //NOTE: bytecode already known via Bundle, so this cherry-picks bindings right away
    public VM MakeVM() => VM.FromBytecode(Bundle.Stream);
  }

}
