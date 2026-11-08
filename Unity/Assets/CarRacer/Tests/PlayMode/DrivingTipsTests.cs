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
        private TrackScreen _screen;

        [SetUp]
        public void SetUp() => _seen = DrivingTipsPanel.Seen;

        [TearDown]
        public void TearDown()
        {
            DrivingTipsPanel.Seen = _seen;

            // Here rather than at the end of the test, so a failed assertion can't leave its car on the grid for the
            // next test to drive into.
            if (_screen != null)
            {
                Object.Destroy(_screen.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator TheTipsShowUntilSeen()
        {
            DrivingTipsPanel.Seen = false;
            _screen = TrackScreen.Create();
            yield return null;
            var panel = _screen.GetComponent<DrivingTipsPanel>();
            Assert.IsNotNull(panel, "a first drive should show the tips");
            Assert.IsFalse(DrivingTipsPanel.Seen, "showing the tips isn't seeing them");

            // The race HUD is a UiRoot layer too, so pick the tips layer by name rather than by child order.
            var layer = _screen.transform.Find("Driving tips").GetComponent<UiRoot>();

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
            Object.Destroy(_screen.gameObject);
            yield return null;

            _screen = TrackScreen.Create();
            yield return null;
            Assert.IsNull(_screen.GetComponent<DrivingTipsPanel>(), "tips already seen shouldn't show again");
        }
    }
}
