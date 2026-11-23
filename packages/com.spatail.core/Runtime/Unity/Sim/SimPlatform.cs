using System;
using Spatail.Core;
using UnityEngine;

namespace Spatail
{
    /// <summary>
    /// Runs Spatail with no headset and no phone: a synthetic room, a fly camera, the mouse as the pointer and the
    /// keyboard for prompts. This is what you get when you press Play in the editor, and it exercises exactly the
    /// same planner, layout and rendering code as the devices.
    ///
    /// Controls: hold right mouse to look, WASD/QE to move while looking. Click to interact. Just start typing to
    /// write a prompt, Enter to send, Esc to cancel. F1-F6 send the six demo prompts. While moving the pump,
    /// Z and X rotate it.
    /// </summary>
    public static class SimPlatform
    {
        public static SpatailApp Launch()
        {
            var cameraObject = new GameObject("Sim Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Theme.Hex(0xF3F2FB);
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 60f;
            camera.fieldOfView = 55f;
            cameraObject.AddComponent<AudioListener>();

            RoomModel room = SimRoom.Create();
            cameraObject.transform.position = room.userPosition.ToUnity();
            cameraObject.transform.rotation = Quaternion.Euler(14f, 0f, 0f);
            var controller = cameraObject.AddComponent<SimCameraController>();

            var pointer = cameraObject.AddComponent<MousePointer>();
            pointer.Camera = camera;
            var keyboard = cameraObject.AddComponent<KeyboardTextEntry>();

            SimRoomVisuals.Build(room);

            var services = new PlatformServices
            {
                Head = camera,
                Room = new SimRoomProvider(),
                TextEntry = keyboard,
                DeviceName = "sim",
                WorldUi = true,
                StationRotateInput = SimCameraController.StationRotate,
            };
            services.Pointers.Add(pointer);
            SpatailApp app = SpatailApp.Launch(services);
            controller.App = app;
            keyboard.App = app;
            return app;
        }
    }

    public sealed class SimRoomProvider : IRoomProvider
    {
        public bool IsReady => true;
        public string Status => "Simulated room";
        public RoomModel Capture() => SimRoom.Create();
#pragma warning disable 67
        public event Action Changed;
#pragma warning restore 67
    }

    /// <summary>Draws the simulated room so content has something to sit in: a gridded floor and pale walls.</summary>
    public static class SimRoomVisuals
    {
        public static void Build(RoomModel room)
        {
            var root = new GameObject("Sim Room");
            UnityEngine.Object.DontDestroyOnLoad(root);
            var builder = new MeshBuilder();
            Color floor = Theme.Hex(0xECEAF8), line = Theme.Hex(0xDAD7EE), wall = Theme.Hex(0xF8F7FD);

            foreach (RoomEntity e in room.entities)
            {
                Matrix4x4 m = Matrix4x4.TRS(e.position.ToUnity(), e.rotation.ToUnity(), Vector3.one);
                if (e.kind == EntityKind.Floor)
                {
                    builder.AddBox(m * Matrix4x4.Translate(new Vector3(0, -0.01f, 0)), new Vector3(e.size.x, 0.02f, e.size.z), floor);
                    // Half-metre grid.
                    for (float x = -e.size.x * 0.5f; x <= e.size.x * 0.5f + 0.001f; x += 0.5f)
                        builder.AddBox(m * Matrix4x4.Translate(new Vector3(x, 0.001f, 0)), new Vector3(0.006f, 0.002f, e.size.z), line);
                    for (float z = -e.size.z * 0.5f; z <= e.size.z * 0.5f + 0.001f; z += 0.5f)
                        builder.AddBox(m * Matrix4x4.Translate(new Vector3(0, 0.001f, z)), new Vector3(e.size.x, 0.002f, 0.006f), line);
                }
                else if (e.kind == EntityKind.Wall)
                {
                    // Sit the wall slab behind its surface plane so wall-mounted content is not buried.
                    builder.AddBox(m * Matrix4x4.Translate(new Vector3(0, 0, -0.03f)), new Vector3(e.size.x, e.size.y, 0.05f), wall);
                }
            }
            GameObject go = MeshFactory.Spawn("Surfaces", root.transform, builder.ToMesh("Sim Room"), Assets.SharedSolid("sim-room", Color.white, 0.35f));
            go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
        }
    }

    /// <summary>Editor-style fly camera: hold the right mouse button to look and move.</summary>
    public sealed class SimCameraController : MonoBehaviour
    {
        public SpatailApp App;
        float _yaw, _pitch = 14f;

        void Start()
        {
            Vector3 e = transform.eulerAngles;
            _yaw = e.y;
            _pitch = e.x > 180f ? e.x - 360f : e.x;
        }

        /// <summary>Degrees of yaw to apply to the station this frame (Z / X keys).</summary>
        public static float StationRotate()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            float dir = (Input.GetKey(KeyCode.X) ? 1f : 0f) - (Input.GetKey(KeyCode.Z) ? 1f : 0f);
            return dir * 90f * Time.unscaledDeltaTime;
#else
            return 0f;
#endif
        }

        void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButton(1))
            {
                _yaw += Input.GetAxis("Mouse X") * 3f;
                _pitch = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * 3f, -80f, 80f);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

                var move = new Vector3(
                    (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
                    (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f),
                    (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f));
                float speed = Input.GetKey(KeyCode.LeftShift) ? 4f : 1.6f;
                transform.position += transform.rotation * move * (speed * Time.unscaledDeltaTime);
            }
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f) transform.position += transform.forward * (scroll * 0.15f);

            if (App != null && App.PromptBar != null)
            {
                string[] demo =
                {
                    "Build me a walkthrough for this pump", "Show me how the water flows", "Has it failed before?",
                    "Put this month's output by the bench", "What parts do I need?", "Bring Sara in",
                };
                for (int i = 0; i < demo.Length; i++)
                    if (Input.GetKeyDown(KeyCode.F1 + i)) App.Submit(demo[i]);
            }
#endif
        }
    }

    /// <summary>The mouse as a pointer: a ray from the camera through the cursor, pressed with the left button.</summary>
    public sealed class MousePointer : PointerSource
    {
        public Camera Camera;

        public override bool TryGetRay(out Ray ray)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Camera != null)
            {
                ray = Camera.ScreenPointToRay(Input.mousePosition);
                return true;
            }
#endif
            ray = default(Ray);
            return false;
        }

        public override bool IsPressed
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return Input.GetMouseButton(0);
#else
                return false;
#endif
            }
        }
    }

    /// <summary>Typing on a physical keyboard. Starts by itself when a letter is pressed, so prompts can just be typed.</summary>
    public sealed class KeyboardTextEntry : MonoBehaviour, ITextEntry
    {
        public SpatailApp App;

        string _text = "";
        Action<string> _onChanged, _onSubmit;
        Action _onCancel;

        public bool IsOpen { get; private set; }

        public void Open(string initial, Action<string> onChanged, Action<string> onSubmit, Action onCancel)
        {
            _text = initial ?? "";
            _onChanged = onChanged;
            _onSubmit = onSubmit;
            _onCancel = onCancel;
            IsOpen = true;
        }

        public void Close() => IsOpen = false;

        void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            // Leave the keyboard to the fly camera while the right mouse button is held.
            if (Input.GetMouseButton(1)) return;
            string input = Input.inputString;
            if (string.IsNullOrEmpty(input) && !(IsOpen && Input.GetKeyDown(KeyCode.Escape))) return;

            if (!IsOpen)
            {
                // The first printable character opens the prompt bar.
                bool printable = false;
                foreach (char c in input) if (c >= ' ' && c != 127) printable = true;
                if (!printable || App == null || App.PromptBar == null || App.State != AppState.Ready) return;
                App.PromptBar.BeginTyping();
                if (!IsOpen) return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                IsOpen = false;
                if (_onCancel != null) _onCancel();
                return;
            }
            foreach (char c in input)
            {
                if (c == '\b')
                {
                    if (_text.Length > 0) _text = _text.Substring(0, _text.Length - 1);
                }
                else if (c == '\n' || c == '\r')
                {
                    IsOpen = false;
                    string submitted = _text;
                    _text = "";
                    if (_onSubmit != null) _onSubmit(submitted);
                    return;
                }
                else if (c >= ' ')
                {
                    _text += c;
                }
            }
            if (_onChanged != null) _onChanged(_text);
#endif
        }
    }
}
