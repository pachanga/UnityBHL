using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityBHL
{

  [CustomEditor(typeof(Settings))]
  public class SettingsInspector : Editor
  {
    bool _showProjContents;
    string _cachedProjPath;
    string _cachedProjContents;
    Vector2 _projContentsScroll;

    public override void OnInspectorGUI()
    {
      serializedObject.Update();

      var bhlProjPathProp = serializedObject.FindProperty(nameof(Settings.bhlProjPath));
      EditorGUILayout.BeginHorizontal();
      EditorGUILayout.PropertyField(bhlProjPathProp, new GUIContent("bhl.proj Path", bhlProjPathProp.tooltip));
      if(GUILayout.Button("...", GUILayout.Width(30)))
        Browse();
      EditorGUILayout.EndHorizontal();

      serializedObject.ApplyModifiedProperties();

      var settings = (Settings)target;
      var resolved = settings.ResolvedBhlProjPath;

      bool projExists = File.Exists(resolved);

      var prev = GUI.color;
      GUI.color = projExists ? Color.green : Color.red;
      EditorGUILayout.LabelField("Resolved path", resolved);
      GUI.color = prev;

      if(projExists)
      {
        if(GUILayout.Button("Open in external editor", GUILayout.ExpandWidth(false)))
          EditorUtility.OpenWithDefaultApp(resolved);

        _showProjContents = EditorGUILayout.Foldout(_showProjContents, "bhl.proj contents", true);
        if(_showProjContents)
        {
          if(_cachedProjPath != resolved)
          {
            _cachedProjPath = resolved;
            _cachedProjContents = File.ReadAllText(resolved);
          }

          var height = EditorStyles.textArea.CalcHeight(new GUIContent(_cachedProjContents), EditorGUIUtility.currentViewWidth);

          _projContentsScroll = EditorGUILayout.BeginScrollView(_projContentsScroll, GUILayout.Height(200));
          EditorGUILayout.SelectableLabel(_cachedProjContents, EditorStyles.textArea, GUILayout.Height(height));
          EditorGUILayout.EndScrollView();
        }
      }

      EditorGUILayout.Space();

      serializedObject.Update();

      var indirectCallsProp = serializedObject.FindProperty(nameof(Settings.indirectCalls));
      EditorGUILayout.PropertyField(indirectCallsProp, new GUIContent("Hotreload Support", indirectCallsProp.tooltip));
      var bakedBundlePathProp = serializedObject.FindProperty(nameof(Settings.bakedBundlePath));
      EditorGUILayout.PropertyField(bakedBundlePathProp, new GUIContent("Asset Path", bakedBundlePathProp.tooltip));

      serializedObject.ApplyModifiedProperties();
    }

    void Browse()
    {
      var picked = EditorUtility.OpenFilePanel("Select bhl.proj", Settings.ProjectRoot, "proj");
      if(string.IsNullOrEmpty(picked))
        return;

      serializedObject.FindProperty(nameof(Settings.bhlProjPath)).stringValue = ToProjectRelativePath(picked);
    }

    //NOTE: stored relative to the project root so it stays valid across different checkouts
    static string ToProjectRelativePath(string absolute_path)
    {
      var rel = Path.GetRelativePath(Settings.ProjectRoot, absolute_path);
      return rel.Replace(Path.DirectorySeparatorChar, '/');
    }
  }

}
