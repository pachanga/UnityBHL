using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

//NOTE: runs the real Generator with BothParser, as 'bindgen --parser=both' used to: the generator drives the
//      two passes (ParseDecls over the declarations files, then ParseSource over the rest) itself, so the test
//      checks the parsers on the sequence a real run has, not on one made up here
[TestFixture]
public class ParserTests
{
  static string FixturesDir
  {
    get
    {
      //NOTE: from the test assembly back to the sources: the fixtures are read as a real project's are
      var dir = TestContext.CurrentContext.TestDirectory;
      for(var d = new DirectoryInfo(dir); d != null; d = d.Parent)
      {
        var candidate = Path.Combine(d.FullName, "Fixtures", "bhl_bindgen.json");
        if(File.Exists(candidate))
          return Path.GetDirectoryName(candidate);
      }
      throw new FileNotFoundException("Fixtures/bhl_bindgen.json not found above " + dir);
    }
  }

  static List<Config> LoadFixtureConfig(string result_dir)
  {
    var opts = new JsonSerializerOptions { IncludeFields = true, ReadCommentHandling = JsonCommentHandling.Skip };
    var confs = Config.LoadAll(Path.Combine(FixturesDir, "bhl_bindgen.json"),
      json => JsonSerializer.Deserialize<Config>(json, opts));
    //NOTE: nothing of the fixtures is written, the generated code isn't what's checked here
    foreach(var conf in confs)
      conf.result_dir = result_dir;
    return confs;
  }

  static string Run(ICsParser parser)
  {
    var result_dir = Path.Combine(Path.GetTempPath(), "bindgen_tests_" + Guid.NewGuid().ToString("N"));
    try
    {
      return new Generator(LoadFixtureConfig(result_dir), parser).Run().summary;
    }
    finally
    {
      if(Directory.Exists(result_dir))
        Directory.Delete(result_dir, true);
    }
  }

  //NOTE: the check itself: BothParser throws a GenException naming the file and the differing line of the
  //      model dump when the hand parser and Roslyn disagree about anything in the fixtures
  [Test]
  public void HandParserMatchesRoslyn()
  {
    var defines = LoadFixtureConfig("unused").Last().defines;
    Assert.DoesNotThrow(() => Run(new BothParser(new HandParser(defines), new RoslynParser(defines))));
  }

  //NOTE: the fixtures are worth checking against only if they're actually parsed into something
  [Test]
  public void FixturesCoverSomething()
  {
    var defines = LoadFixtureConfig("unused").Last().defines;
    var summary = Run(new HandParser(defines));
    var funcs = int.Parse(summary.Split(' ')[1]);
    var classes = int.Parse(summary.Split("funcs, ")[1].Split(' ')[0]);
    Assert.That(funcs, Is.GreaterThan(5), "funcs in the fixtures: " + summary);
    Assert.That(classes, Is.GreaterThan(3), "classes in the fixtures: " + summary);
  }

  static IEnumerable<string> SyntaxFiles =>
    Directory.GetFiles(Path.Combine(Path.GetDirectoryName(FixturesDir), "Syntax"), "*.cs");

  //NOTE: straight to the parsers, no generator: the file may hold what the generator rejects, the two only
  //      have to build the same model of it (or fail the same way - an error is dumped as the model too)
  [TestCaseSource(nameof(SyntaxFiles))]
  public void HandParserMatchesRoslynOnSyntax(string file)
  {
    var src = File.ReadAllText(file);
    var bound = new HashSet<string> { "Thing", "Other", "Third", "IThing", "Unit" };
    foreach(var defines in new[] { new List<string> { "FIXTURE_DEFINE" }, new List<string>(),
      new List<string> { "FIXTURE_DEFINE", "NESTED_DEFINE" } })
    {
      var both = new BothParser(new HandParser(defines), new RoslynParser(defines));
      AssertSameModel(() => both.ParseDecls(file, src, new CsDecls()), defines);
      AssertSameModel(() => both.ParseSource(file, src, bound, new CsDecls()), defines);
    }
  }

  //NOTE: keeps the test above honest. BothParser dumps an error as the model, so if the hand parser rejected
  //      the whole file the two would only be compared as identical error strings - agreeing on nothing
  [TestCaseSource(nameof(SyntaxFiles))]
  public void SyntaxFixtureParsesIntoAModel(string file)
  {
    var defines = new List<string> { "FIXTURE_DEFINE" };
    var decls = new CsDecls();
    Assert.DoesNotThrow(() => new HandParser(defines).ParseDecls(file, File.ReadAllText(file), decls));
    Assert.That(decls.Binds.Count + decls.BindAlls.Count + decls.Classes.Count + decls.Coroutines.Count,
      Is.GreaterThan(3), "the syntax fixture parses into nothing: " + file);
  }

  //NOTE: only a difference between the parsers fails. The two rejecting the same thing the same way is an
  //      agreement as well (BothParser dumps an error as the model), and it rethrows the hand parser's own
  //      exception after the comparison - which is what a file of shapes the generator rejects is full of
  static void AssertSameModel(Action parse, List<string> defines)
  {
    try
    {
      parse();
    }
    catch(GenException e)
    {
      if(e.Message.Contains("differs from Roslyn"))
        Assert.Fail($"defines [{string.Join(",", defines)}]: {e.Message}");
    }
  }

  //NOTE: one generation at a time per config: the editor and the console tool write the same result_dirs,
  //      and a run deletes the generated files which aren't in its own set (BindGenLock)
  [Test]
  public void OneGenerationAtATimePerConfig()
  {
    var defines = LoadFixtureConfig("unused").Last().defines;
    var key = Path.Combine(FixturesDir, "bhl_bindgen.json");

    using(var held = BindGenLock.TryAcquire(key, 0))
    {
      Assert.That(held, Is.Not.Null, "the lock is free to begin with");
      Assert.That(BindGenLock.TryAcquire(key, 0), Is.Null, "a second one doesn't get it");
      Assert.Throws<BindGenBusyException>(() => Run(new HandParser(defines)), "a run doesn't start");

      //NOTE: another config is another lock
      var other = BindGenLock.TryAcquire(key + ".other", 0);
      Assert.That(other, Is.Not.Null, "a different config doesn't contend");
      other.Dispose();
    }

    //NOTE: released - and it stays usable, the lock file isn't left locked
    Assert.DoesNotThrow(() => Run(new HandParser(defines)), "the lock is free again");
  }

  //NOTE: both parsers read the same #if: a define of the config takes the branch, the other one is skipped
  [Test]
  public void DefinesAreHonoured()
  {
    var defines = LoadFixtureConfig("unused").Last().defines;
    var summary = Run(new BothParser(new HandParser(defines), new RoslynParser(defines)));
    Assert.That(summary, Does.Contain("funcs"), summary);

    //NOTE: without the define the other branch is taken, and the parsers have to agree there too
    var none = new List<string>();
    Assert.DoesNotThrow(() => Run(new BothParser(new HandParser(none), new RoslynParser(none))));
  }
}
