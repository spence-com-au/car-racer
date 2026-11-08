using System;
using CarRacer.Core.Numerics;

namespace CarRacer.Core.Driving
{
    /// <summary>Chase camera framing (design doc §6). Distances in metres, field of view in degrees.</summary>
    [Serializable]
    public sealed class ChaseCameraSettings
    {
        public float Distance = 6.5f;
        public float DistanceAtTopSpeed = 8f;
        public float Height = 2.6f;
        public float HeightAtTopSpeed = 3.2f;

        /// <summary>The camera looks at a point this high above the car, this far ahead of it.</summary>
        public float LookHeight = 1f;
        public float LookAhead = 3f;

        public float FieldOfView = 60f;
        public float FieldOfViewAtTopSpeed = 72f;

        /// <summary>Speed at which the "at top speed" values apply fully; boosting goes no further.</summary>
        public float TopSpeed = CarTuning.BaseMaxSpeed;

        /// <summary>How quickly the camera swings round behind the car (per second). Higher is stiffer.</summary>
        public float HeadingSharpness = 5f;

        /// <summary>How quickly the camera catches up with where it should be (per second).</summary>
        public float PositionSharpness = 10f;

        public float FieldOfViewSharpness = 3f;

        /// <summary>
        /// The same framing without the speed effects (reduced motion, design doc §6): the camera
        /// keeps its resting distance, height and field of view at every speed.
        /// </summary>
        public ChaseCameraSettings WithReducedMotion()
        {
            var calm = (ChaseCameraSettings)MemberwiseClone();
            calm.DistanceAtTopSpeed = Distance;
            calm.HeightAtTopSpeed = Height;
            calm.FieldOfViewAtTopSpeed = FieldOfView;
            return calm;
        }
    }

    public struct ChaseCameraState
    {
        public Vec3 Position;
        public Vec3 LookAt;
        public float FieldOfView;

        /// <summary>The direction the camera is chasing, which trails the car's heading in corners.</summary>
        public float Heading;

        public bool Initialised;
    }

    /// <summary>
    /// A camera that sits behind the car, swings round after it in corners, and rises, pulls back
    /// and widens its field of view as the car speeds up. All smoothing is exponential, so it
    /// behaves the same at 30 and 120 fps.
    /// </summary>
    public static class ChaseCameraRig
    {
        public static ChaseCameraState Step(ChaseCameraState state, Vec3 target, float targetHeading, float speed, float deltaSeconds, ChaseCameraSettings settings)
        {
            var (distance, height, fov) = Framing(speed, settings);

            if (!state.Initialised)
            {
                state.Heading = targetHeading;
                state.FieldOfView = fov;
            }
            else
            {
                state.Heading = Angles.WrapSigned(state.Heading + Angles.WrapSigned(targetHeading - state.Heading) * Interpolation.Blend(settings.HeadingSharpness, deltaSeconds));
                state.FieldOfView = Interpolation.Lerp(state.FieldOfView, fov, Interpolation.Blend(settings.FieldOfViewSharpness, deltaSeconds));
            }

            var forward = Angles.Forward(state.Heading);
            Vec3 desired = target - forward * distance + Vec3.Up * height;
            state.Position = state.Initialised
                ? Vec3.Lerp(state.Position, desired, Interpolation.Blend(settings.PositionSharpness, deltaSeconds))
                : desired;
            state.LookAt = target + forward * settings.LookAhead + Vec3.Up * settings.LookHeight;
            state.Initialised = true;
            return state;
        }

        /// <summary>
        /// The rear-view glance (design doc §6): <paramref name="chase"/>'s framing turned round, in
        /// front of the car and looking back past it, square to the car rather than trailing it. It
        /// isn't smoothed, so the view cuts there and back; keep stepping the chase camera meanwhile.
        /// </summary>
        public static ChaseCameraState RearView(ChaseCameraState chase, Vec3 target, float targetHeading, float speed, ChaseCameraSettings settings)
        {
            var (distance, height, _) = Framing(speed, settings);
            var forward = Angles.Forward(targetHeading);
            chase.Position = target + forward * distance + Vec3.Up * height;
            chase.LookAt = target - forward * settings.LookAhead + Vec3.Up * settings.LookHeight;
            chase.Heading = Angles.WrapSigned(targetHeading + MathF.PI);
            return chase;
        }

        /// <summary>
        /// The distance, height and field of view the camera settles at for <paramref name="speed"/>: each runs from
        /// its resting value to its top-speed one as the speed, whichever way the car is going, nears the top speed.
        /// </summary>
        private static (float Distance, float Height, float FieldOfView) Framing(float speed, ChaseCameraSettings settings)
        {
            float t = settings.TopSpeed > 0f ? Math.Max(0f, Math.Min(1f, Math.Abs(speed) / settings.TopSpeed)) : 0f;
            return (
                Interpolation.Lerp(settings.Distance, settings.DistanceAtTopSpeed, t),
                Interpolation.Lerp(settings.Height, settings.HeightAtTopSpeed, t),
                Interpolation.Lerp(settings.FieldOfView, settings.FieldOfViewAtTopSpeed, t));
        }
    }
}
