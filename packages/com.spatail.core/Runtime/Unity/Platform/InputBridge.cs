using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && SPATAIL_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using ETouchPhase = UnityEngine.InputSystem.TouchPhase;
#endif

namespace Spatail
{
    /// <summary>Keys the simulator and dev tool care about, independent of which input backend is active.</summary>
    public enum SimKey { W, A, S, D, Q, E, Z, X, Shift, Escape, F1, F2, F3, F4, F5, F6 }

    public struct TouchPoint
    {
        public int id;
        public Vector2 position;
        public bool began;
        public bool ended;
    }

    /// <summary>
    /// Mouse, keyboard and touch through whichever input backend the project has active. Unity projects can run the
    /// Input System package, the legacy Input Manager, or both; reading the wrong one throws at runtime, so everything
    /// in Spatail that needs raw input goes through here.
    /// </summary>
    public static class InputBridge
    {
        static readonly List<TouchPoint> TouchBuffer = new List<TouchPoint>();
        static readonly System.Text.StringBuilder Typed = new System.Text.StringBuilder();
        static int _typedFrame = -1;
        static string _typedThisFrame = "";

#if ENABLE_INPUT_SYSTEM && SPATAIL_INPUT_SYSTEM
        static bool _hooked;
        static Keyboard _hookedKeyboard;

        static void EnsureHooks()
        {
            if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && (!_hooked || keyboard != _hookedKeyboard))
            {
                if (_hookedKeyboard != null) _hookedKeyboard.onTextInput -= OnTextInput;
                keyboard.onTextInput += OnTextInput;
                _hookedKeyboard = keyboard;
                _hooked = true;
            }
        }

        static void OnTextInput(char c) => Typed.Append(c);

        static UnityEngine.InputSystem.Key Map(SimKey key)
        {
            switch (key)
            {
                case SimKey.W: return UnityEngine.InputSystem.Key.W;
                case SimKey.A: return UnityEngine.InputSystem.Key.A;
                case SimKey.S: return UnityEngine.InputSystem.Key.S;
                case SimKey.D: return UnityEngine.InputSystem.Key.D;
                case SimKey.Q: return UnityEngine.InputSystem.Key.Q;
                case SimKey.E: return UnityEngine.InputSystem.Key.E;
                case SimKey.Z: return UnityEngine.InputSystem.Key.Z;
                case SimKey.X: return UnityEngine.InputSystem.Key.X;
                case SimKey.Shift: return UnityEngine.InputSystem.Key.LeftShift;
                case SimKey.Escape: return UnityEngine.InputSystem.Key.Escape;
                case SimKey.F1: return UnityEngine.InputSystem.Key.F1;
                case SimKey.F2: return UnityEngine.InputSystem.Key.F2;
                case SimKey.F3: return UnityEngine.InputSystem.Key.F3;
                case SimKey.F4: return UnityEngine.InputSystem.Key.F4;
                case SimKey.F5: return UnityEngine.InputSystem.Key.F5;
                default: return UnityEngine.InputSystem.Key.F6;
            }
        }

        public static bool HasMouse => Mouse.current != null;
        public static Vector2 MousePosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        public static bool MouseButton(int button)
        {
            Mouse m = Mouse.current;
            if (m == null) return false;
            return button == 0 ? m.leftButton.isPressed : (button == 1 ? m.rightButton.isPressed : m.middleButton.isPressed);
        }
        /// <summary>Mouse movement this frame in pixels.</summary>
        public static Vector2 MouseDelta => Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
        /// <summary>Scroll wheel this frame; positive is away from the user. Roughly one unit per notch.</summary>
        public static float Scroll
        {
            get
            {
                if (Mouse.current == null) return 0f;
                float y = Mouse.current.scroll.ReadValue().y;
                // Some platforms report 120 per notch, others 1.
                return Mathf.Abs(y) > 10f ? y / 120f : y;
            }
        }
        public static bool KeyHeld(SimKey key) => Keyboard.current != null && Keyboard.current[Map(key)].isPressed;
        public static bool KeyPressed(SimKey key) => Keyboard.current != null && Keyboard.current[Map(key)].wasPressedThisFrame;

        public static List<TouchPoint> Touches
        {
            get
            {
                EnsureHooks();
                TouchBuffer.Clear();
                foreach (ETouch t in ETouch.activeTouches)
                {
                    TouchBuffer.Add(new TouchPoint
                    {
                        id = t.touchId,
                        position = t.screenPosition,
                        began = t.phase == ETouchPhase.Began,
                        ended = t.phase == ETouchPhase.Ended || t.phase == ETouchPhase.Canceled,
                    });
                }
                return TouchBuffer;
            }
        }

        static string ReadTyped()
        {
            EnsureHooks();
            string s = Typed.ToString();
            Typed.Length = 0;
            return s;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        static KeyCode Map(SimKey key)
        {
            switch (key)
            {
                case SimKey.W: return KeyCode.W;
                case SimKey.A: return KeyCode.A;
                case SimKey.S: return KeyCode.S;
                case SimKey.D: return KeyCode.D;
                case SimKey.Q: return KeyCode.Q;
                case SimKey.E: return KeyCode.E;
                case SimKey.Z: return KeyCode.Z;
                case SimKey.X: return KeyCode.X;
                case SimKey.Shift: return KeyCode.LeftShift;
                case SimKey.Escape: return KeyCode.Escape;
                case SimKey.F1: return KeyCode.F1;
                case SimKey.F2: return KeyCode.F2;
                case SimKey.F3: return KeyCode.F3;
                case SimKey.F4: return KeyCode.F4;
                case SimKey.F5: return KeyCode.F5;
                default: return KeyCode.F6;
            }
        }

        static Vector2 _lastMouse;
        static int _lastMouseFrame = -1;
        static Vector2 _mouseDelta;

        public static bool HasMouse => Input.mousePresent;
        public static Vector2 MousePosition => Input.mousePosition;
        public static bool MouseButton(int button) => Input.GetMouseButton(button);
        /// <summary>Mouse movement this frame in pixels.</summary>
        public static Vector2 MouseDelta
        {
            get
            {
                // Computed from positions so it does not depend on the "Mouse X/Y" axes being defined.
                if (_lastMouseFrame != Time.frameCount)
                {
                    Vector2 now = Input.mousePosition;
                    _mouseDelta = _lastMouseFrame == Time.frameCount - 1 ? now - _lastMouse : Vector2.zero;
                    _lastMouse = now;
                    _lastMouseFrame = Time.frameCount;
                }
                return _mouseDelta;
            }
        }
        /// <summary>Scroll wheel this frame; positive is away from the user. Roughly one unit per notch.</summary>
        public static float Scroll => Input.mouseScrollDelta.y;
        public static bool KeyHeld(SimKey key) => Input.GetKey(Map(key));
        public static bool KeyPressed(SimKey key) => Input.GetKeyDown(Map(key));

        public static List<TouchPoint> Touches
        {
            get
            {
                TouchBuffer.Clear();
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch t = Input.GetTouch(i);
                    TouchBuffer.Add(new TouchPoint
                    {
                        id = t.fingerId,
                        position = t.position,
                        began = t.phase == TouchPhase.Began,
                        ended = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled,
                    });
                }
                return TouchBuffer;
            }
        }

        static string ReadTyped() => Input.inputString ?? "";
#else
        public static bool HasMouse => false;
        public static Vector2 MousePosition => Vector2.zero;
        public static bool MouseButton(int button) => false;
        public static Vector2 MouseDelta => Vector2.zero;
        public static float Scroll => 0f;
        public static bool KeyHeld(SimKey key) => false;
        public static bool KeyPressed(SimKey key) => false;
        public static List<TouchPoint> Touches { get { TouchBuffer.Clear(); return TouchBuffer; } }
        static string ReadTyped() => "";
#endif

        /// <summary>
        /// Characters typed this frame, including '\b' for backspace and '\n' or '\r' for Enter. Safe to read from
        /// several places in one frame.
        /// </summary>
        public static string TypedThisFrame
        {
            get
            {
                if (_typedFrame != Time.frameCount)
                {
                    _typedFrame = Time.frameCount;
                    _typedThisFrame = ReadTyped();
                }
                return _typedThisFrame;
            }
        }
    }
}
