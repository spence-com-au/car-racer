using System;
using CarRacer.Core.Numerics;

namespace CarRacer.Core.Driving
{
    /// <summary>Overhead arena camera framing (design doc §6). Distances in metres, field of view in degrees.</summary>
    [Serializable]
    public sealed class OverheadCameraSettings
    {
        /// <summary>The camera sits this high above the point it looks at...</summary>
        public float Height = 30f;

        /// <summary>...and this far back from it (towards -Z), so it looks down at a steep angle without turning.</summary>
        public float Back = 20f;

        /// <summary>How far the camera's focus is pulled from the car towards the arena's centre, so rivals near the middle stay in shot.</summary>
        public float CentrePull = 0.35f;

        public float FieldOfView = 55f;

        /// <summary>How quickly the camera catches up with where it should be (per second).</summary>
        public float Sharpness = 4f;
    }

    /// <summary>
    /// The arena camera: high and angled, always facing the same way, following the player's car
    /// but leaning towards the arena's centre so everyone is visible (design doc §6). Smoothing is
    /// exponential, as in <see cref="ChaseCameraRig"/>.
    /// </summary>
    public static class OverheadCameraRig
    {
        public static ChaseCameraState Step(ChaseCameraState state, Vec3 target, Vec3 arenaCentre, float deltaSeconds, OverheadCameraSettings settings)
        {
            var focus = Vec3.Lerp(target, arenaCentre, settings.CentrePull);
            focus = new Vec3(focus.X, target.Y, focus.Z);
            var desired = focus + new Vec3(0f, settings.Height, -settings.Back);
            float blend = state.Initialised ? Interpolation.Blend(settings.Sharpness, deltaSeconds) : 1f;
            state.Position = Vec3.Lerp(state.Position, desired, blend);
            state.LookAt = Vec3.Lerp(state.LookAt, focus, blend);
            state.FieldOfView = settings.FieldOfView;
            state.Heading = 0f;
            state.Initialised = true;
            return state;
        }
    }
}
