using System;
using System.Collections.Generic;
using System.IO;
using bhl;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace UnityBHL
{

  [CustomEditor(typeof(Settings))]
  public class SettingsInspector : Editor
  {
    bool _showProjContents;
    string _cachedProjPath;
    string _cachedProjContents;
    string _editBuffer;
    DateTime _loadedWriteTimeUtc;
    string _lastValidatedText;
    string _lastValidationError;
    List<string> _cachedSrcDirs;
    List<string> _cachedPostprocSources;
    Vector2 _projContentsScroll;

    public override void OnInspectorGUI()
    {
      DrawMainFields();
      DrawResultPath();
    }

    //NOTE: split from Result Path (below) so the Control Panel can interleave its own
    //      "Recompile On File Changes" toggle between the two - it must show below
    //      Recompile On Play (drawn here) but above Result Path (see DrawResultPath)
    public void DrawMainFields()
    {
      serializedObject.Update();

      var debugPortProp = serializedObject.FindProperty(nameof(Settings.debugPort));
      EditorGUILayout.PropertyField(debugPortProp, new GUIContent("Debug Port", debugPortProp.tooltip));

      EditorGUILayout.Space();

      var bhlProjPathProp = serializedObject.FindProperty(nameof(Settings.bhlProjPath));
      EditorGUILayout.BeginHorizontal();
      EditorGUILayout.PropertyField(bhlProjPathProp, new GUIContent("bhl.proj Path", bhlProjPathProp.tooltip));
      if(GUILayout.Button("...", GUILayout.Width(30)))
        Browse();
      EditorGUILayout.EndHorizontal();

      serializedObject.ApplyModifiedProperties();

      var settings = (Settings)target;
      var resolved = settings.ResolvedBhlProjPath;

      bool projExists = File.Exists(resolved);

      if(projExists)
      {
        if(_cachedProjPath != resolved)
          LoadProj(resolved);

        var prev = GUI.color;
        GUI.color = Color.green;
        _showProjContents = EditorGUILayout.Foldout(_showProjContents, "Full path: " + resolved, true);
        GUI.color = prev;

        if(_showProjContents)
        {
          if(File.GetLastWriteTimeUtc(resolved) != _loadedWriteTimeUtc)
          {
            EditorGUILayout.HelpBox("File changed on disk since it was loaded here.", MessageType.Warning);
            if(GUILayout.Button("Reload", GUILayout.ExpandWidth(false)))
              LoadProj(resolved);
          }

          var height = EditorStyles.textArea.CalcHeight(new GUIContent(_editBuffer), EditorGUIUtility.currentViewWidth);

          _projContentsScroll = EditorGUILayout.BeginScrollView(_projContentsScroll, GUILayout.Height(200));
          _editBuffer = EditorGUILayout.TextArea(_editBuffer, EditorStyles.textArea, GUILayout.Height(height));
          EditorGUILayout.EndScrollView();

          bool dirty = _editBuffer != _cachedProjContents;

          if(_lastValidatedText != _editBuffer)
          {
            _lastValidatedText = _editBuffer;
            TryValidateProjText(_editBuffer, resolved, out _lastValidationError);
          }

          bool valid = _lastValidationError == null;
          if(dirty && !valid)
            EditorGUILayout.HelpBox("Invalid bhl.proj: " + _lastValidationError, MessageType.Error);

          EditorGUILayout.BeginHorizontal();
          using(new EditorGUI.DisabledScope(!dirty || !valid))
          {
            if(GUILayout.Button("Save", GUILayout.ExpandWidth(false)))
              SaveProj(resolved);
          }
          using(new EditorGUI.DisabledScope(!dirty))
          {
            if(GUILayout.Button("Revert", GUILayout.ExpandWidth(false)))
              _editBuffer = _cachedProjContents;
          }
          EditorGUILayout.EndHorizontal();
        }

        DrawPathArray("Script sources", _cachedSrcDirs, "No src_dirs configured in bhl.proj.", Directory.Exists);
      }
      else
      {
        var prev = GUI.color;
        GUI.color = Color.red;
        EditorGUILayout.LabelField("Full path", resolved);
        GUI.color = prev;

        if(GUILayout.Button("Create default", GUILayout.ExpandWidth(false)))
          CreateEmptyProj(resolved);
      }

      EditorGUILayout.Space();

      serializedObject.Update();

      var envVarsProp = serializedObject.FindProperty(nameof(Settings.postprocEnvVars));
      EditorGUILayout.PropertyField(envVarsProp, new GUIContent("Postproc Env Vars"), true);
      if(envVarsProp.isExpanded)
      {
        EditorGUI.indentLevel++;
        EditorGUILayout.HelpBox(envVarsProp.tooltip, MessageType.Info);
        EditorGUI.indentLevel--;
      }

      DrawPathArray("Postproc sources", _cachedPostprocSources, "No postproc_sources configured in bhl.proj.", File.Exists);

      var asmdefDirProp = serializedObject.FindProperty(nameof(Settings.postprocAsmdefDir));
      EditorGUILayout.BeginHorizontal();
      EditorGUILayout.PropertyField(asmdefDirProp, new GUIContent("Postproc Asmdef Dir", asmdefDirProp.tooltip));
      if(GUILayout.Button("Clear Generated", GUILayout.Width(100)))
        PostprocBridge.Clear((Settings)target);
      EditorGUILayout.EndHorizontal();

      var forceOnPlayProp = serializedObject.FindProperty(nameof(Settings.forceRecompileOnPlay));
      EditorGUILayout.PropertyField(forceOnPlayProp, new GUIContent("Recompile On Play", forceOnPlayProp.tooltip));

      serializedObject.ApplyModifiedProperties();
    }

    public void DrawResultPath()
    {
      serializedObject.Update();

      var bakedBundlePathProp = serializedObject.FindProperty(nameof(Settings.bakedBundlePath));
      var bakedBundlePathRect = EditorGUILayout.GetControlRect();
      EditorGUI.PropertyField(bakedBundlePathRect, bakedBundlePathProp, new GUIContent("Result Path", bakedBundlePathProp.tooltip));
      if(string.IsNullOrEmpty(bakedBundlePathProp.stringValue) && Event.current.type == EventType.Repaint)
      {
        var fieldRect = EditorGUI.IndentedRect(bakedBundlePathRect);
        fieldRect.xMin += EditorGUIUtility.labelWidth;
        var prevColor = GUI.color;
        GUI.color = new Color(GUI.color.r, GUI.color.g, GUI.color.b, 0.4f);
        GUI.Label(fieldRect, "not set - bhl.proj's result_file is ignored in the Editor");
        GUI.color = prevColor;
      }

      serializedObject.ApplyModifiedProperties();
    }

    void CreateEmptyProj(string resolved)
    {
      var dir = Path.GetDirectoryName(resolved);
      if(!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        Directory.CreateDirectory(dir);

      var include = ToRelativeUnixPath(dir, UnityBhlProjInclude);

      File.WriteAllText(resolved,
        "{\n" +
        "  \"src_dirs\" : [\"./\"],\n" +
        $"  \"includes\" : [\"{include}\"]\n" +
        "}");

      AssetDatabase.Refresh();
    }

    //NOTE: resolved via PackageInfo (not a plain path join) so it also works when this
    //      package is a git/registry dependency living in Library/PackageCache, not just
    //      when it's embedded directly under the project's Packages/ folder.
    //      A PackageCache folder name carries a version/commit-hash suffix that changes
    //      whenever the package is re-resolved (fresh install, version bump, different
    //      machine) - wildcard that segment so a generated bhl.proj doesn't go stale.
    string UnityBhlProjInclude
    {
      get
      {
        var scriptPath = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
        var packageInfo = PackageInfo.FindForAssetPath(scriptPath);
        if(packageInfo == null)
        {
          var root = Path.GetFullPath(Path.Combine(Settings.ProjectRoot, Path.GetDirectoryName(Path.GetDirectoryName(scriptPath))));
          return Path.Combine(root, "bhl.proj");
        }

        var packageDir = packageInfo.resolvedPath.TrimEnd('/', '\\');
        var cacheDir = Path.GetDirectoryName(packageDir);
        bool inPackageCache = string.Equals(Path.GetFileName(cacheDir), "PackageCache", StringComparison.OrdinalIgnoreCase);

        return inPackageCache
          ? Path.Combine(cacheDir, packageInfo.name + "@*")
          : Path.Combine(packageDir, "bhl.proj");
      }
    }

    void LoadProj(string resolved)
    {
      _cachedProjPath = resolved;
      _cachedProjContents = File.ReadAllText(resolved);
      _editBuffer = _cachedProjContents;
      _loadedWriteTimeUtc = File.GetLastWriteTimeUtc(resolved);
      _cachedSrcDirs = TryParseProjList(resolved, p => p.src_dirs);
      _cachedPostprocSources = TryParseProjList(resolved, p => p.postproc_sources);
    }

    void SaveProj(string resolved)
    {
      File.WriteAllText(resolved, _editBuffer);
      AssetDatabase.Refresh();
      LoadProj(resolved);
    }

    //NOTE: validated against a scratch copy sitting next to the real bhl.proj (not the
    //      system temp dir) - relative entries (src_dirs, includes, ...) are normalized
    //      against the proj file's own directory, so validating from an unrelated
    //      directory made every relative path fail to resolve
    static void TryValidateProjText(string text, string resolvedPath, out string error)
    {
      var dir = Path.GetDirectoryName(resolvedPath);
      var tmp = Path.Combine(dir, "~" + Path.GetRandomFileName() + ".proj");
      try
      {
        File.WriteAllText(tmp, text);
        ProjectConf.ReadFromFile(tmp);
        error = null;
      }
      catch(Exception e)
      {
        error = e.Message;
      }
      finally
      {
        File.Delete(tmp);
      }
    }

    //NOTE: list fields come back normalized (relative paths resolved against the proj
    //      file's own directory) by ProjectConf.Setup() - swallow parse errors since this
    //      only feeds an informational display, not the actual compile
    static List<string> TryParseProjList(string proj_path, Func<ProjectConf, List<string>> select)
    {
      try
      {
        return select(ProjectConf.ReadFromFile(proj_path));
      }
      catch
      {
        return new List<string>();
      }
    }

    //NOTE: shows an array-literal-style list ([a, b, ...]), each entry colored green if
    //      it exists on disk (per the given check) or red otherwise
    static void DrawPathArray(string label, List<string> paths, string emptyMessage, Func<string, bool> exists)
    {
      EditorGUILayout.Space();
      EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
      if(paths.Count == 0)
      {
        EditorGUILayout.HelpBox(emptyMessage, MessageType.Warning);
        return;
      }

      EditorGUILayout.BeginHorizontal();
      GUILayout.Label("[", GUILayout.ExpandWidth(false));
      for(int i = 0; i < paths.Count; ++i)
      {
        var path = paths[i];
        var prev = GUI.color;
        GUI.color = exists(path) ? Color.green : Color.red;
        GUILayout.Label(path, GUILayout.ExpandWidth(false));
        GUI.color = prev;

        if(i < paths.Count - 1)
          GUILayout.Label(", ", GUILayout.ExpandWidth(false));
      }
      GUILayout.Label("]", GUILayout.ExpandWidth(false));
      EditorGUILayout.EndHorizontal();
    }

    static string ToRelativeUnixPath(string from_dir, string to_path)
    {
      var rel = Path.GetRelativePath(from_dir, to_path);
      return rel.Replace(Path.DirectorySeparatorChar, '/');
    }

    void Browse()
    {
      var picked = EditorUtility.OpenFilePanel("Select bhl.proj", Settings.ProjectRoot, "proj");
      if(string.IsNullOrEmpty(picked))
        return;

      //NOTE: stored relative to the project root so it stays valid across different checkouts
      serializedObject.FindProperty(nameof(Settings.bhlProjPath)).stringValue = ToRelativeUnixPath(Settings.ProjectRoot, picked);
    }
  }

}
