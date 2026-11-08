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
        /// Restores the signing settings afterwards, so a later release build in the same session
        /// still uses the keystore configured in Player Settings rather than the public debug key.
        /// </summary>
        [MenuItem("Car Racer/Build/Development APK")]
        public static void BuildDevelopmentApk()
        {
            PrepareProject();
            bool useCustomKeystore = PlayerSettings.Android.useCustomKeystore;
            string keystoreName = PlayerSettings.Android.keystoreName;
            string keystorePass = PlayerSettings.Android.keystorePass;
            string keyaliasName = PlayerSettings.Android.keyaliasName;
            string keyaliasPass = PlayerSettings.Android.keyaliasPass;
            try
            {
                Apply(AndroidSigning.SharedDebug);
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
            Apply(AndroidSigning.FromArguments(args, development));

            string output = args.TryGetValue("customBuildPath", out string path) && !string.IsNullOrEmpty(path)
                ? path
                : "build/Android/CarRacer.apk";

            bool succeeded = Build(output, development);
            EditorApplication.Exit(succeeded ? 0 : 1);
        }

        /// <summary>Leaves the keystore settings alone when <paramref name="signing"/> uses Unity's own debug key.</summary>
        private static void Apply(AndroidSigning signing)
        {
            PlayerSettings.Android.useCustomKeystore = signing.UseCustomKeystore;
            if (signing.UseCustomKeystore)
            {
                PlayerSettings.Android.keystoreName = signing.KeystoreName;
                PlayerSettings.Android.keystorePass = signing.KeystorePass;
                PlayerSettings.Android.keyaliasName = signing.KeyaliasName;
                PlayerSettings.Android.keyaliasPass = signing.KeyaliasPass;
            }
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

        /// <summary>
        /// Every Android build comes through here, so the rule that a release is never signed with the shared debug
        /// keystore holds for whichever entry point set the signing up.
        /// </summary>
        private static bool Build(string outputPath, bool development)
        {
            var signing = new AndroidSigning(
                PlayerSettings.Android.useCustomKeystore,
                PlayerSettings.Android.keystoreName,
                PlayerSettings.Android.keystorePass,
                PlayerSettings.Android.keyaliasName,
                PlayerSettings.Android.keyaliasPass);
            if (!signing.IsAllowedFor(development))
            {
                Debug.LogError(
                    $"Refusing to sign a release with the shared debug keystore ({AndroidSigning.SharedDebugKeystorePath}); "
                    + "use a release keystore.");
                return false;
            }

            return Build(outputPath, BuildTarget.Android, development);
        }

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
