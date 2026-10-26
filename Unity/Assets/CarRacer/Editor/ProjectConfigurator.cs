using CarRacer.Game;
using UnityEditor;
#if UNITY_ANDROID
using UnityEditor.Android;
#endif
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarRacer.Editor
{
    /// <summary>
    /// Player settings applied from code rather than hand-edited in ProjectSettings, so they're
    /// reviewable in diffs and the CI build always uses them (BuildScript calls Apply first).
    /// </summary>
    public static class ProjectConfigurator
    {
        public const string ApplicationId = "io.github.joshuaspence.carracer";
        public const string ProductName = "Car Racer";

        [MenuItem("Car Racer/Apply Project Settings")]
        public static void Apply()
        {
            PlayerSettings.companyName = ProductName;
            PlayerSettings.productName = ProductName;

            var android = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(android, ApplicationId);
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(android, ApiCompatibilityLevel.NET_Standard);

            // Oldest supported phone is a Pixel 2 XL (Android 8 at launch, up to 11).
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.forceInternetPermission = true;

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            // Unity defaults to preferExternal. Install sessions (the Files app's, for one) default to
            // internal storage, so the system logged the manifest's choice as dropped, and installs
            // from the Files app were abandoned ("Cancelling") though adb worked. Internal-only matches.
            PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.ForceInternal;

            // Vulkan first, GLES3 fallback. Adreno 5xx (Pixel 2 XL) should be steered to GLES3
            // with a Vulkan device filter list; that's set up with the rendering work in M1.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });

#if UNITY_ANDROID
            ApplyIcons();
#endif
            EditorUserBuildSettings.buildAppBundle = false;
            EnsureAlwaysIncludedShader(SimpleMaterials.ShaderName);
            AssetDatabase.SaveAssets();
        }

        /// <summary>The app icon's textures, rendered by <c>tools/icon/generate_icon.py</c>.</summary>
        public const string IconFolder = "Assets/CarRacer/Icon/";
        public const string IconBackgroundPath = IconFolder + "icon-background.png";
        public const string IconForegroundPath = IconFolder + "icon-foreground.png";
        public const string IconRoundPath = IconFolder + "icon-round.png";
        public const string IconLegacyPath = IconFolder + "icon-legacy.png";

#if UNITY_ANDROID
        /// <summary>
        /// Sets the launcher icon for every Android density: the adaptive icon's two layers (which
        /// the launcher masks to its own shape), and pre-masked round and legacy icons for launchers
        /// that ask for those. Unity scales each texture down to the density's size. Android-only, like
        /// <see cref="AndroidManifestPostProcessor"/>: other editors don't have the Android module.
        /// </summary>
        private static void ApplyIcons()
        {
            var background = AssetDatabase.LoadAssetAtPath<Texture2D>(IconBackgroundPath);
            var foreground = AssetDatabase.LoadAssetAtPath<Texture2D>(IconForegroundPath);
            var round = AssetDatabase.LoadAssetAtPath<Texture2D>(IconRoundPath);
            var legacy = AssetDatabase.LoadAssetAtPath<Texture2D>(IconLegacyPath);
            if (background == null || foreground == null || round == null || legacy == null)
            {
                Debug.LogError($"Couldn't load the app icon from {IconFolder}.");
                return;
            }

            var android = NamedBuildTarget.Android;
            SetIcons(android, AndroidPlatformIconKind.Adaptive, background, foreground);
            SetIcons(android, AndroidPlatformIconKind.Round, round);
            SetIcons(android, AndroidPlatformIconKind.Legacy, legacy);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { legacy }, IconKind.Any);
        }

        /// <summary>Gives every density of an icon kind the same layers (adaptive icons: background, then foreground).</summary>
        private static void SetIcons(NamedBuildTarget target, PlatformIconKind kind, params Texture2D[] layers)
        {
            var icons = PlayerSettings.GetPlatformIcons(target, kind);
            foreach (var icon in icons)
            {
                icon.SetTextures(layers);
            }

            PlayerSettings.SetPlatformIcons(target, kind, icons);
        }
#endif

        public const string GraphicsSettingsPath = "ProjectSettings/GraphicsSettings.asset";

        /// <summary>
        /// Adds a shader to Graphics Settings' always-included shaders. A build keeps only shaders
        /// that some asset references, so a shader that only code asks for (via Shader.Find) must be
        /// listed here or it renders pink on the phone.
        /// </summary>
        public static void EnsureAlwaysIncludedShader(string shaderName)
        {
            if (!TryFindAlwaysIncluded(shaderName, out var settings, out var shaders, out var shader, out int index))
            {
                Debug.LogError($"Couldn't add shader '{shaderName}' to the always-included shaders.");
                return;
            }

            if (index >= 0)
            {
                return;
            }

            shaders.InsertArrayElementAtIndex(shaders.arraySize);
            shaders.GetArrayElementAtIndex(shaders.arraySize - 1).objectReferenceValue = shader;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        public static bool IsAlwaysIncluded(string shaderName) =>
            TryFindAlwaysIncluded(shaderName, out _, out _, out _, out int index) && index >= 0;

        /// <summary>Finds the shader and the always-included list; <paramref name="index"/> is its position in the list, or -1.</summary>
        private static bool TryFindAlwaysIncluded(string shaderName, out SerializedObject settings, out SerializedProperty shaders, out Shader shader, out int index)
        {
            settings = null;
            shaders = null;
            index = -1;
            shader = Shader.Find(shaderName);
            var assets = AssetDatabase.LoadAllAssetsAtPath(GraphicsSettingsPath);
            if (shader == null || assets.Length == 0)
            {
                return false;
            }

            settings = new SerializedObject(assets[0]);
            shaders = settings.FindProperty("m_AlwaysIncludedShaders");
            for (int i = 0; i < shaders.arraySize; i++)
            {
                if (shaders.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                {
                    index = i;
                    break;
                }
            }

            return true;
        }
    }
}
