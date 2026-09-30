using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
using UnityEngine;

namespace UnityBHL
{

  //NOTE: mirrors Unity's built-in "Create > C# Script" flow (inline rename in the Project
  //      window, chosen name becomes the class name for Component) - there's no
  //      ScriptedImporter for .bhl, so nothing else gives a new file that icon/rename UX
  public static class CreateBHLScript
  {
    [MenuItem("Assets/Create/BHL/Component", priority = 80)]
    static void CreateComponent()
    {
      Create("NewComponent.bhl", ScriptableObject.CreateInstance<DoCreateComponent>());
    }

    [MenuItem("Assets/Create/BHL/Script", priority = 81)]
    static void CreateScript()
    {
      Create("NewScript.bhl", ScriptableObject.CreateInstance<DoCreateEmptyScript>());
    }

    static void Create(string defaultName, CreateFileAction action)
    {
      var icon = EditorGUIUtility.IconContent("TextAsset Icon").image as Texture2D;
      var path = AssetDatabase.GenerateUniqueAssetPath(ResolveTargetDir() + "/" + defaultName);
      action.StartNameEditing(path, icon);
    }

    //NOTE: a script created outside bhl.proj's src_dirs is invisible to the BHL compiler -
    //      prefer the active Project-window folder only if it's actually one of them,
    //      otherwise redirect into a configured src_dir so the new file isn't dead on arrival
    static string ResolveTargetDir()
    {
      var active = GetActiveFolderPath();

      List<string> srcDirs;
      try
      {
        srcDirs = EditorCompiler.LoadProjectConf().src_dirs;
      }
      catch
      {
        //NOTE: no Settings/bhl.proj configured yet - nothing to check against
        return active;
      }

      if(srcDirs.Count == 0 || srcDirs.Any(d => IsInside(active, d)))
        return active;

      var redirect = srcDirs.Select(ToAssetPath).FirstOrDefault(p => p != null);
      if(redirect != null)
      {
        //NOTE: "Assets" is just the default/no-selection state, not a folder the user
        //      deliberately picked - redirecting there quietly avoids a warning on every
        //      single invocation of the menu with nothing selected
        if(active != "Assets")
          Debug.LogWarning($"[BHL] '{active}' isn't a configured src_dir - creating the script under '{redirect}' instead so it gets compiled.");
        return redirect;
      }

      Debug.LogWarning($"[BHL] None of bhl.proj's src_dirs are inside Assets/ - the new script under '{active}' won't be compiled unless you move it into a configured src_dir.");
      return active;
    }

    static bool IsInside(string dir, string root)
    {
      var d = Path.GetFullPath(dir).TrimEnd('/', '\\');
      var r = Path.GetFullPath(root).TrimEnd('/', '\\');
      return d.Equals(r, StringComparison.OrdinalIgnoreCase) ||
             d.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    //NOTE: null when the src_dir lives outside Assets/ - such a dir has no representation
    //      as a Unity asset path, so the Project-window create/rename flow can't target it
    static string ToAssetPath(string absoluteDir)
    {
      var full = Path.GetFullPath(absoluteDir).TrimEnd('/', '\\');
      var dataPath = Path.GetFullPath(Application.dataPath).TrimEnd('/', '\\');

      if(full.Equals(dataPath, StringComparison.OrdinalIgnoreCase))
        return "Assets";
      if(full.StartsWith(dataPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        return "Assets" + full.Substring(dataPath.Length).Replace(Path.DirectorySeparatorChar, '/');

      return null;
    }

    //NOTE: ProjectWindowUtil.GetActiveFolderPath() is internal - this is the same
    //      reflection-based reach-around every other package uses to match "the folder
    //      you right-clicked, or have open" instead of always dropping into Assets/
    static string GetActiveFolderPath()
    {
      var method = typeof(ProjectWindowUtil).GetMethod("GetActiveFolderPath", BindingFlags.Static | BindingFlags.NonPublic);
      return method != null ? (string)method.Invoke(null, null) : "Assets";
    }

    static void WriteAndShow(string pathName, string content)
    {
      var full = Path.GetFullPath(pathName);
      File.WriteAllText(full, content);
      AssetDatabase.ImportAsset(pathName);
      ProjectWindowUtil.ShowCreatedAsset(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(pathName));
      EditorUtility.OpenWithDefaultApp(full);
    }

    static string BuildComponentContent(string className)
    {
      return
        "import \"unity\"\n\n" +
        $"class {className} : unity.BHLComponent\n" +
        "{\n" +
        "  override func Update()\n" +
        "  {\n" +
        "  }\n" +
        "}\n";
    }

    //NOTE: AssetCreationEndAction (EntityId) replaced EndNameEditAction (int instance IDs) in
    //      6000.4, and 6000.6 turned the old one into a compile error
#if UNITY_6000_4_OR_NEWER
    abstract class CreateFileAction : AssetCreationEndAction
    {
      public override void Action(EntityId entityId, string pathName, string resourceFile)
      {
        CreateFile(pathName);
      }

      public void StartNameEditing(string path, Texture2D icon)
      {
        ProjectWindowUtil.StartNameEditingIfProjectWindowExists(EntityId.None, this, path, icon, null);
      }

      protected abstract void CreateFile(string pathName);
    }
#else
    abstract class CreateFileAction : EndNameEditAction
    {
      public override void Action(int instanceId, string pathName, string resourceFile)
      {
        CreateFile(pathName);
      }

      public void StartNameEditing(string path, Texture2D icon)
      {
        ProjectWindowUtil.StartNameEditingIfProjectWindowExists(0, this, path, icon, null);
      }

      protected abstract void CreateFile(string pathName);
    }
#endif

    //NOTE: a class extending unity.BHLComponent - ScriptBHL attaches these to GameObjects
    class DoCreateComponent : CreateFileAction
    {
      protected override void CreateFile(string pathName)
      {
        var class_name = Path.GetFileNameWithoutExtension(pathName);
        WriteAndShow(pathName, BuildComponentContent(class_name));
      }
    }

    //NOTE: no template - just a blank .bhl file
    class DoCreateEmptyScript : CreateFileAction
    {
      protected override void CreateFile(string pathName)
      {
        WriteAndShow(pathName, "");
      }
    }
  }

}
