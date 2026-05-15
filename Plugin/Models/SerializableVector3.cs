using System.Numerics;

namespace GposeCameraSaver.Models;

public sealed class SerializableVector3
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public SerializableVector3()
    {
    }

    public SerializableVector3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static SerializableVector3 FromVector3(Vector3 value) => new(value.X, value.Y, value.Z);

    public Vector3 ToVector3() => new(X, Y, Z);
}
