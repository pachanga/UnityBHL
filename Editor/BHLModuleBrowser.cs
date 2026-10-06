using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UnityBHL
{

  //NOTE: reusable BHL module/function lookup + autocomplete, for any Editor tool that
  //      needs to let the user target a specific module/function (e.g. a test runner) -
  //      extracted from one such tool (ATF) once a second consumer needed the same logic
  public static class BHLModuleBrowser
  {
    public const int DefaultMaxCompletions = 8;

    //NOTE: prefers bhl.proj's own inc_dirs (resolved relative to bhl.proj's directory),
    //      falling back to bhl.proj's own directory if none are configured
    public static IEnumerable<string> GetSearchRoots()
    {
      var settings = Settings.Instance;
      if(settings == null)
        yield break;

      var projDir = Path.GetDirectoryName(Path.GetFullPath(settings.ResolvedBhlProjPath));
      if(projDir == null)
        yield break;

      var inc = settings.BhlProj?.inc_dirs;
      if(inc != null && inc.Count > 0)
      {
        foreach(var dir in inc)
          yield return Path.GetFullPath(Path.Combine(projDir, dir));
      }
      else
      {
        yield return projDir;
      }
    }

    //NOTE: prefers bhl.proj's own module->file mapping (handles includes/aliasing),
    //      falling back to a plain path guess under the search roots
    public static string FindModuleFile(string module_name)
    {
      if(string.IsNullOrEmpty(module_name))
        return null;

      var cfg = Settings.Instance?.BhlProj;
      if(cfg != null && cfg.TryMapModuleToFile(module_name, out var file))
        return file;

      var rel = module_name.Replace('/', Path.DirectorySeparatorChar) + ".bhl";
      foreach(var root in GetSearchRoots())
      {
        var path = Path.Combine(root, rel);
        if(File.Exists(path))
          return path;
      }

      return null;
    }

    //NOTE: matches module names containing 'partial' (not just prefix), mirroring how
    //      the original tool this was extracted from behaved
    public static List<string> FindModuleCompletions(string partial, int max = DefaultMaxCompletions)
    {
      var result = new List<string>();
      if(string.IsNullOrEmpty(partial))
        return result;

      try
      {
        foreach(var root in GetSearchRoots())
        {
          if(!Directory.Exists(root))
            continue;

          var trimmed = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
          foreach(var file in Directory.GetFiles(root, "*.bhl", SearchOption.AllDirectories))
          {
            var rel = file.Substring(trimmed.Length + 1).Replace('\\', '/');
            var name = rel.Substring(0, rel.Length - 4); // strip .bhl
            if(name != partial && name.Contains(partial))
            {
              result.Add(name);
              if(result.Count >= max)
                return result;
            }
          }
        }
      }
      catch(Exception) {}

      return result;
    }

    public const string AnyFuncPattern = @"\bfunc\s+([a-zA-Z_][a-zA-Z0-9_]*)\s*\(";

    //NOTE: func_pattern lets a caller constrain which functions count (its own Group(1)
    //      must capture the function name) - e.g. a test runner that only wants entry
    //      points shaped like 'func ... ([]string args)'. 'valid' is null if the module
    //      file couldn't be found, true if 'partial' exactly matches a declared function
    //      (or is empty), false otherwise - mirrors a text field's "is this usable" state
    public static (List<string> completions, bool? valid) FindFuncCompletions(
      string module_name, string partial, string func_pattern = AnyFuncPattern, int max = DefaultMaxCompletions)
    {
      var completions = new List<string>();

      var file_path = FindModuleFile(module_name);
      if(file_path == null)
        return (completions, null);

      try
      {
        var text = File.ReadAllText(file_path);
        var matches = Regex.Matches(text, func_pattern);
        bool found_exact = string.IsNullOrEmpty(partial);
        foreach(Match m in matches)
        {
          var name = m.Groups[1].Value;
          if(name == partial)
          {
            found_exact = true;
            continue;
          }
          if(!string.IsNullOrEmpty(partial) && !name.Contains(partial))
            continue;
          if(!completions.Contains(name) && completions.Count < max)
            completions.Add(name);
        }
        return (completions, found_exact);
      }
      catch(Exception)
      {
        return (completions, null);
      }
    }

    //NOTE: height DrawCompletions(Rect, ...) below needs for 'count' rows - exposed so a
    //      non-layout caller (e.g. a PropertyDrawer.GetPropertyHeight) can reserve space
    public static float GetCompletionsHeight(int count)
    {
      return count == 0 ? 0f : count * (EditorGUIUtility.singleLineHeight + 2) + 4;
    }

    //NOTE: rect-based primitive - draws nothing if the list is empty. The layout-based
    //      overload below (for a plain OnGUI()) reserves its own rect and delegates here,
    //      so both it and non-layout callers (e.g. a PropertyDrawer) share one code path
    public static void DrawCompletions(Rect rect, List<string> list, Action<string> on_pick)
    {
      if(list.Count == 0)
        return;

      GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);

      var row = new Rect(rect.x + 2, rect.y + 2, rect.width - 4, EditorGUIUtility.singleLineHeight);
      for(int i = 0; i < list.Count; ++i)
      {
        if(GUI.Button(row, list[i], EditorStyles.miniButton))
        {
          on_pick(list[i]);
          break;
        }
        row.y += EditorGUIUtility.singleLineHeight + 2;
      }
    }

    //NOTE: small reusable "dropdown of buttons below a text field" widget, for a plain
    //      OnGUI() caller (e.g. a custom EditorWindow) - draws nothing if the list is empty
    public static void DrawCompletions(List<string> list, Action<string> on_pick)
    {
      if(list.Count == 0)
        return;

      var rect = GUILayoutUtility.GetRect(0, GetCompletionsHeight(list.Count), GUILayout.ExpandWidth(true));
      DrawCompletions(rect, list, on_pick);
    }

    const float MarkSize = 16f;

    //NOTE: the same BHL logo used for the .bhl Project window icon, as a visual cue that
    //      a field has BHL autocomplete - drawn explicitly rather than via GUIContent's
    //      image, which EditorGUI/EditorGUILayout field controls don't render for a
    //      value-type field (string/int/...)
    static void DrawMark()
    {
      var icon = EditorCompiler.IconSmall;
      if(icon != null)
        GUILayout.Label(icon, GUILayout.Width(MarkSize), GUILayout.Height(MarkSize));
    }

    //NOTE: a BHL module name field - the mark, the label+text field, and the autocomplete
    //      dropdown, all in one call. Recomputes completions every call rather than
    //      caching across frames (same as BHLModuleFieldDrawer) - GetSearchRoots/
    //      FindModuleCompletions are cheap and bounded (max), so this is fine for typical
    //      project sizes. For a serialized field, use [BHLModuleField] instead
    public static string DrawModuleField(string label, string value, int max = DefaultMaxCompletions)
    {
      EditorGUILayout.BeginHorizontal();
      DrawMark();
      var new_value = EditorGUILayout.TextField(label, value);
      EditorGUILayout.EndHorizontal();

      var completions = FindModuleCompletions(new_value, max);
      DrawCompletions(completions, picked =>
      {
        new_value = picked;
        GUIUtility.keyboardControl = 0;
      });

      return new_value;
    }

    //NOTE: a BHL function name field, resolved against 'module_name' (e.g. a sibling
    //      DrawModuleField's current value) - same func_pattern/'valid' semantics as
    //      FindFuncCompletions, additionally used here to tint the field when the current
    //      text doesn't match a declared function. For a serialized field, use
    //      [BHLFuncField] instead
    public static string DrawFuncField(string label, string module_name, string value, out bool? valid,
      string func_pattern = AnyFuncPattern, int max = DefaultMaxCompletions)
    {
      List<string> completions;
      (completions, valid) = string.IsNullOrEmpty(module_name)
        ? (new List<string>(), (bool?)null)
        : FindFuncCompletions(module_name, value, func_pattern, max);

      var prev_bg = GUI.backgroundColor;
      if(valid == false)
        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);

      EditorGUILayout.BeginHorizontal();
      DrawMark();
      var new_value = EditorGUILayout.TextField(label, value);
      EditorGUILayout.EndHorizontal();

      GUI.backgroundColor = prev_bg;

      DrawCompletions(completions, picked =>
      {
        new_value = picked;
        GUIUtility.keyboardControl = 0;
      });

      return new_value;
    }
  }

}
