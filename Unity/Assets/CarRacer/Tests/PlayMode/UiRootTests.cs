using System.Collections;
using CarRacer.Game.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace CarRacer.Tests
{
    /// <summary>A UI Toolkit layer built from code lays its elements out in 720-pixel-tall reference units.</summary>
    public class UiRootTests
    {
        private GameObject _host;

        [SetUp]
        public void SetUp() => _host = new GameObject("UI test");

        [TearDown]
        public void TearDown() => Object.Destroy(_host);

        [UnityTest]
        public IEnumerator ElementsLayOutAtTheHouseSizes()
        {
            var layer = UiRoot.Create(_host.transform, "Test layer");
            var panel = UiKit.Panel(400f);
            var button = UiKit.Button("Go", () => { }, primary: true);
            panel.Add(UiKit.Heading("Hello"));
            panel.Add(button);
            layer.Safe.Add(panel);

            // Layout happens on the panel's next update.
            yield return null;
            yield return null;

            Assert.That(button.resolvedStyle.height, Is.EqualTo(UiKit.ButtonHeight).Within(0.5f));
            Assert.That(panel.resolvedStyle.width, Is.EqualTo(400f).Within(0.5f));
            Assert.That(button.worldBound.height, Is.GreaterThan(0f), "the button should be laid out on screen");
            Assert.AreEqual(PickingMode.Ignore, layer.Safe.pickingMode, "containers mustn't block touches to the game");
        }

        [UnityTest]
        public IEnumerator TheSafeContainerIsInsetFromEachEdgeByTheSafeArea()
        {
            var layer = UiRoot.Create(_host.transform, "Safe layer");

            // A different inset on every edge, so an edge applied to the wrong side shows. `Screen.safeArea` runs from
            // the bottom-left: 10% off the left, 5% off the bottom, 5% off the right and 15% off the top.
            var width = Screen.width;
            var height = Screen.height;
            layer.SafeAreaOverride = new Rect(0.10f * width, 0.05f * height, 0.85f * width, 0.80f * height);

            // The override is picked up in the next `Update` and laid out on the panel's update after that.
            yield return null;
            yield return null;

            var full = layer.Full.worldBound;
            var safe = layer.Safe.worldBound;
            Assert.That(full.width, Is.GreaterThan(0f));
            Assert.That(safe.xMin, Is.EqualTo(full.xMin + 0.10f * full.width).Within(0.5f), "left edge");
            Assert.That(safe.yMin, Is.EqualTo(full.yMin + 0.15f * full.height).Within(0.5f), "top edge");
            Assert.That(safe.xMax, Is.EqualTo(full.xMax - 0.05f * full.width).Within(0.5f), "right edge");
            Assert.That(safe.yMax, Is.EqualTo(full.yMax - 0.05f * full.height).Within(0.5f), "bottom edge");
        }

        [UnityTest]
        public IEnumerator ThePanelIsScaledTo720ReferencePixelsTall()
        {
            var layer = UiRoot.Create(_host.transform, "Scaled layer");
            yield return null;
            yield return null;

            Assert.That(layer.Full.worldBound.height, Is.EqualTo(720f).Within(1f));
        }
    }
}
