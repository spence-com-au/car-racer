using System;
using System.Collections.Generic;
using System.IO;
using CarRacer.Core.Session;
using CarRacer.Game;
using CarRacer.Game.Net;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using BuildFlag = CarRacer.Core.Session.CommandLineArguments.BuildFlag;

namespace CarRacer.Editor
{
    /// <summary>
    /// Build entry points: menu items for local builds, and <see cref="BuildFromCommandLine"/>
    /// (Android) and <see cref="BuildLinuxServerFromCommandLine"/> (the Linux host, design doc
    /// §2.3) for GameCI (<c>buildMethod</c> in the workflows).
    /// </summary>
    public static class BuildScript
    {
        public const string BootstrapScenePath = "Assets/Scenes/" + Bootstrap.SceneName + ".unity";

        /// <summary>
        /// Committed debug key, relative to the Unity project. Unity otherwise signs development
        /// builds with a per-machine debug key, and Android refuses to install a build over one
        /// signed with a different key, so every CI run's APK would need an uninstall first.
        /// It uses the standard Android debug credentials and must never sign a release.
        /// </summary>
        public const string SharedDebugKeystorePath = "Keystores/debug.keystore";

        /// <summary>
        /// Restores the signing settings afterwards, so a later release build in the same session
        /// still uses the keystore configured in Player Settings rather than the public debug key.
        /// </summary>
        [MenuItem("Car Racer/Build/Development APK")]
        public static void BuildDevelopmentApk()
        {
            if (!PrepareProject())
            {
                return;
            }

            bool useCustomKeystore = PlayerSettings.Android.useCustomKeystore;
            string keystoreName = PlayerSettings.Android.keystoreName;
            string keystorePass = PlayerSettings.Android.keystorePass;
            string keyaliasName = PlayerSettings.Android.keyaliasName;
            string keyaliasPass = PlayerSettings.Android.keyaliasPass;
            try
            {
                UseSharedDebugKeystore();
                Build("build/Android/CarRacer-dev.apk", development: true);
            }
            finally
            {
                PlayerSettings.Android.useCustomKeystore = useCustomKeystore;
                PlayerSettings.Android.keystoreName = keystoreName;
                PlayerSettings.Android.keystorePass = keystorePass;
                PlayerSettings.Android.keyaliasName = keyaliasName;
                PlayerSettings.Android.keyaliasPass = keyaliasPass;
            }
        }

        /// <summary>
        /// Uses the keystore configured in Player Settings (Publishing Settings) on this machine, and
        /// refuses to build if that is the shared debug keystore.
        /// </summary>
        [MenuItem("Car Racer/Build/Release APK")]
        public static void BuildReleaseApk()
        {
            if (!PrepareProject())
            {
                return;
            }

            if (PlayerSettings.Android.useCustomKeystore && IsSharedDebugKeystore(PlayerSettings.Android.keystoreName))
            {
                Debug.LogError($"Refusing to sign a release with the shared debug keystore ({SharedDebugKeystorePath}); set a release keystore in Player Settings.");
                return;
            }

            Build("build/Android/CarRacer.apk", development: false);
        }

        private static bool IsSharedDebugKeystore(string keystoreName) =>
            !string.IsNullOrEmpty(keystoreName)
            && string.Equals(Path.GetFullPath(keystoreName), Path.GetFullPath(SharedDebugKeystorePath), StringComparison.Ordinal);

        /// <summary>
        /// A plain Linux player, which hosts with no player when run with
        /// <c>-batchmode -nographics -server</c> (design doc §2.3). It isn't Unity's Dedicated Server
        /// subtarget, which needs an editor module the CI images may not have; the player runs the
        /// same way, it just carries rendering code it never uses.
        /// </summary>
        [MenuItem("Car Racer/Build/Linux Server")]
        public static void BuildLinuxServer()
        {
            if (PrepareProject(BuildTarget.StandaloneLinux64) && Build(DefaultLinuxServerPath, BuildTarget.StandaloneLinux64, development: false))
            {
                CopyServerMusic(DefaultLinuxServerPath);
            }
        }

        public const string DefaultLinuxServerPath = "build/StandaloneLinux64/CarRacer";

        /// <summary>Called by GameCI for the Linux host; reads -customBuildPath and -buildVersion.</summary>
        public static void BuildLinuxServerFromCommandLine()
        {
            var args = ParseArguments(Environment.GetCommandLineArgs());
            if (!PrepareProject(BuildTarget.StandaloneLinux64))
            {
                EditorApplication.Exit(1);
                return;
            }

            if (args.TryGetValue(BuildFlag.BuildVersion, out string version) && !string.IsNullOrEmpty(version))
            {
                PlayerSettings.bundleVersion = version;
            }

            string output = args.TryGetValue(BuildFlag.CustomBuildPath, out string path) && !string.IsNullOrEmpty(path) ? path : DefaultLinuxServerPath;
            bool succeeded = Build(output, BuildTarget.StandaloneLinux64, development: false) && CopyServerMusic(output);
            EditorApplication.Exit(succeeded ? 0 : 1);
        }

        private const string MusicSource = "Assets/CarRacer/Resources/Music";

        /// <summary>Copies every tune into a Music folder beside the server built at <paramref name="playerPath"/>.</summary>
        private static bool CopyServerMusic(string playerPath)
        {
            string target = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(playerPath)), TvViewer.MusicFolder);
            Directory.CreateDirectory(target);
            int copied = 0;
            foreach (string file in Directory.GetFiles(MusicSource, "*.ogg"))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
                copied++;
            }

            Debug.Log($"Copied {copied} tunes to {target}");
            return copied > 0;
        }

        [MenuItem("Car Racer/Create Bootstrap Scene")]
        public static void EnsureBootstrapScene()
        {
            if (!File.Exists(BootstrapScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(BootstrapScenePath));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, BootstrapScenePath);
            }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootstrapScenePath, true) };
        }

        /// <summary>
        /// Called by GameCI. Reads the arguments GameCI passes (-customBuildPath, -buildVersion,
        /// -androidVersionCode, -androidKeystoreName/Pass, -androidKeyaliasName/Pass) plus our own
        /// -carRacerDevelopment flag (via customParameters) for PR builds.
        /// </summary>
        public static void BuildFromCommandLine()
        {
            var args = ParseArguments(Environment.GetCommandLineArgs());
            if (!PrepareProject())
            {
                EditorApplication.Exit(1);
                return;
            }

            if (args.TryGetValue(BuildFlag.BuildVersion, out string version) && !string.IsNullOrEmpty(version))
            {
                PlayerSettings.bundleVersion = version;
            }

            if (args.TryGetValue(BuildFlag.AndroidVersionCode, out string code) && int.TryParse(code, out int versionCode) && versionCode > 0)
            {
                PlayerSettings.Android.bundleVersionCode = versionCode;
            }

            bool development = args.ContainsKey("carRacerDevelopment");
            if (args.TryGetValue(BuildFlag.AndroidKeystoreName, out string keystore) && !string.IsNullOrEmpty(keystore))
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = keystore;
                PlayerSettings.Android.keystorePass = args.TryGetValue(BuildFlag.AndroidKeystorePass, out string kp) ? kp : string.Empty;
                PlayerSettings.Android.keyaliasName = args.TryGetValue(BuildFlag.AndroidKeyaliasName, out string an) ? an : string.Empty;
                PlayerSettings.Android.keyaliasPass = args.TryGetValue(BuildFlag.AndroidKeyaliasPass, out string ap) ? ap : string.Empty;
            }
            else if (development)
            {
                UseSharedDebugKeystore();
            }
            else
            {
                // A non-development build without a release keystore falls back to Unity's per-machine
                // debug key, never the public shared one.
                PlayerSettings.Android.useCustomKeystore = false;
            }

            string output = args.TryGetValue(BuildFlag.CustomBuildPath, out string path) && !string.IsNullOrEmpty(path)
                ? path
                : "build/Android/CarRacer.apk";

            bool succeeded = Build(output, development);
            EditorApplication.Exit(succeeded ? 0 : 1);
        }

        private static void UseSharedDebugKeystore()
        {
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = SharedDebugKeystorePath;
            PlayerSettings.Android.keystorePass = "android";
            PlayerSettings.Android.keyaliasName = "androiddebugkey";
            PlayerSettings.Android.keyaliasPass = "android";
        }

        /// <summary>
        /// Applies the settings a build needs, or returns false if the editor isn't on
        /// <paramref name="target"/>. A build can't follow a platform switch in the same call: the
        /// switch recompiles scripts only on the next editor update, so editor code compiled for the
        /// old platform would still run, and an APK would lose everything inside
        /// <c>#if UNITY_ANDROID</c> (<see cref="ProjectConfigurator"/>'s icons, and the manifest's
        /// permissions and deep link).
        /// </summary>
        private static bool PrepareProject(BuildTarget target = BuildTarget.Android)
        {
            var active = EditorUserBuildSettings.activeBuildTarget;
            if (active != target)
            {
                if (Application.isBatchMode)
                {
                    Debug.LogError($"The editor is on {active}, not {target}; start it with -buildTarget {target}.");
                }
                else if (EditorUserBuildSettings.SwitchActiveBuildTarget(BuildPipeline.GetBuildTargetGroup(target), target))
                {
                    Debug.LogError($"Switched the editor from {active} to {target}; build again once its scripts recompile.");
                }
                else
                {
                    Debug.LogError($"Couldn't switch the editor from {active} to {target}; check that its build support module is installed.");
                }

                return false;
            }

            ProjectConfigurator.Apply();
            EnsureBootstrapScene();
            return true;
        }

        private static bool Build(string outputPath, bool development) => Build(outputPath, BuildTarget.Android, development);

        private static bool Build(string outputPath, BuildTarget target, bool development)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
            var options = new BuildPlayerOptions
            {
                scenes = new[] { BootstrapScenePath },
                locationPathName = outputPath,
                target = target,
                options = development ? BuildOptions.Development : BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"Build {summary.result}: {summary.outputPath} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalErrors} errors, {summary.totalTime})");
            return summary.result == BuildResult.Succeeded;
        }

        private static Dictionary<string, string> ParseArguments(string[] argv) =>
            CommandLineArguments.Parse(argv, CommandLineArguments.BuildValueArguments);
    }
}
