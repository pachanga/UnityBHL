using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace UnityBHL
{

  //NOTE: utility window (true in GetWindow below) - no dock tab, fixed small size, more
  //      in line with how an "About" dialog is usually presented than a regular window
  public class AboutWindow : EditorWindow
  {
    [MenuItem("BHL/About", priority = 40)]
    static void Open()
    {
      var window = GetWindow<AboutWindow>(true, "Unity BHL SDK", true);
      window.minSize = window.maxSize = new Vector2(360, 160);
    }

    void OnGUI()
    {
      var info = PackageInfo.FindForAssembly(typeof(AboutWindow).Assembly);

      EditorGUILayout.Space();

      if(!string.IsNullOrEmpty(info?.description))
        EditorGUILayout.LabelField($"{info.description} (v{info.version})", EditorStyles.wordWrappedLabel);

      EditorGUILayout.LabelField($"BHL({bhl.Version.Name})");
    }
  }

}
