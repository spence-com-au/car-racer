#if UNITY_ANDROID
using System.IO;
using System.Xml;
using CarRacer.Core.Discovery;
using UnityEditor.Android;

namespace CarRacer.Editor
{
    /// <summary>
    /// Adds the permissions LAN play needs to the generated Android manifest, and a filter so
    /// <c>carracer://join</c> links (the host's QR code, design doc §1.1) open the game, so the
    /// project doesn't need a hand-maintained custom AndroidManifest.xml.
    /// </summary>
    public sealed class AndroidManifestPostProcessor : IPostGenerateGradleAndroidProject
    {
        private const string AndroidNamespace = "http://schemas.android.com/apk/res/android";

        private static readonly string[] Permissions =
        {
            "android.permission.INTERNET",
            "android.permission.ACCESS_NETWORK_STATE",
            "android.permission.ACCESS_WIFI_STATE",
            // Required to acquire a WifiManager.MulticastLock for discovery.
            "android.permission.CHANGE_WIFI_MULTICAST_STATE",
            "android.permission.VIBRATE",
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
            document.Save(manifestPath);
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
                SetAndroidAttribute(document, data, "scheme", JoinLink.Scheme);
                SetAndroidAttribute(document, data, "host", JoinLink.Host);
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

        /// <summary>Whether <paramref name="activity"/> has a descendant <paramref name="tag"/> element whose <c>android:name</c> is <paramref name="name"/>.</summary>
        private static bool HasChild(XmlElement activity, string tag, string name)
        {
            foreach (XmlNode node in activity.GetElementsByTagName(tag))
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
