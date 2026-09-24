using System;
using System.Collections.Generic;
using UnityEngine;
using bhl;

namespace UnityBHL
{

  //NOTE: AddComponentMenu's last path segment doubles as the Inspector header title -
  //      "BHL Script" so the header itself is identifiable, not just the menu location
  [AddComponentMenu("BHL/BHL Script")]
  public class ScriptBHL : MonoBehaviour
  {
    [Tooltip("BHL module (source file, without extension) the class lives in")]
    public string ModuleName;

    [Tooltip("BHL class to instantiate")]
    public string ClassName;

    public List<FieldValue> FieldValues = new List<FieldValue>();

    Val _instance;
    bool _hasInstance;

    VM.Fiber _updateFiber;

    //NOTE: cached once (FindMethod is a linear scan); IsCoro comes from FuncAttrib at compile time
    struct CachedMethod
    {
      public FuncSymbolScript Symbol;
      public bool IsCoro;

      public void Resolve(VM vm, Val instance, string name)
      {
        Symbol = vm.FindMethod(instance, name);
        IsCoro = Symbol != null && Symbol.attribs.HasFlag(FuncAttrib.Coro);
      }
    }

    CachedMethod _awakeMethod;
    CachedMethod _updateMethod;
    CachedMethod _onDestroyMethod;

    public bool HasInstance => _hasInstance;

    //NOTE: lets BHL.ReloadModules find every live ScriptBHL for a reloaded module to migrate
    static readonly Dictionary<string, List<ScriptBHL>> _byModule = new Dictionary<string, List<ScriptBHL>>();

    public static IReadOnlyList<ScriptBHL> GetByModule(string module_name)
    {
      return _byModule.TryGetValue(module_name, out var list) ? list : (IReadOnlyList<ScriptBHL>)System.Array.Empty<ScriptBHL>();
    }

    //NOTE: every module with at least one live ScriptBHL instance - for a caller (e.g.
    //      a manual "hot reload on recompile") that wants to migrate everything
    //      currently live, without needing its own separate tracking
    public static IEnumerable<string> RegisteredModules => _byModule.Keys;

    //NOTE: called whenever the VM singleton gets recreated
    public static void ClearRegistry()
    {
      _byModule.Clear();
    }

    void Register()
    {
      if(string.IsNullOrEmpty(ModuleName))
        return;

      if(!_byModule.TryGetValue(ModuleName, out var list))
      {
        list = new List<ScriptBHL>();
        _byModule[ModuleName] = list;
      }

      if(!list.Contains(this))
        list.Add(this);
    }

    void Unregister()
    {
      if(string.IsNullOrEmpty(ModuleName))
        return;

      if(_byModule.TryGetValue(ModuleName, out var list))
        list.Remove(this);
    }

    void OnEnable()
    {
      CreateInstance();
    }

    void CreateInstance()
    {
      if(_hasInstance)
        return;

      if(string.IsNullOrEmpty(ModuleName) || string.IsNullOrEmpty(ClassName))
        return;

      var vm = BHL.VM;
      if(!vm.LoadModule(ModuleName))
        throw new Exception($"BHL module '{ModuleName}' not found - make sure bytecode is loaded (BHL.SetBytecode/LoadBakedBundle) and the module name is correct");

      _instance = vm.NewInstance(ClassName);
      _hasInstance = true;
      _updateFiber = null;
      ResolveLifecycleMethods(vm);

      //NOTE: opt-in - only set if the class itself declares a matching field, mirroring
      //      Unity's own MonoBehaviour.gameObject/.transform rather than forcing every
      //      lifecycle method's signature to carry them
      TrySetNativeField(vm, "gameObject", UnityBindings.TypeGameObject, UnityBindings.NewGameObject(gameObject));
      TrySetNativeField(vm, "transform", UnityBindings.TypeTransform, UnityBindings.NewTransform(transform));

      foreach(var fv in FieldValues)
        fv.ApplyTo(vm, ref _instance, this);

      Register();

      CallLifecycleMethod(vm, _awakeMethod);
    }

    void TrySetNativeField(VM vm, string field_name, ClassSymbolNative expected_type, Val value)
    {
      if(_instance.type is ClassSymbol cls
        && cls.Resolve(field_name) is FieldSymbol fs
        && fs.type.Get() == expected_type)
        vm.SetFieldValue(ref _instance, field_name, value);
    }

    void Update()
    {
      if(!_hasInstance)
        return;

      var vm = BHL.VM;

      //NOTE: resume a suspended call rather than starting a new one, so yield() spans frames
      if(_updateFiber != null)
      {
        if(!_updateFiber.Tick())
        {
          _updateFiber.Release();
          _updateFiber = null;
        }
        return;
      }

      if(_updateMethod.Symbol == null)
        return;

      var args = new StackList<Val>();

      //NOTE: a non-coroutine Update runs synchronously via Execute(), no Fiber overhead
      if(!_updateMethod.IsCoro)
      {
        vm.ExecuteMethod(ref _instance, _updateMethod.Symbol, args);
        return;
      }

      var fb = vm.CallMethod(ref _instance, _updateMethod.Symbol, args, VM.FiberOptions.Detach);

      if(!fb.Tick())
        fb.Release();
      else
        _updateFiber = fb;
    }

    void OnDisable()
    {
      DestroyInstance();
    }

    void OnDestroy()
    {
      DestroyInstance();
    }

    void DestroyInstance()
    {
      if(!_hasInstance)
        return;

      if(_updateFiber != null)
      {
        //NOTE: with_children - don't leave paral-spawned fibers running past destruction
        _updateFiber.Stop(with_children: true);
        _updateFiber.Release();
        _updateFiber = null;
      }

      //NOTE: TryGetVM so a torn-down VM isn't resurrected just to run this probe
      if(BHL.TryGetVM(out var vm))
        CallLifecycleMethod(vm, _onDestroyMethod);

      Unregister();

      _instance.ReleaseData();
      _hasInstance = false;
    }

    //NOTE: called by BHL.ReloadModules for every ScriptBHL under a reloaded module
    public void MigrateInstance()
    {
      if(!_hasInstance)
        return;

      var vm = BHL.VM;
      vm.MigrateInstance(ref _instance);
      //NOTE: reloaded class is a fresh object - cached methods no longer belong to it
      ResolveLifecycleMethods(vm);
    }

    void ResolveLifecycleMethods(VM vm)
    {
      _awakeMethod.Resolve(vm, _instance, "Awake");
      RejectIfCoro(ref _awakeMethod, "Awake");

      _updateMethod.Resolve(vm, _instance, "Update");

      _onDestroyMethod.Resolve(vm, _instance, "OnDestroy");
      RejectIfCoro(ref _onDestroyMethod, "OnDestroy");
    }

    void RejectIfCoro(ref CachedMethod method, string name)
    {
      if(method.Symbol != null && method.IsCoro)
      {
        Debug.LogError($"[BHL] {this}: '{name}' must not be a coroutine (yield() isn't supported here) - ignoring it", this);
        method.Symbol = null;
      }
    }

    void CallLifecycleMethod(VM vm, CachedMethod method)
    {
      if(method.Symbol == null)
        return;

      vm.ExecuteMethod(ref _instance, method.Symbol, new StackList<Val>());
    }
  }

}
