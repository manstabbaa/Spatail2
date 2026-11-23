using UnityEngine;

namespace Spatail.DevTool
{
    /// <summary>
    /// Starts the dev tool without anything in the scene. On a phone or tablet it brings up the AR session; in the
    /// editor (and on desktop players) it runs the shared simulator instead, with the same dev tool UI on top, so the
    /// tooling can be worked on in Play mode without a device.
    /// </summary>
    public static class DevToolBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (SpatailApp.Instance != null) return;
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            if (Application.isMobilePlatform && !Application.isEditor)
            {
                ArPlatform.Launch();
            }
            else
            {
                SpatailApp app = SimPlatform.Launch(worldUi: false);
                DevToolUi.Attach(app, null, null);
            }
        }
    }
}
