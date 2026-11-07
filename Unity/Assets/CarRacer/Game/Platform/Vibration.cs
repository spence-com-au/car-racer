using System;
using CarRacer.Core.Driving;
using UnityEngine;

namespace CarRacer.Game.Platform
{
    /// <summary>
    /// Plays <see cref="HapticPulse"/>s with Android's Vibrator (VibrationEffect.createOneShot,
    /// API 26+, so every supported phone). Phones without amplitude control get the default
    /// strength. The player can turn it off; the choice is remembered. No-op off Android.
    /// </summary>
    public static class Vibration
    {
        public const string EnabledKey = "haptics";

        private static bool? _enabled;
#if UNITY_ANDROID && !UNITY_EDITOR
        private const int DefaultAmplitude = -1;
        private static AndroidJavaObject _vibrator;
        private static AndroidJavaClass _effects;
        private static bool _hasAmplitudeControl;
        private static bool _unavailable;
#endif

        public static bool Enabled
        {
            get => _enabled ?? (bool)(_enabled = PlayerPrefs.GetInt(EnabledKey, 1) != 0);
            set
            {
                _enabled = value;
                PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
            }
        }

        public static string Label => Enabled ? "Haptics: on" : "Haptics: off";

        public static void Play(HapticPulse pulse)
        {
            if (!Enabled || pulse.Milliseconds <= 0)
            {
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            if (_unavailable)
            {
                return;
            }

            try
            {
                if (_vibrator == null)
                {
                    using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                    using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                    _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                    _effects = new AndroidJavaClass("android.os.VibrationEffect");
                    _hasAmplitudeControl = _vibrator.Call<bool>("hasAmplitudeControl");
                }

                int amplitude = _hasAmplitudeControl ? Mathf.Clamp(pulse.Amplitude, 1, 255) : DefaultAmplitude;
                using var effect = _effects.CallStatic<AndroidJavaObject>("createOneShot", (long)pulse.Milliseconds, amplitude);
                _vibrator.Call("vibrate", effect);
            }
            catch (Exception e)
            {
                _unavailable = true;
                Debug.LogWarning($"Vibration unavailable: {e.Message}");
            }
#endif
        }
    }
}
