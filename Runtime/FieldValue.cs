using System;
using UnityEngine;
using bhl;

namespace UnityBHL
{
  public enum FieldType
  {
    Int,
    Float,
    Bool,
    String,
    Vector3,
  }

  //NOTE: v1 ships a small type set - grow as real ScriptBHL content needs more
  [Serializable]
  public class FieldValue
  {
    public string FieldName;
    public FieldType Type;

    public int IntValue;
    public float FloatValue;
    public bool BoolValue;
    public string StringValue;
    public Vector3 Vector3Value;

    public void ApplyTo(VM vm, ref Val instance)
    {
      Val v;
      switch(Type)
      {
        case FieldType.Int: v = Val.NewInt(IntValue); break;
        case FieldType.Float: v = Val.NewFlt(FloatValue); break;
        case FieldType.Bool: v = Val.NewBool(BoolValue); break;
        case FieldType.String: v = Val.NewStr(StringValue ?? ""); break;
        case FieldType.Vector3: v = UnityBindings.NewVector3(Vector3Value); break;
        default: throw new Exception($"Unsupported BHL field type: {Type}");
      }

      vm.SetFieldValue(ref instance, FieldName, v);
    }
  }

}
