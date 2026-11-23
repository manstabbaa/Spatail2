using UnityEngine;

namespace Spatail.Quest
{
    /// <summary>
    /// Starts Spatail when the app starts. No scene objects are involved: the scene is empty and everything is built
    /// from code, so there are no prefab or scene references to break.
    ///
    /// On a headset this launches the Quest platform (passthrough, room understanding, controllers and hands).
    /// In the Editor with no XR device active it launches the desktop simulator instead, so pressing Play works
    /// without hardware.
    /// </summary>
    public static class QuestBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (SpatailApp.Instance != null) return;

#if UNITY_EDITOR || UNITY_STANDALONE
            // XR Plug-in Management has already started its loader by the time the first scene has loaded, so this
            // tells us whether a headset (for example over Link) is actually driving the display.
            if (!UnityEngine.XR.XRSettings.isDeviceActive)
            {
                Debug.Log("[Spatail] No XR device active: starting the desktop simulator.");
                SimPlatform.Launch();
                return;
            }
#endif
            QuestPlatform.Launch();
        }
    }
}
