using System;

namespace CarRacer.Core.Numerics
{
    /// <summary>
    /// Engine-independent 3D vector. Uses Unity's convention: Y is up, left-handed.
    /// Public mutable fields so Unity's JsonUtility can serialize track data.
    /// </summary>
    [Serializable]
    public struct Vec3 : IEquatable<Vec3>
    {
        public float X;
        public float Y;
        public float Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly Vec3 Zero = new Vec3(0f, 0f, 0f);
        public static readonly Vec3 Up = new Vec3(0f, 1f, 0f);
        public static readonly Vec3 Forward = new Vec3(0f, 0f, 1f);
        public static readonly Vec3 Right = new Vec3(1f, 0f, 0f);

        public float Length => MathF.Sqrt(X * X + Y * Y + Z * Z);
        public float LengthSquared => X * X + Y * Y + Z * Z;
        public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);

        /// <summary>The vector projected onto the ground plane (Y = 0).</summary>
        public Vec3 Flattened => new Vec3(X, 0f, Z);

        public Vec3 Normalized
        {
            get
            {
                float len = Length;
                return len > 1e-6f ? this / len : Zero;
            }
        }

        public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);

        public static float Distance(Vec3 a, Vec3 b) => (a - b).Length;

        /// <summary>True if the two points are within <paramref name="radius"/> of each other on the ground plane.</summary>
        public static bool WithinFlat(Vec3 a, Vec3 b, float radius) => (a - b).Flattened.LengthSquared <= radius * radius;

        public static Vec3 Lerp(Vec3 a, Vec3 b, float t) => a + (b - a) * t;

        /// <summary>Rotates a vector around the up axis by the given angle (clockwise when viewed from above, like Unity).</summary>
        public static Vec3 RotateAroundUp(Vec3 v, float radians)
        {
            float c = MathF.Cos(radians);
            float s = MathF.Sin(radians);
            return new Vec3(v.X * c + v.Z * s, v.Y, -v.X * s + v.Z * c);
        }

        public bool ApproximatelyEquals(Vec3 other, float tolerance = 1e-4f) =>
            MathF.Abs(X - other.X) <= tolerance &&
            MathF.Abs(Y - other.Y) <= tolerance &&
            MathF.Abs(Z - other.Z) <= tolerance;

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator *(float s, Vec3 a) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator /(Vec3 a, float s) => new Vec3(a.X / s, a.Y / s, a.Z / s);
        public static bool operator ==(Vec3 a, Vec3 b) => a.Equals(b);
        public static bool operator !=(Vec3 a, Vec3 b) => !a.Equals(b);

        public bool Equals(Vec3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object obj) => obj is Vec3 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
    }
}
