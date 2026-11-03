using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace UnityBHL
{
  //NOTE: runs the bindings generator (Tools~/bindgen) when a file the bindings are made of changes, so
  //      [BhlBind]/[BhlClass]/[BhlCustom] declarations take effect without the console. The generator is
  //      configured by bhl_bindgen.json next to bhl.proj, without it nothing happens. It's a dotnet tool
  //      for now, so the dotnet SDK is needed
  public class BindGen : AssetPostprocessor
  {
    [Serializable]
    class Conf
    {
      public string result_dir;
    }

    static readonly Regex decl_attr = new Regex(@"^\s*\[(BhlBind|BhlClass|BhlCustom)\b", RegexOptions.Multiline);

    [MenuItem("BHL/Generate Bindings")]
    static void GenerateMenu()
    {
      if(ConfPath() == null)
        Debug.LogError($"[BHL] bindgen: no {ConfName} next to bhl.proj");
      else
        Generate();
    }

    const string ConfName = "bhl_bindgen.json";

    static string ConfPath()
    {
      var settings = Settings.Instance;
      if(settings == null)
        return null;
      var path = Path.Combine(Path.GetDirectoryName(settings.ResolvedBhlProjPath), ConfName);
      return File.Exists(path) ? path : null;
    }

    //NOTE: the main output file, it lists the files the output is made of
    static string ResultPath(string conf_path)
    {
      var conf = JsonUtility.FromJson<Conf>(File.ReadAllText(conf_path));
      return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(conf_path), conf.result_dir, "register.cs"));
    }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] moved_from)
    {
      var changed = imported.Concat(deleted).Concat(moved).Concat(moved_from)
        .Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        .Select(Path.GetFullPath)
        .ToList();
      if(changed.Count == 0)
        return;

      var conf_path = ConfPath();
      if(conf_path == null)
        return;

      var result = ResultPath(conf_path);
      var deps = ReadDeps(conf_path, result);
      bool need = deps == null || changed.Any(p => deps.Contains(p) || IsDecl(p));
      if(need)
        Generate();
    }

    //NOTE: the files listed in the result's header, null if there's no result yet
    static HashSet<string> ReadDeps(string conf_path, string result)
    {
      if(!File.Exists(result))
        return null;
      var dir = Path.GetDirectoryName(conf_path);
      var deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach(var line in File.ReadLines(result))
      {
        if(!line.StartsWith("//"))
          break;
        if(line.StartsWith("//dep: "))
          deps.Add(Path.GetFullPath(Path.Combine(dir, line.Substring(7))));
      }
      return deps;
    }

    //NOTE: a new declarations file isn't listed in the result yet
    static bool IsDecl(string path)
    {
      if(!File.Exists(path))
        return false;
      return decl_attr.IsMatch(File.ReadAllText(path));
    }

    static void Generate()
    {
      var conf_path = ConfPath();
      var tool_dir = Path.Combine(PackageInfo.FindForAssembly(typeof(BindGen).Assembly).resolvedPath, "Tools~/bindgen");
      var dll = Path.Combine(tool_dir, "bin/Release/net8.0/bindgen.dll");

      var dotnet = FindDotnet();
      if(dotnet == null)
      {
        Debug.LogError("[BHL] bindgen: the dotnet SDK is not found, install it or put dotnet into PATH");
        return;
      }

      //NOTE: from scratch, gamectl builds it too, maybe by another dotnet SDK, and an incremental build
      //      over the output of another SDK loses files of it
      if(IsStale(tool_dir, dll) && Run(dotnet, $"build -c Release --no-incremental \"{Path.Combine(tool_dir, "bindgen.csproj")}\"", "Building the bindings generator", out var build_out) != 0)
      {
        Debug.LogError("[BHL] bindgen: building the generator failed\n" + build_out);
        return;
      }

      if(Run(dotnet, $"\"{dll}\" \"{conf_path}\"", "Generating BHL bindings", out var output) != 0)
      {
        Debug.LogError("[BHL] bindgen: " + output);
        return;
      }

      //NOTE: the generator writes the files only if they changed
      var stats = Regex.Match(output, @"(\d+) written, (\d+) deleted");
      if(stats.Success && (stats.Groups[1].Value != "0" || stats.Groups[2].Value != "0"))
      {
        Debug.Log("[BHL] " + output.Trim());
        AssetDatabase.Refresh();
      }
    }

    static bool IsStale(string tool_dir, string dll)
    {
      if(!File.Exists(dll))
        return true;
      var built = File.GetLastWriteTimeUtc(dll);
      return Directory.GetFiles(tool_dir, "*.cs").Concat(Directory.GetFiles(tool_dir, "*.csproj")).Concat(Directory.GetFiles(tool_dir, "*.props"))
        .Any(f => File.GetLastWriteTimeUtc(f) > built);
    }

    static int Run(string exe, string args, string title, out string output)
    {
      EditorUtility.DisplayProgressBar("BHL", title, 0.5f);
      try
      {
        var psi = new ProcessStartInfo(exe, args) {
          UseShellExecute = false,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          CreateNoWindow = true,
        };
        using(var p = Process.Start(psi))
        {
          var stderr = p.StandardError.ReadToEndAsync();
          var stdout = p.StandardOutput.ReadToEnd();
          p.WaitForExit();
          output = stdout + stderr.Result;
          return p.ExitCode;
        }
      }
      finally
      {
        EditorUtility.ClearProgressBar();
      }
    }

    //NOTE: an editor started from the Dock/Hub doesn't get the shell's PATH on macOS, so the usual
    //      install locations are checked as well
    static string FindDotnet()
    {
      var exe = Application.platform == RuntimePlatform.WindowsEditor ? "dotnet.exe" : "dotnet";
      var dirs = new List<string>();
      var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
      if(!string.IsNullOrEmpty(root))
        dirs.Add(root);
      dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
      dirs.AddRange(new[] {
        "/usr/local/share/dotnet", "/opt/homebrew/bin", "/usr/local/bin", "/usr/share/dotnet",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"),
      });
      return dirs.Where(d => !string.IsNullOrEmpty(d)).Select(d => Path.Combine(d, exe)).FirstOrDefault(File.Exists);
    }
  }
}
