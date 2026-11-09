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
  //NOTE: globs relative to the config, dir/*.cs or dir/**/*.cs; one starting with '!' excludes its files
  //      (e.g. the generated code under a source dir)
  public List<string> sources = new List<string>();
  //NOTE: the dir of the output files, the generated ones no longer produced are deleted from it
  public string result_dir;
  public List<string> imports = new List<string>();
  public List<string> defines = new List<string>();
  //NOTE: types marshalled with Val.Encode/Decode (@bhl_custom_rw in meta)
  public List<string> custom_rw = new List<string>();
  //NOTE: a text file of '<C# type> <BHL type>' lines: the BHL names of the types declared elsewhere (e.g. by meta)
  //      when they differ from the C# ones, a type not listed is named in BHL as in C#
  public string type_names_file;
  //NOTE: the configs of other bindings (e.g. of a package) this one uses, relative to it: each is generated to its
  //      own result_dir, their declarations are known here, not the other way round
  public List<string> includes = new List<string>();
  //NOTE: the C# namespace of the declarations: one in a namespace under it is in the BHL namespace of the rest,
  //      [BhlClass(typeof(T))] interface Shape in <decls_namespace>.Geo is Geo.Shape; a name given explicitly is kept
  public string decls_namespace;
#pragma warning restore CS0649

  //NOTE: the dir of the config, its paths are relative to it
  [NonSerialized] public string Dir;
  [NonSerialized] public string FilePath;
  [NonSerialized] public List<Config> Includes = new List<Config>();
  [NonSerialized] public List<string> SourceFiles = new List<string>();
  [NonSerialized] public Dictionary<string, string> TypeNames = new Dictionary<string, string>();

  //NOTE: from_json is the JSON reader of the host, comments aren't supported by Unity's one
  public static Config Load(string path, Func<string, Config> from_json)
  {
    var conf = from_json(File.ReadAllText(path));
    var dir = Path.GetDirectoryName(Path.GetFullPath(path));
    conf.Dir = Norm(dir);
    conf.FilePath = Norm(Path.GetFullPath(path));
    if(string.IsNullOrEmpty(conf.@namespace) || string.IsNullOrEmpty(conf.register_class) || string.IsNullOrEmpty(conf.result_dir))
      throw new GenException($"{path}: 'namespace', 'register_class' and 'result_dir' are required");
    conf.result_dir = Norm(Path.GetFullPath(Path.Combine(dir, conf.result_dir)));
    var excluded = new HashSet<string>(conf.sources.Where(p => p.StartsWith("!")).SelectMany(p => Glob(dir, p.Substring(1))));
    conf.SourceFiles = conf.sources.Where(p => !p.StartsWith("!")).SelectMany(p => Glob(dir, p)).Distinct().Where(f => !excluded.Contains(f)).ToList();
    if(!string.IsNullOrEmpty(conf.type_names_file))
      conf.TypeNames = LoadTypeNames(Norm(Path.GetFullPath(Path.Combine(dir, conf.type_names_file))));
    return conf;
  }

  //NOTE: the config and the ones it includes, recursively: an included one goes before the ones including it,
  //      the config itself is the last
  public static List<Config> LoadAll(string path, Func<string, Config> from_json)
  {
    var all = new List<Config>();
    LoadIncluding(Norm(Path.GetFullPath(path)), from_json, all, new List<string>());
    return all;
  }

  static Config LoadIncluding(string path, Func<string, Config> from_json, List<Config> all, List<string> loading)
  {
    var loaded = all.FirstOrDefault(c => c.FilePath == path);
    if(loaded != null)
      return loaded;
    if(loading.Contains(path))
      throw new GenException($"{path}: it includes itself ({string.Join(" -> ", loading)} -> {path})");
    if(!File.Exists(path))
      throw new GenException($"{(loading.Count > 0 ? loading[loading.Count - 1] + ": " : "")}included '{path}' is not found");
    loading.Add(path);
    var conf = Load(path, from_json);
    foreach(var inc in conf.includes)
      conf.Includes.Add(LoadIncluding(Norm(Path.GetFullPath(Path.Combine(conf.Dir, inc))), from_json, all, loading));
    loading.RemoveAt(loading.Count - 1);
    all.Add(conf);
    return conf;
  }

  //NOTE: the configs it includes, recursively
  public IEnumerable<Config> AllIncludes()
  {
    return Includes.SelectMany(i => i.AllIncludes().Prepend(i)).Distinct();
  }

  static Dictionary<string, string> LoadTypeNames(string path)
  {
    if(!File.Exists(path))
      throw new GenException($"type names file '{path}' is not found, it's made by the project's build");
    var names = new Dictionary<string, string>();
    int line_num = 0;
    foreach(var line in File.ReadLines(path))
    {
      ++line_num;
      var l = line.Trim();
      if(l.Length == 0 || l.StartsWith("#"))
        continue;
      var parts = l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
      if(parts.Length != 2)
        throw new GenException($"{path}({line_num}): expected '<C# type> <BHL type>'");
      if(names.TryGetValue(parts[0], out var other) && other != parts[1])
        throw new GenException($"{path}({line_num}): C# type '{parts[0]}' is named both '{other}' and '{parts[1]}'");
      names[parts[0]] = parts[1];
    }
    return names;
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
