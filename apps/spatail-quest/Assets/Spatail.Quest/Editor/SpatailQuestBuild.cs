using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Spatail.Quest.EditorTools
{
    /// <summary>
    /// Builds the Quest APK from the menu or from the command line.
    ///
    /// Command line (note: no -quit; the method exits the editor itself once the build has finished):
    ///   Unity -batchmode -projectPath apps/spatail-quest -buildTarget Android
    ///         -executeMethod Spatail.Quest.EditorTools.SpatailQuestBuild.BuildFromCommandLine -logFile -
    /// Set SPATAIL_DEVELOPMENT_BUILD=1 for a development build.
    /// </summary>
    public static class SpatailQuestBuild
    {
        public const string OutputPath = "Builds/Spatail-Quest.apk";

        [MenuItem("Spatail/Build Quest APK", priority = 20)]
        public static void BuildFromMenu()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                SpatailQuestSetup.Apply();
                EditorUtility.DisplayDialog("Spatail", "The project is switching to the Android build target. When the editor has finished reloading, choose Spatail > Build Quest APK again.", "OK");
                return;
            }
            if (!SpatailQuestSetup.Apply())
            {
                if (!EditorUtility.DisplayDialog("Spatail", "Project setup reported problems (see the Console). Build anyway?", "Build", "Cancel")) return;
            }
            bool ok = Build(EditorUserBuildSettings.development);
            if (ok) EditorUtility.RevealInFinder(OutputPath);
            EditorUtility.DisplayDialog("Spatail", ok ? "Built " + OutputPath : "The build failed. See the Console.", "OK");
        }

        /// <summary>Runs the build. The project must already be on the Android build target.</summary>
        public static bool Build(bool development)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            EditorUserBuildSettings.buildAppBundle = false;
            var options = new BuildPlayerOptions
            {
                scenes = new[] { SpatailQuestSetup.ScenePath },
                locationPathName = OutputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = development ? BuildOptions.Development : BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log("[Spatail] Built " + summary.outputPath + " (" + (summary.totalSize / (1024 * 1024)) + " MB) in " + summary.totalTime);
                return true;
            }
            Debug.LogError("[Spatail] Build " + summary.result + " with " + summary.totalErrors + " error(s).");
            return false;
        }

        // ---- command line ------------------------------------------------------------------

        enum Phase { WaitForEditor, WaitForMetaRules, Build }

        static Phase _phase;
        static double _phaseStarted;
        static Task _metaRules;

        /// <summary>
        /// Entry point for -executeMethod. The work is spread over editor updates because Meta's project config and
        /// its setup rules only become available, and only run, once the editor is ticking.
        /// </summary>
        public static void BuildFromCommandLine()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                Debug.LogError("[Spatail] Start Unity with -buildTarget Android.");
                EditorApplication.Exit(2);
                return;
            }
            _phase = Phase.WaitForEditor;
            _phaseStarted = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            try
            {
                double elapsed = EditorApplication.timeSinceStartup - _phaseStarted;
                switch (_phase)
                {
                    case Phase.WaitForEditor:
                        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                        if (!SpatailQuestSetup.MetaConfigReady && elapsed < 120) return;
                        if (!SpatailQuestSetup.Apply()) Debug.LogWarning("[Spatail] Continuing after setup problems.");
                        try { _metaRules = SpatailQuestSetup.RunMetaProjectSetup(); }
                        catch (Exception ex) { Debug.LogWarning("[Spatail] Meta Project Setup Tool was not run: " + ex.Message); }
                        Next(Phase.WaitForMetaRules);
                        return;

                    case Phase.WaitForMetaRules:
                        // Best effort: carry on if Meta's tool is disabled or slow.
                        if (_metaRules != null && !_metaRules.IsCompleted && elapsed < 90) return;
                        AssetDatabase.SaveAssets();
                        Next(Phase.Build);
                        return;

                    case Phase.Build:
                        EditorApplication.update -= Tick;
                        bool development = Environment.GetEnvironmentVariable("SPATAIL_DEVELOPMENT_BUILD") == "1";
                        EditorApplication.Exit(Build(development) ? 0 : 1);
                        return;
                }
            }
            catch (Exception ex)
            {
                EditorApplication.update -= Tick;
                Debug.LogError("[Spatail] Command-line build failed: " + ex);
                EditorApplication.Exit(1);
            }
        }

        static void Next(Phase phase)
        {
            _phase = phase;
            _phaseStarted = EditorApplication.timeSinceStartup;
        }
    }
}
