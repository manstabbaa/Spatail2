using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
#endif

namespace Spatail.DevTool
{
    /// <summary>
    /// Builds the AR Foundation rig in code and hands Spatail its platform services: the AR camera as the head, ARKit
    /// as the live space sensor, touch as the pointer and on-device speech recognition.
    /// The rig mirrors what AR Foundation's own "XR Origin (Mobile AR)" and "AR Session" menu items create.
    /// </summary>
    public static class ArPlatform
    {
        public static SpatailApp Launch()
        {
            // AR Session: session lifetime plus the input subsystem that reports the device pose.
            var sessionObject = new GameObject("AR Session");
            Object.DontDestroyOnLoad(sessionObject);
            sessionObject.AddComponent<ARSession>();
            sessionObject.AddComponent<ARInputManager>();

            // XR Origin > Camera Offset > Main Camera. Built inactive so every component is configured before it wakes.
            var originObject = new GameObject("XR Origin");
            originObject.SetActive(false);
            Object.DontDestroyOnLoad(originObject);
            var origin = originObject.AddComponent<XROrigin>();

            var offsetObject = new GameObject("Camera Offset");
            offsetObject.transform.SetParent(originObject.transform, false);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.transform.SetParent(offsetObject.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Color;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 30f;
            cameraObject.AddComponent<AudioListener>();
            var cameraManager = cameraObject.AddComponent<ARCameraManager>();
            cameraObject.AddComponent<ARCameraBackground>();
            AddPoseDriver(cameraObject);

            origin.CameraFloorOffsetObject = offsetObject;
            origin.Camera = camera;
            // Handheld AR reports the true device pose; no artificial eye height.
            origin.CameraYOffset = 0f;

            var planes = originObject.AddComponent<ARPlaneManager>();
            planes.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;

            // On the same object as the plane manager, which is how ARRaycastManager finds the planes to answer
            // world rays with (ARPlaneManager.OnEnable registers itself with it).
            var raycasts = originObject.AddComponent<ARRaycastManager>();

            var sensor = originObject.AddComponent<ArSensor>();
            sensor.Planes = planes;
            sensor.Raycasts = raycasts;
            sensor.CameraManager = cameraManager;
            sensor.Camera = camera;
            sensor.UseMesh(AddMeshing(originObject));

            var pointer = cameraObject.AddComponent<TouchPointer>();
            pointer.Camera = camera;

            originObject.SetActive(true);

            var speech = sessionObject.AddComponent<IosSpeechInput>();

            var services = new PlatformServices
            {
                Head = camera,
                Sensor = sensor,
                Speech = speech,
                DeviceName = "ios",
                WorldUi = false,
                StationRotateInput = pointer.TwistDegrees,
            };
            services.Pointers.Add(pointer);

            SpatailApp app = SpatailApp.Launch(services);
            DevToolUi ui = DevToolUi.Attach(app, speech, sensor);
            pointer.IsOverUi = ui.IsOverUi;
            pointer.IsMoveMode = () => app.IsMovingStation;

            return app;
        }

        /// <summary>
        /// Scene reconstruction, which only devices with a LiDAR scanner provide. The manager must sit on a child of
        /// the XR Origin, and switches itself off when the device has no meshing subsystem. Each piece of mesh is a
        /// copy of the template: geometry only, nothing drawn and no colliders.
        /// </summary>
        static ARMeshManager AddMeshing(GameObject originObject)
        {
            var template = new GameObject("AR Mesh Template");
            template.SetActive(false);
            template.transform.SetParent(originObject.transform, false);
            var filter = template.AddComponent<MeshFilter>();

            var meshObject = new GameObject("AR Meshes");
            meshObject.transform.SetParent(originObject.transform, false);
            var meshes = meshObject.AddComponent<ARMeshManager>();
            meshes.meshPrefab = filter;
            meshes.normals = false;
            return meshes;
        }

        static void AddPoseDriver(GameObject cameraObject)
        {
#if ENABLE_INPUT_SYSTEM
            // The same bindings AR Foundation's editor utility sets up (XROriginCreateUtil.CreateARMainCamera).
            var driver = cameraObject.AddComponent<TrackedPoseDriver>();
            var position = new InputAction("Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
            position.AddBinding("<HandheldARInputDevice>/devicePosition");
            var rotation = new InputAction("Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
            rotation.AddBinding("<HandheldARInputDevice>/deviceRotation");
            driver.positionInput = new InputActionProperty(position);
            driver.rotationInput = new InputActionProperty(rotation);
            // If the Input System never delivers a pose (its XR device layout can be compiled out), fall back.
            cameraObject.AddComponent<ArPoseWatchdog>().Driver = driver;
#else
            // The Input System backend is switched off in this project, so read the device pose from the XR module.
            cameraObject.AddComponent<ArCameraPoseDriver>();
#endif
        }
    }
}
