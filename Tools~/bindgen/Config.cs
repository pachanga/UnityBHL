using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

public class Config
{
  public string Namespace { get; set; }
  public string RegisterClass { get; set; }
  public List<string> Decls { get; set; } = new List<string>();
  public List<string> Sources { get; set; } = new List<string>();
  //NOTE: the dir of the output files, the generated ones no longer produced are deleted from it
  public string ResultDir { get; set; }
  public List<string> Imports { get; set; } = new List<string>();
  public List<string> Defines { get; set; } = new List<string>();
  //NOTE: types marshalled with Val.Encode/Decode (@bhl_custom_rw in meta)
  public List<string> CustomRw { get; set; } = new List<string>();

  //NOTE: the dir of the config, its paths are relative to it
  public string Dir;
  public List<string> DeclFiles = new List<string>();
  public List<string> SourceFiles = new List<string>();

  public static Config Load(string path)
  {
    var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, ReadCommentHandling = JsonCommentHandling.Skip };
    var conf = JsonSerializer.Deserialize<Config>(File.ReadAllText(path), opts);
    var dir = Path.GetDirectoryName(Path.GetFullPath(path));
    conf.Dir = Norm(dir);
    if(string.IsNullOrEmpty(conf.Namespace) || string.IsNullOrEmpty(conf.RegisterClass) || string.IsNullOrEmpty(conf.ResultDir))
      throw new GenException($"{path}: 'namespace', 'register_class' and 'result_dir' are required");
    conf.ResultDir = Norm(Path.GetFullPath(Path.Combine(dir, conf.ResultDir)));
    conf.DeclFiles = conf.Decls.SelectMany(p => Glob(dir, p)).Distinct().ToList();
    conf.SourceFiles = conf.Sources.SelectMany(p => Glob(dir, p)).Distinct().ToList();
    return conf;
  }

  //NOTE: supports 'dir/file.cs', 'dir/*.cs' and 'dir/**/*.cs'
  static IEnumerable<string> Glob(string base_dir, string pattern)
  {
    var full = Norm(Path.GetFullPath(Path.Combine(base_dir, pattern)));
    int star = full.IndexOf('*');
    if(star < 0)
      return File.Exists(full) ? new[] { full } : new string[0];

    var root = full.Substring(0, full.LastIndexOf('/', star));
    var mask = full.Substring(root.Length + 1);
    bool rec = mask.StartsWith("**/");
    if(rec)
      mask = mask.Substring(3);
    if(mask.Contains('/') || mask.Contains("**"))
      throw new GenException($"unsupported glob '{pattern}'");
    if(!Directory.Exists(root))
      return new string[0];
    return Directory.GetFiles(root, mask, rec ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
      .Select(Norm).OrderBy(f => f, StringComparer.Ordinal);
  }

  public static string Norm(string path) => path.Replace('\\', '/');
}
