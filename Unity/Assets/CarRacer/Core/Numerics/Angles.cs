using System;

namespace CarRacer.Core.Numerics
{
    /// <summary>
    /// Yaw helpers for the ground plane. A heading is radians clockwise from +Z viewed from above, matching
    /// <see cref="Vec3.RotateAroundUp"/>: heading 0 faces +Z and heading π/2 faces +X.
    /// </summary>
    public static class Angles
    {
        private const float TwoPi = 2f * MathF.PI;

        /// <summary>The heading a vector points along, ignoring its Y component.</summary>
        public static float Heading(Vec3 v) => MathF.Atan2(v.X, v.Z);

        /// <summary>The unit ground-plane vector a heading points along.</summary>
        public static Vec3 Forward(float heading) => new Vec3(MathF.Sin(heading), 0f, MathF.Cos(heading));

        /// <summary>The same angle in [-π, π], for differences between headings.</summary>
        public static float WrapSigned(float radians)
        {
            radians %= TwoPi;
            if (radians > MathF.PI)
            {
                radians -= TwoPi;
            }
            else if (radians < -MathF.PI)
            {
                radians += TwoPi;
            }

            return radians;
        }

        /// <summary>The same angle in [0, 2π), for an absolute heading.</summary>
        public static float WrapPositive(float radians)
        {
            radians %= TwoPi;
            return radians < 0f ? radians + TwoPi : radians;
        }
    }
}
