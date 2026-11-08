using System;
using System.IO;

namespace CarRacer.Core.Tests
{
    internal static class ModelFiles
    {
        public static string Resolve(string file)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Unity")))
            {
                dir = dir.Parent;
            }

            if (dir == null)
            {
                throw new DirectoryNotFoundException(
                    $"No `Unity` folder in any parent of {AppContext.BaseDirectory}, where the models were expected.");
            }

            return Path.Combine(dir.FullName, "Unity", "Assets", "CarRacer", "Resources", "Models", file);
        }
    }
}
