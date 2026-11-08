using System;
using System.Collections.Generic;

namespace CarRacer.Core.Session
{
    /// <summary>
    /// The <c>-key [value]</c> arguments GameCI passes to the build scripts. A flag takes the next entry as its value
    /// unless that starts with <c>-</c>; the names in <c>valueArguments</c> always take it, so a password starting
    /// with <c>-</c> isn't mistaken for the next flag.
    /// </summary>
    public static class CommandLineArguments
    {
        /// <summary>
        /// Every flag <c>BuildScript</c> reads a value from. Each one belongs in <see cref="BuildValueArguments"/>,
        /// which a test checks.
        /// </summary>
        public static class BuildFlag
        {
            public const string CustomBuildPath = "customBuildPath";
            public const string BuildVersion = "buildVersion";
            public const string AndroidVersionCode = "androidVersionCode";
            public const string AndroidKeystoreName = "androidKeystoreName";
            public const string AndroidKeystorePass = "androidKeystorePass";
            public const string AndroidKeyaliasName = "androidKeyaliasName";
            public const string AndroidKeyaliasPass = "androidKeyaliasPass";
        }

        /// <summary>The <c>valueArguments</c> for <c>BuildScript</c>: every <see cref="BuildFlag"/>.</summary>
        public static readonly HashSet<string> BuildValueArguments = new HashSet<string>(StringComparer.Ordinal)
        {
            BuildFlag.CustomBuildPath,
            BuildFlag.BuildVersion,
            BuildFlag.AndroidVersionCode,
            BuildFlag.AndroidKeystoreName,
            BuildFlag.AndroidKeystorePass,
            BuildFlag.AndroidKeyaliasName,
            BuildFlag.AndroidKeyaliasPass,
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
}
