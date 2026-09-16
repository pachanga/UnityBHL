using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityBHL
{

  [CustomEditor(typeof(ScriptBHL))]
  public class ScriptBHLInspector : Editor
  {
    public override void OnInspectorGUI()
    {
      serializedObject.Update();

      DrawPropertiesExcluding(serializedObject, "m_Script",
        nameof(ScriptBHL.ModuleName), nameof(ScriptBHL.ClassName), nameof(ScriptBHL.FieldValues));

      var script = (ScriptBHL)target;

      DrawModulePicker(script);
      DrawClassPicker(script);

      EditorGUILayout.Space();
      DrawFieldValues(script);

      if(Application.isPlaying)
      {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Instance", script.HasInstance ? "live" : "not created");
      }

      serializedObject.ApplyModifiedProperties();
    }

    void DrawModulePicker(ScriptBHL script)
    {
      var moduleNameProp = serializedObject.FindProperty(nameof(ScriptBHL.ModuleName));
      var modules = ClassIntrospection.GetModules();

      if(modules.Count == 0)
      {
        EditorGUILayout.PropertyField(moduleNameProp, new GUIContent("Module"));
        EditorGUILayout.HelpBox("No modules found - check BHLSettings' bhl.proj path, then Refresh.", MessageType.Info);
        return;
      }

      //NOTE: see DrawClassPicker - a fake "<none>" entry keeps an empty ModuleName
      //      visibly distinct from "the first real module", so picking a real one is
      //      always a genuine index change that EndChangeCheck() actually detects
      var options = new List<string> { "<none>" };
      options.AddRange(modules);

      int current = string.IsNullOrEmpty(moduleNameProp.stringValue) ? 0 : options.IndexOf(moduleNameProp.stringValue);

      EditorGUI.BeginChangeCheck();
      int picked = EditorGUILayout.Popup("Module", current < 0 ? 0 : current, options.ToArray());
      if(EditorGUI.EndChangeCheck())
      {
        moduleNameProp.stringValue = picked == 0 ? "" : options[picked];
        //NOTE: the previous ClassName almost certainly doesn't belong to the new module
        serializedObject.FindProperty(nameof(ScriptBHL.ClassName)).stringValue = "";
      }

      if(current < 0 && !string.IsNullOrEmpty(moduleNameProp.stringValue))
      {
        EditorGUILayout.HelpBox(
          $"'{moduleNameProp.stringValue}' not found - pick one above, or Refresh.",
          MessageType.Warning
        );
      }
    }

    void DrawClassPicker(ScriptBHL script)
    {
      var classNameProp = serializedObject.FindProperty(nameof(ScriptBHL.ClassName));
      var classes = ClassIntrospection.GetClasses(script.ModuleName);

      if(classes.Count == 0)
      {
        EditorGUILayout.PropertyField(classNameProp, new GUIContent("Class"));
        if(!string.IsNullOrEmpty(script.ModuleName))
        {
          EditorGUILayout.HelpBox(
            $"No classes found in module '{script.ModuleName}' - fix any compile error, then Refresh.",
            MessageType.Info
          );
        }
        return;
      }

      //NOTE: a fake "<none>" entry at index 0 so an empty ClassName maps to its own,
      //      visibly distinct selection - otherwise Popup's current<0 fallback displays
      //      the first real class as if already selected (tick mark included), and
      //      picking that same already-displayed item never registers as a change
      var options = new List<string> { "<none>" };
      options.AddRange(classes);

      int current = string.IsNullOrEmpty(classNameProp.stringValue) ? 0 : options.IndexOf(classNameProp.stringValue);

      EditorGUI.BeginChangeCheck();
      int picked = EditorGUILayout.Popup("Class", current < 0 ? 0 : current, options.ToArray());
      if(EditorGUI.EndChangeCheck())
        classNameProp.stringValue = picked == 0 ? "" : options[picked];

      if(current < 0 && !string.IsNullOrEmpty(classNameProp.stringValue))
      {
        EditorGUILayout.HelpBox(
          $"'{classNameProp.stringValue}' not found in '{script.ModuleName}' - pick one above, or Refresh.",
          MessageType.Warning
        );
      }
    }

    void DrawFieldValues(ScriptBHL script)
    {
      EditorGUILayout.BeginHorizontal();
      GUILayout.Label("Fields", EditorStyles.boldLabel);
      if(GUILayout.Button("Refresh", GUILayout.Width(60)))
        ClassIntrospection.Invalidate();
      EditorGUILayout.EndHorizontal();

      if(string.IsNullOrEmpty(script.ModuleName) || string.IsNullOrEmpty(script.ClassName))
      {
        EditorGUILayout.HelpBox("Set Module and Class above to configure fields.", MessageType.None);
        return;
      }

      var available = ClassIntrospection.GetFields(script.ModuleName, script.ClassName, out bool resolved)
        .Where(f => f.Type.HasValue)
        .ToList();

      if(!resolved)
      {
        EditorGUILayout.HelpBox(
          $"'{script.ClassName}' not found in '{script.ModuleName}' - " +
          "check the names, fix any compile error, then Refresh.",
          MessageType.Warning
        );
      }

      var list = serializedObject.FindProperty(nameof(ScriptBHL.FieldValues));

      //NOTE: every available field gets its own row with an override toggle, matching
      //      how a plain C# MonoBehaviour shows all its serialized fields up front -
      //      no need for a separate "Add field" step just to discover what exists.
      //      Unchecked fields simply aren't added to FieldValues, so FieldValue.ApplyTo
      //      never touches them and the BHL class's own initializer stands.
      foreach(var field in available)
        DrawFieldRow(list, field);

      DrawStaleFieldRows(list, available);
    }

    static void DrawFieldRow(SerializedProperty list, ClassIntrospection.FieldInfo field)
    {
      int index = FindFieldIndex(list, field.Name);

      EditorGUILayout.BeginHorizontal();

      bool overridden = EditorGUILayout.ToggleLeft(field.Name, index >= 0, GUILayout.Width(140));

      if(overridden && index < 0)
      {
        list.arraySize++;
        index = list.arraySize - 1;
        var elem = list.GetArrayElementAtIndex(index);
        elem.FindPropertyRelative(nameof(FieldValue.FieldName)).stringValue = field.Name;
        elem.FindPropertyRelative(nameof(FieldValue.Type)).enumValueIndex = (int)field.Type.Value;
      }
      else if(!overridden && index >= 0)
      {
        list.DeleteArrayElementAtIndex(index);
        index = -1;
      }

      if(index >= 0)
        DrawValueField(list.GetArrayElementAtIndex(index), field.Type.Value);

      EditorGUILayout.EndHorizontal();
    }

    static int FindFieldIndex(SerializedProperty list, string name)
    {
      for(int i = 0; i < list.arraySize; ++i)
        if(list.GetArrayElementAtIndex(i).FindPropertyRelative(nameof(FieldValue.FieldName)).stringValue == name)
          return i;
      return -1;
    }

    //NOTE: a FieldValues entry whose name no longer matches any available field
    //      (renamed/deleted in BHL) - surfaced with a remove button rather than
    //      silently lingering forever in the serialized list
    static void DrawStaleFieldRows(SerializedProperty list, List<ClassIntrospection.FieldInfo> available)
    {
      var known = new HashSet<string>(available.Select(f => f.Name));

      for(int i = list.arraySize - 1; i >= 0; --i)
      {
        var name = list.GetArrayElementAtIndex(i).FindPropertyRelative(nameof(FieldValue.FieldName)).stringValue;
        if(known.Contains(name))
          continue;

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"'{name}' (missing)", GUILayout.Width(140));
        if(GUILayout.Button("x", GUILayout.Width(20)))
          list.DeleteArrayElementAtIndex(i);
        EditorGUILayout.EndHorizontal();
      }
    }

    static void DrawValueField(SerializedProperty elem, FieldType type)
    {
      string propName = type switch
      {
        FieldType.Int => nameof(FieldValue.IntValue),
        FieldType.Float => nameof(FieldValue.FloatValue),
        FieldType.Bool => nameof(FieldValue.BoolValue),
        FieldType.String => nameof(FieldValue.StringValue),
        FieldType.Vector3 => nameof(FieldValue.Vector3Value),
        _ => null,
      };

      if(propName != null)
        EditorGUILayout.PropertyField(elem.FindPropertyRelative(propName), GUIContent.none);
    }
  }

}
