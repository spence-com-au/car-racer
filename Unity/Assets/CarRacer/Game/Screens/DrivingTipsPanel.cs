using CarRacer.Core.Driving;
using CarRacer.Game.Driving;
using CarRacer.Game.Platform;
using CarRacer.Game.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace CarRacer.Game.Screens
{
    /// <summary>
    /// "How to drive" (design doc §5): a panel of <see cref="DrivingTips"/> the first time this phone
    /// is on the track, until "Got it" is pressed; Settings can bring it back. It sits over the
    /// session screens so the lobby can't hide it, and driving carries on underneath, as touches
    /// steer whatever is drawn over them.
    /// </summary>
    public sealed class DrivingTipsPanel : MonoBehaviour
    {
        private UiRoot _layer;

        public static bool Seen
        {
            get => PlayerPrefs.GetInt(DrivingTips.SeenKey, 0) != 0;
            set => PlayerPrefs.SetInt(DrivingTips.SeenKey, value ? 1 : 0);
        }

        /// <summary>For the Settings button: tapping it flips whether the tips show on the next drive.</summary>
        public static string Label => Seen ? "Driving tips: show again" : "Driving tips: next drive";

        /// <summary>Shows the tips on <paramref name="screen"/> unless they have been seen.</summary>
        public static void ShowIfNew(TrackScreen screen, PlayerCarInput input)
        {
            if (!Seen && input != null)
            {
                screen.gameObject.AddComponent<DrivingTipsPanel>().Build();
            }
        }

        private void Build()
        {
            _layer = UiRoot.Create(transform, "Driving tips", order: 20);
            _layer.Restyle += Rebuild;
            Rebuild();
        }

        private void Rebuild()
        {
            _layer.Safe.Clear();
            var column = UiKit.Fill(new VisualElement { name = "tips column" });
            column.style.alignItems = Align.Center;
            column.style.justifyContent = Justify.Center;
            _layer.Safe.Add(column);

            var panel = UiKit.Panel(720f);
            panel.style.maxHeight = Length.Percent(90);
            panel.Add(UiKit.Heading("How to drive"));
            var tips = DrivingTips.For(Accessibility.LeftHanded, PlayerCarInput.AutoAccelerateEnabled, PlayerCarInput.TiltEnabled, lookBack: true);
            foreach (string tip in tips)
            {
                var line = UiKit.Body("•  " + tip);
                line.style.marginTop = 6f;
                panel.Add(line);
            }

            panel.Add(UiKit.Space(12f));
            panel.Add(UiKit.Button("Got it", Dismiss, primary: true));
            column.Add(panel);
        }

        private void Dismiss()
        {
            Seen = true;
            Destroy(_layer.gameObject);
            Destroy(this);
        }
    }
}
