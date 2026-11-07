using System.Collections;
using CarRacer.Game.Screens;
using CarRacer.Game.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace CarRacer.Tests
{
    /// <summary>The "how to drive" tips show on the first drive and not once they have been seen.</summary>
    public class DrivingTipsTests
    {
        private bool _seen;

        [SetUp]
        public void SetUp() => _seen = DrivingTipsPanel.Seen;

        [TearDown]
        public void TearDown() => DrivingTipsPanel.Seen = _seen;

        [UnityTest]
        public IEnumerator TheTipsShowUntilSeen()
        {
            DrivingTipsPanel.Seen = false;
            var first = TrackScreen.Create();
            yield return null;
            var panel = first.GetComponent<DrivingTipsPanel>();
            Assert.IsNotNull(panel, "a first drive should show the tips");
            Assert.IsFalse(DrivingTipsPanel.Seen, "showing the tips isn't seeing them");

            // The race HUD is a UiRoot layer too, so pick the tips layer by name rather than by child order.
            var layer = first.transform.Find("Driving tips").GetComponent<UiRoot>();

            // "Got it" is what makes the tips seen: it persists that and takes the panel and its layer down.
            var gotIt = layer.Safe.Q<Button>();
            Assert.AreEqual("Got it", gotIt.text);
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = gotIt;
                gotIt.SendEvent(submit);
            }

            yield return null;
            Assert.IsTrue(DrivingTipsPanel.Seen, "pressing Got it should remember the tips were seen");
            Assert.IsTrue(panel == null, "pressing Got it should remove the panel");
            Assert.IsTrue(layer == null, "pressing Got it should remove the tips layer");
            Object.Destroy(first.gameObject);
            yield return null;

            var later = TrackScreen.Create();
            yield return null;
            Assert.IsNull(later.GetComponent<DrivingTipsPanel>(), "tips already seen shouldn't show again");
            Object.Destroy(later.gameObject);
        }
    }
}
