<?php
//The PHP helpers of the bindings generator (bin~/bindgen.inc.php) on a config tree in a temp dir, no tool run:
//php Tests~/bindgen_php/bindgen_test.php
require __DIR__ . '/../../bin~/bindgen.inc.php';

function check(bool $cond, string $what)
{
  if(!$cond)
  {
    fwrite(STDERR, "FAIL: $what\n");
    exit(1);
  }
}

function put(string $path, string $text)
{
  @mkdir(dirname($path), 0777, true);
  file_put_contents($path, $text);
}

$root = UnityBHL\_bindgen_normalize(sys_get_temp_dir() . '/bindgen_test_' . getmypid());
put("$root/pkg/bhl_bindgen.json", json_encode(['sources' => ['Src/*.cs'], 'result_dir' => 'Gen']));
put("$root/pkg/Src/A.cs", '');
put("$root/pkg/Src/notes.txt", '');
put("$root/game/bhl_bindgen.json", json_encode([
  'sources' => ['Bind/**/*.cs', '!Bind/Gen/*.cs', 'Extra.cs'],
  'result_dir' => 'Bind/Gen',
  'includes' => ['../pkg/bhl_bindgen.json'],
]));
put("$root/game/Bind/B.cs", '');
put("$root/game/Bind/Sub/C.cs", '');
put("$root/game/Bind/Gen/register.cs", "//generated\n//dep: Bind/B.cs\n//dep: ../pkg/Src/A.cs\nusing bhl;\n//dep: not/a/dep.cs\n");
put("$root/game/Extra.cs", '');

$conf = "$root/game/./bhl_bindgen.json";
try
{
  check(UnityBHL\bindgen_conf_files($conf) === ["$root/pkg/bhl_bindgen.json", "$root/game/bhl_bindgen.json"], 'the included configs first, normalized');
  check(UnityBHL\bindgen_source_files($conf) === ["$root/game/Bind/B.cs", "$root/game/Bind/Sub/C.cs", "$root/game/Extra.cs", "$root/pkg/Src/A.cs"],
    'the sources: recursive, a file, an excluded glob (the output), the included config\'s, sorted');
  [$main, $deps] = UnityBHL\bindgen_last_output("$root/game/bhl_bindgen.json");
  check($main === "$root/game/Bind/Gen/register.cs" && $deps === ["$root/game/Bind/B.cs", "$root/game/../pkg/Src/A.cs"], 'the //dep: header only');
  check(UnityBHL\bindgen_last_output("$root/pkg/bhl_bindgen.json")[1] === [], 'no output yet');

  $inputs = UnityBHL\bindgen_inputs($conf, "$root/no_tool");
  check($inputs === ["$root/pkg/bhl_bindgen.json", "$root/game/bhl_bindgen.json", "$root/game/Bind/B.cs", "$root/game/../pkg/Src/A.cs"], 'the inputs: the configs and the deps of their outputs');

  $hash = UnityBHL\bindgen_sources_hash($conf);
  put("$root/game/Bind/D.cs", '');
  check(UnityBHL\bindgen_sources_hash($conf) !== $hash, 'a new source changes the hash');

  check(UnityBHL\bindgen_dist_dll("$root/no_tool") === null, 'no prebuilt tool');
  $threw = false;
  try { UnityBHL\bindgen_glob($root, 'a/**/b/*.cs'); } catch(Exception $e) { $threw = true; }
  check($threw, 'an unsupported glob throws');
}
finally
{
  UnityBHL\_bindgen_rmdir($root);
}
echo "OK\n";
