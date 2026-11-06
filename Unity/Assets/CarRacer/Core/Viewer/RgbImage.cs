using System;

namespace CarRacer.Core.Viewer
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

        /// <summary>This colour moved <paramref name="t"/> (0..1) of the way towards <paramref name="other"/>.</summary>
        public Rgb Mix(Rgb other, float t) => new Rgb(Lerp(R, other.R, t), Lerp(G, other.G, t), Lerp(B, other.B, t));

        public bool Equals(Rgb other) => R == other.R && G == other.G && B == other.B;

        public override bool Equals(object obj) => obj is Rgb other && Equals(other);

        public override int GetHashCode() => (R << 16) | (G << 8) | B;

        public override string ToString() => $"#{R:x2}{G:x2}{B:x2}";

        private static byte ToByte(float v) => (byte)Math.Round(Math.Max(0f, Math.Min(1f, v)) * 255f);

        private static byte Lerp(byte a, byte b, float t) => (byte)Math.Round(a + (b - a) * Math.Max(0f, Math.Min(1f, t)));
    }

    /// <summary>
    /// A picture as packed RGB bytes, top row first: the format the TV viewer's frames go to the
    /// video encoder in (design doc §2.4). Shapes are anti-aliased by coverage, so they look smooth
    /// once scaled up on a television.
    /// </summary>
    public sealed class RgbImage
    {
        public RgbImage(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "An image needs a positive size");
            }

            Width = width;
            Height = height;
            Pixels = new byte[width * height * 3];
        }

        public int Width { get; }
        public int Height { get; }

        /// <summary>R, G, B for each pixel, row by row from the top left.</summary>
        public byte[] Pixels { get; }

        public Rgb Get(int x, int y)
        {
            int i = (y * Width + x) * 3;
            return new Rgb(Pixels[i], Pixels[i + 1], Pixels[i + 2]);
        }

        public void Set(int x, int y, Rgb colour)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
            {
                return;
            }

            int i = (y * Width + x) * 3;
            Pixels[i] = colour.R;
            Pixels[i + 1] = colour.G;
            Pixels[i + 2] = colour.B;
        }

        /// <summary>Lays <paramref name="colour"/> over the pixel with <paramref name="alpha"/> (0..1) of its strength.</summary>
        public void Blend(int x, int y, Rgb colour, float alpha)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height || alpha <= 0f)
            {
                return;
            }

            if (alpha >= 1f)
            {
                Set(x, y, colour);
                return;
            }

            int i = (y * Width + x) * 3;
            Pixels[i] = (byte)(Pixels[i] + (colour.R - Pixels[i]) * alpha + 0.5f);
            Pixels[i + 1] = (byte)(Pixels[i + 1] + (colour.G - Pixels[i + 1]) * alpha + 0.5f);
            Pixels[i + 2] = (byte)(Pixels[i + 2] + (colour.B - Pixels[i + 2]) * alpha + 0.5f);
        }

        public void Fill(Rgb colour) => FillRect(0, 0, Width, Height, colour);

        public void FillRect(int x, int y, int width, int height, Rgb colour)
        {
            int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
            int x1 = Math.Min(Width, x + width), y1 = Math.Min(Height, y + height);
            for (int py = y0; py < y1; py++)
            {
                int i = (py * Width + x0) * 3;
                for (int px = x0; px < x1; px++)
                {
                    Pixels[i++] = colour.R;
                    Pixels[i++] = colour.G;
                    Pixels[i++] = colour.B;
                }
            }
        }

        /// <summary>A disc centred on (<paramref name="cx"/>, <paramref name="cy"/>), its edge anti-aliased.</summary>
        public void FillCircle(float cx, float cy, float radius, Rgb colour)
        {
            if (radius <= 0f)
            {
                return;
            }

            int x0 = (int)Math.Floor(cx - radius - 1f), x1 = (int)Math.Ceiling(cx + radius + 1f);
            int y0 = (int)Math.Floor(cy - radius - 1f), y1 = (int)Math.Ceiling(cy + radius + 1f);
            for (int y = Math.Max(0, y0); y <= Math.Min(Height - 1, y1); y++)
            {
                float dy = y + 0.5f - cy;
                for (int x = Math.Max(0, x0); x <= Math.Min(Width - 1, x1); x++)
                {
                    float dx = x + 0.5f - cx;
                    float coverage = radius + 0.5f - (float)Math.Sqrt(dx * dx + dy * dy);
                    Blend(x, y, colour, coverage);
                }
            }
        }

        /// <summary>A ring between <paramref name="inner"/> and <paramref name="outer"/> radii, both edges anti-aliased.</summary>
        public void FillRing(float cx, float cy, float inner, float outer, Rgb colour)
        {
            int x0 = (int)Math.Floor(cx - outer - 1f), x1 = (int)Math.Ceiling(cx + outer + 1f);
            int y0 = (int)Math.Floor(cy - outer - 1f), y1 = (int)Math.Ceiling(cy + outer + 1f);
            for (int y = Math.Max(0, y0); y <= Math.Min(Height - 1, y1); y++)
            {
                float dy = y + 0.5f - cy;
                for (int x = Math.Max(0, x0); x <= Math.Min(Width - 1, x1); x++)
                {
                    float dx = x + 0.5f - cx;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                    float coverage = Math.Min(outer + 0.5f - d, d - inner + 0.5f);
                    Blend(x, y, colour, coverage);
                }
            }
        }

        /// <summary>A line <paramref name="thickness"/> pixels wide with round ends.</summary>
        public void DrawLine(float ax, float ay, float bx, float by, float thickness, Rgb colour)
        {
            float half = thickness / 2f;
            int x0 = (int)Math.Floor(Math.Min(ax, bx) - half - 1f), x1 = (int)Math.Ceiling(Math.Max(ax, bx) + half + 1f);
            int y0 = (int)Math.Floor(Math.Min(ay, by) - half - 1f), y1 = (int)Math.Ceiling(Math.Max(ay, by) + half + 1f);
            float vx = bx - ax, vy = by - ay;
            float lengthSquared = vx * vx + vy * vy;
            for (int y = Math.Max(0, y0); y <= Math.Min(Height - 1, y1); y++)
            {
                for (int x = Math.Max(0, x0); x <= Math.Min(Width - 1, x1); x++)
                {
                    float px = x + 0.5f - ax, py = y + 0.5f - ay;
                    float t = lengthSquared > 0f ? Math.Max(0f, Math.Min(1f, (px * vx + py * vy) / lengthSquared)) : 0f;
                    float dx = px - t * vx, dy = py - t * vy;
                    float coverage = half + 0.5f - (float)Math.Sqrt(dx * dx + dy * dy);
                    Blend(x, y, colour, coverage);
                }
            }
        }

        /// <summary>Copies every pixel of <paramref name="source"/>, which must be the same size.</summary>
        public void CopyFrom(RgbImage source)
        {
            if (source.Width != Width || source.Height != Height)
            {
                throw new ArgumentException("Images differ in size", nameof(source));
            }

            Buffer.BlockCopy(source.Pixels, 0, Pixels, 0, Pixels.Length);
        }
    }
}
