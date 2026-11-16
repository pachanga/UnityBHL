#if !BHL_FRONT
using UnityEngine;
#endif
using bhl;
using Types = bhl.Types;

namespace UnityBHL
{

  //NOTE: registers a "unity" module - BHL scripts `import "unity"`.
  //      The file is also read as a declarations source by bhl's CsSourceBindings (bhl.proj's
  //      `bindings`), so the LSP and the CLI - which never load the Unity assembly - can resolve
  //      that import. They parse it with BHL_FRONT defined and interpret Register() alone, and only
  //      straight-line code is allowed: no control flow, no `return` anywhere in the file. So
  //      everything which touches Unity or returns a value lives under `#if !BHL_FRONT`, and what's
  //      left is the declarations with their native bodies omitted - the same shape bindgen emits.
  //      Keep the two halves of a declaration adjacent: the whole point is that the signature a
  //      script sees and the body it runs can't drift apart, which a separate mirror file did
  [BhlBinding("unity", "1.0.0")]
  public class UnityBindings : IUserBindings
  {
    //must be present due to loading class instance from dll requirements
    public UnityBindings()
    {}

#if !BHL_FRONT
    //NOTE: the symbols belong to the Types they were registered into - the editor compiles and the
    //      Class picker make Types of their own next to the VM's, and a Val carrying a symbol of one
    //      doesn't match the same symbol of another. Kept per Types rather than in statics, which
    //      whichever Register() ran last would otherwise own
    public class Symbols
    {
      public ClassSymbolNative Vector3;
      public ClassSymbolNative Quaternion;
      public ClassSymbolNative GameObject;
      public ClassSymbolNative Transform;
      public ClassSymbolNative Rigidbody;
      public ClassSymbolScript BHLComponent;
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Types, Symbols> _symbols =
      new System.Runtime.CompilerServices.ConditionalWeakTable<Types, Symbols>();

    //NOTE: null when this Types never had the module registered
    public static Symbols Of(Types types)
    {
      _symbols.TryGetValue(types, out var syms);
      return syms;
    }
#endif

    public void Register(Types types)
    {
      var module = new ModuleDeclared("unity");
      var ns = module.ns.Nest("unity");

      var vec3 = new ClassSymbolNative(new Origin(), "Vector3"
#if !BHL_FRONT
        , typeof(Vector3)
        , delegate(VM.ExecState exec, ref Val v, IType type) { v.EncodeVector3(type, new Vector3()); }
        , v => v.DecodeVector3()
#endif
      );
      ns.Define(vec3);

      vec3.Define(new FieldSymbol(new Origin(), "x", Types.Float
#if !BHL_FRONT
        , delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.SetFlt(ctx.num); }
        , delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ctx.num = v.num; }
#endif
      ));
      vec3.Define(new FieldSymbol(new Origin(), "y", Types.Float
#if !BHL_FRONT
        , delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.SetFlt(ctx._num2); }
        , delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ctx._num2 = v.num; }
#endif
      ));
      vec3.Define(new FieldSymbol(new Origin(), "z", Types.Float
#if !BHL_FRONT
        , delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.SetFlt(ctx._num3); }
        , delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ctx._num3 = v.num; }
#endif
      ));

      vec3.Setup();

      var quat = new ClassSymbolNative(new Origin(), "Quaternion"
#if !BHL_FRONT
        , typeof(Quaternion)
        , delegate(VM.ExecState exec, ref Val v, IType type) { v.EncodeQuaternion(type, Quaternion.identity); }
        , v => v.DecodeQuaternion()
#endif
      );
      ns.Define(quat);

      quat.Define(new FieldSymbol(new Origin(), "x", Types.Float
#if !BHL_FRONT
        , delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.SetFlt(ctx.num); }
        , delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ctx.num = v.num; }
#endif
      ));
      quat.Define(new FieldSymbol(new Origin(), "y", Types.Float
#if !BHL_FRONT
        , delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.SetFlt(ctx._num2); }
        , delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ctx._num2 = v.num; }
#endif
      ));
      quat.Define(new FieldSymbol(new Origin(), "z", Types.Float
#if !BHL_FRONT
        , delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.SetFlt(ctx._num3); }
        , delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ctx._num3 = v.num; }
#endif
      ));
      quat.Define(new FieldSymbol(new Origin(), "w", Types.Float
#if !BHL_FRONT
        , delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.SetFlt(ctx._num4); }
        , delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ctx._num4 = v.num; }
#endif
      ));

      quat.Setup();

      //NOTE: reference types - no encode/decode delegates, Val just boxes the native
      //      object directly (default native_object_getter falls back to v.obj). Transform
      //      is defined first since GameObject.transform references it as a field type
      var transform = new ClassSymbolNative(new Origin(), "Transform"
#if !BHL_FRONT
        , typeof(Transform)
#endif
      );
      ns.Define(transform);

      transform.Define(new FieldSymbol(new Origin(), "position", vec3
#if !BHL_FRONT
        , delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.EncodeVector3(vec3, ((Transform)ctx.obj).position); }
        , delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ((Transform)ctx.obj).position = v.DecodeVector3(); }
#endif
      ));
      transform.Define(new FieldSymbol(new Origin(), "rotation", quat
#if !BHL_FRONT
        , delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.EncodeQuaternion(quat, ((Transform)ctx.obj).rotation); }
        , delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ((Transform)ctx.obj).rotation = v.DecodeQuaternion(); }
#endif
      ));
      transform.Define(new FieldSymbol(new Origin(), "localPosition", vec3
#if !BHL_FRONT
        , delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.EncodeVector3(vec3, ((Transform)ctx.obj).localPosition); }
        , delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ((Transform)ctx.obj).localPosition = v.DecodeVector3(); }
#endif
      ));
      transform.Define(new FieldSymbol(new Origin(), "localRotation", quat
#if !BHL_FRONT
        , delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.EncodeQuaternion(quat, ((Transform)ctx.obj).localRotation); }
        , delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ((Transform)ctx.obj).localRotation = v.DecodeQuaternion(); }
#endif
      ));
      transform.Define(new FieldSymbol(new Origin(), "localScale", vec3
#if !BHL_FRONT
        , delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.EncodeVector3(vec3, ((Transform)ctx.obj).localScale); }
        , delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ((Transform)ctx.obj).localScale = v.DecodeVector3(); }
#endif
      ));

      transform.Setup();

      var rigidbody = new ClassSymbolNative(new Origin(), "Rigidbody"
#if !BHL_FRONT
        , typeof(Rigidbody)
#endif
      );
      ns.Define(rigidbody);

      //NOTE: BHL-side name stays "velocity" - wired to linearVelocity to avoid the
      //      obsolete-API warning Rigidbody.velocity now emits (Unity 2023.1+)
      rigidbody.Define(new FieldSymbol(new Origin(), "velocity", vec3
#if !BHL_FRONT
        , delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.EncodeVector3(vec3, ((Rigidbody)ctx.obj).linearVelocity); }
        , delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ((Rigidbody)ctx.obj).linearVelocity = v.DecodeVector3(); }
#endif
      ));
      rigidbody.Define(new FieldSymbol(new Origin(), "mass", Types.Float
#if !BHL_FRONT
        , delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.SetFlt(((Rigidbody)ctx.obj).mass); }
        , delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ((Rigidbody)ctx.obj).mass = (float)v.num; }
#endif
      ));

      rigidbody.Setup();

      var gameObject = new ClassSymbolNative(new Origin(), "GameObject"
#if !BHL_FRONT
        , typeof(GameObject)
#endif
      );
      ns.Define(gameObject);

      gameObject.Define(new FieldSymbol(new Origin(), "name", Types.String
#if !BHL_FRONT
        , delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.SetStr(((GameObject)ctx.obj).name); }
        , delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ((GameObject)ctx.obj).name = v.str; }
#endif
      ));
      //NOTE: getter-only, matches Unity's own GameObject.transform (can't be reassigned)
      gameObject.Define(new FieldSymbol(new Origin(), "transform", transform
#if !BHL_FRONT
        , delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.SetObj(((GameObject)ctx.obj).transform, transform); }
        , null
#endif
      ));
      //NOTE: GameObject.rigidbody itself was removed in Unity 5 - this re-creates the old
      //      shortcut ergonomics via GetComponent<Rigidbody>() each access (not cached),
      //      null if there's no Rigidbody on the GameObject
      gameObject.Define(new FieldSymbol(new Origin(), "rigidbody", rigidbody
#if !BHL_FRONT
        , delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.SetObj(((GameObject)ctx.obj).GetComponent<Rigidbody>(), rigidbody); }
        , null
#endif
      ));

      gameObject.Setup();

      //NOTE: optional base class for ScriptBHL classes - Awake/Update/OnDestroy are virtual
      //      no-op stubs, override only what you need; ScriptBHL's own duck-typed field/method
      //      detection also works without extending this, so it's not required
      var component = std.bind.NewClassSymbolScript(module, "BHLComponent");
      ns.Define(component);
      component.Define(new FieldSymbolScript(new Origin(), "gameObject", gameObject));
      component.Define(new FieldSymbolScript(new Origin(), "transform", transform));
      std.bind.DefineVirtualMethod(component, "Awake", Types.Void);
      std.bind.DefineVirtualMethod(component, "Update", Types.Void);
      std.bind.DefineVirtualMethod(component, "OnDestroy", Types.Void);
      component.Setup();

      //NOTE: the three log functions, declared one by one rather than by a helper taking the
      //      UnityEngine.Debug method: a helper's lambda body can't be read with BHL_FRONT
      var log = new FuncSymbolNative(new Origin(), "Log", Types.Void
#if !BHL_FRONT
        , (VM.ExecState exec, FuncArgsInfo args_info) => { Debug.Log(exec.stack.PopFast()); return null; }
#else
        , null
#endif
        , new FuncArgSymbol("msg", Types.String)
      );
      ns.Define(log);

      var log_warning = new FuncSymbolNative(new Origin(), "LogWarning", Types.Void
#if !BHL_FRONT
        , (VM.ExecState exec, FuncArgsInfo args_info) => { Debug.LogWarning(exec.stack.PopFast()); return null; }
#else
        , null
#endif
        , new FuncArgSymbol("msg", Types.String)
      );
      ns.Define(log_warning);

      var log_error = new FuncSymbolNative(new Origin(), "LogError", Types.Void
#if !BHL_FRONT
        , (VM.ExecState exec, FuncArgsInfo args_info) => { Debug.LogError(exec.stack.PopFast()); return null; }
#else
        , null
#endif
        , new FuncArgSymbol("msg", Types.String)
      );
      ns.Define(log_error);

      types.RegisterModule(module);

#if !BHL_FRONT
      _symbols.Remove(types);
      _symbols.Add(types, new Symbols {
        Vector3 = vec3, Quaternion = quat, GameObject = gameObject,
        Transform = transform, Rigidbody = rigidbody, BHLComponent = component
      });
#endif
    }

#if !BHL_FRONT
    public static Val NewVector3(Types types, Vector3 v)
    {
      var val = new Val();
      val.EncodeVector3(Of(types).Vector3, v);
      return val;
    }

    public static Val NewQuaternion(Types types, Quaternion q)
    {
      var val = new Val();
      val.EncodeQuaternion(Of(types).Quaternion, q);
      return val;
    }

    public static Val NewGameObject(Types types, GameObject go)
    {
      return Val.NewObj(go, Of(types).GameObject);
    }

    public static Val NewTransform(Types types, Transform t)
    {
      return Val.NewObj(t, Of(types).Transform);
    }

    public static Val NewRigidbody(Types types, Rigidbody rb)
    {
      return Val.NewObj(rb, Of(types).Rigidbody);
    }
#endif
  }

#if !BHL_FRONT
  //NOTE: encoded straight into num/_num2/_num3/_num4, no boxing - obj/_refc are nulled
  //      explicitly since pooled stack slots aren't zeroed on Push(). The type symbol is passed in
  //      rather than read from a static: it belongs to the Types the module was registered into
  public static class NativeStructCodec
  {
    public static void EncodeVector3(this ref Val dv, IType type, Vector3 v)
    {
      dv.type = type;
      dv.obj = null;
      dv._refc = null;
      dv.num = v.x;
      dv._num2 = v.y;
      dv._num3 = v.z;
    }

    public static Vector3 DecodeVector3(this Val dv)
    {
      return new Vector3((float)dv.num, (float)dv._num2, (float)dv._num3);
    }

    public static void EncodeQuaternion(this ref Val dv, IType type, Quaternion q)
    {
      dv.type = type;
      dv.obj = null;
      dv._refc = null;
      dv.num = q.x;
      dv._num2 = q.y;
      dv._num3 = q.z;
      dv._num4 = q.w;
    }

    public static Quaternion DecodeQuaternion(this Val dv)
    {
      return new Quaternion((float)dv.num, (float)dv._num2, (float)dv._num3, (float)dv._num4);
    }
  }
#endif

}
