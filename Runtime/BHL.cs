using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;
using bhl;

namespace UnityBHL
{

  //NOTE: purely VM-side, no compiler front-end dependency - safe to ship in a Player build
  public static class BHL
  {
    static VM _vm;
    static byte[] _lastBytecode;
    //NOTE: guards AttachBytecode's one-time RegisterRequiredBindings call
    static bool _bindingsResolved;

    //NOTE: wrapped in VMTracker so the singleton shows up in the Control Panel by default
    static IVMFactory _factory = new VMFactory(new VMTracker(new DefaultVMCreator(), "BHL.VM"));

    public static VM VM
    {
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      get
      {
        if(_vm == null)
          EnsureVM();
        return _vm;
      }
    }

    public static bool IsReady => _vm != null;

    //NOTE: lets Editor tooling (e.g. ClassIntrospection) reuse the already-compiled
    //      bytecode instead of triggering its own separate compile
    public static byte[] LastBytecode => _lastBytecode;

    //NOTE: non-creating accessor, for teardown paths that shouldn't resurrect a torn-down VM
    public static bool TryGetVM(out VM vm)
    {
      vm = _vm;
      return vm != null;
    }

    //NOTE: call before anything first accesses BHL.VM (e.g. from your own BeforeSceneLoad hook)
    public static void Configure(IVMFactory factory) => _factory = factory ?? new VMFactory(new VMTracker(new DefaultVMCreator(), "BHL.VM"));

    //NOTE: plain-creator convenience, still wrapped in VMTracker/VMFactory like the default
    public static void Configure(IVMCreator creator) => Configure(new VMFactory(new VMTracker(creator ?? new DefaultVMCreator(), "BHL.VM")));

    //NOTE: resets only the lazily-built state, not the injected creator config
  #if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
  #endif
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Cleanup()
    {
      _vm = null;
      _bindingsResolved = false;
      ScriptBHL.ClearRegistry();
      VMTracker.Clear();
      //NOTE: pooled VMs reference pre-reload Types/Symbol state - unsafe to reuse after this
      _factory.CleanVMCache();
    }

    static void EnsureVM()
    {
      if(_vm != null)
        return;

      //NOTE: MakeVM not RentVM - the singleton is always fresh; pooling is for other callers
      _vm = _factory.MakeVM();

      //NOTE: reapply bytecode from before Cleanup() nulled _vm, if any
      if(_lastBytecode == null)
      {
#if UNITY_EDITOR
        TryRestoreLastEditorCompile();
#endif
      }

      if(_lastBytecode != null)
        AttachBytecode(_lastBytecode);
    }

    //NOTE: also used by EditorCompiler.LoadProjectConf for proj.result_file, so the two
    //      can't drift apart. Lives here since Editor references Runtime, not vice versa.
    public const string LastEditorCompilePath = "Library/BHL/bhl.bytes";

#if UNITY_EDITOR
    //NOTE: restores bytecode a domain reload wiped from _lastBytecode (e.g. Reload
    //      Domain on entering Play Mode, right after ExitingEditMode's compile set it)
    static void TryRestoreLastEditorCompile()
    {
      try
      {
        if(File.Exists(LastEditorCompilePath))
          _lastBytecode = File.ReadAllBytes(LastEditorCompilePath);
      }
      catch(Exception)
      {
        //NOTE: best-effort - an unreadable/missing file just means nothing to restore
      }
    }
#endif

    //NOTE: called by EditorCompiler after every compile, or once at startup in a Player build
    public static void SetBytecode(byte[] bytes)
    {
      _lastBytecode = bytes;
      //NOTE: MakeVM directly, not EnsureVM - avoids parsing the bytecode header twice
      if(_vm == null)
        _vm = _factory.MakeVM();
      AttachBytecode(bytes);
    }

    //NOTE: cherry-picks bindings from BindingsRegistry per the bytecode's own embedded
    //      required-bindings metadata, once per VM lifetime
    static void AttachBytecode(byte[] bytes)
    {
      var loader = new ModuleLoader(_vm.types, new MemoryStream(bytes));

      if(!_bindingsResolved)
      {
        BindingsRegistry.RegisterRequiredBindings(_vm.types, loader);

        //NOTE: the VM's own module registry only mirrors types.modules once, at
        //      construction time - anything RegisterRequiredBindings just added has to be
        //      synced in explicitly, or imports resolving against the VM never find it
        foreach(var (name, _) in loader.RequiredBindings)
        {
          var decl = _vm.types.FindRegisteredModule(name);
          if(decl != null)
            _vm.RegisterModule(decl);
        }

        _bindingsResolved = true;
      }

      _vm.Loader = loader;
    }

    //NOTE: explicit entry point for a Player build with no Editor driver; defaults to Resources/bhl
    public static void LoadBakedBundle(BytecodeSource bundle = default)
    {
      if(bundle.Path2Stream == null)
        bundle = BytecodeSource.FromResources();

      using(var stream = bundle.Stream)
      using(var ms = new MemoryStream())
      {
        stream.CopyTo(ms);
        SetBytecode(ms.ToArray());
      }
    }

    //NOTE: reloads/migrates already-loaded modules, loads new ones, unloads deleted ones.
    //      Doesn't re-resolve bindings - a brand-new top-level import of a binding the
    //      original compile didn't need won't resolve until the next fresh Play session
    public static void ReloadModules(IEnumerable<string> module_names, byte[] fresh_bytecode)
    {
      if(_vm == null)
        return;

      _lastBytecode = fresh_bytecode;
      _vm.Loader = new ModuleLoader(_vm.types, new MemoryStream(fresh_bytecode));

      foreach(var module_name in module_names)
      {
        ModuleDeclared decl;
        try
        {
          decl = _vm.Loader.Load(module_name, _vm);
        }
        catch(Exception)
        {
          decl = null;
        }

        if(decl == null)
        {
          _vm.UnloadModule(module_name);
          continue;
        }

        if(_vm.FindModule(module_name) != null)
        {
          _vm.Reload(new Module(decl));
          _vm.RelinkImports(module_name);

          foreach(var script in ScriptBHL.GetByModule(module_name))
            script.MigrateInstance();
        }
        else
        {
          _vm.LoadModule(new Module(decl));
        }
      }
    }
  }

}
