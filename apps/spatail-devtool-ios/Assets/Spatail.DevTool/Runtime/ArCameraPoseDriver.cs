using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;

namespace Spatail.DevTool
{
    /// <summary>
    /// Drives the AR camera from the XR input subsystem directly. Only used when the project's Input System backend
    /// is disabled and the Input System tracked pose driver therefore cannot run. Reads the same features AR
    /// Foundation's legacy ARPoseDriver reads.
    /// </summary>
    public sealed class ArCameraPoseDriver : MonoBehaviour
    {
        static readonly List<InputDevice> Devices = new List<InputDevice>();

        void OnEnable() => Application.onBeforeRender += Apply;
        void OnDisable() => Application.onBeforeRender -= Apply;
        void Update() => Apply();

        void Apply()
        {
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.TrackedDevice, Devices);
            foreach (InputDevice device in Devices)
            {
                Vector3 position;
                Quaternion rotation;
                bool hasPosition = device.TryGetFeatureValue(CommonUsages.centerEyePosition, out position)
                                   || device.TryGetFeatureValue(CommonUsages.colorCameraPosition, out position)
                                   || device.TryGetFeatureValue(CommonUsages.devicePosition, out position);
                bool hasRotation = device.TryGetFeatureValue(CommonUsages.centerEyeRotation, out rotation)
                                   || device.TryGetFeatureValue(CommonUsages.colorCameraRotation, out rotation)
                                   || device.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation);
                if (!hasPosition || !hasRotation) continue;
                transform.localPosition = position;
                transform.localRotation = rotation;
                return;
            }
        }
    }

    /// <summary>
    /// Safety net for the camera pose. AR Foundation's recommended setup drives the camera with the Input System's
    /// tracked pose driver; if the session is tracking but the camera has not moved at all, that driver is not
    /// receiving the device pose, and this switches to <see cref="ArCameraPoseDriver"/> so the app still works.
    /// </summary>
    public sealed class ArPoseWatchdog : MonoBehaviour
    {
        /// <summary>The pose driver to replace if it turns out not to be working.</summary>
        public Behaviour Driver;

        float _trackingFor;

        void Update()
        {
            if (ARSession.state != ARSessionState.SessionTracking)
            {
                _trackingFor = 0f;
                return;
            }
            bool moved = transform.localPosition != Vector3.zero || transform.localRotation != Quaternion.identity;
            if (moved)
            {
                // The pose is flowing; nothing more to watch.
                enabled = false;
                return;
            }
            _trackingFor += Time.unscaledDeltaTime;
            if (_trackingFor < 2f) return;

            Debug.LogWarning("[Spatail] The Input System pose driver is not receiving the device pose; reading it from the XR device instead.");
            if (Driver != null) Driver.enabled = false;
            gameObject.AddComponent<ArCameraPoseDriver>();
            enabled = false;
        }
    }
}
