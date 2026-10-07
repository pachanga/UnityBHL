using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityBHL
{

  //NOTE: sets ScriptBHL.cs's own script icon (Inspector header, Hierarchy/Project rows,
  //      "Add Component" search) to the BHL logo. MonoImporter persists this into the
  //      script's own .meta, so - unlike the field-level marks elsewhere in this package,
  //      which redraw every GUI frame - this only needs to run once (skipped entirely once
  //      the icon already matches) rather than on every domain reload
  [InitializeOnLoad]
  static class ScriptBHLIconAssigner
  {
    static ScriptBHLIconAssigner()
    {
      //NOTE: deferred - AssetDatabase/MonoImporter queries aren't safe mid-compile,
      //      and this runs on every domain reload (including right after one)
      EditorApplication.delayCall += Assign;
    }

    static void Assign()
    {
      var icon = EditorCompiler.Icon;
      if(icon == null)
        return;

      var script = MonoImporter.GetAllRuntimeMonoScripts()
        .FirstOrDefault(s => s.GetClass() == typeof(ScriptBHL));
      if(script == null)
        return;

      var path = AssetDatabase.GetAssetPath(script);
      if(!(AssetImporter.GetAtPath(path) is MonoImporter importer))
        return;

      if(importer.GetIcon() == icon)
        return;

      importer.SetIcon(icon);
      importer.SaveAndReimport();
    }
  }

}
