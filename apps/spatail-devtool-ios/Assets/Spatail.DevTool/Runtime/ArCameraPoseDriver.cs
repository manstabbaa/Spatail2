using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

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
}
