using System;
using System.Collections;
using System.IO;
using CarRacer.Core.Platform;
using UnityEngine;
using UnityEngine.Networking;

namespace CarRacer.Game.Platform
{
    /// <summary>
    /// Keeps a sideloaded phone up to date from GitHub Releases (design doc §15). Once a launch it
    /// asks GitHub for the latest release, and if that's newer the main menu offers it. A tap
    /// downloads the APK into the app's cache, and another hands it to Android's installer through
    /// a FileProvider (declared by AndroidManifestPostProcessor), which asks the player to confirm.
    /// Only release builds on Android look: development builds are signed with the debug key, so
    /// Android would refuse a release over them. A failed check stays quiet, since the Wi-Fi at a
    /// LAN game may have no internet.
    /// </summary>
    public sealed class AppUpdater : MonoBehaviour
    {
        /// <summary>The folder in the app's cache the APK downloads to, and all the FileProvider shares.</summary>
        public const string CacheFolder = "updates";

        /// <summary>Follows the application id in the FileProvider's authority.</summary>
        public const string AuthoritySuffix = ".updates";

        private const string ApkType = "application/vnd.android.package-archive";
        private const int GrantReadUriPermission = 1;

        private static AppUpdater _instance;

        public static UpdateStatus Status { get; } = new UpdateStatus();

        /// <summary>The main menu's button, or null with nothing to offer.</summary>
        public static string ButtonLabel => AppUpdate.ButtonLabel(Status);

        /// <summary>The main menu's line about the update, or null.</summary>
        public static string Line => AppUpdate.Line(Status, Application.version);

        /// <summary>Asks GitHub for the latest release, once a launch.</summary>
        public static void CheckForUpdate()
        {
            if (_instance != null || Application.platform != RuntimePlatform.Android || Debug.isDebugBuild)
            {
                return;
            }

            var host = new GameObject(nameof(AppUpdater));
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<AppUpdater>();
            _instance.StartCoroutine(_instance.Check());
        }

        /// <summary>The button: downloads the update (again, after a failure), or opens the installer once it's downloaded.</summary>
        public static void Tap()
        {
            if (_instance == null)
            {
                return;
            }

            switch (Status.State)
            {
                case UpdateState.Available:
                case UpdateState.Failed:
                    _instance.StartCoroutine(_instance.Download());
                    break;
                case UpdateState.Downloaded:
                    OpenInstaller();
                    break;
            }
        }

        private IEnumerator Check()
        {
            // An APK from an earlier update is installed or stale by now.
            string folder = DownloadFolder();
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }

            using var request = UnityWebRequest.Get(AppUpdate.LatestReleaseUrl);
            request.SetRequestHeader("Accept", "application/vnd.github+json");
            request.SetRequestHeader("User-Agent", "CarRacer/" + Application.version);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.Log($"Couldn't check for an update: {request.error}");
                yield break;
            }

            Status.Release = AppUpdate.Newer(request.downloadHandler.text, Application.version);
            if (Status.Release != null)
            {
                Status.State = UpdateState.Available;
            }
        }

        private IEnumerator Download()
        {
            Status.State = UpdateState.Downloading;
            Status.Progress = 0f;
            Directory.CreateDirectory(DownloadFolder());
            using var request = new UnityWebRequest(Status.Release.ApkUrl, UnityWebRequest.kHttpVerbGET, new DownloadHandlerFile(ApkPath()), null);
            var download = request.SendWebRequest();
            while (!download.isDone)
            {
                Status.Progress = request.downloadProgress;
                yield return null;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Fail(request.error);
                yield break;
            }

            Status.State = UpdateState.Downloaded;
        }

        /// <summary>Hands the APK to Android's installer, which asks the player to confirm, and the first time to let the game install apps.</summary>
        private static void OpenInstaller()
        {
            try
            {
                using var activity = CurrentActivity();
                using var file = new AndroidJavaObject("java.io.File", ApkPath());
                using var fileProvider = new AndroidJavaClass("androidx.core.content.FileProvider");
                using var uri = fileProvider.CallStatic<AndroidJavaObject>("getUriForFile", activity, Application.identifier + AuthoritySuffix, file);
                using var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.VIEW");
                using var typed = intent.Call<AndroidJavaObject>("setDataAndType", uri, ApkType);
                using var granted = intent.Call<AndroidJavaObject>("addFlags", GrantReadUriPermission);
                activity.Call("startActivity", intent);
            }
            catch (Exception e)
            {
                Fail($"Couldn't open the installer ({e.Message})");
            }
        }

        private static void Fail(string error)
        {
            Debug.LogWarning($"Update to {Status.Release.Version} failed: {error}");
            Status.State = UpdateState.Failed;
            Status.Error = error;
        }

        /// <summary>The updates folder in Context.getCacheDir, which the FileProvider's cache-path names.</summary>
        private static string DownloadFolder()
        {
            using var activity = CurrentActivity();
            using var cache = activity.Call<AndroidJavaObject>("getCacheDir");
            return Path.Combine(cache.Call<string>("getAbsolutePath"), CacheFolder);
        }

        private static string ApkPath() => Path.Combine(DownloadFolder(), AppUpdate.ApkName);

        private static AndroidJavaObject CurrentActivity()
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            return unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        }
    }
}
