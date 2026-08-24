using System.Collections.Generic;
using System.IO;
using bhl;

namespace UnityBHL
{

  //NOTE: compiles the project and resolves classes/fields for ScriptBHLInspector's
  //      pickers. Compiles at most ONCE, lazily, on first use - the result is then
  //      served from cache indefinitely until the Inspector's explicit "Refresh"
  //      button calls Invalidate(). Deliberately not tied to any automatic/periodic
  //      trigger (a mouse-move repaint, a timer, another system's compile event) -
  //      that's what caused compiling on every Inspector repaint, and separately
  //      caused stale/reordered results earlier when invalidation was wired to
  //      other systems' compile timing instead of an explicit user action.
  public static class ClassIntrospection
  {
    public struct FieldInfo
    {
      public string Name;
      public FieldType? Type;
    }

    static List<string> _modulesCache;

    //NOTE: for the Inspector's manual "Refresh" - the only thing that triggers a
    //      recompile; files may have been added/removed/renamed, or edited
    public static void Invalidate()
    {
      _modulesCache = null;
      _compiled = false;
      _cachedBytes = null;
      _cachedTypes = null;
      _cachedDecls = null;
    }

    //NOTE: derived straight from bhl.proj's src_dirs + file names - no compile needed,
    //      since a module's name is just its file path relative to inc_path
    public static List<string> GetModules()
    {
      if(_modulesCache != null)
        return _modulesCache;

      var modules = new List<string>();

      ProjectConf proj;
      try
      {
        proj = EditorCompiler.LoadProjectConf();
      }
      catch(System.Exception)
      {
        _modulesCache = modules;
        return modules;
      }

      var files = new List<string>();
      foreach(var src_dir in proj.src_dirs)
        CompilationExecutor.AddFilesFromDir(src_dir, files);

      foreach(var f in files)
      {
        try
        {
          modules.Add(proj.inc_path.FilePath2ModuleName(f));
        }
        catch(System.Exception)
        {
          //NOTE: outside inc_path - shouldn't happen since AddFilesFromDir only
          //      walked src_dirs, but degrade gracefully rather than throw
        }
      }

      _modulesCache = modules;
      return modules;
    }

    //NOTE: any BHL class is offered, not just ones implementing some marker interface -
    //      BHL script classes can't implement/extend anything native (a hard compiler
    //      restriction, see tests/test_interface.cs and tests/test_class.cs), so a
    //      native-declared marker interface isn't an option; a .bhl-declared one is a
    //      possible follow-up once that's worth the added friction of requiring it
    public static List<string> GetClasses(string module_name)
    {
      if(string.IsNullOrEmpty(module_name))
        return new List<string>();

      var classes = new List<string>();
      if(TryLoadModule(module_name, out var decl))
        CollectClasses(decl.ns, "", classes);

      return classes;
    }

    public static List<FieldInfo> GetFields(string module_name, string class_name)
    {
      var fields = new List<FieldInfo>();
      if(TryResolveClass(module_name, class_name, out var cls))
      {
        foreach(var sym in cls)
          if(sym is VariableSymbol vs)
            fields.Add(new FieldInfo { Name = vs.name, Type = MapType(vs.type.Get()) });
      }

      return fields;
    }

    //NOTE: class_name may be namespace-qualified (e.g. "Foo.Bar") - ResolveNamedByPath
    //      already handles dotted paths through nested namespaces
    static bool TryResolveClass(string module_name, string class_name, out ClassSymbolScript cls)
    {
      cls = null;

      if(string.IsNullOrEmpty(class_name) || !TryLoadModule(module_name, out var decl))
        return false;

      cls = decl.ResolveNamedByPath(class_name) as ClassSymbolScript;
      return cls != null;
    }

    static bool _compiled;
    static byte[] _cachedBytes;
    static Types _cachedTypes;
    static Dictionary<string, ModuleDeclared> _cachedDecls;

    static bool TryLoadModule(string module_name, out ModuleDeclared decl)
    {
      decl = null;

      if(string.IsNullOrEmpty(module_name))
        return false;

      if(!_compiled)
      {
        //NOTE: marked compiled regardless of outcome - a failed attempt (compile
        //      errors, missing Settings/bhl.proj) shouldn't retry on every repaint
        //      either; the user fixes the issue and clicks Refresh
        _compiled = true;
        try
        {
          //NOTE: CompileAll, not CompileAllWithProgressBar - this can run from inside
          //      ScriptBHLInspector's OnInspectorGUI, and EditorUtility.DisplayProgressBar/
          //      ClearProgressBar are themselves IMGUI overlays; invoking them mid-Layout/
          //      Event pass desyncs Unity's control-ID counting between passes, which
          //      manifests as clicks on one Popup misfiring EndChangeCheck() on another
          _cachedBytes = EditorCompiler.CompileAll();
          _cachedTypes = new Types();
          //NOTE: matches EditorCompiler.Compile's conf.bindings - needed to resolve native imports
          EditorCompiler.LoadProjectConf().LoadBindings().Register(_cachedTypes);
          _cachedDecls = new Dictionary<string, ModuleDeclared>();
        }
        catch(System.Exception)
        {
          _cachedBytes = null;
        }
      }

      if(_cachedBytes == null)
        return false;

      if(_cachedDecls.TryGetValue(module_name, out decl))
        return decl != null;

      var loader = new ModuleLoader(_cachedTypes, new MemoryStream(_cachedBytes));

      ModuleDeclared Resolve(string name)
      {
        if(_cachedDecls.TryGetValue(name, out var cached))
          return cached;

        //NOTE: a native binding module (e.g. "unity") isn't in the compiled bundle - check
        //      _cachedTypes first, same as the compiler/VM both do
        ModuleDeclared d = _cachedTypes.FindRegisteredModule(name);
        if(d == null)
        {
          try { d = loader.Load(name, null); }
          catch(System.Exception) { d = null; }
        }

        _cachedDecls[name] = d;
        return d;
      }

      try
      {
        decl = Resolve(module_name);
        //NOTE: Setup is what actually populates ClassSymbolScript._all_members (via
        //      SetupAllMembers) - without it every class enumerates as empty, so
        //      GetFields/GetClasses would silently find nothing. Guarded by its own
        //      is_setup flag, so re-resolving an already-cached decl is a safe no-op.
        decl?.Setup(Resolve);
      }
      catch(System.Exception)
      {
        decl = null;
      }

      _cachedDecls[module_name] = decl;
      return decl != null;
    }

    //NOTE: recurses into nested namespaces, accumulating a dotted prefix, so a class
    //      declared inside `namespace Foo { class Bar {...} }` is offered as "Foo.Bar"
    static void CollectClasses(Namespace ns, string prefix, List<string> classes)
    {
      foreach(var sym in ns.members)
      {
        if(sym is ClassSymbolScript cls)
          classes.Add(prefix + cls.name);
        else if(sym is Namespace nested)
          CollectClasses(nested, prefix + nested.name + ".", classes);
      }
    }

    static FieldType? MapType(IType t)
    {
      if(t == Types.Int) return FieldType.Int;
      if(t == Types.Float) return FieldType.Float;
      if(t == Types.Bool) return FieldType.Bool;
      if(t == Types.String) return FieldType.String;
      if(t == UnityBindings.TypeVector3) return FieldType.Vector3;
      return null;
    }
  }

}
