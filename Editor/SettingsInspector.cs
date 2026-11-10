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
    readonly ConfFileView _projView = new ConfFileView("bhl.proj", path => { ProjectConf.ReadFromFile(path); return null; });
    List<string> _cachedSrcDirs = new List<string>();
    List<string> _cachedPostprocSources = new List<string>();

    readonly ConfFileView _bindgenView = new ConfFileView(BindGen.ConfName, path => { BindGen.LoadConfigs(path); return null; });
    List<string> _bindgenSources = new List<string>();
    List<string> _bindgenIncludes = new List<string>();
    List<string> _bindgenResultDirs = new List<string>();
    readonly Dictionary<string, bool> _pathArrayFoldouts = new Dictionary<string, bool>();
    readonly Dictionary<string, string> _pathArrayErrors = new Dictionary<string, string>();

    public override void OnInspectorGUI()
    {
      DrawMainFields();
      DrawResultPath();
    }

    //NOTE: split from Result Resource Path (below) so the Control Panel can interleave its
    //      own "Recompile On File Changes" toggle between the two - it must show below
    //      Recompile On Play (drawn here) but above Result Resource Path (see DrawResultPath)
    public void DrawMainFields()
    {
      DrawOverrideToggle();

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

      ApplySettings();

      var settings = (Settings)target;
      var resolved = settings.ResolvedBhlProjPath;

      bool projExists = File.Exists(resolved);

      if(projExists)
      {
        if(_projView.Draw(resolved))
          LoadProj(resolved);

        DrawPathArray("Script sources", _cachedSrcDirs, "No src_dirs configured in bhl.proj.", Directory.Exists);

        DrawBindgen(resolved);
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

      DrawPathArray("Postproc sources", _cachedPostprocSources, "No postproc_sources configured in bhl.proj.", File.Exists);

      var envVarsProp = serializedObject.FindProperty(nameof(Settings.postprocEnvVars));
      EditorGUILayout.PropertyField(envVarsProp, new GUIContent("Postproc Env Vars"), true);
      if(envVarsProp.isExpanded)
      {
        EditorGUI.indentLevel++;
        EditorGUILayout.HelpBox(envVarsProp.tooltip, MessageType.Info);
        EditorGUI.indentLevel--;
      }

      var asmdefDirProp = serializedObject.FindProperty(nameof(Settings.postprocAsmdefDir));
      EditorGUILayout.BeginHorizontal();
      EditorGUILayout.PropertyField(asmdefDirProp, new GUIContent("Postproc Asmdef Dir", asmdefDirProp.tooltip));
      if(GUILayout.Button("Clear Generated", GUILayout.Width(100)))
        PostprocBridge.Clear((Settings)target);
      EditorGUILayout.EndHorizontal();

      //NOTE: logVerbosity is an int (room for finer levels later), but exposed here as a
      //      plain on/off toggle - 0 (off) maps to "no console spam", any positive value
      //      to "on" (currently just 1, the only level bhl's compiler actually emits at)
      var logVerbosityProp = serializedObject.FindProperty(nameof(Settings.logVerbosity));
      bool verboseLogs = EditorGUILayout.Toggle(new GUIContent("Verbose Logs", logVerbosityProp.tooltip), logVerbosityProp.intValue > 0);
      logVerbosityProp.intValue = verboseLogs ? 1 : 0;

      var hotReloadProp = serializedObject.FindProperty(nameof(Settings.hotReloadOnRecompile));
      EditorGUILayout.PropertyField(hotReloadProp, new GUIContent("Hot Reload", hotReloadProp.tooltip));

      var recompileOnPlayProp = serializedObject.FindProperty(nameof(Settings.recompileOnPlay));
      EditorGUILayout.PropertyField(recompileOnPlayProp, new GUIContent("Recompile On Play", recompileOnPlayProp.tooltip));

      ApplySettings();
    }

    public void DrawResultPath()
    {
      serializedObject.Update();

      var bakedBundlePathProp = serializedObject.FindProperty(nameof(Settings.bakedBundlePath));
      var bakedBundlePathRect = EditorGUILayout.GetControlRect();
      EditorGUI.PropertyField(bakedBundlePathRect, bakedBundlePathProp, new GUIContent("Result Resource Path", bakedBundlePathProp.tooltip));
      if(string.IsNullOrEmpty(bakedBundlePathProp.stringValue) && Event.current.type == EventType.Repaint)
      {
        var fieldRect = EditorGUI.IndentedRect(bakedBundlePathRect);
        fieldRect.xMin += EditorGUIUtility.labelWidth;
        var prevColor = GUI.color;
        GUI.color = new Color(GUI.color.r, GUI.color.g, GUI.color.b, 0.4f);
        GUI.Label(fieldRect, "e.g. Assets/Resources/bhl.bytes - not baked if left empty");
        GUI.color = prevColor;
      }

      ApplySettings();
    }

    //NOTE: ApplyModifiedProperties returns true only when something actually changed.
    //      While overridden locally, target is Settings.LocalOverride (not a real asset,
    //      see Settings.Instance) - persist to EditorPrefs instead of AssetDatabase,
    //      which would silently no-op on a non-asset ScriptableObject anyway. Otherwise
    //      SaveAssetIfDirty flushes just this asset to disk, not the whole project
    //      (unlike AssetDatabase.SaveAssets), so it's cheap enough to call on every edit
    void ApplySettings()
    {
      if(!serializedObject.ApplyModifiedProperties())
        return;

      if(Settings.IsOverriddenLocally && target == Settings.Instance)
        Settings.SaveLocalOverride();
      else
        AssetDatabase.SaveAssetIfDirty(target);
    }

    //NOTE: lets a developer keep their own values for everything in Settings (e.g. a
    //      different debugPort, or postprocEnvVars pointing at a local checkout) without
    //      touching the shared, git-tracked BHLSettings.asset - stored in EditorPrefs
    //      (see Settings.IsOverriddenLocally), never committed
    static void DrawOverrideToggle()
    {
      if(Settings.IsOverriddenLocally)
      {
        EditorGUILayout.HelpBox(
          "Overriding settings locally (stored in EditorPrefs, not shared via git).",
          MessageType.Warning);
        if(GUILayout.Button("Stop Overriding", GUILayout.ExpandWidth(false)))
          Settings.IsOverriddenLocally = false;
      }
      else
      {
        if(GUILayout.Button("Override Locally", GUILayout.ExpandWidth(false)))
          Settings.IsOverriddenLocally = true;
      }

      EditorGUILayout.Space();
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
          ? Path.Combine(cacheDir, packageInfo.name + "@*", "bhl.proj")
          : Path.Combine(packageDir, "bhl.proj");
      }
    }

    //NOTE: bhl_bindgen.json next to bhl.proj, the bindings generator's config (see BindGen): shown as bhl.proj is,
    //      with the files it's made of, the configs it includes and where the bindings are written
    void DrawBindgen(string proj_path)
    {
      EditorGUILayout.Space();
      EditorGUILayout.LabelField(BindGen.ConfName, EditorStyles.boldLabel);

      var path = Path.Combine(Path.GetDirectoryName(proj_path), BindGen.ConfName);
      if(!File.Exists(path))
      {
        EditorGUILayout.HelpBox($"No {BindGen.ConfName} next to bhl.proj: the project doesn't generate bindings from C#.", MessageType.None);
        return;
      }

      if(_bindgenView.Draw(path))
        LoadBindgen(path);

      DrawPathArray("Binding sources", _bindgenSources, $"No sources matched in {BindGen.ConfName}.", File.Exists);
      if(_bindgenIncludes.Count > 0)
        DrawPathArray("Included configs", _bindgenIncludes, "", File.Exists);
      DrawPathArray("Bindings result dirs", _bindgenResultDirs, $"No result_dir in {BindGen.ConfName}.", Directory.Exists);
    }

    void LoadBindgen(string path)
    {
      _bindgenSources = new List<string>();
      _bindgenIncludes = new List<string>();
      _bindgenResultDirs = new List<string>();
      try
      {
        var confs = BindGen.LoadConfigs(path);
        foreach(var conf in confs)
        {
          _bindgenSources.AddRange(conf.SourceFiles);
          _bindgenResultDirs.Add(conf.result_dir);
          if(conf != confs[confs.Count - 1])
            _bindgenIncludes.Add(conf.FilePath);
        }
        _pathArrayErrors["Binding sources"] = null;
      }
      catch(Exception e)
      {
        _pathArrayErrors["Binding sources"] = e.Message;
      }
    }

    void LoadProj(string resolved)
    {
      _cachedSrcDirs = TryParseProjList(resolved, p => p.src_dirs, out var srcDirsError);
      _pathArrayErrors["Script sources"] = srcDirsError;
      _cachedPostprocSources = TryParseProjList(resolved, p => p.postproc_sources, out var postprocSourcesError);
      _pathArrayErrors["Postproc sources"] = postprocSourcesError;
    }

    //NOTE: list fields come back normalized (relative paths resolved against the proj
    //      file's own directory, wildcards expanded, non-existent entries dropped) by
    //      ProjectConf.Setup() - a parse/setup exception is reported via 'error' rather
    //      than swallowed, so a real problem doesn't just look like "nothing configured"
    static List<string> TryParseProjList(string proj_path, Func<ProjectConf, List<string>> select, out string error)
    {
      try
      {
        error = null;
        return select(ProjectConf.ReadFromFile(proj_path)) ?? new List<string>();
      }
      catch(Exception e)
      {
        error = e.Message;
        return new List<string>();
      }
    }

    //NOTE: collapsible dropdown, one path per line, each colored green if it exists on
    //      disk (per the given check) or red otherwise
    void DrawPathArray(string label, List<string> paths, string emptyMessage, Func<string, bool> exists)
    {
      EditorGUILayout.Space();

      if(paths.Count == 0)
      {
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        _pathArrayErrors.TryGetValue(label, out var error);
        if(error != null)
          EditorGUILayout.HelpBox($"Failed to parse: {error}", MessageType.Error);
        else
          EditorGUILayout.HelpBox(emptyMessage, MessageType.Warning);
        return;
      }

      _pathArrayFoldouts.TryGetValue(label, out bool expanded);
      expanded = EditorGUILayout.Foldout(expanded, $"{label} ({paths.Count})", true);
      _pathArrayFoldouts[label] = expanded;

      if(!expanded)
        return;

      EditorGUI.indentLevel++;
      foreach(var path in paths)
      {
        var prev = GUI.color;
        GUI.color = exists(path) ? Color.green : Color.red;
        EditorGUILayout.LabelField(path);
        GUI.color = prev;
      }
      EditorGUI.indentLevel--;
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
