using UnityEngine;

namespace Spatail.Quest
{
    /// <summary>
    /// Assembles everything Spatail needs on a Meta Quest: the camera rig with passthrough, live sensing (depth and
    /// the passthrough camera), controller and hand pointers, the system keyboard and speech input. Built entirely
    /// at runtime.
    ///
    /// Components are added while their GameObject is inactive, configured, and only then activated. That way
    /// OVRManager and the others see the intended settings in their Awake, exactly as if they had been set up in a
    /// scene.
    /// </summary>
    public static class QuestPlatform
    {
        public static OVRCameraRig Rig { get; private set; }
        public static QuestSensor Sensor { get; private set; }

        public static SpatailApp Launch()
        {
            // ---- camera rig + passthrough ------------------------------------------------------
            var rigObject = new GameObject("Spatail Camera Rig");
            rigObject.SetActive(false);
            Object.DontDestroyOnLoad(rigObject);

            var manager = rigObject.AddComponent<OVRManager>();
            // Tracking origin is FloorLevel by default, which puts y = 0 on the floor.
            manager.isInsightPassthroughEnabled = true;

            Rig = rigObject.AddComponent<OVRCameraRig>();
            // In SDK 209 a passthrough layer is always composited as the underlay behind the app's own pixels.
            rigObject.AddComponent<OVRPassthroughLayer>();
            rigObject.SetActive(true);

            // OVRCameraRig creates its anchors and eye cameras in Awake, so they exist now.
            Camera head = Rig.centerEyeAnchor.GetComponent<Camera>();
            if (head == null) head = Rig.centerEyeAnchor.gameObject.AddComponent<Camera>();
            ConfigureCamera(head);
            ConfigureCamera(Rig.leftEyeAnchor.GetComponent<Camera>());
            ConfigureCamera(Rig.rightEyeAnchor.GetComponent<Camera>());
            if (Object.FindAnyObjectByType<AudioListener>() == null) head.gameObject.AddComponent<AudioListener>();

            // ---- live sensing ------------------------------------------------------------------
            // No MRUK component and no stored room: MR Utility Kit's native library starts by itself, and the sensor
            // adds the depth ray caster and the passthrough camera once it knows what this headset supports.
            var sensorObject = new GameObject("Spatail Sensor");
            sensorObject.SetActive(false);
            Object.DontDestroyOnLoad(sensorObject);
            Sensor = sensorObject.AddComponent<QuestSensor>();
            Sensor.Rig = Rig;
            sensorObject.SetActive(true);

            // ---- input ---------------------------------------------------------------------------
            var inputObject = new GameObject("Spatail Quest Input");
            Object.DontDestroyOnLoad(inputObject);

            var services = new PlatformServices
            {
                Head = head,
                Sensor = Sensor,
                DeviceName = "quest",
                WorldUi = true,
                // Right thumbstick left/right turns the pump while it is being moved.
                StationRotateInput = () =>
                    OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch).x * 90f * Time.unscaledDeltaTime,
            };

            services.Pointers.Add(AddController(inputObject, OVRInput.Controller.RTouch));
            services.Pointers.Add(AddController(inputObject, OVRInput.Controller.LTouch));
            services.Pointers.Add(AddHand(inputObject, OVRPlugin.Hand.HandRight));
            services.Pointers.Add(AddHand(inputObject, OVRPlugin.Hand.HandLeft));

            if (TouchScreenKeyboard.isSupported)
            {
                services.TextEntry = inputObject.AddComponent<SystemKeyboardTextEntry>();
            }
            else
            {
                // Editor with a headset over Link: there is no system keyboard, so type on the PC.
                services.TextEntry = inputObject.AddComponent<KeyboardTextEntry>();
            }

            var speech = inputObject.AddComponent<AndroidSpeechInput>();
            services.Speech = speech;

            SpatailApp app = SpatailApp.Launch(services);
            var pcKeyboard = services.TextEntry as KeyboardTextEntry;
            if (pcKeyboard != null) pcKeyboard.App = app;
            return app;
        }

        /// <summary>Passthrough shows wherever the app renders nothing, so the eye buffer must clear to transparent.</summary>
        static void ConfigureCamera(Camera camera)
        {
            if (camera == null) return;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 50f;
            camera.allowHDR = false;
        }

        static ControllerPointer AddController(GameObject host, OVRInput.Controller controller)
        {
            var pointer = host.AddComponent<ControllerPointer>();
            pointer.Rig = Rig;
            pointer.Controller = controller;
            return pointer;
        }

        static HandPointer AddHand(GameObject host, OVRPlugin.Hand hand)
        {
            var pointer = host.AddComponent<HandPointer>();
            pointer.Rig = Rig;
            pointer.Hand = hand;
            return pointer;
        }
    }
}
