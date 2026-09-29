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
    //NOTE: the actual BHL logo, falling back to a built-in icon only if that's somehow
    //      unavailable (e.g. package resolution failed) rather than drawing nothing.
    //      IconSmall is a flattened, fully-opaque-square variant (no transparency at
    //      all) for the tiny list-view row icon, so Unity's own default file icon
    //      (still drawn underneath - we only ever overlay, never suppress it) can never
    //      show through any part of our icon, only around it if our coverage falls
    //      short (see the overdraw in OnProjectWindowItemOnGUI below); the full-res
    //      logo is used everywhere else (grid tiles, About window, window tab icons)
    static readonly Texture2D ScriptIconSmall = EditorCompiler.IconSmall != null ? EditorCompiler.IconSmall : LoadIcon("boo Script Icon", "TextAsset Icon");
    static readonly Texture2D ScriptIconLarge = EditorCompiler.Icon != null ? EditorCompiler.Icon : LoadIcon("boo Script Icon", "TextAsset Icon");
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

      //NOTE: excludes folders - a package folder like "com.bitgames.bhl" coincidentally
      //      ends in ".bhl" too, purely as a naming accident, and isn't an actual .bhl file
      bool isFolder = AssetDatabase.IsValidFolder(path);
      bool isBhlProj = !isFolder && Path.GetFileName(path).Equals("bhl.proj", StringComparison.OrdinalIgnoreCase);
      bool isBhlScript = !isFolder && path.EndsWith(".bhl", StringComparison.OrdinalIgnoreCase);
      if(!isBhlProj && !isBhlScript)
        return;

      //NOTE: list-view rows are wide/short, grid-view tiles are tall/narrow (icon on top,
      //      label below) - this is the standard heuristic for telling them apart here
      bool isListView = selectionRect.width > selectionRect.height;

      Texture2D icon;
      if(isBhlProj)
        icon = ProjIcon;
      else
        icon = isListView ? ScriptIconSmall : ScriptIconLarge;

      if(icon == null)
        return;

      //NOTE: derive the icon slot size from the row/tile itself rather than hardcoding
      //      16f - at small zoom levels Project window's "grid" mode is wider-than-tall
      //      (isListView true here) but its actual icon size isn't always exactly 16px,
      //      leaving a sliver of whatever's underneath visible when it wasn't
      float iconSize = isListView ? selectionRect.height : selectionRect.width;

      //NOTE: Unity draws its own default file icon for .bhl/bhl.proj underneath this
      //      overlay (we only paint on top, never suppress it) - a couple of points of
      //      deliberate overdraw guarantees full coverage regardless of any small
      //      mismatch between our computed rect and Unity's actual internal icon slot,
      //      rather than chasing pixel-perfect alignment with an unknown target
      const float overdraw = 2f;
      float x = Mathf.Round(selectionRect.x) - overdraw;
      float y = Mathf.Round(selectionRect.y) - overdraw;
      float size = Mathf.Round(iconSize) + overdraw * 2f;
      var iconRect = new Rect(x, y, size, size);

      GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
    }
  }

}
