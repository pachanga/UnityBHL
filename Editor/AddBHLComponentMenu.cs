using System;
using System.IO;
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
using UnityEngine;

namespace UnityBHL
{

  //NOTE: a "Component/..." MenuItem shows up both in the top Component menu AND inside
  //      any GameObject Inspector's "Add Component" search popup - unlike a plain
  //      [AddComponentMenu] entry (which can only ever call AddComponent<T>()), a MenuItem
  //      runs arbitrary code, which is what lets this also create the backing .bhl file
  //      and wire ScriptBHL up to it, mirroring "Add Component > New Script" for C#
  public static class AddBHLComponentMenu
  {
    [MenuItem("Component/BHL/New BHL Component", false, 11)]
    static void Create(MenuCommand menuCommand)
    {
      var go = menuCommand.context as GameObject ?? Selection.activeGameObject;
      if(go == null)
        return;

      var icon = EditorGUIUtility.IconContent("TextAsset Icon").image as Texture2D;
      var path = AssetDatabase.GenerateUniqueAssetPath(CreateBHLScript.ResolveTargetDir() + "/NewComponent.bhl");

      var action = ScriptableObject.CreateInstance<DoCreateAndAttach>();
      action.TargetGameObject = go;

      ProjectWindowUtil.StartNameEditingIfProjectWindowExists(0, action, path, icon, null);
    }

    [MenuItem("Component/BHL/New BHL Component", true)]
    static bool ValidateCreate()
    {
      return Selection.activeGameObject != null;
    }

    class DoCreateAndAttach : EndNameEditAction
    {
      public GameObject TargetGameObject;

      public override void Action(int instanceId, string pathName, string resourceFile)
      {
        var class_name = Path.GetFileNameWithoutExtension(pathName);
        CreateBHLScript.WriteAndShow(pathName, CreateBHLScript.BuildComponentContent(class_name));

        if(TargetGameObject != null)
          Attach(TargetGameObject, Path.GetFullPath(pathName), class_name);
      }

      static void Attach(GameObject go, string fullPath, string className)
      {
        string moduleName;
        try
        {
          moduleName = EditorCompiler.LoadProjectConf().inc_path.FilePath2ModuleName(fullPath);
        }
        catch(Exception e)
        {
          Debug.LogWarning($"[BHL] Created '{className}' but couldn't resolve its module name to wire up the component: {e.Message}");
          return;
        }

        var comp = Undo.AddComponent<ScriptBHL>(go);
        comp.ModuleName = moduleName;
        comp.ClassName = className;
        EditorUtility.SetDirty(comp);
      }
    }
  }

}
