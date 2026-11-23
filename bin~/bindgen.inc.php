<?php
namespace UnityBHL;

//The bindings generator (Tools~/bindgen) from PHP: BHL bindings made from C# sources without compiling them. No
//dependencies: composer loads it (autoload.files), bin~/bhl-bindgen runs it from a shell. Paths are with '/' on
//every OS, as the generator's. See README, «Bindings generator»

//The tool's project dir; $tool - another one (e.g. a checkout of the tool under development)
function bindgen_tool_dir(?string $tool = null) : string
{
  return $tool ?? _bindgen_normalize(dirname(__DIR__) . '/Tools~/bindgen');
}

//The sources of the tool: its own and the generator it shares with the Unity editor (Editor/BindGen)
function bindgen_tool_files(?string $tool = null) : array
{
  $dir = bindgen_tool_dir($tool);
  return array_merge(glob("$dir/*.cs"), glob("$dir/*.csproj"), glob("$dir/../../Editor/BindGen/*.cs"));
}

function bindgen_tool_dll(?string $tool = null) : string
{
  return bindgen_tool_dir($tool) . '/bin/Release/net8.0/bindgen.dll';
}

//The env var BHL_BINDGEN_REBUILD=1: the tool is built from the sources, always, rather than prebuilt
function bindgen_rebuild() : bool
{
  return (bool)getenv('BHL_BINDGEN_REBUILD');
}

//The prebuilt tool, dist/ of the tool dir: there in the release zip unitybhl.zip, not in the tag's archive or a
//checkout; null if there's none or BHL_BINDGEN_REBUILD is set
function bindgen_dist_dll(?string $tool = null) : ?string
{
  $dll = bindgen_tool_dir($tool) . '/dist/bindgen.dll';
  return !bindgen_rebuild() && is_file($dll) ? $dll : null;
}

//Built only when stale and from scratch, without bin/ and obj/: a build over the leftovers of another one (another
//dotnet SDK, dist.sh's publish) loses files of the output (bindgen.runtimeconfig.json), --no-incremental too
function bindgen_build_tool(?string $tool = null)
{
  $dir = bindgen_tool_dir($tool);
  if(!bindgen_rebuild() && !_bindgen_older(bindgen_tool_dll($tool), bindgen_tool_files($tool)))
    return;
  foreach(['bin', 'obj'] as $sub)
    _bindgen_rmdir("$dir/$sub");
  _bindgen_shell('dotnet build -c Release --nologo -v q ' . escapeshellarg("$dir/bindgen.csproj"));
}

//The tool to run: the prebuilt one, else built from the sources if needed
function bindgen_dll(?string $tool = null) : string
{
  $dll = bindgen_dist_dll($tool);
  if($dll !== null)
    return $dll;
  bindgen_build_tool($tool);
  return bindgen_tool_dll($tool);
}

//Runs the tool with the args (the config and its flags: --watch, ...), the exit code
function bindgen_exec(array $args, ?string $tool = null) : int
{
  //NOTE: the dll is run by dotnet rather than by its native launcher, which may look for the runtime
  //      in a dotnet install other than the one in PATH
  $cmd = 'dotnet ' . implode(' ', array_map('escapeshellarg', array_merge([bindgen_dll($tool)], $args)));
  passthru($cmd, $code);
  return $code;
}

//Generates the bindings of the config, the output is written only if it changed
function bindgen_run(string $conf_file, ?string $tool = null)
{
  if(($code = bindgen_exec([$conf_file], $tool)) !== 0)
    throw new \Exception("bindgen of $conf_file failed: $code");
}

//The main output file of the config and the files the last output was made of, listed by the '//dep:' lines of it
//as the Unity editor reads them; whether they exist isn't checked
function bindgen_last_output(string $conf_file) : array
{
  $dir = dirname($conf_file);
  $conf = _bindgen_read_conf($conf_file);
  if(empty($conf['result_dir']))
    throw new \Exception("No 'result_dir' in $conf_file");

  $main = "$dir/{$conf['result_dir']}/register.cs";
  $deps = [];
  if(is_file($main))
  {
    foreach(file($main, FILE_IGNORE_NEW_LINES) as $line)
    {
      if(strpos($line, '//') !== 0)
        break;
      if(strpos($line, '//dep: ') === 0)
        $deps[] = "$dir/" . substr($line, 7);
    }
  }
  return [$main, $deps];
}

//The config and the ones it includes ('includes', relative to it), recursively: the included ones first, as the
//generator loads them (it reports a cycle or a missing one)
function bindgen_conf_files(string $conf_file) : array
{
  $conf_file = _bindgen_normalize($conf_file);
  $files = [];
  $conf = is_file($conf_file) ? _bindgen_read_conf($conf_file) : [];
  foreach($conf['includes'] ?? [] as $inc)
  {
    foreach(bindgen_conf_files(dirname($conf_file) . "/$inc") as $file)
    {
      if(!in_array($file, $files))
        $files[] = $file;
    }
  }
  if(!in_array($conf_file, $files))
    $files[] = $conf_file;
  return $files;
}

//What the generated bindings are made of, e.g. for the staleness of a BHL build: the configs (the included ones
//too), the tool and the files the last outputs were made of (the declarations among them).
//shortcut: a new file with [BhlBind] methods or declarations isn't listed until the next run, the source files are
function bindgen_inputs(string $conf_file, ?string $tool = null) : array
{
  $files = bindgen_tool_files($tool);
  foreach(bindgen_conf_files($conf_file) as $file)
  {
    $files[] = $file;
    $files = array_merge($files, bindgen_last_output($file)[1]);
  }
  return array_values(array_filter($files, 'is_file'));
}

//The files of the 'sources' globs of the configs (the included ones too) but the ones of their globs starting with
//'!', as the generator reads them (dir/*.cs or dir/**/*.cs, relative to the config), sorted
function bindgen_source_files(string $conf_file) : array
{
  $files = [];
  foreach(bindgen_conf_files($conf_file) as $file)
  {
    $conf = _bindgen_read_conf($file);
    $included = [];
    $excluded = [];
    foreach($conf['sources'] ?? [] as $pattern)
    {
      if(strpos($pattern, '!') === 0)
        $excluded = array_merge($excluded, bindgen_glob(dirname($file), substr($pattern, 1)));
      else
        $included = array_merge($included, bindgen_glob(dirname($file), $pattern));
    }
    $files = array_merge($files, array_diff($included, $excluded));
  }
  $files = array_values(array_unique($files));
  sort($files);
  return $files;
}

//The glob of the generator: a file, dir/<mask> or dir/**/<mask> (the files of the dir and its subdirs)
function bindgen_glob(string $base_dir, string $pattern) : array
{
  $full = _bindgen_normalize("$base_dir/$pattern");
  $star = strpos($full, '*');
  if($star === false)
    return is_file($full) ? [$full] : [];

  $root = substr($full, 0, strrpos(substr($full, 0, $star), '/'));
  $mask = substr($full, strlen($root) + 1);
  $rec = strpos($mask, '**/') === 0;
  if($rec)
    $mask = substr($mask, 3);
  if(strpos($mask, '/') !== false || strpos($mask, '**') !== false)
    throw new \Exception("Unsupported glob '$pattern'");
  if(!is_dir($root))
    return [];
  $files = $rec ? _bindgen_files_rec($root) : glob("$root/*");
  return array_values(array_filter(array_map('UnityBHL\_bindgen_normalize', $files), fn($f) => is_file($f) && fnmatch($mask, basename($f))));
}

//The hash of the list of the source files: a file added, deleted or renamed with an old mtime changes it
function bindgen_sources_hash(string $conf_file) : string
{
  return md5(implode("\n", bindgen_source_files($conf_file)));
}

////////////////////////////////////////////////// internals

//'/' separators, '.' and '..' resolved
function _bindgen_normalize(string $path) : string
{
  $path = preg_replace('/\/+/', '/', str_replace('\\', '/', $path));
  $parts = [];
  foreach(explode('/', $path) as $part)
  {
    if($part == '.')
      continue;
    if($part == '..')
      array_pop($parts);
    else
      $parts[] = $part;
  }
  return implode('/', $parts);
}

function _bindgen_read_conf(string $file) : array
{
  $text = @file_get_contents($file);
  if($text === false)
    throw new \Exception("Can't read $file");
  $conf = json_decode($text, true);
  if(!is_array($conf))
    throw new \Exception("Bad JSON in $file");
  return $conf;
}

//The file is missing or older than one of the deps
function _bindgen_older(string $file, array $deps) : bool
{
  if(!is_file($file))
    return true;
  $mtime = filemtime($file);
  foreach($deps as $dep)
  {
    if(is_file($dep) && filemtime($dep) > $mtime)
      return true;
  }
  return false;
}

function _bindgen_files_rec(string $dir) : array
{
  $files = [];
  foreach(new \RecursiveIteratorIterator(new \RecursiveDirectoryIterator($dir, \FilesystemIterator::SKIP_DOTS)) as $path => $info)
  {
    if(!$info->isDir())
      $files[] = $path;
  }
  return $files;
}

function _bindgen_rmdir(string $dir)
{
  if(!is_dir($dir))
    return;
  foreach(new \RecursiveIteratorIterator(new \RecursiveDirectoryIterator($dir, \FilesystemIterator::SKIP_DOTS), \RecursiveIteratorIterator::CHILD_FIRST) as $path => $info)
    $info->isDir() && !$info->isLink() ? rmdir($path) : unlink($path);
  rmdir($dir);
}

function _bindgen_shell(string $cmd)
{
  passthru($cmd, $code);
  if($code !== 0)
    throw new \Exception("'$cmd' failed: $code");
}
