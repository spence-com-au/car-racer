using System;
using System.Collections.Generic;
using System.IO;
using CarRacer.Game;
using CarRacer.Game.Net;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

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

        [MenuItem("Car Racer/Build/Development APK")]
        public static void BuildDevelopmentApk()
        {
            PrepareProject();
            UseSharedDebugKeystore();
            Build("build/Android/CarRacer-dev.apk", development: true);
        }

        /// <summary>Uses the keystore configured in Player Settings (Publishing Settings) on this machine.</summary>
        [MenuItem("Car Racer/Build/Release APK")]
        public static void BuildReleaseApk()
        {
            PrepareProject();
            Build("build/Android/CarRacer.apk", development: false);
        }

        /// <summary>
        /// A plain Linux player, which hosts with no player when run with
        /// <c>-batchmode -nographics -server</c> (design doc §2.3). It isn't Unity's Dedicated Server
        /// subtarget, which needs an editor module the CI images may not have; the player runs the
        /// same way, it just carries rendering code it never uses.
        /// </summary>
        [MenuItem("Car Racer/Build/Linux Server")]
        public static void BuildLinuxServer()
        {
            PrepareProject(BuildTarget.StandaloneLinux64);
            if (Build(DefaultLinuxServerPath, BuildTarget.StandaloneLinux64, development: false))
            {
                CopyServerMusic(DefaultLinuxServerPath);
            }
        }

        public const string DefaultLinuxServerPath = "build/StandaloneLinux64/CarRacer";

        /// <summary>Called by GameCI for the Linux host; reads -customBuildPath and -buildVersion.</summary>
        public static void BuildLinuxServerFromCommandLine()
        {
            var args = ParseArguments(Environment.GetCommandLineArgs());
            PrepareProject(BuildTarget.StandaloneLinux64);
            if (args.TryGetValue("buildVersion", out string version) && !string.IsNullOrEmpty(version))
            {
                PlayerSettings.bundleVersion = version;
            }

            string output = args.TryGetValue("customBuildPath", out string path) && !string.IsNullOrEmpty(path) ? path : DefaultLinuxServerPath;
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
            PrepareProject();

            if (args.TryGetValue("buildVersion", out string version) && !string.IsNullOrEmpty(version))
            {
                PlayerSettings.bundleVersion = version;
            }

            if (args.TryGetValue("androidVersionCode", out string code) && int.TryParse(code, out int versionCode) && versionCode > 0)
            {
                PlayerSettings.Android.bundleVersionCode = versionCode;
            }

            bool development = args.ContainsKey("carRacerDevelopment");
            if (args.TryGetValue("androidKeystoreName", out string keystore) && !string.IsNullOrEmpty(keystore))
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = keystore;
                PlayerSettings.Android.keystorePass = args.TryGetValue("androidKeystorePass", out string kp) ? kp : string.Empty;
                PlayerSettings.Android.keyaliasName = args.TryGetValue("androidKeyaliasName", out string an) ? an : string.Empty;
                PlayerSettings.Android.keyaliasPass = args.TryGetValue("androidKeyaliasPass", out string ap) ? ap : string.Empty;
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

            string output = args.TryGetValue("customBuildPath", out string path) && !string.IsNullOrEmpty(path)
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

        private static void PrepareProject(BuildTarget target = BuildTarget.Android)
        {
            if (EditorUserBuildSettings.activeBuildTarget != target)
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildPipeline.GetBuildTargetGroup(target), target);
            }

            ProjectConfigurator.Apply();
            EnsureBootstrapScene();
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

        /// <summary>Arguments that always take the next argv entry as their value, even if it starts with "-" (e.g. a password).</summary>
        private static readonly HashSet<string> ValueArguments = new HashSet<string>(StringComparer.Ordinal)
        {
            "customBuildPath",
            "buildVersion",
            "androidVersionCode",
            "androidKeystoreName",
            "androidKeystorePass",
            "androidKeyaliasName",
            "androidKeyaliasPass",
        };

        private static Dictionary<string, string> ParseArguments(string[] argv)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < argv.Length; i++)
            {
                if (!argv[i].StartsWith("-", StringComparison.Ordinal))
                {
                    continue;
                }

                string key = argv[i].TrimStart('-');
                bool hasNext = i + 1 < argv.Length;
                string value = hasNext && (ValueArguments.Contains(key) || !argv[i + 1].StartsWith("-", StringComparison.Ordinal))
                    ? argv[++i]
                    : string.Empty;
                result[key] = value;
            }

            return result;
        }
    }
}
