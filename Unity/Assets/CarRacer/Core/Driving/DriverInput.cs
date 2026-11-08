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
        /// The strongest steer wins, so a gamepad and the touch screen can be used side by side.
        /// Braking overrides accelerating. With auto-accelerate on (design doc §5) the car drives at
        /// full throttle unless braking.
        /// </summary>
        public static CarInput Combine(DriverControls a, DriverControls b, bool autoAccelerate)
        {
            float steer = Math.Abs(a.Steer) >= Math.Abs(b.Steer) ? a.Steer : b.Steer;
            float brake = Math.Clamp(Math.Max(a.Brake, b.Brake), 0f, 1f);
            float accelerate = autoAccelerate ? 1f : Math.Clamp(Math.Max(a.Accelerate, b.Accelerate), 0f, 1f);
            float throttle = brake > BrakeThreshold ? -brake : accelerate;
            return new CarInput(Math.Clamp(steer, -1f, 1f), throttle, a.Drift || b.Drift);
        }
    }
}
