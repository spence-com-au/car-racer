using System;
using System.Collections.Generic;
using System.IO;

namespace CarRacer.Core.Session
{
    /// <summary>
    /// The <c>-key [value]</c> arguments GameCI passes to the build scripts. A flag takes the next entry as its value
    /// unless that starts with <c>-</c>; the names in <c>valueArguments</c> always take it, so a password starting
    /// with <c>-</c> isn't mistaken for the next flag.
    /// </summary>
    public static class CommandLineArguments
    {
        /// <summary>The <c>valueArguments</c> for <c>BuildScript</c>: every flag it reads a value from.</summary>
        public static readonly HashSet<string> BuildValueArguments = new HashSet<string>(StringComparer.Ordinal)
        {
            "customBuildPath",
            "buildVersion",
            "androidVersionCode",
            "androidKeystoreName",
            "androidKeystorePass",
            "androidKeyaliasName",
            "androidKeyaliasPass",
        };

        /// <summary>Maps each flag, without its dashes, to its value; an empty string for a flag with none.</summary>
        public static Dictionary<string, string> Parse(IReadOnlyList<string> argv, ISet<string> valueArguments)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < argv.Count; i++)
            {
                if (!argv[i].StartsWith("-", StringComparison.Ordinal))
                {
                    continue;
                }

                string key = argv[i].TrimStart('-');
                bool hasNext = i + 1 < argv.Count;
                string value = hasNext && (valueArguments.Contains(key) || !argv[i + 1].StartsWith("-", StringComparison.Ordinal))
                    ? argv[++i]
                    : string.Empty;
                result[key] = value;
            }

            return result;
        }
    }

    /// <summary>
    /// The Android keystore a build signs with, chosen from the <see cref="CommandLineArguments"/> GameCI passes, and
    /// the rule that only a development build may use the shared debug one.
    /// </summary>
    public sealed class AndroidSigning
    {
        /// <summary>
        /// Committed debug key, relative to the Unity project. Unity otherwise signs development builds with a
        /// per-machine debug key, and Android refuses to install a build over one signed with a different key, so
        /// every CI run's APK would need an uninstall first. It uses the standard Android debug credentials and must
        /// never sign a release.
        /// </summary>
        public const string SharedDebugKeystorePath = "Keystores/debug.keystore";

        /// <summary>The shared debug keystore, with the standard Android debug credentials.</summary>
        public static readonly AndroidSigning SharedDebug =
            new AndroidSigning(true, SharedDebugKeystorePath, "android", "androiddebugkey", "android");

        private static readonly AndroidSigning UnityDebug =
            new AndroidSigning(false, string.Empty, string.Empty, string.Empty, string.Empty);

        public AndroidSigning(
            bool useCustomKeystore,
            string keystoreName,
            string keystorePass,
            string keyaliasName,
            string keyaliasPass)
        {
            UseCustomKeystore = useCustomKeystore;
            KeystoreName = keystoreName;
            KeystorePass = keystorePass;
            KeyaliasName = keyaliasName;
            KeyaliasPass = keyaliasPass;
        }

        /// <summary>False signs with Unity's per-machine debug key, and the other properties mean nothing.</summary>
        public bool UseCustomKeystore { get; }

        public string KeystoreName { get; }

        public string KeystorePass { get; }

        public string KeyaliasName { get; }

        public string KeyaliasPass { get; }

        /// <summary>Whether this signs with the shared debug keystore, however its path is spelled.</summary>
        public bool IsSharedDebug =>
            UseCustomKeystore
            && !string.IsNullOrEmpty(KeystoreName)
            && string.Equals(
                Path.GetFullPath(KeystoreName),
                Path.GetFullPath(SharedDebugKeystorePath),
                StringComparison.Ordinal);

        /// <summary>
        /// The signing <paramref name="args"/> ask for: the keystore passed with <c>-androidKeystoreName</c> and its
        /// password and alias flags, else the shared debug keystore for a development build, else Unity's per-machine
        /// debug key (never the shared one, which is public).
        /// </summary>
        public static AndroidSigning FromArguments(IReadOnlyDictionary<string, string> args, bool development)
        {
            if (args.TryGetValue("androidKeystoreName", out string keystore) && !string.IsNullOrEmpty(keystore))
            {
                return new AndroidSigning(
                    true,
                    keystore,
                    ValueOrEmpty(args, "androidKeystorePass"),
                    ValueOrEmpty(args, "androidKeyaliasName"),
                    ValueOrEmpty(args, "androidKeyaliasPass"));
            }

            return development ? SharedDebug : UnityDebug;
        }

        /// <summary>Only a development build may sign with the shared debug keystore, which anyone can use.</summary>
        public bool IsAllowedFor(bool development) => development || !IsSharedDebug;

        private static string ValueOrEmpty(IReadOnlyDictionary<string, string> args, string key) =>
            args.TryGetValue(key, out string value) ? value : string.Empty;
    }
}
