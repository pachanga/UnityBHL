using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityBHL
{

  //NOTE: .bhl and bhl.proj have no ScriptedImporter (that would mean actually importing
  //      them as an asset type, with all the meta-file/import-error baggage that brings) -
  //      this just paints a distinct icon over their Project window row/tile so they're
  //      recognizable at a glance, without touching how they're imported at all
  [InitializeOnLoad]
  static class BHLProjectIcons
  {
    static readonly Texture2D ScriptIcon = LoadIcon("boo Script Icon", "TextAsset Icon");
    static readonly Texture2D ProjIcon = LoadIcon("AssemblyDefinitionAsset Icon", "SettingsIcon", "TextAsset Icon");

    static BHLProjectIcons()
    {
      EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemOnGUI;
    }

    //NOTE: built-in icon names aren't a stable public API across Unity versions - fall
    //      back down a list rather than risk a null texture (silently drawing nothing)
    static Texture2D LoadIcon(params string[] names)
    {
      foreach(var name in names)
      {
        var tex = EditorGUIUtility.IconContent(name)?.image as Texture2D;
        if(tex != null)
          return tex;
      }

      return null;
    }

    static void OnProjectWindowItemOnGUI(string guid, Rect selectionRect)
    {
      var path = AssetDatabase.GUIDToAssetPath(guid);
      if(string.IsNullOrEmpty(path))
        return;

      Texture2D icon;
      if(Path.GetFileName(path).Equals("bhl.proj", StringComparison.OrdinalIgnoreCase))
        icon = ProjIcon;
      else if(path.EndsWith(".bhl", StringComparison.OrdinalIgnoreCase))
        icon = ScriptIcon;
      else
        return;

      if(icon == null)
        return;

      //NOTE: list-view rows are wide/short, grid-view tiles are tall/narrow (icon on top,
      //      label below) - this is the standard heuristic for telling them apart here
      bool isListView = selectionRect.width > selectionRect.height;
      var iconRect = isListView
        ? new Rect(selectionRect.x, selectionRect.y, 16f, 16f)
        : new Rect(selectionRect.x, selectionRect.y, selectionRect.width, selectionRect.width);

      GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
    }
  }

}
