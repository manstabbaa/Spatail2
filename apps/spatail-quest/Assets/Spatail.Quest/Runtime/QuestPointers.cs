using UnityEngine;

namespace Spatail.Quest
{
    /// <summary>A Touch controller as a pointer: the ray leaves along the controller, the index trigger presses.</summary>
    public sealed class ControllerPointer : PointerSource
    {
        public OVRCameraRig Rig;
        public OVRInput.Controller Controller = OVRInput.Controller.RTouch;

        public override bool ShowRay => true;

        bool IsLeft => Controller == OVRInput.Controller.LTouch;

        public override bool TryGetRay(out Ray ray)
        {
            ray = default(Ray);
            if (Rig == null) return false;
            if (!OVRInput.IsControllerConnected(Controller) || !OVRInput.GetControllerPositionTracked(Controller)) return false;
            // With hands and controllers both enabled, a controller lying on the desk should not point at anything.
            OVRInput.Hand hand = IsLeft ? OVRInput.Hand.HandLeft : OVRInput.Hand.HandRight;
            if (OVRInput.GetControllerIsInHandState(hand) == OVRInput.ControllerInHandState.ControllerNotInHand) return false;

            Transform anchor = IsLeft ? Rig.leftControllerAnchor : Rig.rightControllerAnchor;
            if (anchor == null) return false;
            ray = new Ray(anchor.position, anchor.forward);
            return true;
        }

        public override bool IsPressed => OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, Controller);
    }

    /// <summary>
    /// A tracked hand as a pointer: the system's pointer pose gives the ray, an index pinch presses.
    /// Reads hand state straight from OVRPlugin, the same way OVRHand does, because OVRHand's hand type can only be
    /// assigned in the Inspector and this rig is built in code.
    /// </summary>
    public sealed class HandPointer : PointerSource
    {
        public OVRCameraRig Rig;
        public OVRPlugin.Hand Hand = OVRPlugin.Hand.HandRight;

        OVRPlugin.HandState _state = new OVRPlugin.HandState();
        int _frame = -1;
        bool _valid, _pinching;
        Ray _ray;

        public override bool ShowRay => true;

        void Refresh()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            _valid = false;
            _pinching = false;
            if (Rig == null || Rig.trackingSpace == null) return;
            if (!OVRPlugin.GetHandState(OVRPlugin.Step.Render, Hand, ref _state)) return;

            bool tracked = (_state.Status & OVRPlugin.HandStatus.HandTracked) != 0;
            bool inputValid = (_state.Status & OVRPlugin.HandStatus.InputStateValid) != 0;
            bool systemGesture = (_state.Status & OVRPlugin.HandStatus.SystemGestureInProgress) != 0;
            if (!tracked || !inputValid || systemGesture) return;
            // A hand that is holding a controller points with the controller instead.
            if (OVRInput.GetControllerIsInHandState((OVRInput.Hand)Hand) == OVRInput.ControllerInHandState.ControllerInHand) return;

            // The pointer pose is reported in tracking space.
            Vector3 position = _state.PointerPose.Position.FromFlippedZVector3f();
            Quaternion rotation = _state.PointerPose.Orientation.FromFlippedZQuatf();
            Transform space = Rig.trackingSpace;
            _ray = new Ray(space.TransformPoint(position), space.rotation * rotation * Vector3.forward);
            _pinching = (_state.Pinches & OVRPlugin.HandFingerPinch.Index) != 0;
            _valid = true;
        }

        public override bool TryGetRay(out Ray ray)
        {
            Refresh();
            ray = _ray;
            return _valid;
        }

        public override bool IsPressed
        {
            get
            {
                Refresh();
                return _valid && _pinching;
            }
        }
    }
}
