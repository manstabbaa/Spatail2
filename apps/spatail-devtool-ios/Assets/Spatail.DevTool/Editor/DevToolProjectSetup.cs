using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;

namespace Spatail.DevTool.Editor
{
    /// <summary>
    /// Makes a fresh clone buildable without any manual project configuration. Runs once per editor session when the
    /// project opens, and on demand from Spatail > Setup Project. Every step is idempotent: it only writes a setting
    /// that is not already what it should be.
    /// </summary>
    [InitializeOnLoad]
    public static class DevToolProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/DevTool.unity";
        public const string BundleId = "com.spatail.devtool";
        public const string TeamIdVariable = "SPATAIL_IOS_TEAM_ID";

        const string ArKitLoader = "UnityEngine.XR.ARKit.ARKitLoader";
        const string ArKitDefine = "UNITY_XR_ARKIT_LOADER_ENABLED";
        const string XrSettingsKey = "com.unity.xr.management.loader_settings";
        const string SessionKey = "Spatail.DevTool.SetupRan";
        const string InputPromptKey = "Spatail.DevTool.InputPromptShown";
        const string MinimumIos = "15.0";

        static DevToolProjectSetup()
        {
            // Batch builds call Run() themselves, once the asset database is ready.
            if (Application.isBatchMode) return;
            if (SessionState.GetBool(SessionKey, false)) return;
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                SessionState.SetBool(SessionKey, true);
                Run(false);
            };
        }

        [MenuItem("Spatail/Setup Project", priority = 0)]
        public static void RunFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Spatail", "Stop Play mode before running project setup.", "OK");
                return;
            }
            List<string> changes = Run(true);
            EditorUtility.DisplayDialog("Spatail", changes.Count == 0
                ? "The project is already set up."
                : "Project set up:\n\n- " + string.Join("\n- ", changes.ToArray()), "OK");
        }

        /// <summary>Applies every setup step and returns a description of what changed.</summary>
        public static List<string> Run(bool verbose)
        {
            var changes = new List<string>();
            Step("scene", () => EnsureScene(changes));
            Step("player settings", () => ApplyPlayerSettings(changes));
            Step("ARKit loader", () => EnableArKit(changes));
            Step("input backend", () => EnsureInputBackend(changes));
            AssetDatabase.SaveAssets();
            if (changes.Count > 0) Debug.Log("[Spatail] Project setup: " + string.Join("; ", changes.ToArray()));
            else if (verbose) Debug.Log("[Spatail] Project setup: nothing to change.");
            return changes;
        }

        static void Step(string name, Action action)
        {
            try { action(); }
            catch (Exception e) { Debug.LogWarning("[Spatail] Setup step '" + name + "' did not complete: " + e.Message + "\nRun Spatail > Setup Project to try again."); }
        }

        // ---- scene ---------------------------------------------------------------------------

        /// <summary>
        /// The app builds itself at runtime, so the scene is intentionally empty; it only has to exist and be first
        /// in the build list.
        /// </summary>
        static void EnsureScene(List<string> changes)
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                // Adding a scene alongside an unsaved untitled one is not allowed, so replace it in that case.
                bool untitled = string.IsNullOrEmpty(SceneManager.GetActiveScene().path);
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, untitled ? NewSceneMode.Single : NewSceneMode.Additive);
                EditorSceneManager.SaveScene(scene, ScenePath);
                if (!untitled) EditorSceneManager.CloseScene(scene, true);
                AssetDatabase.Refresh();
                changes.Add("created " + ScenePath);
            }

            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (scenes.Length == 0 || scenes[0].path != ScenePath || !scenes[0].enabled)
            {
                var list = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
                list.AddRange(scenes.Where(s => s.path != ScenePath));
                EditorBuildSettings.scenes = list.ToArray();
                changes.Add("put the scene first in the build list");
            }
        }

        // ---- player settings -----------------------------------------------------------------

        static void ApplyPlayerSettings(List<string> changes)
        {
            NamedBuildTarget ios = NamedBuildTarget.iOS;

            if (PlayerSettings.companyName != "Spatail") { PlayerSettings.companyName = "Spatail"; changes.Add("company name"); }
            if (PlayerSettings.productName != "Spatail DevTool") { PlayerSettings.productName = "Spatail DevTool"; changes.Add("product name"); }
            if (PlayerSettings.GetApplicationIdentifier(ios) != BundleId) { PlayerSettings.SetApplicationIdentifier(ios, BundleId); changes.Add("bundle id " + BundleId); }
            if (PlayerSettings.colorSpace != ColorSpace.Linear) { PlayerSettings.colorSpace = ColorSpace.Linear; changes.Add("linear colour space"); }

            if (PlayerSettings.GetScriptingBackend(ios) != ScriptingImplementation.IL2CPP) { PlayerSettings.SetScriptingBackend(ios, ScriptingImplementation.IL2CPP); changes.Add("IL2CPP"); }
            // 1 = ARM64, the only architecture the ARKit plug-in accepts.
            if (PlayerSettings.GetArchitecture(ios) != 1) { PlayerSettings.SetArchitecture(ios, 1); changes.Add("ARM64"); }

            // ARKit refuses to build unless Metal is the first graphics API.
            GraphicsDeviceType[] apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.iOS);
            if (apis.Length == 0 || apis[0] != GraphicsDeviceType.Metal)
            {
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.iOS, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.iOS, new[] { GraphicsDeviceType.Metal });
                changes.Add("Metal graphics");
            }

            if (IsLowerVersion(PlayerSettings.iOS.targetOSVersionString, MinimumIos)) { PlayerSettings.iOS.targetOSVersionString = MinimumIos; changes.Add("minimum iOS " + MinimumIos); }
            if (PlayerSettings.iOS.targetDevice != iOSTargetDevice.iPhoneAndiPad) { PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad; changes.Add("iPhone and iPad"); }

            const string camera = "Spatail uses the camera to see the room and place content in it.";
            const string microphone = "Spatail listens when you tap the microphone so you can say what you need.";
            if (PlayerSettings.iOS.cameraUsageDescription != camera) { PlayerSettings.iOS.cameraUsageDescription = camera; changes.Add("camera usage description"); }
            if (PlayerSettings.iOS.microphoneUsageDescription != microphone) { PlayerSettings.iOS.microphoneUsageDescription = microphone; changes.Add("microphone usage description"); }

            // The planner relay and the session link are plain HTTP/TCP on the local network.
            if (PlayerSettings.insecureHttpOption != InsecureHttpOption.AlwaysAllowed) { PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed; changes.Add("allow HTTP for the LAN planner"); }

            if (PlayerSettings.defaultInterfaceOrientation != UIOrientation.AutoRotation)
            {
                PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
                PlayerSettings.allowedAutorotateToPortrait = true;
                PlayerSettings.allowedAutorotateToLandscapeLeft = true;
                PlayerSettings.allowedAutorotateToLandscapeRight = true;
                PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
                changes.Add("auto-rotation");
            }
            if (!PlayerSettings.statusBarHidden) { PlayerSettings.statusBarHidden = true; changes.Add("hidden status bar"); }

            // Signing: use the team from the environment when there is one, otherwise leave it to Xcode.
            string team = Environment.GetEnvironmentVariable(TeamIdVariable);
            if (!string.IsNullOrEmpty(team))
            {
                if (!PlayerSettings.iOS.appleEnableAutomaticSigning) { PlayerSettings.iOS.appleEnableAutomaticSigning = true; changes.Add("automatic signing"); }
                if (PlayerSettings.iOS.appleDeveloperTeamID != team) { PlayerSettings.iOS.appleDeveloperTeamID = team; changes.Add("signing team from " + TeamIdVariable); }
            }
        }

        static bool IsLowerVersion(string current, string minimum)
        {
            Version a, b;
            if (!Version.TryParse(Normalise(current), out a)) return true;
            if (!Version.TryParse(Normalise(minimum), out b)) return false;
            return a < b;
        }

        static string Normalise(string version)
        {
            version = (version ?? "").Trim();
            return version.Contains(".") ? version : version + ".0";
        }

        // ---- XR plug-in management -----------------------------------------------------------

        static void EnableArKit(List<string> changes)
        {
            const BuildTargetGroup group = BuildTargetGroup.iOS;

            XRGeneralSettingsPerBuildTarget perTarget;
            if (!EditorBuildSettings.TryGetConfigObject(XrSettingsKey, out perTarget) || perTarget == null)
            {
                string existing = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget").FirstOrDefault();
                if (!string.IsNullOrEmpty(existing))
                    perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(AssetDatabase.GUIDToAssetPath(existing));
                if (perTarget == null)
                {
                    if (!AssetDatabase.IsValidFolder("Assets/XR")) AssetDatabase.CreateFolder("Assets", "XR");
                    perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                    AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                    AssetDatabase.SaveAssets();
                    changes.Add("created XR plug-in settings");
                }
                EditorBuildSettings.AddConfigObject(XrSettingsKey, perTarget, true);
            }

            if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
            if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);

            XRGeneralSettings general = perTarget.SettingsForBuildTarget(group);
            if (general != null && !general.InitManagerOnStart)
            {
                general.InitManagerOnStart = true;
                EditorUtility.SetDirty(general);
            }

            XRManagerSettings manager = perTarget.ManagerSettingsForBuildTarget(group);
            if (manager == null) throw new InvalidOperationException("XR Plug-in Management has no settings for iOS yet.");
            bool assigned = manager.activeLoaders != null && manager.activeLoaders.Any(l => l != null && l.GetType().FullName == ArKitLoader);
            if (!assigned)
            {
                if (!XRPackageMetadataStore.AssignLoader(manager, ArKitLoader, group))
                    throw new InvalidOperationException("Could not enable the ARKit loader; the ARKit package may still be importing.");
                changes.Add("enabled the ARKit loader for iOS");
            }

            // The ARKit package adds this define itself when the loader is on, but not in batch mode before a build.
            // Setting it here means a command-line build compiles the ARKit code paths on its first pass.
            NamedBuildTarget ios = NamedBuildTarget.iOS;
            var defines = new HashSet<string>(PlayerSettings.GetScriptingDefineSymbols(ios).Split(';').Where(d => d.Length > 0));
            if (defines.Add(ArKitDefine))
            {
                PlayerSettings.SetScriptingDefineSymbols(ios, string.Join(";", defines.ToArray()));
                changes.Add("scripting define " + ArKitDefine);
            }
        }

        // ---- input -----------------------------------------------------------------------------

        /// <summary>
        /// AR Foundation drives the camera with the Input System's tracked pose driver, which needs the Input System
        /// backend switched on. The dev tool also works without it (it falls back to reading the XR device pose
        /// directly), so this only offers the change; it never forces it in the editor.
        /// </summary>
        static void EnsureInputBackend(List<string> changes)
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0) return;
            var settings = new SerializedObject(assets[0]);
            SerializedProperty handler = settings.FindProperty("activeInputHandler");
            // 0 = Input Manager only, 1 = Input System only, 2 = both.
            if (handler == null || handler.intValue != 0) return;

            if (!Application.isBatchMode)
            {
                if (SessionState.GetBool(InputPromptKey, false)) return;
                SessionState.SetBool(InputPromptKey, true);
                bool enable = EditorUtility.DisplayDialog("Spatail",
                    "AR Foundation drives the AR camera through the Input System package, which is currently switched off in " +
                    "Player Settings > Active Input Handling.\n\nSwitch it to Both and restart the editor now?",
                    "Switch and restart", "Not now");
                if (!enable) return;
            }

            handler.intValue = 2;
            settings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            changes.Add("active input handling: both");
            if (!Application.isBatchMode) EditorApplication.OpenProject(Directory.GetCurrentDirectory());
        }
    }
}
