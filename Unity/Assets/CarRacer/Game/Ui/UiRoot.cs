using System;
using CarRacer.Core.Platform;
using CarRacer.Game.Platform;
using UnityEngine;
using UnityEngine.UIElements;

namespace CarRacer.Game.Ui
{
    /// <summary>
    /// A UI Toolkit layer built from code (design doc §3): no UXML or USS assets, just elements made
    /// by <see cref="UiKit"/>. Every layer shares one panel scaled so the screen is 720 reference pixels
    /// tall (<see cref="UiMetrics"/>), with a font on the root so text renders without a theme.
    /// <see cref="Full"/> covers the screen; <see cref="Safe"/> sits inside the safe area, clear of
    /// notches. Neither container takes touches itself, so only real controls block the game. When
    /// large text is switched, <see cref="Restyle"/> asks the owner to rebuild its elements.
    /// </summary>
    public sealed class UiRoot : MonoBehaviour
    {
        private static PanelSettings _settings;
        private static Font _font;

        private UIDocument _document;
        private Rect _safeArea;
        private Vector2 _screenSize;
        private bool _largeText;

        /// <summary>The whole screen, for backdrops and full-screen banners.</summary>
        public VisualElement Full { get; private set; }

        /// <summary>The safe area, for anything that must be readable or tappable.</summary>
        public VisualElement Safe { get; private set; }

        /// <summary>
        /// Stands in for <c>Screen.safeArea</c> (device pixels from the bottom-left) when set. Tests use it to fake a
        /// cutout, which the headless test runner's screen never has.
        /// </summary>
        public Rect? SafeAreaOverride { get; set; }

        /// <summary>Raised when large text is switched; rebuild the elements so they pick up the new sizes.</summary>
        public event Action Restyle;

        private Rect CurrentSafeArea => SafeAreaOverride ?? Screen.safeArea;

        /// <summary>Makes a layer under <paramref name="parent"/>; layers with a higher <paramref name="order"/> draw on top.</summary>
        public static UiRoot Create(Transform parent, string name, int order = 0)
        {
            // Configured while inactive, so UIDocument attaches to the panel once, with its settings.
            var host = new GameObject(name);
            host.SetActive(false);
            host.transform.SetParent(parent, false);
            var document = host.AddComponent<UIDocument>();
            document.panelSettings = Settings();
            document.sortingOrder = order;
            var root = host.AddComponent<UiRoot>();
            root._document = document;
            host.SetActive(true);
            root.Build();
            return root;
        }

        private static PanelSettings Settings()
        {
            if (_settings == null)
            {
                _settings = ScriptableObject.CreateInstance<PanelSettings>();
                _settings.hideFlags = HideFlags.HideAndDontSave;
                _settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                _settings.referenceResolution = new Vector2Int(1280, (int)UiMetrics.ReferenceHeight);
                _settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                _settings.match = 1f;

                // Every element is styled from code, so the theme is empty; it only stops the "no theme" warning.
                var theme = ScriptableObject.CreateInstance<ThemeStyleSheet>();
                theme.hideFlags = HideFlags.HideAndDontSave;
                _settings.themeStyleSheet = theme;
            }

            return _settings;
        }

        private void Build()
        {
            _font = _font != null ? _font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            // The document's root only grows to fit its content, and everything in it is positioned
            // absolutely, so it must be told to fill the screen or the whole layer is 0 pixels tall.
            var top = UiKit.Fill(_document.rootVisualElement);
            top.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(_font));

            Full = UiKit.Fill(new VisualElement { name = "full" });
            top.Add(Full);
            Safe = UiKit.Fill(new VisualElement { name = "safe" });
            Full.Add(Safe);
            _largeText = Accessibility.LargeText;
            ApplySafeArea();
        }

        private void Update()
        {
            if (CurrentSafeArea != _safeArea || _screenSize.x != Screen.width || _screenSize.y != Screen.height)
            {
                ApplySafeArea();
            }

            if (_largeText != Accessibility.LargeText)
            {
                _largeText = Accessibility.LargeText;
                Restyle?.Invoke();
            }
        }

        private void ApplySafeArea()
        {
            _safeArea = CurrentSafeArea;
            _screenSize = new Vector2(Screen.width, Screen.height);
            var insets = UiMetrics.SafeInsets(Screen.width, Screen.height, _safeArea.x, _safeArea.y, _safeArea.width, _safeArea.height);
            Safe.style.left = insets.Left;
            Safe.style.top = insets.Top;
            Safe.style.right = insets.Right;
            Safe.style.bottom = insets.Bottom;
        }
    }
}
