using bhl;

namespace UnityBHL
{

  //NOTE: a seam for game code to depend on instead of the static BHL.VM directly
  public interface IVMProvider
  {
    VM VM { get; }
  }

  public class VMProvider : IVMProvider
  {
    public VM VM => BHL.VM;
  }

}
