using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    //NOTE: every lookup below takes the bhl.proj to search, null - the project's (Settings.BhlProj); another one (e.g.
    //      the tests of a tool with a bhl.proj of their own) comes from LoadProj
    static BHLProjectConfig Proj(BHLProjectConfig proj) => proj ?? Settings.Instance?.BhlProj;

    static readonly Dictionary<string, (DateTime mtime, BHLProjectConfig proj)> _projs = new Dictionary<string, (DateTime, BHLProjectConfig)>();

    //NOTE: a bhl.proj by its path, parsed again only when the file changes; null - none or not a bhl.proj
    public static BHLProjectConfig LoadProj(string path)
    {
      if(string.IsNullOrEmpty(path) || !File.Exists(path))
        return null;
      var full = Path.GetFullPath(path);
      var mtime = File.GetLastWriteTimeUtc(full);
      if(_projs.TryGetValue(full, out var cached) && cached.mtime == mtime)
        return cached.proj;

      BHLProjectConfig result = null;
      try
      {
        var proj = bhl.ProjectConf.ReadFromFile(full);
        result = new BHLProjectConfig(proj.inc_path, proj.inc_dirs, proj.src_dirs, proj.defines, proj.result_file);
      }
      catch(Exception) {}
      _projs[full] = (mtime, result);
      return result;
    }

    //NOTE: inc_dirs, else src_dirs - same fallback ProjectConf.Setup() uses for inc_path.
    //      Both already absolute/resolved by Setup(), which ReadFromFile always runs
    public static IEnumerable<string> GetSearchRoots(BHLProjectConfig proj = null)
    {
      var cfg = Proj(proj);
      if(cfg == null)
        yield break;

      var dirs = cfg.inc_dirs.Count > 0 ? cfg.inc_dirs : cfg.src_dirs;
      foreach(var dir in dirs)
        yield return dir;
    }

    //NOTE: TryMapModuleToFile already searches these same roots - no separate fallback needed
    public static string FindModuleFile(string module_name, BHLProjectConfig proj = null)
    {
      if(string.IsNullOrEmpty(module_name))
        return null;

      var cfg = Proj(proj);
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
    //NOTE: by the roots, a list each: consumers of different bhl.proj don't evict each other
    static readonly Dictionary<string, List<string>> _moduleListCache = new Dictionary<string, List<string>>();

    //NOTE: forces the next FindModuleCompletions call to re-walk the search roots
    public static void Invalidate()
    {
      _moduleListCache.Clear();
    }

    static List<string> GetAllModuleNames(BHLProjectConfig proj)
    {
      var roots = string.Join("|", GetSearchRoots(proj));
      if(_moduleListCache.TryGetValue(roots, out var cached))
        return cached;

      var result = new List<string>();
      try
      {
        foreach(var root in GetSearchRoots(proj))
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

      result.Sort(StringComparer.Ordinal);
      _moduleListCache[roots] = result;
      return result;
    }

    //NOTE: fuzzy, best first: the chars of 'partial' in order (e.g. 'tm3p' - test_m3_perf), see FuzzyScore
    public static List<string> FindModuleCompletions(string partial, int max = DefaultMaxCompletions, BHLProjectConfig proj = null)
    {
      if(string.IsNullOrEmpty(partial))
        return new List<string>();
      return FuzzyRank(partial, GetAllModuleNames(proj).Where(name => name != partial), n => n, max);
    }

    //NOTE: the items whose names match the query best first (FuzzyScore), then the shorter, then by name
    public static List<T> FuzzyRank<T>(string query, IEnumerable<T> items, Func<T, string> name_of, int max)
    {
      return items
        .Select(item => (item, name: name_of(item), score: FuzzyScore(query, name_of(item))))
        .Where(m => m.score > int.MinValue)
        .OrderByDescending(m => m.score)
        .ThenBy(m => m.name.Length)
        .ThenBy(m => m.name, StringComparer.Ordinal)
        .Take(max)
        .Select(m => m.item)
        .ToList();
    }

    //NOTE: the chars of the query in the name in order, case-insensitive: a char at the start of a word ('/', '_', '.',
    //      a capital) and a run of them count more, a gap less; int.MinValue - no match. A substring is a match too
    public static int FuzzyScore(string query, string name)
    {
      int score = 0;
      int last = -1;
      int q = 0;
      for(int i = 0; i < name.Length && q < query.Length; ++i)
      {
        if(char.ToLowerInvariant(name[i]) != char.ToLowerInvariant(query[q]))
          continue;
        bool word_start = i == 0 || name[i - 1] == '/' || name[i - 1] == '_' || name[i - 1] == '.' ||
          (char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]));
        score += 1 + (word_start ? 8 : 0) + (last == i - 1 ? 5 : 0) - (last >= 0 ? Math.Min(i - last - 1, 3) : Math.Min(i, 3));
        last = i;
        ++q;
      }
      return q == query.Length ? score : int.MinValue;
    }

    //NOTE: a return type before the name is optional ('func float Foo(', 'coro func []string Bar(')
    public const string AnyFuncPattern = @"\bfunc\s+(?:[^(\n]*?\s+)?([a-zA-Z_][a-zA-Z0-9_]*)\s*\(";
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
    //      An empty partial lists them in the order of the file, else fuzzy, best first (FuzzyScore)
    public static (List<(string name, BHLSymbolKind kind)> completions, bool? valid) FindFuncCompletions(
      string module_name, string partial, BHLSymbolKind kind = BHLSymbolKind.Func,
      string func_pattern = AnyFuncPattern, int max = DefaultMaxCompletions, BHLProjectConfig proj = null)
    {
      var completions = new List<(string name, BHLSymbolKind kind)>();

      var file_path = FindModuleFile(module_name, proj);
      if(file_path == null)
        return (completions, null);

      try
      {
        var text = File.ReadAllText(file_path);
        bool found_exact = string.IsNullOrEmpty(partial);
        var all = new List<(string name, BHLSymbolKind kind)>();
        foreach(var sym in ScanSymbols(text, kind, func_pattern))
        {
          if(sym.Name == partial)
          {
            found_exact = true;
            continue;
          }
          if(!all.Exists(c => c.name == sym.Name))
            all.Add((sym.Name, sym.Kind));
        }
        completions = string.IsNullOrEmpty(partial) ? all.Take(max).ToList() : FuzzyRank(partial, all, c => c.name, max);
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
    //NOTE: highlighted - the row Up / Down chose (Enter takes it), -1 - none
    public static void DrawCompletions(Rect rect, List<string> list, Action<string> on_pick, int highlighted = -1)
    {
      if(list.Count == 0)
        return;

      GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);

      var row = new Rect(rect.x + 2, rect.y + 2, rect.width - 4, EditorGUIUtility.singleLineHeight);
      for(int i = 0; i < list.Count; ++i)
      {
        //NOTE: a tint of the row's button, the style is opaque
        var prev_bg = GUI.backgroundColor;
        if(i == highlighted)
          GUI.backgroundColor = HighlightColor;
        bool clicked = GUI.Button(row, list[i], EditorStyles.miniButton);
        GUI.backgroundColor = prev_bg;
        if(clicked)
        {
          on_pick(list[i]);
          break;
        }
        row.y += EditorGUIUtility.singleLineHeight + 2;
      }
    }

    //NOTE: layout-based convenience for a plain OnGUI() caller (e.g. a custom EditorWindow)
    public static void DrawCompletions(List<string> list, Action<string> on_pick, int highlighted = -1)
    {
      if(list.Count == 0)
        return;

      var rect = GUILayoutUtility.GetRect(0, GetCompletionsHeight(list.Count), GUILayout.ExpandWidth(true));
      DrawCompletions(rect, list, on_pick, highlighted);
    }

    static Color HighlightColor => new Color(0.35f, 0.65f, 1f);

    //NOTE: the completion highlighted in a field, by the field's control and the text it was chosen for: a new text
    //      starts at the first one again
    static readonly Dictionary<string, (string text, int index)> _highlighted = new Dictionary<string, (string, int)>();

    //NOTE: the highlighted row of the field's completions while it has the keyboard, -1 - none
    static int Highlighted(string control, string text, int count)
    {
      if(count == 0 || GUI.GetNameOfFocusedControl() != control)
        return -1;
      return _highlighted.TryGetValue(control, out var h) && h.text == text ? Math.Min(h.index, count - 1) : 0;
    }

    //NOTE: the keys of a field with completions while it has the keyboard, before the field takes them: Up / Down move
    //      the highlighted row (round), Enter - its index, else -1; the events are used
    static int HandleKeys(string control, string text, int count)
    {
      var e = Event.current;
      if(e == null || e.type != EventType.KeyDown || count == 0 || GUI.GetNameOfFocusedControl() != control)
        return -1;
      int index = Math.Max(Highlighted(control, text, count), 0);
      switch(e.keyCode)
      {
        case KeyCode.DownArrow:
          index = (index + 1) % count;
          break;
        case KeyCode.UpArrow:
          index = (index - 1 + count) % count;
          break;
        case KeyCode.Return:
        case KeyCode.KeypadEnter:
          e.Use();
          _highlighted.Remove(control);
          return index;
        default:
          return -1;
      }
      _highlighted[control] = (text, index);
      e.Use();
      return -1;
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
    //      Enter in the field takes the best completion. proj - the bhl.proj to search (LoadProj), null - the project's
    public static string DrawModuleField(string label, string value, int max = DefaultMaxCompletions, BHLProjectConfig proj = null)
    {
      bool valid = string.IsNullOrEmpty(value) || FindModuleFile(value, proj) != null;

      var prev_bg = GUI.backgroundColor;
      if(!valid)
        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);

      string control = "BHLModuleBrowser.Module." + label;
      int picked = HandleKeys(control, value, FindModuleCompletions(value, max, proj).Count);
      EditorGUILayout.BeginHorizontal();
      DrawMark();
      GUI.SetNextControlName(control);
      var new_value = EditorGUILayout.TextField(label, value);
      if(GUILayout.Button("Refresh", GUILayout.Width(60)))
        Invalidate();
      EditorGUILayout.EndHorizontal();

      GUI.backgroundColor = prev_bg;

      var completions = FindModuleCompletions(new_value, max, proj);
      int highlighted = Highlighted(control, new_value, completions.Count);
      //NOTE: drawn all the same: the controls of every event are the ones of the layout
      if(picked >= 0 && picked < completions.Count)
      {
        new_value = completions[picked];
        GUIUtility.keyboardControl = 0;
      }
      DrawCompletions(completions, choice =>
      {
        new_value = choice;
        GUIUtility.keyboardControl = 0;
      }, highlighted);

      return new_value;
    }

    //NOTE: same, resolved against module_name; also red-tints the field when invalid.
    //      For a serialized field, use [BHLFuncField] instead. kind lets a caller also
    //      match class declarations within the module (tagged in the dropdown when All)
    //      Enter in the field takes the best completion. proj - the bhl.proj to search (LoadProj), null - the project's
    public static string DrawFuncField(string label, string module_name, string value, out bool? valid,
      BHLSymbolKind kind = BHLSymbolKind.Func, string func_pattern = AnyFuncPattern, int max = DefaultMaxCompletions,
      BHLProjectConfig proj = null)
    {
      List<(string name, BHLSymbolKind kind)> completions;
      (completions, valid) = string.IsNullOrEmpty(module_name)
        ? (new List<(string name, BHLSymbolKind kind)>(), (bool?)null)
        : FindFuncCompletions(module_name, value, kind, func_pattern, max, proj);

      string control = "BHLModuleBrowser.Func." + label;
      int picked = HandleKeys(control, value, completions.Count);

      //NOTE: valid == null means "couldn't even check" (no module, or module doesn't
      //      resolve) - that's still worth flagging once the user has actually typed a
      //      name here, same as an outright mismatch (valid == false). A still-empty
      //      field isn't tinted either way - nothing to complain about yet
      var prev_bg = GUI.backgroundColor;
      if(valid != true && !string.IsNullOrEmpty(value))
        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);

      EditorGUILayout.BeginHorizontal();
      DrawMark();
      GUI.SetNextControlName(control);
      var new_value = EditorGUILayout.TextField(label, value);
      EditorGUILayout.EndHorizontal();

      GUI.backgroundColor = prev_bg;

      //NOTE: the completions of the value before the field, the same rows as the layout's
      int highlighted = Highlighted(control, value, completions.Count);
      if(picked >= 0 && picked < completions.Count)
      {
        new_value = completions[picked].name;
        GUIUtility.keyboardControl = 0;
      }

      var labels = new List<string>();
      foreach(var c in completions)
        labels.Add(kind == BHLSymbolKind.All ? $"{c.name} [{c.kind}]" : c.name);

      DrawCompletions(labels, picked =>
      {
        var idx = labels.IndexOf(picked);
        if(idx >= 0)
          new_value = completions[idx].name;
        GUIUtility.keyboardControl = 0;
      }, highlighted);

      return new_value;
    }
  }

}
