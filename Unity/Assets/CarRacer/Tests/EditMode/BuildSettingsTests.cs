#if UNITY_ANDROID
using System.IO;
using System.Xml;
using CarRacer.Core.Discovery;
#endif
using CarRacer.Editor;
using CarRacer.Game;
using NUnit.Framework;
using UnityEditor;
#if UNITY_ANDROID
using UnityEditor.Android;
using UnityEditor.Build;
#endif
using UnityEngine;

namespace CarRacer.Tests
{
    /// <summary>Things a phone build needs that the editor would hide: the editor has every shader, a build only what it references.</summary>
    public class BuildSettingsTests
    {
        [Test]
        public void TheMaterialShaderIsAlwaysIncludedInBuilds()
        {
            Assert.IsNotNull(Shader.Find(SimpleMaterials.ShaderName));
            ProjectConfigurator.Apply();
            Assert.IsTrue(ProjectConfigurator.IsAlwaysIncluded(SimpleMaterials.ShaderName));
        }

        [Test]
        public void TheAppInstallsToInternalStorage()
        {
            ProjectConfigurator.Apply();
            Assert.AreEqual(AndroidPreferredInstallLocation.ForceInternal, PlayerSettings.Android.preferredInstallLocation);
        }

        [Test]
        public void TheAppIconTexturesLoad()
        {
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Texture2D>(ProjectConfigurator.IconBackgroundPath));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Texture2D>(ProjectConfigurator.IconForegroundPath));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Texture2D>(ProjectConfigurator.IconRoundPath));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Texture2D>(ProjectConfigurator.IconLegacyPath));
        }

#if UNITY_ANDROID
        [Test]
        public void EveryAndroidIconDensityHasTheAppIcon()
        {
            ProjectConfigurator.Apply();
            var background = AssetDatabase.LoadAssetAtPath<Texture2D>(ProjectConfigurator.IconBackgroundPath);
            var foreground = AssetDatabase.LoadAssetAtPath<Texture2D>(ProjectConfigurator.IconForegroundPath);
            var round = AssetDatabase.LoadAssetAtPath<Texture2D>(ProjectConfigurator.IconRoundPath);
            var legacy = AssetDatabase.LoadAssetAtPath<Texture2D>(ProjectConfigurator.IconLegacyPath);

            var adaptive = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, AndroidPlatformIconKind.Adaptive);
            Assert.IsNotEmpty(adaptive);
            foreach (var icon in adaptive)
            {
                CollectionAssert.AreEqual(new[] { background, foreground }, icon.GetTextures());
            }

            foreach (var icon in PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, AndroidPlatformIconKind.Round))
            {
                Assert.AreEqual(round, icon.GetTexture());
            }

            foreach (var icon in PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, AndroidPlatformIconKind.Legacy))
            {
                Assert.AreEqual(legacy, icon.GetTexture());
            }
        }

        private const string AndroidNamespace = "http://schemas.android.com/apk/res/android";

        private const string SettingsActivity = @"<activity android:name="".SettingsActivity"" />";

        private const string GameActivity = @"<activity android:name="".GameActivity"">
              <intent-filter>
                <action android:name=""android.intent.action.MAIN"" />
                <category android:name=""android.intent.category.LAUNCHER"" />
              </intent-filter>
            </activity>";

        private const string JoinLinkFilter = "//activity[@android:name='.GameActivity']"
            + "/intent-filter[action/@android:name='android.intent.action.VIEW']";

        [Test]
        public void TheManifestGetsEachLanPermissionOnce()
        {
            // The sample already declares INTERNET, so the post-processor must skip it rather than add a second.
            XmlDocument manifest = PostProcess(GameActivity);

            foreach (string permission in new[]
            {
                "android.permission.INTERNET",
                "android.permission.ACCESS_NETWORK_STATE",
                "android.permission.ACCESS_WIFI_STATE",
                "android.permission.CHANGE_WIFI_MULTICAST_STATE",
                "android.permission.VIBRATE",
            })
            {
                var declared = Select(manifest, $"/manifest/uses-permission[@android:name='{permission}']");
                Assert.AreEqual(1, declared.Count, permission);
            }
        }

        [Test]
        public void ProcessingAManifestAgainChangesNothing()
        {
            Assert.AreEqual(PostProcess(GameActivity).OuterXml, PostProcess(GameActivity, 2).OuterXml);
        }

        [Test]
        public void TheJoinLinkFilterLandsOnTheLauncherActivity()
        {
            XmlDocument manifest = PostProcess(SettingsActivity + GameActivity);
            var link = new System.Uri(JoinLink.Make("192.168.0.2", 47780));

            XmlNodeList data = Select(manifest, JoinLinkFilter + "/data");
            Assert.AreEqual(1, data.Count);
            Assert.AreEqual(link.Scheme, ((XmlElement)data[0]).GetAttribute("scheme", AndroidNamespace));
            Assert.AreEqual(link.Host, ((XmlElement)data[0]).GetAttribute("host", AndroidNamespace));

            // A camera app or browser only hands a link to an activity that is both DEFAULT and BROWSABLE.
            foreach (string category in new[] { "DEFAULT", "BROWSABLE" })
            {
                string name = $"android.intent.category.{category}";
                Assert.AreEqual(1, Select(manifest, $"{JoinLinkFilter}/category[@android:name='{name}']").Count, name);
            }

            Assert.AreEqual(0, Select(manifest, "//activity[@android:name='.SettingsActivity']/intent-filter").Count);
        }

        [Test]
        public void AnExistingViewActionOnTheLauncherActivityIsLeftAlone()
        {
            const string viewFilter = @"<intent-filter>
                <action android:name=""android.intent.action.VIEW"" />
              </intent-filter>";
            XmlDocument manifest = PostProcess(GameActivity.Replace("</activity>", viewFilter + "</activity>"));

            Assert.AreEqual(2, Select(manifest, "//activity/intent-filter").Count);
            Assert.AreEqual(0, Select(manifest, "//data").Count);
        }

        [Test]
        public void ABuildWithoutALauncherActivityFailsRatherThanDroppingTheJoinLink()
        {
            // Unity's generated manifest could stop declaring the launcher activity where this looks for it; shipping
            // that APK would leave the host's QR code opening nothing, with no error anywhere.
            var failure = Assert.Throws<BuildFailedException>(() => PostProcess(SettingsActivity));
            StringAssert.Contains("LAUNCHER", failure.Message);
        }

        /// <summary>
        /// The manifest after the post-processor has run <paramref name="times"/> times over one declaring
        /// <paramref name="activities"/>.
        /// </summary>
        private static XmlDocument PostProcess(string activities, int times = 1)
        {
            string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string path = Path.Combine(root, "src", "main", "AndroidManifest.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            try
            {
                File.WriteAllText(path, $@"<manifest xmlns:android=""{AndroidNamespace}"">
  <uses-permission android:name=""android.permission.INTERNET"" />
  <application>{activities}</application>
</manifest>");

                for (int i = 0; i < times; i++)
                {
                    new AndroidManifestPostProcessor().OnPostGenerateGradleAndroidProject(root);
                }

                var document = new XmlDocument();
                document.Load(path);
                return document;
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static XmlNodeList Select(XmlDocument document, string xpath)
        {
            var namespaces = new XmlNamespaceManager(document.NameTable);
            namespaces.AddNamespace("android", AndroidNamespace);
            return document.SelectNodes(xpath, namespaces);
        }
#endif

        [Test]
        public void MaterialsUseThatShader()
        {
            var material = SimpleMaterials.Get(new Color(0.1f, 0.2f, 0.3f));
            Assert.AreEqual(SimpleMaterials.ShaderName, material.shader.name);
            Assert.AreEqual(new Color(0.1f, 0.2f, 0.3f), material.color);
        }
    }
}
