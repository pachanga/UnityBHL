using UnityEngine;

namespace UnityBHL
{

  //NOTE: marks a serialized string field as a BHL module name, drawn with autocomplete
  //      via BHLFieldDrawers (Editor-only)
  public class BHLModuleFieldAttribute : PropertyAttribute
  {
  }

  //NOTE: marks a serialized string field as a BHL function name, resolved against a
  //      sibling field (moduleFieldName). funcPattern constrains which functions count
  public class BHLFuncFieldAttribute : PropertyAttribute
  {
    public readonly string ModuleFieldName;
    public readonly string FuncPattern;

    public BHLFuncFieldAttribute(string moduleFieldName, string funcPattern = null)
    {
      ModuleFieldName = moduleFieldName;
      FuncPattern = funcPattern;
    }
  }

}
