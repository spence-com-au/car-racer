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
