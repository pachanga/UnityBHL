using UnityEngine;
using bhl;
using Types = bhl.Types;

namespace UnityBHL
{

  //NOTE: registers a "unity" module - BHL scripts `import "unity"`
  [BhlBinding("unity", "1.0.0")]
  public class UnityBindings : IUserBindings
  {
    //NOTE: module initializers aren't guaranteed under IL2CPP, hence the explicit hooks
  #if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
  #endif
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    internal static void Init() => BindingsRegistry.Register<UnityBindings>();

    //must be present due to loading class instance from dll requirements
    public UnityBindings()
    {}

    //NOTE: built once, mirrors bhl.Types.Float/Types.Type
    static readonly ModuleDeclared _module;
    public static readonly ClassSymbolNative TypeVector3;
    public static readonly ClassSymbolNative TypeQuaternion;
    public static readonly ClassSymbolNative TypeGameObject;
    public static readonly ClassSymbolNative TypeTransform;
    public static readonly ClassSymbolNative TypeRigidbody;

    static UnityBindings()
    {
      _module = new ModuleDeclared("unity");
      var ns = _module.ns.Nest("unity");

      TypeVector3 = new ClassSymbolNative(new Origin(), "Vector3", typeof(Vector3),
        delegate(VM.ExecState exec, ref Val v, IType type) { v.EncodeVector3(new Vector3()); },
        v => v.DecodeVector3()
      );
      ns.Define(TypeVector3);

      TypeVector3.Define(new FieldSymbol(new Origin(), "x", Types.Float,
        delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.SetFlt(ctx.num); },
        delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ctx.num = v.num; }
      ));
      TypeVector3.Define(new FieldSymbol(new Origin(), "y", Types.Float,
        delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.SetFlt(ctx._num2); },
        delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ctx._num2 = v.num; }
      ));
      TypeVector3.Define(new FieldSymbol(new Origin(), "z", Types.Float,
        delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.SetFlt(ctx._num3); },
        delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ctx._num3 = v.num; }
      ));

      TypeVector3.Setup();

      TypeQuaternion = new ClassSymbolNative(new Origin(), "Quaternion", typeof(Quaternion),
        delegate(VM.ExecState exec, ref Val v, IType type) { v.EncodeQuaternion(Quaternion.identity); },
        v => v.DecodeQuaternion()
      );
      ns.Define(TypeQuaternion);

      TypeQuaternion.Define(new FieldSymbol(new Origin(), "x", Types.Float,
        delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.SetFlt(ctx.num); },
        delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ctx.num = v.num; }
      ));
      TypeQuaternion.Define(new FieldSymbol(new Origin(), "y", Types.Float,
        delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.SetFlt(ctx._num2); },
        delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ctx._num2 = v.num; }
      ));
      TypeQuaternion.Define(new FieldSymbol(new Origin(), "z", Types.Float,
        delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.SetFlt(ctx._num3); },
        delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ctx._num3 = v.num; }
      ));
      TypeQuaternion.Define(new FieldSymbol(new Origin(), "w", Types.Float,
        delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.SetFlt(ctx._num4); },
        delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ctx._num4 = v.num; }
      ));

      TypeQuaternion.Setup();

      //NOTE: reference types - no encode/decode delegates, Val just boxes the native
      //      object directly (default native_object_getter falls back to v.obj). Transform
      //      is defined first since GameObject.transform references it as a field type
      TypeTransform = new ClassSymbolNative(new Origin(), "Transform", typeof(Transform));
      ns.Define(TypeTransform);

      TypeTransform.Define(new FieldSymbol(new Origin(), "position", TypeVector3,
        delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.EncodeVector3(((Transform)ctx.obj).position); },
        delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ((Transform)ctx.obj).position = v.DecodeVector3(); }
      ));
      TypeTransform.Define(new FieldSymbol(new Origin(), "rotation", TypeQuaternion,
        delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.EncodeQuaternion(((Transform)ctx.obj).rotation); },
        delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ((Transform)ctx.obj).rotation = v.DecodeQuaternion(); }
      ));
      TypeTransform.Define(new FieldSymbol(new Origin(), "localPosition", TypeVector3,
        delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.EncodeVector3(((Transform)ctx.obj).localPosition); },
        delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ((Transform)ctx.obj).localPosition = v.DecodeVector3(); }
      ));
      TypeTransform.Define(new FieldSymbol(new Origin(), "localRotation", TypeQuaternion,
        delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.EncodeQuaternion(((Transform)ctx.obj).localRotation); },
        delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ((Transform)ctx.obj).localRotation = v.DecodeQuaternion(); }
      ));
      TypeTransform.Define(new FieldSymbol(new Origin(), "localScale", TypeVector3,
        delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.EncodeVector3(((Transform)ctx.obj).localScale); },
        delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ((Transform)ctx.obj).localScale = v.DecodeVector3(); }
      ));

      TypeTransform.Setup();

      TypeRigidbody = new ClassSymbolNative(new Origin(), "Rigidbody", typeof(Rigidbody));
      ns.Define(TypeRigidbody);

      //NOTE: BHL-side name stays "velocity" - wired to linearVelocity to avoid the
      //      obsolete-API warning Rigidbody.velocity now emits (Unity 2023.1+)
      TypeRigidbody.Define(new FieldSymbol(new Origin(), "velocity", TypeVector3,
        delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.EncodeVector3(((Rigidbody)ctx.obj).linearVelocity); },
        delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ((Rigidbody)ctx.obj).linearVelocity = v.DecodeVector3(); }
      ));
      TypeRigidbody.Define(new FieldSymbol(new Origin(), "mass", Types.Float,
        delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.SetFlt(((Rigidbody)ctx.obj).mass); },
        delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ((Rigidbody)ctx.obj).mass = (float)v.num; }
      ));

      TypeRigidbody.Setup();

      TypeGameObject = new ClassSymbolNative(new Origin(), "GameObject", typeof(GameObject));
      ns.Define(TypeGameObject);

      TypeGameObject.Define(new FieldSymbol(new Origin(), "name", Types.String,
        delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.SetStr(((GameObject)ctx.obj).name); },
        delegate(VM.ExecState exec, ref Val ctx, Val v, FieldSymbol fld) { ((GameObject)ctx.obj).name = v.str; }
      ));
      //NOTE: getter-only, matches Unity's own GameObject.transform (can't be reassigned)
      TypeGameObject.Define(new FieldSymbol(new Origin(), "transform", TypeTransform,
        delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.SetObj(((GameObject)ctx.obj).transform, TypeTransform); },
        null
      ));
      //NOTE: GameObject.rigidbody itself was removed in Unity 5 - this re-creates the old
      //      shortcut ergonomics via GetComponent<Rigidbody>() each access (not cached),
      //      null if there's no Rigidbody on the GameObject
      TypeGameObject.Define(new FieldSymbol(new Origin(), "rigidbody", TypeRigidbody,
        delegate(VM.ExecState exec, Val ctx, ref Val v, FieldSymbol fld) { v.SetObj(((GameObject)ctx.obj).GetComponent<Rigidbody>(), TypeRigidbody); },
        null
      ));

      TypeGameObject.Setup();

      DefineLogFunc(ns, "Log", Debug.Log);
      DefineLogFunc(ns, "LogWarning", Debug.LogWarning);
      DefineLogFunc(ns, "LogError", Debug.LogError);
    }

    static void DefineLogFunc(Namespace ns, string name, System.Action<object> log)
    {
      var fn = new FuncSymbolNative(new Origin(), name, Types.Void,
        (VM.ExecState exec, FuncArgsInfo args_info) =>
        {
          string s = exec.stack.PopFast();
          log(s);
          return null;
        },
        new FuncArgSymbol("msg", Types.String)
      );
      ns.Define(fn);
    }

    public void Register(Types types)
    {
      types.RegisterModule(_module);
    }

    public static Val NewVector3(Vector3 v)
    {
      var val = new Val();
      val.EncodeVector3(v);
      return val;
    }

    public static Val NewQuaternion(Quaternion q)
    {
      var val = new Val();
      val.EncodeQuaternion(q);
      return val;
    }

    public static Val NewGameObject(GameObject go)
    {
      return Val.NewObj(go, TypeGameObject);
    }

    public static Val NewTransform(Transform t)
    {
      return Val.NewObj(t, TypeTransform);
    }

    public static Val NewRigidbody(Rigidbody rb)
    {
      return Val.NewObj(rb, TypeRigidbody);
    }
  }

  //NOTE: encoded straight into num/_num2/_num3/_num4, no boxing - obj/_refc are nulled
  //      explicitly since pooled stack slots aren't zeroed on Push()
  public static class NativeStructCodec
  {
    public static void EncodeVector3(this ref Val dv, Vector3 v)
    {
      dv.type = UnityBindings.TypeVector3;
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

    public static void EncodeQuaternion(this ref Val dv, Quaternion q)
    {
      dv.type = UnityBindings.TypeQuaternion;
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

}
