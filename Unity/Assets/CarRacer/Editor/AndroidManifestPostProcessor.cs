#if UNITY_ANDROID
using System.IO;
using System.Xml;
using CarRacer.Game.Platform;
using UnityEditor.Android;

namespace CarRacer.Editor
{
    /// <summary>
    /// Adds the permissions LAN play needs to the generated Android manifest, a filter so
    /// <c>carracer://join</c> links (the host's QR code, design doc §1.1) open the game, and the
    /// FileProvider that hands a downloaded update to Android's installer (design doc §15), so the
    /// project doesn't need a hand-maintained custom AndroidManifest.xml.
    /// </summary>
    public sealed class AndroidManifestPostProcessor : IPostGenerateGradleAndroidProject
    {
        private const string AndroidNamespace = "http://schemas.android.com/apk/res/android";
        private const string FileProvider = "androidx.core.content.FileProvider";
        private const string UpdatePaths = "car_racer_update_paths";

        private static readonly string[] Permissions =
        {
            "android.permission.INTERNET",
            "android.permission.ACCESS_NETWORK_STATE",
            "android.permission.ACCESS_WIFI_STATE",
            // Required to acquire a WifiManager.MulticastLock for discovery.
            "android.permission.CHANGE_WIFI_MULTICAST_STATE",
            "android.permission.VIBRATE",
            // Without it Android's installer refuses the APK AppUpdater hands it.
            "android.permission.REQUEST_INSTALL_PACKAGES",
        };

        public int callbackOrder => 0;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            string manifestPath = Path.Combine(unityLibraryPath, "src", "main", "AndroidManifest.xml");
            var document = new XmlDocument();
            document.Load(manifestPath);
            XmlElement manifest = document.DocumentElement;

            foreach (string permission in Permissions)
            {
                if (HasPermission(manifest, permission))
                {
                    continue;
                }

                XmlElement element = document.CreateElement("uses-permission");
                XmlAttribute name = document.CreateAttribute("android", "name", AndroidNamespace);
                name.Value = permission;
                element.Attributes.Append(name);
                manifest.PrependChild(element);
            }

            AddJoinLinkFilter(document);
            AddUpdateProvider(document, unityLibraryPath);
            document.Save(manifestPath);
        }

        /// <summary>
        /// Shares AppUpdater's download folder, and nothing else, with Android's installer, which
        /// can't read the app's own files: the provider hands it a <c>content://</c> link instead.
        /// FileProvider comes with androidx.core, which Unity's activity already brings in.
        /// </summary>
        private static void AddUpdateProvider(XmlDocument document, string unityLibraryPath)
        {
            string resources = Path.Combine(unityLibraryPath, "src", "main", "res", "xml");
            Directory.CreateDirectory(resources);
            File.WriteAllText(
                Path.Combine(resources, UpdatePaths + ".xml"),
                $"<paths>\n    <cache-path name=\"{AppUpdater.CacheFolder}\" path=\"{AppUpdater.CacheFolder}/\" />\n</paths>\n");

            var application = (XmlElement)document.GetElementsByTagName("application")[0];
            if (HasChild(application, "provider", FileProvider))
            {
                return;
            }

            XmlElement provider = Named(document, "provider", FileProvider);
            SetAndroidAttribute(document, provider, "authorities", "${applicationId}" + AppUpdater.AuthoritySuffix);
            SetAndroidAttribute(document, provider, "exported", "false");
            SetAndroidAttribute(document, provider, "grantUriPermissions", "true");
            XmlElement paths = Named(document, "meta-data", "android.support.FILE_PROVIDER_PATHS");
            SetAndroidAttribute(document, paths, "resource", "@xml/" + UpdatePaths);
            provider.AppendChild(paths);
            application.AppendChild(provider);
        }

        /// <summary>Lets the launcher activity open <c>carracer://join?…</c> links from the camera app or a browser.</summary>
        private static void AddJoinLinkFilter(XmlDocument document)
        {
            foreach (XmlNode node in document.GetElementsByTagName("activity"))
            {
                if (!(node is XmlElement activity) || !HasChild(activity, "category", "android.intent.category.LAUNCHER"))
                {
                    continue;
                }

                if (HasChild(activity, "action", "android.intent.action.VIEW"))
                {
                    return;
                }

                XmlElement filter = document.CreateElement("intent-filter");
                filter.AppendChild(Named(document, "action", "android.intent.action.VIEW"));
                filter.AppendChild(Named(document, "category", "android.intent.category.DEFAULT"));
                filter.AppendChild(Named(document, "category", "android.intent.category.BROWSABLE"));
                XmlElement data = document.CreateElement("data");
                SetAndroidAttribute(document, data, "scheme", "carracer");
                SetAndroidAttribute(document, data, "host", "join");
                filter.AppendChild(data);
                activity.AppendChild(filter);
                return;
            }
        }

        private static XmlElement Named(XmlDocument document, string tag, string name)
        {
            XmlElement element = document.CreateElement(tag);
            SetAndroidAttribute(document, element, "name", name);
            return element;
        }

        private static void SetAndroidAttribute(XmlDocument document, XmlElement element, string localName, string value)
        {
            XmlAttribute attribute = document.CreateAttribute("android", localName, AndroidNamespace);
            attribute.Value = value;
            element.Attributes.Append(attribute);
        }

        /// <summary>Whether <paramref name="parent"/> has a descendant <paramref name="tag"/> element whose <c>android:name</c> is <paramref name="name"/>.</summary>
        private static bool HasChild(XmlElement parent, string tag, string name)
        {
            foreach (XmlNode node in parent.GetElementsByTagName(tag))
            {
                if (node is XmlElement e && e.GetAttribute("name", AndroidNamespace) == name)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasPermission(XmlElement manifest, string permission)
        {
            foreach (XmlNode node in manifest.ChildNodes)
            {
                if (node is XmlElement e && e.Name == "uses-permission" && e.GetAttribute("name", AndroidNamespace) == permission)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
#endif
