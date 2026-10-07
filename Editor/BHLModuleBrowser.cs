using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UnityBHL
{

  //NOTE: reusable BHL module/function lookup + autocomplete for Editor tools; extracted
  //      from ATF once a second consumer needed the same logic
  public static class BHLModuleBrowser
  {
    public const int DefaultMaxCompletions = 8;

    //NOTE: inc_dirs, else src_dirs - same fallback ProjectConf.Setup() uses for inc_path.
    //      Both already absolute/resolved by Setup(), which ReadFromFile always runs
    public static IEnumerable<string> GetSearchRoots()
    {
      var cfg = Settings.Instance?.BhlProj;
      if(cfg == null)
        yield break;

      var dirs = cfg.inc_dirs.Count > 0 ? cfg.inc_dirs : cfg.src_dirs;
      foreach(var dir in dirs)
        yield return dir;
    }

    //NOTE: TryMapModuleToFile already searches these same roots - no separate fallback needed
    public static string FindModuleFile(string module_name)
    {
      if(string.IsNullOrEmpty(module_name))
        return null;

      var cfg = Settings.Instance?.BhlProj;
      if(cfg != null && cfg.TryMapModuleToFile(module_name, out var file))
        return file;

      return null;
    }

    //NOTE: the recursive *.bhl walk below has to visit every filesystem entry under each
    //      root, not just the matching ones - in a project where src_dirs/inc_dirs
    //      includes a root with tens of thousands of unrelated files (e.g. generated
    //      level data living alongside real .bhl sources), that's genuinely expensive.
    //      FindModuleCompletions runs on every OnGUI call (a consumer like ATFWnd forces
    //      ~10 repaints/sec via Repaint()), so the walk only actually runs once - lazily,
    //      on first use - and is cached from then on, same as ClassIntrospection's
    //      compile-once-until-Refresh approach; re-run only if the roots themselves
    //      change (e.g. bhl.proj repointed in Settings) or Invalidate() is called
    //      explicitly (e.g. a "Refresh" button, for a .bhl file added outside Unity)
    static List<string> _moduleListCache;
    static string _moduleListCacheRoots;

    //NOTE: forces the next FindModuleCompletions call to re-walk the search roots
    public static void Invalidate()
    {
      _moduleListCache = null;
    }

    static List<string> GetAllModuleNames()
    {
      var roots = string.Join("|", GetSearchRoots());
      if(_moduleListCache != null && roots == _moduleListCacheRoots)
        return _moduleListCache;

      var result = new List<string>();
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
            result.Add(rel.Substring(0, rel.Length - 4)); // strip .bhl
          }
        }
      }
      catch(Exception) {}

      _moduleListCache = result;
      _moduleListCacheRoots = roots;
      return result;
    }

    //NOTE: matches names containing 'partial', not just prefix
    public static List<string> FindModuleCompletions(string partial, int max = DefaultMaxCompletions)
    {
      var result = new List<string>();
      if(string.IsNullOrEmpty(partial))
        return result;

      foreach(var name in GetAllModuleNames())
      {
        if(name != partial && name.Contains(partial))
        {
          result.Add(name);
          if(result.Count >= max)
            break;
        }
      }

      return result;
    }

    public const string AnyFuncPattern = @"\bfunc\s+([a-zA-Z_][a-zA-Z0-9_]*)\s*\(";
    public const string AnyClassPattern = @"\bclass\s+([a-zA-Z_][a-zA-Z0-9_]*)\b";

    //NOTE: also doubles as a per-result tag (FindFuncCompletions never tags a result
    //      as All - only used as a parameter there to mean "search both")
    public enum BHLSymbolKind
    {
      All,
      Func,
      Class
    }

    //NOTE: a func/class found while scanning a file, with its fully dot-qualified name
    //      (enclosing namespace(s) included, e.g. "Foo.Bar")
    readonly struct ScannedSymbol
    {
      public readonly string Name;
      public readonly BHLSymbolKind Kind;
      public ScannedSymbol(string name, BHLSymbolKind kind) { Name = name; Kind = kind; }
    }

    //NOTE: replaces comment/string-literal contents with spaces (preserving length and
    //      newlines) so a stray '{'/'}' - or a keyword-looking word - inside either
    //      doesn't corrupt the namespace-brace tracking below. Doesn't handle every
    //      BHL lexical edge case (e.g. verbatim strings), just the common ones
    static string StripCommentsAndStrings(string text)
    {
      var sb = new StringBuilder(text.Length);
      int i = 0;
      while(i < text.Length)
      {
        char c = text[i];

        if(c == '/' && i + 1 < text.Length && text[i + 1] == '/')
        {
          while(i < text.Length && text[i] != '\n')
          {
            sb.Append(' ');
            ++i;
          }
          continue;
        }

        if(c == '/' && i + 1 < text.Length && text[i + 1] == '*')
        {
          sb.Append(' ', 2);
          i += 2;
          while(i < text.Length && !(text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/'))
          {
            sb.Append(text[i] == '\n' ? '\n' : ' ');
            ++i;
          }
          if(i < text.Length)
          {
            sb.Append(' ', 2);
            i += 2;
          }
          continue;
        }

        if(c == '"')
        {
          sb.Append(' ');
          ++i;
          while(i < text.Length && text[i] != '"')
          {
            //NOTE: an escaped quote (\") doesn't end the string
            if(text[i] == '\\' && i + 1 < text.Length)
            {
              sb.Append(' ', 2);
              i += 2;
              continue;
            }
            sb.Append(text[i] == '\n' ? '\n' : ' ');
            ++i;
          }
          if(i < text.Length)
          {
            sb.Append(' ');
            ++i;
          }
          continue;
        }

        sb.Append(c);
        ++i;
      }
      return sb.ToString();
    }

    static readonly Regex NamespaceBlockPattern = new Regex(@"\bnamespace\s+([a-zA-Z_][a-zA-Z0-9_]*)\s*\{");

    //NOTE: finds every `namespace Name { ... }` block's full extent (via plain brace
    //      counting - safe here since comments/strings are already stripped) so a
    //      symbol found inside one can be prefixed with its name. Nested namespaces
    //      produce nested (start, end) ranges
    static List<(int start, int end, string name)> FindNamespaceBlocks(string stripped_text)
    {
      var result = new List<(int start, int end, string name)>();

      foreach(Match m in NamespaceBlockPattern.Matches(stripped_text))
      {
        int brace_pos = m.Index + m.Length - 1;
        int depth = 1;
        int j = brace_pos + 1;
        while(j < stripped_text.Length && depth > 0)
        {
          if(stripped_text[j] == '{')
            ++depth;
          else if(stripped_text[j] == '}')
            --depth;
          ++j;
        }

        result.Add((brace_pos, j, m.Groups[1].Value));
      }

      return result;
    }

    //NOTE: dot-joined names of every namespace block enclosing 'position', outermost first
    static string GetNamespacePrefix(List<(int start, int end, string name)> blocks, int position)
    {
      if(blocks.Count == 0)
        return "";

      var enclosing = new List<(int start, string name)>();
      foreach(var b in blocks)
        if(position >= b.start && position < b.end)
          enclosing.Add((b.start, b.name));

      if(enclosing.Count == 0)
        return "";

      enclosing.Sort((a, b) => a.start.CompareTo(b.start));

      var sb = new StringBuilder();
      foreach(var e in enclosing)
      {
        sb.Append(e.name);
        sb.Append('.');
      }
      return sb.ToString();
    }

    //NOTE: every func_pattern/AnyClassPattern match in 'text', as (fully-qualified name,
    //      kind) - used by FindFuncCompletions so namespace-prefixing works correctly
    static IEnumerable<ScannedSymbol> ScanSymbols(string text, BHLSymbolKind kind, string func_pattern)
    {
      var stripped = StripCommentsAndStrings(text);
      var blocks = FindNamespaceBlocks(stripped);

      if(kind == BHLSymbolKind.All || kind == BHLSymbolKind.Func)
        foreach(Match m in Regex.Matches(stripped, func_pattern))
          yield return new ScannedSymbol(GetNamespacePrefix(blocks, m.Index) + m.Groups[1].Value, BHLSymbolKind.Func);

      if(kind == BHLSymbolKind.All || kind == BHLSymbolKind.Class)
        foreach(Match m in Regex.Matches(stripped, AnyClassPattern))
          yield return new ScannedSymbol(GetNamespacePrefix(blocks, m.Index) + m.Groups[1].Value, BHLSymbolKind.Class);
    }

    //NOTE: kind/func_pattern constrain which symbols count (for Func, Group(1) must
    //      capture the name; class matching always uses AnyClassPattern). Scoped to a
    //      single already-known module file - cheap, no project-wide scanning.
    //      valid: null = module file not found, true = exact match (or partial empty), false = no match
    public static (List<(string name, BHLSymbolKind kind)> completions, bool? valid) FindFuncCompletions(
      string module_name, string partial, BHLSymbolKind kind = BHLSymbolKind.Func,
      string func_pattern = AnyFuncPattern, int max = DefaultMaxCompletions)
    {
      var completions = new List<(string name, BHLSymbolKind kind)>();

      var file_path = FindModuleFile(module_name);
      if(file_path == null)
        return (completions, null);

      try
      {
        var text = File.ReadAllText(file_path);
        bool found_exact = string.IsNullOrEmpty(partial);
        foreach(var sym in ScanSymbols(text, kind, func_pattern))
        {
          var name = sym.Name;
          if(name == partial)
          {
            found_exact = true;
            continue;
          }
          if(!string.IsNullOrEmpty(partial) && !name.Contains(partial))
            continue;
          if(!completions.Exists(c => c.name == name) && completions.Count < max)
            completions.Add((name, sym.Kind));
        }
        return (completions, found_exact);
      }
      catch(Exception)
      {
        return (completions, null);
      }
    }

    //NOTE: height for 'count' rows - lets a non-layout caller (e.g. GetPropertyHeight) reserve space
    public static float GetCompletionsHeight(int count)
    {
      return count == 0 ? 0f : count * (EditorGUIUtility.singleLineHeight + 2) + 4;
    }

    //NOTE: rect-based primitive; the layout overload below reserves its own rect and delegates here
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

    //NOTE: layout-based convenience for a plain OnGUI() caller (e.g. a custom EditorWindow)
    public static void DrawCompletions(List<string> list, Action<string> on_pick)
    {
      if(list.Count == 0)
        return;

      var rect = GUILayoutUtility.GetRect(0, GetCompletionsHeight(list.Count), GUILayout.ExpandWidth(true));
      DrawCompletions(rect, list, on_pick);
    }

    const float MarkSize = 16f;

    //NOTE: drawn explicitly - GUIContent's image isn't rendered for value-type fields.
    //      Public so a consumer with its own, differently-sourced picker (e.g.
    //      ScriptBHLInspector's Class field, backed by ClassIntrospection rather than
    //      this class's own Find*Completions) can still show the same visual cue
    public static void DrawMark()
    {
      var icon = EditorCompiler.IconSmall;
      if(icon != null)
        GUILayout.Label(icon, GUILayout.Width(MarkSize), GUILayout.Height(MarkSize));
    }

    //NOTE: mark + labeled field + dropdown in one call (completions filtered fresh every
    //      call, cheap; the underlying module list itself is cached - see Invalidate()
    //      above - so a "Refresh" button is offered here for a .bhl file added/removed
    //      outside Unity, where nothing would otherwise trigger a re-scan). Also
    //      red-tints the field when the typed name doesn't exactly match an existing
    //      module, same as DrawFuncField - a partial match can still show completions
    //      below without the field itself looking like a valid, committed value. For a
    //      serialized field, use [BHLModuleField] instead
    public static string DrawModuleField(string label, string value, int max = DefaultMaxCompletions)
    {
      bool valid = string.IsNullOrEmpty(value) || FindModuleFile(value) != null;

      var prev_bg = GUI.backgroundColor;
      if(!valid)
        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);

      EditorGUILayout.BeginHorizontal();
      DrawMark();
      var new_value = EditorGUILayout.TextField(label, value);
      if(GUILayout.Button("Refresh", GUILayout.Width(60)))
        Invalidate();
      EditorGUILayout.EndHorizontal();

      GUI.backgroundColor = prev_bg;

      var completions = FindModuleCompletions(new_value, max);
      DrawCompletions(completions, picked =>
      {
        new_value = picked;
        GUIUtility.keyboardControl = 0;
      });

      return new_value;
    }

    //NOTE: same, resolved against module_name; also red-tints the field when invalid.
    //      For a serialized field, use [BHLFuncField] instead. kind lets a caller also
    //      match class declarations within the module (tagged in the dropdown when All)
    public static string DrawFuncField(string label, string module_name, string value, out bool? valid,
      BHLSymbolKind kind = BHLSymbolKind.Func, string func_pattern = AnyFuncPattern, int max = DefaultMaxCompletions)
    {
      List<(string name, BHLSymbolKind kind)> completions;
      (completions, valid) = string.IsNullOrEmpty(module_name)
        ? (new List<(string name, BHLSymbolKind kind)>(), (bool?)null)
        : FindFuncCompletions(module_name, value, kind, func_pattern, max);

      //NOTE: valid == null means "couldn't even check" (no module, or module doesn't
      //      resolve) - that's still worth flagging once the user has actually typed a
      //      name here, same as an outright mismatch (valid == false). A still-empty
      //      field isn't tinted either way - nothing to complain about yet
      var prev_bg = GUI.backgroundColor;
      if(valid != true && !string.IsNullOrEmpty(value))
        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);

      EditorGUILayout.BeginHorizontal();
      DrawMark();
      var new_value = EditorGUILayout.TextField(label, value);
      EditorGUILayout.EndHorizontal();

      GUI.backgroundColor = prev_bg;

      var labels = new List<string>();
      foreach(var c in completions)
        labels.Add(kind == BHLSymbolKind.All ? $"{c.name} [{c.kind}]" : c.name);

      DrawCompletions(labels, picked =>
      {
        var idx = labels.IndexOf(picked);
        if(idx >= 0)
          new_value = completions[idx].name;
        GUIUtility.keyboardControl = 0;
      });

      return new_value;
    }
  }

}
