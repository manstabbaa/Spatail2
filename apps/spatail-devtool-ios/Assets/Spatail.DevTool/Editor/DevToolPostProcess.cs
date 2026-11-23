#if UNITY_IOS
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace Spatail.DevTool.Editor
{
    /// <summary>
    /// Finishes the exported Xcode project: the privacy strings iOS requires before it will grant camera,
    /// microphone, speech recognition and local network access, permission for plain HTTP on the local network,
    /// and the Speech framework for the dictation plugin.
    /// Compiled only when the editor's platform is iOS, because the Xcode API ships with iOS Build Support.
    /// </summary>
    public static class DevToolPostProcess
    {
        const string SpeechPlugin = "Libraries/Plugins/iOS/SpatailSpeech.mm";

        [PostProcessBuild(200)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS) return;
            PatchInfoPlist(pathToBuiltProject);
            PatchProject(pathToBuiltProject);
        }

        static void PatchInfoPlist(string pathToBuiltProject)
        {
            string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromString(File.ReadAllText(plistPath));
            PlistElementDict root = plist.root;

            SetIfMissing(root, "NSCameraUsageDescription", "Spatail uses the camera to see the room and place content in it.");
            SetIfMissing(root, "NSMicrophoneUsageDescription", "Spatail listens when you tap the microphone so you can say what you need.");
            root.SetString("NSSpeechRecognitionUsageDescription", "Spatail turns what you say into a request when you tap the microphone.");
            root.SetString("NSLocalNetworkUsageDescription", "Spatail connects to your planner service and to headsets on your network.");

            // Plain HTTP to machines on the local network (the planner relay on a laptop).
            PlistElement existing;
            PlistElementDict transport = root.values.TryGetValue("NSAppTransportSecurity", out existing) && existing.AsDict() != null
                ? existing.AsDict()
                : root.CreateDict("NSAppTransportSecurity");
            transport.SetBoolean("NSAllowsLocalNetworking", true);

            // The app uses no encryption beyond what iOS provides; saves a question on every TestFlight upload.
            root.SetBoolean("ITSAppUsesNonExemptEncryption", false);

            File.WriteAllText(plistPath, plist.WriteToString());
        }

        static void SetIfMissing(PlistElementDict dict, string key, string value)
        {
            PlistElement existing;
            if (dict.values.TryGetValue(key, out existing) && !string.IsNullOrEmpty(existing.AsString())) return;
            dict.SetString(key, value);
        }

        static void PatchProject(string pathToBuiltProject)
        {
            string projectPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);

            // Plugins compile into the UnityFramework target.
            string framework = project.GetUnityFrameworkTargetGuid();
            project.AddFrameworkToProject(framework, "Speech.framework", false);

            // The dictation plugin is written for ARC.
            string file = project.FindFileGuidByProjectPath(SpeechPlugin);
            if (!string.IsNullOrEmpty(file))
                project.SetCompileFlagsForFile(framework, file, new List<string> { "-fobjc-arc" });

            project.WriteToFile(projectPath);
        }
    }
}
#endif
