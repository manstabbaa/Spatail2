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
        /// <param name="worldUi">Show the floating prompt bar. Pass false when the host app draws its own screen UI.</param>
        public static SpatailApp Launch(bool worldUi = true)
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
                WorldUi = worldUi,
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

        static readonly string[] Demo =
        {
            "Build me a walkthrough for this pump", "Show me how the water flows", "Has it failed before?",
            "Put this month's output by the bench", "What parts do I need?", "Bring Sara in",
        };

        void Start()
        {
            Vector3 e = transform.eulerAngles;
            _yaw = e.y;
            _pitch = e.x > 180f ? e.x - 360f : e.x;
        }

        /// <summary>Degrees of yaw to apply to the station this frame (Z / X keys).</summary>
        public static float StationRotate()
        {
            float dir = (InputBridge.KeyHeld(SimKey.X) ? 1f : 0f) - (InputBridge.KeyHeld(SimKey.Z) ? 1f : 0f);
            return dir * 90f * Time.unscaledDeltaTime;
        }

        void Update()
        {
            if (InputBridge.MouseButton(1))
            {
                Vector2 delta = InputBridge.MouseDelta;
                _yaw += delta.x * 0.18f;
                _pitch = Mathf.Clamp(_pitch - delta.y * 0.18f, -80f, 80f);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

                var move = new Vector3(
                    (InputBridge.KeyHeld(SimKey.D) ? 1f : 0f) - (InputBridge.KeyHeld(SimKey.A) ? 1f : 0f),
                    (InputBridge.KeyHeld(SimKey.E) ? 1f : 0f) - (InputBridge.KeyHeld(SimKey.Q) ? 1f : 0f),
                    (InputBridge.KeyHeld(SimKey.W) ? 1f : 0f) - (InputBridge.KeyHeld(SimKey.S) ? 1f : 0f));
                float speed = InputBridge.KeyHeld(SimKey.Shift) ? 4f : 1.6f;
                transform.position += transform.rotation * move * (speed * Time.unscaledDeltaTime);
            }
            float scroll = InputBridge.Scroll;
            if (Mathf.Abs(scroll) > 0.01f) transform.position += transform.forward * (scroll * 0.15f);

            if (App != null)
                for (int i = 0; i < Demo.Length; i++)
                    if (InputBridge.KeyPressed(SimKey.F1 + i)) App.Submit(Demo[i]);
        }
    }

    /// <summary>The mouse as a pointer: a ray from the camera through the cursor, pressed with the left button.</summary>
    public sealed class MousePointer : PointerSource
    {
        public Camera Camera;

        public override bool TryGetRay(out Ray ray)
        {
            if (Camera != null && InputBridge.HasMouse)
            {
                ray = Camera.ScreenPointToRay(InputBridge.MousePosition);
                return true;
            }
            ray = default(Ray);
            return false;
        }

        public override bool IsPressed => InputBridge.MouseButton(0);
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
            // Leave the keyboard to the fly camera while the right mouse button is held.
            if (InputBridge.MouseButton(1)) return;
            string input = InputBridge.TypedThisFrame;
            bool escape = InputBridge.KeyPressed(SimKey.Escape);
            if (string.IsNullOrEmpty(input) && !(IsOpen && escape)) return;

            if (!IsOpen)
            {
                // The first printable character opens the prompt bar.
                bool printable = false;
                foreach (char c in input) if (c > ' ' && c != 127) printable = true;
                if (!printable || App == null || App.PromptBar == null || App.State != AppState.Ready) return;
                App.PromptBar.BeginTyping();
                if (!IsOpen) return;
            }

            if (escape)
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
                else if (c >= ' ' && c != 127)
                {
                    _text += c;
                }
            }
            if (_onChanged != null) _onChanged(_text);
        }
    }
}
