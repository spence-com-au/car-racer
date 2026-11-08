using System;
using CarRacer.Core.Numerics;

namespace CarRacer.Core.Physics
{
    /// <summary>A car's physical state as one phone sees it at the moment of contact.</summary>
    public struct CarBody
    {
        public Vec3 Position;
        public Vec3 Velocity;
        public float Mass;
        public float Radius;

        public CarBody(Vec3 position, Vec3 velocity, float mass = 1f, float radius = 1f)
        {
            Position = position;
            Velocity = velocity;
            Mass = mass;
            Radius = radius;
        }
    }

    [Serializable]
    public sealed class BumpSettings
    {
        /// <summary>Bounciness of car-to-car contact (0 = dead stop, 1 = perfectly elastic).</summary>
        public float Restitution = 0.8f;

        /// <summary>Multiplier on the physical impulse; above 1 makes bumps more dramatic (arcade feel).</summary>
        public float Exaggeration = 1.4f;

        /// <summary>Smallest horizontal impulse per unit of reduced mass for any approaching contact, so gentle touches still nudge.</summary>
        public float MinImpulsePerMass = 1.5f;

        /// <summary>Largest horizontal impulse per unit of reduced mass; stops head-on boosts launching cars off the map.</summary>
        public float MaxImpulsePerMass = 25f;

        /// <summary>Upward "pop" as a fraction of the horizontal impulse. Applied equally to both cars.</summary>
        public float LiftFraction = 0.15f;
    }

    /// <summary>
    /// Arcade car-to-car collision response for owner-authoritative cars.
    ///
    /// Each phone detects contact between its own car and a (predicted) remote car and calls
    /// <see cref="ComputeSelfImpulse"/> to get the impulse for its own car only. The formula is
    /// antisymmetric in the horizontal plane: when both phones see the same contact, each
    /// applies an equal and opposite push, with no network message needed. The remote car's
    /// phone does the same from its side.
    /// </summary>
    public static class BumpResolver
    {
        /// <summary>Returns the impulse (mass × Δv) to apply to <paramref name="self"/>, or zero if the cars aren't approaching.</summary>
        public static Vec3 ComputeSelfImpulse(CarBody self, CarBody other, BumpSettings settings)
        {
            Vec3 normal = (self.Position - other.Position).Flattened;
            if (normal.LengthSquared < 1e-8f)
            {
                // Coincident centres: push apart along the relative velocity. If that is also zero
                // there is no contact direction both phones would agree on, so apply nothing.
                normal = (self.Velocity - other.Velocity).Flattened;
                if (normal.LengthSquared < 1e-8f)
                {
                    return Vec3.Zero;
                }

                normal = -normal;
            }

            normal = normal.Normalized;
            Vec3 relativeVelocity = (self.Velocity - other.Velocity).Flattened;
            float approachSpeed = -Vec3.Dot(relativeVelocity, normal);
            if (approachSpeed <= 0f)
            {
                return Vec3.Zero;
            }

            float inverseMassSum = 1f / self.Mass + 1f / other.Mass;
            float impulse = (1f + settings.Restitution) * approachSpeed / inverseMassSum * settings.Exaggeration;

            // Clamp using the reduced mass so the clamped value is the same from both sides.
            float reducedMass = 1f / inverseMassSum;
            impulse = Clamp(impulse, settings.MinImpulsePerMass * reducedMass, settings.MaxImpulsePerMass * reducedMass);

            return normal * impulse + Vec3.Up * (impulse * settings.LiftFraction);
        }

        /// <summary>True if two cars' collision circles overlap on the ground plane.</summary>
        public static bool AreTouching(CarBody a, CarBody b)
        {
            float reach = a.Radius + b.Radius;
            return (a.Position - b.Position).Flattened.LengthSquared <= reach * reach;
        }

        private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}
