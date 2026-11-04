using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

//NOTE: bhl_bindgen.json; the fields are named as in it, so both Unity's JsonUtility and System.Text.Json read it
[Serializable]
internal class Config
{
  //NOTE: set by the JSON reader
#pragma warning disable CS0649
  public string @namespace;
  public string register_class;
  public List<string> decls = new List<string>();
  public List<string> sources = new List<string>();
  //NOTE: the dir of the output files, the generated ones no longer produced are deleted from it
  public string result_dir;
  public List<string> imports = new List<string>();
  public List<string> defines = new List<string>();
  //NOTE: types marshalled with Val.Encode/Decode (@bhl_custom_rw in meta)
  public List<string> custom_rw = new List<string>();
#pragma warning restore CS0649

  //NOTE: the dir of the config, its paths are relative to it
  [NonSerialized] public string Dir;
  [NonSerialized] public List<string> DeclFiles = new List<string>();
  [NonSerialized] public List<string> SourceFiles = new List<string>();

  //NOTE: from_json is the JSON reader of the host, comments aren't supported by Unity's one
  public static Config Load(string path, Func<string, Config> from_json)
  {
    var conf = from_json(File.ReadAllText(path));
    var dir = Path.GetDirectoryName(Path.GetFullPath(path));
    conf.Dir = Norm(dir);
    if(string.IsNullOrEmpty(conf.@namespace) || string.IsNullOrEmpty(conf.register_class) || string.IsNullOrEmpty(conf.result_dir))
      throw new GenException($"{path}: 'namespace', 'register_class' and 'result_dir' are required");
    conf.result_dir = Norm(Path.GetFullPath(Path.Combine(dir, conf.result_dir)));
    conf.DeclFiles = conf.decls.SelectMany(p => Glob(dir, p)).Distinct().ToList();
    conf.SourceFiles = conf.sources.SelectMany(p => Glob(dir, p)).Distinct().ToList();
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
