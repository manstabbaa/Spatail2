using System;
using System.Collections.Generic;
using Spatail.Core;
using UnityEngine;

namespace Spatail
{
    /// <summary>Typed input: a system keyboard, a physical keyboard, or anything else that produces text.</summary>
    public interface ITextEntry
    {
        bool IsOpen { get; }
        void Open(string initial, Action<string> onChanged, Action<string> onSubmit, Action onCancel);
        void Close();
    }

    /// <summary>Speech-to-text. Optional; platforms without it return null and the prompt bar falls back to typing.</summary>
    public interface ISpeechInput
    {
        bool IsAvailable { get; }
        bool IsListening { get; }
        /// <summary><paramref name="onResult"/> receives (transcript so far, isFinal).</summary>
        void StartListening(Action<string, bool> onResult, Action<string> onError);
        void StopListening();
    }

    /// <summary>Everything a platform adapter hands to <see cref="SpatailApp"/> at launch.</summary>
    public sealed class PlatformServices
    {
        /// <summary>The camera that follows the user's head (or the phone).</summary>
        public Camera Head;
        /// <summary>Live sensing of the place: ray casts, the camera frame, surfaces noticed so far.</summary>
        public ISpaceSensor Sensor;
        public List<PointerSource> Pointers = new List<PointerSource>();
        public ITextEntry TextEntry;
        public ISpeechInput Speech;
        /// <summary>"quest", "ios" or "sim". Sent to session peers and the planner log.</summary>
        public string DeviceName = "device";
        /// <summary>Headsets get the floating prompt bar; handhelds bring their own screen UI.</summary>
        public bool WorldUi = true;
        /// <summary>Called every frame while the user is repositioning the station; return degrees of yaw to apply.</summary>
        public Func<float> StationRotateInput;
    }
}
