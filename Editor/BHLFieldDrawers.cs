using UnityEditor;
using UnityEngine;

namespace UnityBHL
{

  [CustomPropertyDrawer(typeof(BHLModuleFieldAttribute))]
  public class BHLModuleFieldDrawer : PropertyDrawer
  {
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
      var field_rect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
      EditorGUI.PropertyField(BHLFieldIcon.DrawAndShrink(field_rect), property, label);

      if(property.propertyType != SerializedPropertyType.String)
        return;

      var completions = BHLModuleBrowser.FindModuleCompletions(property.stringValue);
      if(completions.Count == 0)
        return;

      var list_rect = new Rect(position.x, field_rect.yMax, position.width, BHLModuleBrowser.GetCompletionsHeight(completions.Count));
      BHLModuleBrowser.DrawCompletions(list_rect, completions, picked =>
      {
        property.stringValue = picked;
        property.serializedObject.ApplyModifiedProperties();
      });
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
      float height = EditorGUIUtility.singleLineHeight;

      if(property.propertyType == SerializedPropertyType.String)
        height += BHLModuleBrowser.GetCompletionsHeight(BHLModuleBrowser.FindModuleCompletions(property.stringValue).Count);

      return height;
    }
  }

  [CustomPropertyDrawer(typeof(BHLFuncFieldAttribute))]
  public class BHLFuncFieldDrawer : PropertyDrawer
  {
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
      var attr = (BHLFuncFieldAttribute)attribute;

      var field_rect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

      if(property.propertyType != SerializedPropertyType.String)
      {
        EditorGUI.PropertyField(BHLFieldIcon.DrawAndShrink(field_rect), property, label);
        return;
      }

      var module_prop = FindSibling(property, attr.ModuleFieldName);
      var module_name = module_prop?.stringValue;

      var (found, valid) = string.IsNullOrEmpty(module_name)
        ? (new System.Collections.Generic.List<(string name, BHLModuleBrowser.BHLSymbolKind kind)>(), (bool?)null)
        : BHLModuleBrowser.FindFuncCompletions(module_name, property.stringValue, func_pattern: attr.FuncPattern ?? BHLModuleBrowser.AnyFuncPattern);

      var completions = new System.Collections.Generic.List<string>();
      foreach(var f in found)
        completions.Add(f.name);

      var prev_color = GUI.color;
      if(valid == false)
        GUI.color = new Color(1f, 0.6f, 0.6f);
      EditorGUI.PropertyField(BHLFieldIcon.DrawAndShrink(field_rect), property, label);
      GUI.color = prev_color;

      if(completions.Count == 0)
        return;

      var list_rect = new Rect(position.x, field_rect.yMax, position.width, BHLModuleBrowser.GetCompletionsHeight(completions.Count));
      BHLModuleBrowser.DrawCompletions(list_rect, completions, picked =>
      {
        property.stringValue = picked;
        property.serializedObject.ApplyModifiedProperties();
      });
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
      float height = EditorGUIUtility.singleLineHeight;

      if(property.propertyType == SerializedPropertyType.String)
      {
        var attr = (BHLFuncFieldAttribute)attribute;
        var module_prop = FindSibling(property, attr.ModuleFieldName);
        var module_name = module_prop?.stringValue;

        if(!string.IsNullOrEmpty(module_name))
        {
          var (found, _) = BHLModuleBrowser.FindFuncCompletions(module_name, property.stringValue, func_pattern: attr.FuncPattern ?? BHLModuleBrowser.AnyFuncPattern);
          height += BHLModuleBrowser.GetCompletionsHeight(found.Count);
        }
      }

      return height;
    }

    //NOTE: replaces the leaf segment of property's own path - works for top-level,
    //      nested, and array-element fields alike
    static SerializedProperty FindSibling(SerializedProperty property, string sibling_name)
    {
      var path = property.propertyPath;
      var last_dot = path.LastIndexOf('.');
      var sibling_path = last_dot < 0 ? sibling_name : path.Substring(0, last_dot + 1) + sibling_name;
      return property.serializedObject.FindProperty(sibling_path);
    }
  }

  //NOTE: BHL logo mark for [BHLModuleField]/[BHLFuncField] - drawn explicitly since
  //      PropertyField doesn't render GUIContent's image for value-type fields
  static class BHLFieldIcon
  {
    const float Size = 16f;
    const float Gap = 2f;

    //NOTE: draws the icon at rect's right edge, returns a shrunk rect for the field
    public static Rect DrawAndShrink(Rect rect)
    {
      var icon = EditorCompiler.IconSmall;
      if(icon == null)
        return rect;

      var icon_rect = new Rect(rect.xMax - Size, rect.y + (rect.height - Size) / 2f, Size, Size);
      GUI.DrawTexture(icon_rect, icon);

      return new Rect(rect.x, rect.y, rect.width - Size - Gap, rect.height);
    }
  }

}
