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

    //NOTE: a single Module+Class picker instead of a Module-then-Class cascade - Class
    //      names aren't unique across modules, so both fields are still stored, just
    //      picked together. Driven by a GenericMenu (not EditorGUILayout.Popup) so the
    //      menu can group entries by module ("Module/Class (Module.bhl)") while the
    //      closed-state button shows just the concise "Class (Module.bhl)" - Popup
    //      always echoes the full option string, folder prefix included, when closed.
    void DrawClassPicker(ScriptBHL script)
    {
      var moduleNameProp = serializedObject.FindProperty(nameof(ScriptBHL.ModuleName));
      var classNameProp = serializedObject.FindProperty(nameof(ScriptBHL.ClassName));
      var all = ClassIntrospection.GetAllClasses();

      if(all.Count == 0)
      {
        EditorGUILayout.PropertyField(moduleNameProp, new GUIContent("Module"));
        EditorGUILayout.PropertyField(classNameProp, new GUIContent("Class"));
        EditorGUILayout.HelpBox("No modules/classes found - check BHLSettings' bhl.proj path, then Refresh.", MessageType.Info);
        return;
      }

      bool hasSelection = !string.IsNullOrEmpty(moduleNameProp.stringValue) || !string.IsNullOrEmpty(classNameProp.stringValue);
      bool found = hasSelection && all.Any(e => e.Module == moduleNameProp.stringValue && e.Class == classNameProp.stringValue);

      string buttonLabel = !hasSelection ? "<none>" : $"{classNameProp.stringValue} ({moduleNameProp.stringValue}.bhl)";

      EditorGUILayout.BeginHorizontal();
      EditorGUILayout.PrefixLabel("Class");
      if(GUILayout.Button(buttonLabel, EditorStyles.popup))
        ShowClassMenu(script, all, moduleNameProp.stringValue, classNameProp.stringValue);
      EditorGUILayout.EndHorizontal();

      if(hasSelection && !found)
      {
        EditorGUILayout.HelpBox(
          $"'{classNameProp.stringValue} ({moduleNameProp.stringValue}.bhl)' not found - pick one above, or Refresh.",
          MessageType.Warning
        );
      }
    }

    //NOTE: menu item callbacks fire on a later event, well after this Inspector frame -
    //      mutate the target directly (with Undo/SetDirty) rather than holding onto
    //      SerializedProperty references, which aren't safe to use that late
    void ShowClassMenu(ScriptBHL script, List<ClassIntrospection.ClassRef> all, string currentModule, string currentClass)
    {
      var menu = new GenericMenu();

      menu.AddItem(new GUIContent("<none>"), string.IsNullOrEmpty(currentModule) && string.IsNullOrEmpty(currentClass), () =>
      {
        Undo.RecordObject(script, "Clear BHL Class");
        script.ModuleName = "";
        script.ClassName = "";
        EditorUtility.SetDirty(script);
      });

      foreach(var entry in all)
      {
        bool isSelected = entry.Module == currentModule && entry.Class == currentClass;
        menu.AddItem(new GUIContent($"{entry.Module}/{entry.Class} ({entry.Module}.bhl)"), isSelected, () =>
        {
          Undo.RecordObject(script, "Set BHL Class");
          script.ModuleName = entry.Module;
          script.ClassName = entry.Class;
          EditorUtility.SetDirty(script);
        });
      }

      menu.ShowAsContext();
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
        EditorGUILayout.HelpBox("Set Class above to configure fields.", MessageType.None);
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
