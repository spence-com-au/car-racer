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
}
