using System;

namespace CarRacer.Core.Driving
{
    /// <summary>What one input device (touch screen, keyboard, gamepad) is asking for this frame.</summary>
    public struct DriverControls
    {
        /// <summary>-1 (full left) to 1 (full right).</summary>
        public float Steer;

        /// <summary>0 to 1.</summary>
        public float Accelerate;

        /// <summary>0 to 1. Brakes, then reverses once stopped.</summary>
        public float Brake;

        public bool Drift;

        /// <summary>The use-item control was pressed this frame (a fresh tap or key press, not a hold).</summary>
        public bool UseItem;

        /// <summary>The rear-view glance is held (design doc §6).</summary>
        public bool LookBack;
    }

    /// <summary>Merges every device's controls into the single <see cref="CarInput"/> the car model takes.</summary>
    public static class DriverInput
    {
        /// <summary>Brake input below this is treated as resting on the trigger, not braking.</summary>
        public const float BrakeThreshold = 0.1f;

        /// <summary>
        /// The strongest steer wins, so a gamepad and the touch screen can be used side by side; on a
        /// tie, <paramref name="a"/>'s. The harder press of each pedal counts, and so does a button
        /// pressed on either device.
        /// </summary>
        public static DriverControls Merge(DriverControls a, DriverControls b) => new DriverControls
        {
            Steer = Math.Abs(a.Steer) >= Math.Abs(b.Steer) ? a.Steer : b.Steer,
            Accelerate = Math.Max(a.Accelerate, b.Accelerate),
            Brake = Math.Max(a.Brake, b.Brake),
            Drift = a.Drift || b.Drift,
            UseItem = a.UseItem || b.UseItem,
            LookBack = a.LookBack || b.LookBack,
        };

        /// <summary>
        /// Braking overrides accelerating. With auto-accelerate on (design doc §5) the car drives at
        /// full throttle unless braking.
        /// </summary>
        public static CarInput ToCarInput(DriverControls controls, bool autoAccelerate)
        {
            float brake = Math.Clamp(controls.Brake, 0f, 1f);
            float accelerate = autoAccelerate ? 1f : Math.Clamp(controls.Accelerate, 0f, 1f);
            float throttle = brake > BrakeThreshold ? -brake : accelerate;
            return new CarInput(Math.Max(-1f, Math.Min(1f, controls.Steer)), throttle, controls.Drift);
        }
    }
}
