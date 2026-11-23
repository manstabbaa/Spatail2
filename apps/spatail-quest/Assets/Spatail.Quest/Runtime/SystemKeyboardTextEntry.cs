using System;
using UnityEngine;

namespace Spatail.Quest
{
    /// <summary>
    /// Typed input through the headset's system keyboard overlay (which also offers the system's own dictation key).
    /// Requires "Requires System Keyboard" in the Meta project config; the project setup turns it on.
    /// </summary>
    public sealed class SystemKeyboardTextEntry : MonoBehaviour, ITextEntry
    {
        TouchScreenKeyboard _keyboard;
        string _last = "";
        Action<string> _onChanged, _onSubmit;
        Action _onCancel;

        public bool IsOpen => _keyboard != null;

        public void Open(string initial, Action<string> onChanged, Action<string> onSubmit, Action onCancel)
        {
            Close();
            _onChanged = onChanged;
            _onSubmit = onSubmit;
            _onCancel = onCancel;
            _last = initial ?? "";
            _keyboard = TouchScreenKeyboard.Open(_last, TouchScreenKeyboardType.Default, false, false, false, false, "Just say what you need");
            if (_keyboard == null && onCancel != null) onCancel();
        }

        public void Close()
        {
            if (_keyboard == null) return;
            _keyboard.active = false;
            _keyboard = null;
        }

        void Update()
        {
            if (_keyboard == null) return;

            string text = _keyboard.text ?? "";
            if (text != _last)
            {
                _last = text;
                if (_onChanged != null) _onChanged(text);
            }

            switch (_keyboard.status)
            {
                case TouchScreenKeyboard.Status.Done:
                {
                    _keyboard = null;
                    Action<string> submit = _onSubmit;
                    if (submit != null) submit(text);
                    break;
                }
                case TouchScreenKeyboard.Status.Canceled:
                case TouchScreenKeyboard.Status.LostFocus:
                {
                    _keyboard = null;
                    Action cancel = _onCancel;
                    if (cancel != null) cancel();
                    break;
                }
            }
        }
    }
}
