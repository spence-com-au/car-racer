using System;

namespace CarRacer.Core.Numerics
{
    /// <summary>Scalar interpolation shared by the car model and the camera rigs.</summary>
    public static class Interpolation
    {
        /// <summary>The value <paramref name="t"/> of the way from <paramref name="a"/> to <paramref name="b"/>.</summary>
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>
        /// Fraction of the remaining gap closed in <paramref name="dt"/> seconds by exponential smoothing at
        /// <paramref name="sharpness"/> per second, so the same smoothing looks the same at any frame rate.
        /// </summary>
        public static float Blend(float sharpness, float dt) => 1f - MathF.Exp(-sharpness * dt);
    }
}
