using System;

namespace CarRacer.Core.Art
{
    /// <summary>An 8-bit RGB colour.</summary>
    public readonly struct Rgb : IEquatable<Rgb>
    {
        public readonly byte R;
        public readonly byte G;
        public readonly byte B;

        public Rgb(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        /// <summary>From 0..1 components, as Unity colours hold them.</summary>
        public static Rgb FromFloats(float r, float g, float b) => new Rgb(ToByte(r), ToByte(g), ToByte(b));

        /// <summary>Perceived brightness, 0..1, to pick dark or light text over this colour.</summary>
        public float Luma => (0.299f * R + 0.587f * G + 0.114f * B) / 255f;

        public bool Equals(Rgb other) => R == other.R && G == other.G && B == other.B;

        public override bool Equals(object obj) => obj is Rgb other && Equals(other);

        public override int GetHashCode() => (R << 16) | (G << 8) | B;

        public override string ToString() => $"#{R:x2}{G:x2}{B:x2}";

        private static byte ToByte(float v) => (byte)Math.Round(Math.Max(0f, Math.Min(1f, v)) * 255f);
    }
}
