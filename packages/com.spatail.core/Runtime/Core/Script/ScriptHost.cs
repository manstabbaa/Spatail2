using System;
using System.Collections.Generic;

namespace Spatail.Core
{
    /// <summary>Where a ray met the real world (or a stand-in for it).</summary>
    public struct RayHit
    {
        public Vec3 position;
        public Vec3 normal;
        public float distance;
        /// <summary>Optional name of what was hit, when the source knows it.</summary>
        public string label;
    }

    /// <summary>
    /// The camera frame that accompanied a request: where the camera was and how it projects. Programs written
    /// after looking at that frame refer to things by image position; this turns an image position back into a
    /// ray in the world, which is then cast into whatever geometry the device senses.
    /// </summary>
    public sealed class ViewFrame
    {
        public Vec3 position;
        public Quat rotation = Quat.Identity;
        /// <summary>Tangent of half the horizontal and vertical field of view.</summary>
        public float tanHalfX = 0.6f, tanHalfY = 0.45f;
        /// <summary>Principal point in normalised image coordinates (0.5, 0.5 for a centred lens).</summary>
        public float centerU = 0.5f, centerV = 0.5f;
        public int width, height;

        /// <summary>Ray through normalised image coordinates (u right, v down, origin top-left).</summary>
        public void Ray(float u, float v, out Vec3 origin, out Vec3 direction)
        {
            var local = new Vec3((u - centerU) * 2f * tanHalfX, -(v - centerV) * 2f * tanHalfY, 1f);
            origin = position;
            direction = (rotation * local).Normalized;
        }

        /// <summary>Where a world point falls in the image. False when it is behind the camera.</summary>
        public bool Project(Vec3 world, out float u, out float v)
        {
            Vec3 local = rotation.Inverse * (world - position);
            u = v = 0f;
            if (local.z <= 1e-4f) return false;
            u = centerU + local.x / local.z / (2f * tanHalfX);
            v = centerV - local.y / local.z / (2f * tanHalfY);
            return true;
        }

        public static ViewFrame FromFov(Vec3 position, Quat rotation, float verticalFovDegrees, float aspect, int width = 0, int height = 0)
        {
            float tanY = (float)Math.Tan(verticalFovDegrees * Math.PI / 360.0);
            return new ViewFrame { position = position, rotation = rotation, tanHalfY = tanY, tanHalfX = tanY * aspect, width = width, height = height };
        }
    }

    /// <summary>
    /// Everything a running program can do to the outside world. One implementation per host: Unity (headset and
    /// phone), the test harness, and in the browser lab the same contract is implemented in JavaScript.
    ///
    /// The surface is deliberately small and primitive. There is no notion of a "chart" or a "walkthrough" here:
    /// programs compose whatever they need from nodes.
    /// </summary>
    public interface IScriptHost
    {
        // ---- drawing -----------------------------------------------------------------------
        /// <summary>Kinds: group, box, sphere, cylinder, torus, disc, tube, mesh, text, panel.</summary>
        void CreateNode(int id, string kind, Dictionary<string, object> props);
        void UpdateNode(int id, Dictionary<string, object> props);
        void SetTransform(int id, Vec3 position, Quat rotation, Vec3 scale);
        void DestroyNode(int id);

        // ---- sensing the space, live -----------------------------------------------------------
        /// <summary>Casts into the geometry the device senses right now (depth, meshes, planes, stand-ins).</summary>
        bool Raycast(Vec3 origin, Vec3 direction, float maxDistance, out RayHit hit);
        float FloorY { get; }
        /// <summary>Flat surfaces found so far. May be empty: nothing is assumed to have been scanned beforehand.</summary>
        IReadOnlyList<RoomEntity> Surfaces { get; }
        /// <summary>Virtual objects the app has placed itself, such as a stand-in machine.</summary>
        IReadOnlyList<RoomEntity> Props { get; }
        void GetUser(out Vec3 position, out Vec3 forward, out Quat rotation);
        /// <summary>
        /// Looks at the scene again and locates what is described. Calls back on the main thread, possibly later,
        /// with hits ordered best first (empty when nothing was found).
        /// </summary>
        void Find(string description, Action<List<RayHit>> done);
    }
}
