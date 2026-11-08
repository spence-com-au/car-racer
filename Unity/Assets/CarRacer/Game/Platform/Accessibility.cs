using UnityEngine;

namespace CarRacer.Game.Platform
{
    /// <summary>
    /// The accessibility options (design doc §5, §6, §11), each remembered on the phone: the
    /// left-handed control layout, large controls, large text, and reduced motion (no speed zoom or
    /// lean, no flashing warnings). Read every frame by what they change, so they apply at once.
    /// </summary>
    public static class Accessibility
    {
        public const string LeftHandedKey = "leftHanded";
        public const string LargeControlsKey = "largeControls";
        public const string LargeTextKey = "largeText";
        public const string ReducedMotionKey = "reducedMotion";

        /// <summary>How much bigger text is with large text.</summary>
        public const float LargeTextScale = 1.2f;

        private static bool? _leftHanded;
        private static bool? _largeControls;
        private static bool? _largeText;
        private static bool? _reducedMotion;

        public static bool LeftHanded
        {
            get => Get(ref _leftHanded, LeftHandedKey);
            set => Set(ref _leftHanded, LeftHandedKey, value);
        }

        public static bool LargeControls
        {
            get => Get(ref _largeControls, LargeControlsKey);
            set => Set(ref _largeControls, LargeControlsKey, value);
        }

        public static bool LargeText
        {
            get => Get(ref _largeText, LargeTextKey);
            set => Set(ref _largeText, LargeTextKey, value);
        }

        public static bool ReducedMotion
        {
            get => Get(ref _reducedMotion, ReducedMotionKey);
            set => Set(ref _reducedMotion, ReducedMotionKey, value);
        }

        /// <summary>Multiplies a font size for the large-text option.</summary>
        public static int Text(int size) => LargeText ? Mathf.RoundToInt(size * LargeTextScale) : size;

        /// <summary>Reads a remembered on/off choice once, then serves it from <paramref name="cached"/>.</summary>
        internal static bool Get(ref bool? cached, string key, bool fallback = false) =>
            cached ?? (bool)(cached = PlayerPrefs.GetInt(key, fallback ? 1 : 0) != 0);

        /// <summary>Remembers an on/off choice in <paramref name="cached"/> and on the phone.</summary>
        internal static void Set(ref bool? cached, string key, bool value)
        {
            cached = value;
            PlayerPrefs.SetInt(key, value ? 1 : 0);
        }

        /// <summary>Reads a remembered number once, then serves it from <paramref name="cached"/>.</summary>
        internal static int Get(ref int? cached, string key, int fallback) =>
            cached ?? (int)(cached = PlayerPrefs.GetInt(key, fallback));

        /// <summary>Remembers a number in <paramref name="cached"/> and on the phone.</summary>
        internal static void Set(ref int? cached, string key, int value)
        {
            cached = value;
            PlayerPrefs.SetInt(key, value);
        }
    }
}
