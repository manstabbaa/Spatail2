using System.Collections.Generic;
using UnityEngine;

namespace Spatail.DevTool
{
    /// <summary>Reusable pieces of the dev tool UI: text input that works with the on-screen keyboard, and touch scrolling.</summary>
    public sealed partial class DevToolUi
    {
        // ---- text input ----------------------------------------------------------------------

        TouchScreenKeyboard _keyboard;
        string _keyboardField;
        string _committedField, _committedText;
        bool _committedSubmit;

        bool IsEditing(string id) => _keyboard != null && _keyboardField == id;

        /// <summary>
        /// A single-line text field. On devices with an on-screen keyboard it opens the system keyboard (which also
        /// offers dictation) and reports true when the user presses Done; on desktop it is an ordinary text field and
        /// reports true on Enter.
        /// </summary>
        bool TextInput(string id, Rect rect, ref string value, string placeholder,
            TouchScreenKeyboardType type = TouchScreenKeyboardType.Default, bool autocorrect = false, bool secure = false)
        {
            bool submitted = false;
            value = value ?? "";
            if (_committedField == id)
            {
                value = _committedText ?? "";
                submitted = _committedSubmit;
                _committedField = null;
            }

            if (TouchScreenKeyboard.isSupported)
            {
                bool editing = IsEditing(id);
                if (editing) value = _keyboard.text ?? "";
                bool empty = value.Length == 0;
                string shown = empty ? placeholder : (secure ? new string('•', value.Length) : value);
                if (GUI.Button(rect, shown, empty ? _fieldEmpty : _field) && !editing)
                {
                    _keyboardField = id;
                    _keyboard = TouchScreenKeyboard.Open(value, type, autocorrect, false, secure, false, placeholder);
                }
                return submitted;
            }

            Event e = Event.current;
            bool focused = GUI.GetNameOfFocusedControl() == id;
            if (focused && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                submitted = true;
                e.Use();
            }
            GUI.SetNextControlName(id);
            value = secure ? GUI.PasswordField(rect, value, '•', _field) : GUI.TextField(rect, value, _field);
            if (value.Length == 0 && !focused) GUI.Label(rect, placeholder, _fieldEmpty);
            return submitted;
        }

        void PollKeyboard()
        {
            if (_keyboard == null) return;
            TouchScreenKeyboard.Status status = _keyboard.status;
            if (status == TouchScreenKeyboard.Status.Visible) return;
            if (status == TouchScreenKeyboard.Status.Done || status == TouchScreenKeyboard.Status.LostFocus)
            {
                _committedField = _keyboardField;
                _committedText = _keyboard.text;
                _committedSubmit = status == TouchScreenKeyboard.Status.Done;
            }
            _keyboard = null;
            _keyboardField = null;
        }

        void CloseKeyboard()
        {
            if (_keyboard != null) _keyboard.active = false;
            _keyboard = null;
            _keyboardField = null;
        }

        // ---- touch scrolling -----------------------------------------------------------------

        sealed class ScrollState
        {
            public float position;
            public float content;
            public bool pressed, dragging;
            public Vector2 pressedAt;
        }

        readonly Dictionary<string, ScrollState> _scrolls = new Dictionary<string, ScrollState>();

        ScrollState Scroll(string id)
        {
            ScrollState state;
            if (!_scrolls.TryGetValue(id, out state))
            {
                state = new ScrollState();
                _scrolls[id] = state;
            }
            return state;
        }

        /// <summary>
        /// Begins a clipped area that scrolls when dragged with a finger or the mouse wheel. IMGUI scroll views only
        /// move by their scroll bars, so the drag is handled here; a drag that starts on a button cancels the button.
        /// Content is laid out in local coordinates starting at (0, 0). Pair with <see cref="EndScroll"/>.
        /// </summary>
        ScrollState BeginScroll(string id, Rect view, bool horizontal)
        {
            ScrollState state = Scroll(id);
            float viewport = horizontal ? view.width : view.height;
            float max = Mathf.Max(0f, state.content - viewport);
            Event e = Event.current;

            switch (e.rawType)
            {
                case EventType.MouseDown:
                    if (view.Contains(e.mousePosition))
                    {
                        state.pressed = true;
                        state.dragging = false;
                        state.pressedAt = e.mousePosition;
                    }
                    break;
                case EventType.MouseDrag:
                    if (state.pressed)
                    {
                        Vector2 travelled = e.mousePosition - state.pressedAt;
                        float along = horizontal ? travelled.x : travelled.y;
                        float across = horizontal ? travelled.y : travelled.x;
                        if (!state.dragging && Mathf.Abs(along) > P(8) && Mathf.Abs(along) > Mathf.Abs(across))
                        {
                            state.dragging = true;
                            // Whatever was pressed under the finger is no longer being clicked.
                            GUIUtility.hotControl = 0;
                        }
                        if (state.dragging)
                        {
                            state.position -= horizontal ? e.delta.x : e.delta.y;
                            if (e.type != EventType.Used) e.Use();
                        }
                    }
                    break;
                case EventType.MouseUp:
                    state.pressed = false;
                    if (state.dragging)
                    {
                        state.dragging = false;
                        if (e.type != EventType.Used) e.Use();
                    }
                    break;
                case EventType.ScrollWheel:
                    if (view.Contains(e.mousePosition))
                    {
                        state.position += (horizontal ? e.delta.x + e.delta.y : e.delta.y) * P(18);
                        if (e.type != EventType.Used) e.Use();
                    }
                    break;
            }
            state.position = Mathf.Clamp(state.position, 0f, max);

            Rect content = horizontal
                ? new Rect(0, 0, Mathf.Max(view.width, state.content), view.height)
                : new Rect(0, 0, view.width, Mathf.Max(view.height, state.content));
            Vector2 offset = horizontal ? new Vector2(state.position, 0) : new Vector2(0, state.position);
            GUI.BeginScrollView(view, offset, content, false, false, GUIStyle.none, GUIStyle.none);
            return state;
        }

        /// <summary>Ends the area and records how big the content turned out to be, for clamping next frame.</summary>
        void EndScroll(ScrollState state, float contentExtent)
        {
            GUI.EndScrollView(false);
            state.content = contentExtent;
        }

        // ---- small helpers -------------------------------------------------------------------

        void Fill(Rect rect, GUIStyle style)
        {
            if (Event.current.type == EventType.Repaint) style.Draw(rect, GUIContent.none, false, false, false, false);
        }

        float WidthOf(GUIStyle style, string text) => style.CalcSize(new GUIContent(text)).x;

        static string Truncate(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? (s ?? "") : s.Substring(0, max - 1) + "…";
    }
}
