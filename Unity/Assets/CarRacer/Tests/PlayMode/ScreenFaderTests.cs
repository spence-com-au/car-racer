using System.Collections;
using CarRacer.Game.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace CarRacer.Tests
{
    /// <summary>A new screen fades in from black and the cover then gets out of the way.</summary>
    public class ScreenFaderTests
    {
        [UnityTest]
        public IEnumerator TheCoverShowsThenLifts()
        {
            ScreenFader.Install();
            var document = GameObject.Find(nameof(ScreenFader)).GetComponentInChildren<UIDocument>();
            var cover = document.rootVisualElement.Q("cover");
            ScreenFader.Cover();
            Assert.IsTrue(ScreenFader.Fading);
            Assert.AreEqual(DisplayStyle.Flex, cover.style.display.value, "the cover is shown");
            Assert.AreEqual(1f, cover.style.opacity.value, "the cover starts opaque");

            // The fade runs on real time (each frame moving it on by at most a 30 fps step), and the
            // test runner's frames can be far shorter than a phone's, so wait on the clock, not a frame count.
            float giveUp = Time.realtimeSinceStartup + 3f;
            while (ScreenFader.Fading && Time.realtimeSinceStartup < giveUp)
            {
                yield return null;
            }

            Assert.IsFalse(ScreenFader.Fading, "the fade should finish");
            Assert.AreEqual(DisplayStyle.None, cover.style.display.value, "the cover is hidden once the fade ends");
        }
    }
}
