using UnityEngine;

namespace UnityBHL
{

  //NOTE: marks a serialized string field as a BHL module name - drawn with the same
  //      autocomplete dropdown BHLModuleBrowser.FindModuleCompletions powers elsewhere
  //      (see Editor/BHLFieldDrawers.cs). Runtime attribute (PropertyAttribute, like
  //      [Tooltip]/[Range]) so it can decorate fields on any MonoBehaviour/ScriptableObject,
  //      but it only has an effect via the Editor-only PropertyDrawer that reads it
  public class BHLModuleFieldAttribute : PropertyAttribute
  {
  }

  //NOTE: marks a serialized string field as a BHL function name, resolved against a
  //      sibling field (named by moduleFieldName) holding the module name. funcPattern
  //      constrains which functions count, same as BHLModuleBrowser.FindFuncCompletions -
  //      defaults to matching any function
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
