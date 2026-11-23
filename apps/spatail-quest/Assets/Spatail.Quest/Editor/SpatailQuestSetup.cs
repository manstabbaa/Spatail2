using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace Spatail.Quest.EditorTools
{
    /// <summary>
    /// Configures the project for Meta Quest so that a fresh clone opens ready to build: the scene, player settings,
    /// the Meta project config (passthrough, scene, hands, system keyboard) and the OpenXR plug-in with Meta's
    /// feature set. Runs once per editor session and from <c>Spatail > Setup Project</c>. Every step is idempotent
    /// and isolated, so one failing step is reported without stopping the others.
    /// </summary>
    [InitializeOnLoad]
    public static class SpatailQuestSetup
    {
        public const string ScenePath = "Assets/Scenes/Spatail.unity";
        public const string ApplicationId = "com.spatail.quest";

        // Identifiers of the OpenXR features Spatail needs. They are referenced by id rather than by type so this
        // file does not depend on internal or optional classes.
        const string MetaFeatureSetId = "com.meta.openxr.featureset.metaxr";     // Meta.XR.MetaXRFeatureSet.featureSetId
        const string MetaXrFeatureId = "com.meta.openxr.feature.metaxr";         // Meta.XR.MetaXRFeature.featureId
        const string MetaQuestFeatureId = "com.unity.openxr.feature.metaquest";  // MetaQuestFeature.featureId
        const string TouchProfileFeatureId = "com.unity.openxr.feature.input.oculustouch"; // OculusTouchControllerProfile.featureId
        const string XrSettingsKey = "com.unity.xr.management.loader_settings";  // XRGeneralSettings.settingsKey

        const string SessionKey = "Spatail.Quest.SetupApplied";
        const string MetaRulesKey = "Spatail.Quest.MetaRulesApplied";
        static int _waitedFrames;

        static SpatailQuestSetup()
        {
            // Batch builds run the setup themselves, at the right moment.
            if (Application.isBatchMode) return;
            if (!SessionState.GetBool(SessionKey, false)) EditorApplication.update += AutoRun;
            else if (!SessionState.GetBool(MetaRulesKey, false)) EditorApplication.update += AutoRunMetaRules;
        }

        static void AutoRun()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            // Meta's project config only becomes available once its own editor start-up has finished.
            if (!MetaConfigReady && ++_waitedFrames < 900) return;
            EditorApplication.update -= AutoRun;
            SessionState.SetBool(SessionKey, true);
            Apply();
            // If Apply had to switch the build target, the editor reloads scripts and the static constructor
            // schedules the Meta rules afterwards; otherwise schedule them now.
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android) EditorApplication.update += AutoRunMetaRules;
        }

        /// <summary>
        /// Second pass, once Android is the active target: let the Meta SDK's Project Setup Tool apply whatever it
        /// still considers outstanding. Best effort; anything it could not fix is listed in its own window.
        /// </summary>
        static void AutoRunMetaRules()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android || !MetaConfigReady) return;
            EditorApplication.update -= AutoRunMetaRules;
            SessionState.SetBool(MetaRulesKey, true);
            try
            {
                RunMetaProjectSetup();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Spatail] Meta Project Setup Tool was not run: " + ex.Message);
            }
        }

        public static bool MetaConfigReady
        {
            get
            {
                try { return OVRProjectConfig.CachedProjectConfig != null; }
                catch (Exception) { return false; }
            }
        }

        [MenuItem("Spatail/Setup Project", priority = 0)]
        public static void ApplyFromMenu()
        {
            bool ok = Apply();
            EditorUtility.DisplayDialog("Spatail",
                ok ? "The project is configured for Meta Quest.\n\nUse Spatail > Build Quest APK to build."
                   : "Setup finished with problems. See the Console for details.", "OK");
        }

        /// <summary>Applies every setting. Returns false if any step reported a problem.</summary>
        public static bool Apply()
        {
            bool ok = true;
            ok &= Step("scene", EnsureScene);
            ok &= Step("player settings", ApplyPlayerSettings);
            ok &= Step("Meta project config", ApplyMetaConfig);
            ok &= Step("XR plug-in", ApplyXr);
            ok &= Step("build target", EnsureAndroidTarget);
            AssetDatabase.SaveAssets();
            Debug.Log(ok ? "[Spatail] Project setup applied." : "[Spatail] Project setup finished with problems; see the messages above.");
            return ok;
        }

        static bool Step(string name, Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[Spatail] Setup step '" + name + "' failed: " + ex.Message + "\n" + ex.StackTrace);
                return false;
            }
        }

        // ---- scene -------------------------------------------------------------------------

        /// <summary>
        /// Spatail builds everything from code at startup, so the scene only has to exist. It is created here rather
        /// than committed, which keeps hand-written scene files out of the repository.
        /// </summary>
        static void EnsureScene()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                Scene active = SceneManager.GetActiveScene();
                bool untitled = string.IsNullOrEmpty(active.path);
                if (untitled && active.isDirty && !Application.isBatchMode)
                    throw new InvalidOperationException("The open scene has unsaved changes. Save or discard it, then run Spatail > Setup Project again.");

                // An untitled scene cannot have another scene created beside it, so replace it; otherwise work additively
                // and leave whatever the user has open alone.
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, untitled ? NewSceneMode.Single : NewSceneMode.Additive);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new IOException("Could not save " + ScenePath);
                if (!untitled) EditorSceneManager.CloseScene(scene, true);
            }

            bool listed = false;
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
                if (s.path == ScenePath && s.enabled) listed = true;
            if (!listed) EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        // ---- player ------------------------------------------------------------------------

        static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "Spatail";
            PlayerSettings.productName = "Spatail";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationId);

            // Meta requires IL2CPP + ARM64, and (as of SDK 209) Android 12L / API 32 as the minimum.
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            if ((int)PlayerSettings.Android.minSdkVersion < 32) PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)32;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)34;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity;
            PlayerSettings.Android.androidTVCompatibility = false;

            // The AI planner service usually runs on a laptop on the same network, over plain HTTP.
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;

            if (PlayerSettings.colorSpace != ColorSpace.Linear) PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.stereoRenderingPath = StereoRenderingPath.Instancing;
        }

        // ---- Meta --------------------------------------------------------------------------

        static void ApplyMetaConfig()
        {
            OVRProjectConfig config = OVRProjectConfig.CachedProjectConfig;
            if (config == null)
                throw new InvalidOperationException("Meta's project config is not available yet. Wait for the editor to finish importing, then run Spatail > Setup Project.");

            config.targetDeviceTypes = new List<OVRProjectConfig.DeviceType>
            {
                OVRProjectConfig.DeviceType.Quest3,
                OVRProjectConfig.DeviceType.Quest3S,
            };
            config.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
            config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Required;
            // "Supported" rather than "Required": Spatail still runs on a flat floor when there is no room scan.
            config.sceneSupport = OVRProjectConfig.FeatureSupport.Supported;
            config.requiresSystemKeyboard = true;
            config.systemLoadingScreenBackground = OVRProjectConfig.SystemLoadingScreenBackground.ContextualPassthrough;
            // Meta's default network security config forbids cleartext traffic, which would block a planner service
            // reached over http:// on the local network. Turn this back on for store builds that use https.
            config.enableNSCConfig = false;
            OVRProjectConfig.CommitProjectConfig(config);
        }

        // ---- XR ----------------------------------------------------------------------------

        static void ApplyXr()
        {
            const BuildTargetGroup group = BuildTargetGroup.Android;

            XRGeneralSettings general = GetOrCreateXrSettings(group);
            if (general == null || general.Manager == null)
                throw new InvalidOperationException("XR Plug-in Management settings could not be created.");

            bool hasOpenXr = false;
            foreach (XRLoader loader in general.Manager.activeLoaders)
                if (loader is OpenXRLoader) hasOpenXr = true;
            if (!hasOpenXr && !XRPackageMetadataStore.AssignLoader(general.Manager, typeof(OpenXRLoader).FullName, group))
                throw new InvalidOperationException("Could not enable OpenXR for Android. Enable it in Project Settings > XR Plug-in Management.");
            general.InitManagerOnStart = true;
            EditorUtility.SetDirty(general);

            // Make sure the per-target feature list exists before switching features on.
            FeatureHelpers.RefreshFeatures(group);

            OpenXRFeatureSetManager.FeatureSet metaSet = OpenXRFeatureSetManager.GetFeatureSetWithId(group, MetaFeatureSetId);
            if (metaSet == null)
            {
                OpenXRFeatureSetManager.InitializeFeatureSets();
                metaSet = OpenXRFeatureSetManager.GetFeatureSetWithId(group, MetaFeatureSetId);
            }
            if (metaSet == null)
            {
                Debug.LogWarning("[Spatail] The Meta XR feature set was not found. Enable it under Project Settings > XR Plug-in Management > OpenXR > Android.");
            }
            else if (!metaSet.isEnabled)
            {
                metaSet.isEnabled = true;
                OpenXRFeatureSetManager.SetFeaturesFromEnabledFeatureSets(group);
            }

            EnableFeature(group, MetaXrFeatureId, true);
            EnableFeature(group, MetaQuestFeatureId, true);
            EnableFeature(group, TouchProfileFeatureId, true);
            FeatureHelpers.RefreshFeatures(group);
        }

        static void EnableFeature(BuildTargetGroup group, string featureId, bool required)
        {
            OpenXRFeature feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(group, featureId);
            if (feature == null)
            {
                if (required) Debug.LogWarning("[Spatail] OpenXR feature '" + featureId + "' was not found for " + group + ". Check Project Settings > XR Plug-in Management > OpenXR.");
                return;
            }
            if (feature.enabled) return;
            feature.enabled = true;
            EditorUtility.SetDirty(feature);
        }

        /// <summary>
        /// Finds the XR settings for a build target, creating the settings asset if the project has none yet.
        /// Same approach as the Meta SDK's own project setup, because XR Plug-in Management does not expose a
        /// public get-or-create call.
        /// </summary>
        static XRGeneralSettings GetOrCreateXrSettings(BuildTargetGroup group)
        {
            XRGeneralSettings settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
            if (settings != null) return settings;

            XRGeneralSettingsPerBuildTarget perTarget;
            EditorBuildSettings.TryGetConfigObject(XrSettingsKey, out perTarget);
            if (perTarget == null)
            {
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                if (!AssetDatabase.IsValidFolder("Assets/XR")) AssetDatabase.CreateFolder("Assets", "XR");
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                AssetDatabase.SaveAssets();
                EditorBuildSettings.AddConfigObject(XrSettingsKey, perTarget, true);
            }
            if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            return XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
        }

        // ---- build target ------------------------------------------------------------------

        /// <summary>
        /// Meta's and Unity's Android build steps are compiled only while Android is the active build target, so the
        /// project is switched to it up front. The switch triggers a script reload.
        /// </summary>
        static void EnsureAndroidTarget()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android) return;
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException("Android Build Support is not installed. Add it to this editor in Unity Hub (Installs > Add modules), including OpenJDK and the Android SDK & NDK tools.");
            if (Application.isBatchMode)
                throw new InvalidOperationException("Start Unity with -buildTarget Android for command-line builds.");
            Debug.Log("[Spatail] Switching the active build target to Android...");
            EditorUserBuildSettings.SwitchActiveBuildTargetAsync(BuildTargetGroup.Android, BuildTarget.Android);
        }

        // ---- Meta's own rules ----------------------------------------------------------------

        /// <summary>
        /// Asks the Meta SDK's Project Setup Tool to apply its outstanding fixes for Android. This covers everything
        /// Meta checks that the steps above do not set explicitly. It runs over the following editor frames; the
        /// returned task completes when it is done.
        /// </summary>
        public static System.Threading.Tasks.Task RunMetaProjectSetup()
        {
            return OVRProjectSetup.FixAllAsync(BuildTargetGroup.Android);
        }

        [MenuItem("Spatail/Apply Meta Recommended Settings", priority = 1)]
        static void RunMetaProjectSetupFromMenu()
        {
            try
            {
                RunMetaProjectSetup();
                Debug.Log("[Spatail] Asked the Meta Project Setup Tool to apply its fixes. Review them under Meta > Tools > Project Setup Tool.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[Spatail] Meta Project Setup Tool failed: " + ex.Message);
            }
        }
    }
}
