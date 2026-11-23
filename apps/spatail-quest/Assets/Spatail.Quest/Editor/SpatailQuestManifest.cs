#if UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace Spatail.Quest.EditorTools
{
    /// <summary>
    /// Final touches to the generated Android manifest, after Unity's and Meta's own processing:
    /// - RECORD_AUDIO, for speech input (Unity only adds it automatically when the Microphone class is used);
    /// - a package-visibility query for speech recognition services, without which Android 11+ reports that
    ///   recognition is unavailable;
    /// - cleartext HTTP, so the headset can reach a planner service on the local network.
    /// </summary>
    public sealed class SpatailQuestManifest : IPostGenerateGradleAndroidProject
    {
        const string AndroidNamespace = "http://schemas.android.com/apk/res/android";

        // Meta's OVRGradleGeneration uses 99999; this must run after it.
        public int callbackOrder => 100100;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifestPath = Path.Combine(path, "src/main/AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning("[Spatail] AndroidManifest.xml not found at " + manifestPath + "; skipping manifest additions.");
                return;
            }

            var doc = new XmlDocument();
            doc.Load(manifestPath);
            XmlElement manifest = doc.DocumentElement;
            if (manifest == null) return;

            AddPermission(doc, manifest, "android.permission.RECORD_AUDIO");
            AddPermission(doc, manifest, "android.permission.INTERNET");
            AddSpeechQuery(doc, manifest);

            var application = manifest.SelectSingleNode("application") as XmlElement;
            if (application != null) application.SetAttribute("usesCleartextTraffic", AndroidNamespace, "true");

            doc.Save(manifestPath);
        }

        static void AddPermission(XmlDocument doc, XmlElement manifest, string permission)
        {
            foreach (XmlNode node in manifest.SelectNodes("uses-permission"))
            {
                var element = node as XmlElement;
                if (element != null && element.GetAttribute("name", AndroidNamespace) == permission) return;
            }
            XmlElement added = doc.CreateElement("uses-permission");
            added.SetAttribute("name", AndroidNamespace, permission);
            manifest.AppendChild(added);
        }

        static void AddSpeechQuery(XmlDocument doc, XmlElement manifest)
        {
            const string action = "android.speech.RecognitionService";
            var queries = manifest.SelectSingleNode("queries") as XmlElement;
            if (queries == null)
            {
                queries = doc.CreateElement("queries");
                manifest.AppendChild(queries);
            }
            foreach (XmlNode node in queries.SelectNodes("intent/action"))
            {
                var element = node as XmlElement;
                if (element != null && element.GetAttribute("name", AndroidNamespace) == action) return;
            }
            XmlElement intent = doc.CreateElement("intent");
            XmlElement actionElement = doc.CreateElement("action");
            actionElement.SetAttribute("name", AndroidNamespace, action);
            intent.AppendChild(actionElement);
            queries.AppendChild(intent);
        }
    }
}
#endif
