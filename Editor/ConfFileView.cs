using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityBHL
{

  //NOTE: a config file shown in the Settings (bhl.proj, bhl_bindgen.json): its full path, green as it exists, and
  //      unfolded its text editable in place (Save, Revert, a warning when it changed on disk), validated as it's
  //      edited by the given check of a scratch copy next to it - relative paths in it are resolved against its own dir
  class ConfFileView
  {
    readonly string _name;
    readonly Func<string, string> _validate;

    bool _expanded;
    string _path;
    string _contents;
    string _buffer;
    DateTime _writeTimeUtc;
    string _lastValidated;
    string _error;
    Vector2 _scroll;

    //NOTE: validate gets the path of a file with the text to check and returns the error, null if it's valid
    public ConfFileView(string name, Func<string, string> validate)
    {
      _name = name;
      _validate = validate;
    }

    //NOTE: true when the file was (re)loaded, e.g. it's another one or it's saved: what's derived from it is stale
    public bool Draw(string path)
    {
      bool loaded = false;
      if(_path != path)
      {
        Load(path);
        loaded = true;
      }

      var prev = GUI.color;
      GUI.color = Color.green;
      _expanded = EditorGUILayout.Foldout(_expanded, "Full path: " + path, true);
      GUI.color = prev;

      if(!_expanded)
        return loaded;

      if(File.GetLastWriteTimeUtc(path) != _writeTimeUtc)
      {
        EditorGUILayout.HelpBox("File changed on disk since it was loaded here.", MessageType.Warning);
        if(GUILayout.Button("Reload", GUILayout.ExpandWidth(false)))
        {
          Load(path);
          loaded = true;
        }
      }

      var height = EditorStyles.textArea.CalcHeight(new GUIContent(_buffer), EditorGUIUtility.currentViewWidth);
      _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(200));
      _buffer = EditorGUILayout.TextArea(_buffer, EditorStyles.textArea, GUILayout.Height(height));
      EditorGUILayout.EndScrollView();

      bool dirty = _buffer != _contents;

      if(_lastValidated != _buffer)
      {
        _lastValidated = _buffer;
        _error = Validate(_buffer, path);
      }

      if(dirty && _error != null)
        EditorGUILayout.HelpBox($"Invalid {_name}: {_error}", MessageType.Warning);

      EditorGUILayout.BeginHorizontal();
      using(new EditorGUI.DisabledScope(!dirty))
      {
        if(GUILayout.Button("Save", GUILayout.ExpandWidth(false)))
        {
          File.WriteAllText(path, _buffer);
          AssetDatabase.Refresh();
          Load(path);
          loaded = true;
        }
        if(GUILayout.Button("Revert", GUILayout.ExpandWidth(false)))
          _buffer = _contents;
      }
      EditorGUILayout.EndHorizontal();

      return loaded;
    }

    void Load(string path)
    {
      _path = path;
      _contents = File.ReadAllText(path);
      _buffer = _contents;
      _writeTimeUtc = File.GetLastWriteTimeUtc(path);
    }

    //NOTE: a scratch copy next to the real file (not in the system temp dir) - its relative entries (src_dirs,
    //      includes, sources, ...) are resolved against the file's own directory
    string Validate(string text, string path)
    {
      var tmp = Path.Combine(Path.GetDirectoryName(path), "~" + Path.GetRandomFileName() + Path.GetExtension(path));
      try
      {
        File.WriteAllText(tmp, text);
        return _validate(tmp);
      }
      catch(Exception e)
      {
        return e.Message;
      }
      finally
      {
        File.Delete(tmp);
      }
    }
  }

}
