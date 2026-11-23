using System;
using System.Collections.Generic;
using UnityEngine;

namespace Spatail.DevTool
{
    /// <summary>
    /// A finger on the screen as a Spatail pointer: a ray from the camera through the first touch.
    ///
    /// Normally the pointer is "pressed" while the finger is down, and keeps its ray for the frame the finger lifts so
    /// a tap registers as a click on whatever was under it. While the pump is being moved the meaning flips: dragging
    /// steers the pump across the floor without pressing, and lifting the finger is the press that drops it.
    /// Touches that start on the dev tool UI never reach the scene.
    /// </summary>
    public sealed class TouchPointer : PointerSource
    {
        public Camera Camera;
        public Func<Vector2, bool> IsOverUi;
        public Func<bool> IsMoveMode;

        int _frame = -1;
        int _activeId = -1;
        bool _ignored;
        bool _hasRay, _pressed;
        Ray _ray;

        // Two-finger twist tracking.
        int _twistFrame = -1;
        int _twistA = -1, _twistB = -1;
        float _twistAngle, _twistDelta;

        void Refresh()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            _hasRay = false;
            _pressed = false;
            if (Camera == null) return;

            List<TouchPoint> touches = InputBridge.Touches;
            bool found = false;
            TouchPoint active = default(TouchPoint);
            for (int i = 0; i < touches.Count; i++)
            {
                TouchPoint t = touches[i];
                if (_activeId == -1 && !t.ended)
                {
                    // Adopt the first finger that goes down; decide once whether it belongs to the UI.
                    _activeId = t.id;
                    _ignored = IsOverUi != null && IsOverUi(t.position);
                }
                if (t.id == _activeId)
                {
                    active = t;
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                _activeId = -1;
                return;
            }

            if (!_ignored)
            {
                _ray = Camera.ScreenPointToRay(active.position);
                _hasRay = true;
                bool moving = IsMoveMode != null && IsMoveMode();
                _pressed = moving ? active.ended : !active.ended;
            }
            if (active.ended) _activeId = -1;
        }

        public override bool TryGetRay(out Ray ray)
        {
            Refresh();
            ray = _ray;
            return _hasRay;
        }

        public override bool IsPressed
        {
            get
            {
                Refresh();
                return _pressed;
            }
        }

        /// <summary>
        /// Degrees the pump should turn this frame from a two-finger twist. Twisting clockwise on screen turns the
        /// pump clockwise as seen from above.
        /// </summary>
        public float TwistDegrees()
        {
            if (_twistFrame == Time.frameCount) return _twistDelta;
            _twistFrame = Time.frameCount;
            _twistDelta = 0f;

            List<TouchPoint> touches = InputBridge.Touches;
            if (touches.Count < 2 || touches[0].ended || touches[1].ended)
            {
                _twistA = _twistB = -1;
                return 0f;
            }
            Vector2 between = touches[1].position - touches[0].position;
            if (between.sqrMagnitude < 400f) return 0f; // fingers too close together to read an angle
            float angle = Mathf.Atan2(between.y, between.x) * Mathf.Rad2Deg;
            if (touches[0].id == _twistA && touches[1].id == _twistB)
                _twistDelta = -Mathf.DeltaAngle(_twistAngle, angle);
            _twistA = touches[0].id;
            _twistB = touches[1].id;
            _twistAngle = angle;
            return _twistDelta;
        }
    }
}
