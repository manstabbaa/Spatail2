using System;
using System.Collections.Generic;

namespace Spatail.Core
{
    /// <summary>
    /// How a device senses the place it is in, live. One implementation per platform: depth and the passthrough
    /// camera on a headset, ARKit on a phone, a synthetic room in the simulator.
    ///
    /// Nothing here assumes the place was scanned or described beforehand. A sensor answers "what is along this
    /// ray right now" and "what does the camera see right now"; the list of surfaces is whatever has been noticed
    /// so far and may be empty.
    /// </summary>
    public interface ISpaceSensor
    {
        /// <summary>True once tracking is running and rays can be cast.</summary>
        bool IsReady { get; }
        /// <summary>One line for the user or the dev tool.</summary>
        string Status { get; }
        bool Raycast(Vec3 origin, Vec3 direction, float maxDistance, out RayHit hit);
        /// <summary>Best current estimate of the floor height under the user.</summary>
        float FloorY { get; }
        IReadOnlyList<RoomEntity> Surfaces { get; }
        /// <summary>
        /// Captures what the camera sees now. Calls back on the main thread with the frame's pose and projection
        /// and its JPEG bytes; either may be null when the device cannot provide it.
        /// </summary>
        void Capture(Action<ViewFrame, byte[]> done);
        /// <summary>Raised when the surface list or the floor estimate changed materially.</summary>
        event Action Changed;
    }
}
