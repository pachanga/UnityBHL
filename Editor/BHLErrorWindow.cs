using System.IO;
using UnityEditor;
using UnityEngine;
using bhl;

namespace UnityBHL
{

  //NOTE: pops up on any compile failure (Rebuild, background Recompile, or the live-reload
  //      path) via EditorCompiler.LogCompileErrors - the sole UI surface for errors (the
  //      Control Panel used to also show them inline, which just duplicated this window
  //      whenever both happened to be visible at once). Errors still duplicate to Unity's
  //      own Console too (see LogCompileErrors) - this popup exists for the "Open" button,
  //      which reliably jumps to .bhl files even when they live outside the Unity project,
  //      unlike double-clicking a Console entry
  public class BHLErrorWindow : EditorWindow
  {
    Vector2 _scroll;

    public static void ShowErrors()
    {
      var window = GetWindow<BHLErrorWindow>(utility: true, title: "BHL Errors");
      window.minSize = new Vector2(520, 200);
      window.Show();
      window.Focus();
    }

    //NOTE: main-thread only (GetWindow/Close) - call after a compile that's known to
    //      have succeeded, not from EditorCompiler.Compile itself, which must stay
    //      safe to call from a background thread
    public static void HideIfNoErrors()
    {
      if(EditorCompiler.LastErrors.Count > 0)
        return;

      if(HasOpenInstances<BHLErrorWindow>())
        GetWindow<BHLErrorWindow>().Close();
    }

    void OnGUI()
    {
      if(EditorCompiler.LastErrors.Count == 0)
      {
        EditorGUILayout.LabelField("No errors.");
        return;
      }

      _scroll = EditorGUILayout.BeginScrollView(_scroll);
      DrawErrorsList();
      EditorGUILayout.EndScrollView();
    }

    static void DrawErrorsList()
    {
      var errors = EditorCompiler.LastErrors;
      for(int i = 0; i < errors.Count; ++i)
        DrawError(i + 1, errors[i]);
    }

    //NOTE: OpenProject returns false (silently) if the configured editor's integration
    //      declines the request - e.g. some fail for files outside the Unity project,
    //      which .bhl sources frequently are. Fall back to the OS file association so
    //      the button always does something, even without landing on the exact line.
    static void Open(string full_path, int line, int column)
    {
      if(!Unity.CodeEditor.CodeEditor.CurrentEditor.OpenProject(full_path, line, column))
        EditorUtility.OpenWithDefaultApp(full_path);
    }

    static void DrawError(int number, ICompileError err)
    {
      bool has_file = !string.IsNullOrEmpty(err.file) && err.file != "?" && File.Exists(err.file);

      EditorGUILayout.BeginVertical(EditorStyles.helpBox);

      EditorGUILayout.BeginHorizontal();
      GUILayout.Label(EditorGUIUtility.IconContent("console.erroricon.sml"), GUILayout.Width(20), GUILayout.Height(20));

      if(has_file)
      {
        EditorGUILayout.SelectableLabel($"#{number} {err.file}({err.range.start.line},{err.range.start.column})",
          EditorStyles.boldLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        if(GUILayout.Button("Open", GUILayout.Width(50)))
          Open(Path.GetFullPath(err.file), err.range.start.line, err.range.start.column);
      }
      else
      {
        EditorGUILayout.LabelField($"#{number}", EditorStyles.boldLabel);
      }
      EditorGUILayout.EndHorizontal();

      var text_height = EditorStyles.wordWrappedLabel.CalcHeight(new GUIContent(err.text), EditorGUIUtility.currentViewWidth);
      EditorGUILayout.SelectableLabel(err.text, EditorStyles.wordWrappedLabel, GUILayout.Height(text_height));
      EditorGUILayout.EndVertical();
      EditorGUILayout.Space();
    }
  }

}
