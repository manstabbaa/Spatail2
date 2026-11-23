using Spatail.Core;
using UnityEngine;

namespace Spatail
{
    /// <summary>Bridges the engine-independent core types and Unity's. Layouts match, so these are plain copies.</summary>
    public static class Conversions
    {
        public static Vector3 ToUnity(this Vec3 v) => new Vector3(v.x, v.y, v.z);
        public static Vec3 ToCore(this Vector3 v) => new Vec3(v.x, v.y, v.z);
        public static Quaternion ToUnity(this Quat q) => new Quaternion(q.x, q.y, q.z, q.w);
        public static Quat ToCore(this Quaternion q) => new Quat(q.x, q.y, q.z, q.w);
        public static Pose ToUnity(this Pose3 p) => new Pose(p.position.ToUnity(), p.rotation.ToUnity());
        public static Pose3 ToCore(this Pose p) => new Pose3(p.position.ToCore(), p.rotation.ToCore());
    }
}
