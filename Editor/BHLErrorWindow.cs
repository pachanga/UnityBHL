using System.IO;
using UnityEditor;
using UnityEngine;
using bhl;

namespace UnityBHL
{

  //NOTE: pops up on any compile failure (Rebuild, background Recompile, or the live-reload
  //      path) via EditorCompiler.LogCompileErrors - not just visible if the Control Panel
  //      happens to be open
  public class BHLErrorWindow : EditorWindow
  {
    Vector2 _scroll;

    //NOTE: skipped if the Control Panel is already open - it shows the same error
    //      list inline (DrawErrorsList), so the popup would just be redundant
    public static void ShowErrors()
    {
      if(HasOpenInstances<ControlPanel>())
      {
        //NOTE: relying on OnInspectorUpdate's periodic Repaint isn't immediate enough
        //      (can lag until the window is focused) - force it right when errors change
        ControlPanel.RepaintIfOpen();
        return;
      }

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

      ControlPanel.RepaintIfOpen();
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

    //NOTE: shared with ControlPanel, so an open Control Panel shows errors in the same
    //      format as this popup, instead of duplicating the per-error drawing code
    public static void DrawErrorsList()
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
