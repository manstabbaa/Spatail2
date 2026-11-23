using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Spatail.DevTool.Editor
{
    /// <summary>
    /// Exports the Xcode project for the dev tool.
    ///
    /// From the editor: Spatail > Build iOS (Xcode project).
    /// From the command line (run twice on a fresh clone: the first pass configures the project and lets Unity
    /// recompile with the ARKit code paths enabled, the second builds):
    ///
    ///   Unity -batchmode -quit -projectPath apps/spatail-devtool-ios -buildTarget iOS \
    ///         -executeMethod Spatail.DevTool.Editor.DevToolBuild.SetupBatch
    ///   Unity -batchmode -quit -projectPath apps/spatail-devtool-ios -buildTarget iOS \
    ///         -executeMethod Spatail.DevTool.Editor.DevToolBuild.BuildBatch
    ///
    /// Environment: SPATAIL_IOS_TEAM_ID sets the signing team; SPATAIL_DEVELOPMENT_BUILD=1 makes a development build.
    /// </summary>
    public static class DevToolBuild
    {
        public const string OutputPath = "Builds/iOS";

        [MenuItem("Spatail/Build iOS (Xcode project)", priority = 20)]
        public static void BuildFromMenu()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS, BuildTarget.iOS))
            {
                EditorUtility.DisplayDialog("Spatail", "iOS Build Support is not installed for this editor. Add it in Unity Hub > Installs > Add modules.", "OK");
                return;
            }
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
            {
                bool go = EditorUtility.DisplayDialog("Spatail",
                    "The project needs to switch to the iOS platform first. Unity will reimport assets; choose Build iOS again when it finishes.",
                    "Switch to iOS", "Cancel");
                if (go) EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS);
                return;
            }

            DevToolProjectSetup.Run(false);
            BuildReport report = Build();
            if (report.summary.result == BuildResult.Succeeded)
            {
                EditorUtility.RevealInFinder(Path.GetFullPath(OutputPath));
                Debug.Log("[Spatail] Xcode project exported to " + Path.GetFullPath(OutputPath) + ". Open Unity-iPhone.xcodeproj, pick your device and run.");
            }
            else
            {
                EditorUtility.DisplayDialog("Spatail", "The build did not succeed: " + report.summary.result + ". See the Console for details.", "OK");
            }
        }

        /// <summary>Batch entry point: configure the project and exit, so the next invocation compiles with it.</summary>
        public static void SetupBatch()
        {
            try
            {
                DevToolProjectSetup.Run(true);
                AssetDatabase.SaveAssets();
            }
            catch (Exception e)
            {
                Debug.LogError("[Spatail] Setup failed: " + e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Batch entry point: configure, then export the Xcode project. Exits non-zero on failure.</summary>
        public static void BuildBatch()
        {
            int code = 1;
            try
            {
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
                    throw new InvalidOperationException("Run Unity with -buildTarget iOS; the active build target is " + EditorUserBuildSettings.activeBuildTarget + ".");
                DevToolProjectSetup.Run(true);
                BuildReport report = Build();
                code = report.summary.result == BuildResult.Succeeded ? 0 : 1;
                Debug.Log("[Spatail] Build " + report.summary.result + ": " + report.summary.totalErrors + " errors, output " + Path.GetFullPath(OutputPath));
            }
            catch (Exception e)
            {
                Debug.LogError("[Spatail] Build failed: " + e);
            }
            EditorApplication.Exit(code);
        }

        static BuildReport Build()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { DevToolProjectSetup.ScenePath },
                locationPathName = OutputPath,
                target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                options = Environment.GetEnvironmentVariable("SPATAIL_DEVELOPMENT_BUILD") == "1" ? BuildOptions.Development : BuildOptions.None,
            };
            return BuildPipeline.BuildPlayer(options);
        }
    }
}
