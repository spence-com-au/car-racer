using System;
using System.Collections.Generic;
using CarRacer.Core.Cast;

namespace CarRacer.Core.Platform
{
    /// <summary>A release newer than the running app (<see cref="AppUpdate.Newer"/>).</summary>
    public sealed class AppRelease
    {
        /// <summary>Its version without the tag's "v", like "0.14.0".</summary>
        public string Version;

        public string ApkUrl;
    }

    public enum UpdateState
    {
        None,
        Available,
        Downloading,
        Downloaded,
        Failed,
    }

    /// <summary>How far an update has got, for the main menu (<see cref="AppUpdate.ButtonLabel"/>, <see cref="AppUpdate.Line"/>).</summary>
    public sealed class UpdateStatus
    {
        public UpdateState State;
        public AppRelease Release;

        /// <summary>The download's progress, from 0 to 1.</summary>
        public float Progress;

        /// <summary>Why the download or the installer failed.</summary>
        public string Error;
    }

    /// <summary>
    /// A sideloaded phone updating itself from GitHub Releases (design doc §15): whether the latest
    /// release is newer than the running app, and what the main menu says about it.
    /// </summary>
    public static class AppUpdate
    {
        /// <summary>GitHub's newest published release; never a draft or a pre-release.</summary>
        public const string LatestReleaseUrl = "https://api.github.com/repos/spence-com-au/car-racer/releases/latest";

        /// <summary>The APK's name on every release, as the Release workflow uploads it.</summary>
        public const string ApkName = "CarRacer.apk";

        /// <summary>
        /// The release in <paramref name="latestReleaseJson"/> (GitHub's latest-release response) if
        /// it's newer than <paramref name="runningVersion"/> and has the APK; otherwise null, as when
        /// either version can't be read.
        /// </summary>
        public static AppRelease Newer(string latestReleaseJson, string runningVersion)
        {
            var release = Json.TryParseObject(latestReleaseJson);
            if (!TryParseVersion(Json.String(release, "tag_name"), out var latest)
                || !TryParseVersion(runningVersion, out var running)
                || latest <= running)
            {
                return null;
            }

            foreach (var asset in Json.Array(release, "assets"))
            {
                var fields = asset as Dictionary<string, object>;
                string url = Json.String(fields, "browser_download_url");
                if (Json.String(fields, "name") == ApkName && !string.IsNullOrEmpty(url))
                {
                    return new AppRelease { Version = latest.ToString(), ApkUrl = url };
                }
            }

            return null;
        }

        /// <summary>The button beside Change name: "Update", "Downloading", "Install" or "Try again"; null with nothing to offer.</summary>
        public static string ButtonLabel(UpdateStatus status)
        {
            switch (status.State)
            {
                case UpdateState.Available:
                    return "Update";
                case UpdateState.Downloading:
                    return "Downloading";
                case UpdateState.Downloaded:
                    return "Install";
                case UpdateState.Failed:
                    return "Try again";
                default:
                    return null;
            }
        }

        /// <summary>The line under the title that explains the button, or null.</summary>
        public static string Line(UpdateStatus status, string runningVersion)
        {
            string version = status.Release?.Version;
            switch (status.State)
            {
                case UpdateState.Available:
                    return $"Version {version} is out; this phone has {runningVersion}.";
                case UpdateState.Downloading:
                    return $"Downloading version {version}: {(int)(Math.Clamp(status.Progress, 0f, 1f) * 100f)}%";
                case UpdateState.Downloaded:
                    return $"Version {version} is ready. Tap Install, and if Android asks, let Car Racer install apps.";
                case UpdateState.Failed:
                    return $"Updating to {version} failed: {status.Error}";
                default:
                    return null;
            }
        }

        /// <summary>Reads "0.14.0" or a tag like "v0.14.0".</summary>
        private static bool TryParseVersion(string text, out Version version)
        {
            version = null;
            return !string.IsNullOrEmpty(text) && Version.TryParse(text.TrimStart('v'), out version);
        }
    }
}
