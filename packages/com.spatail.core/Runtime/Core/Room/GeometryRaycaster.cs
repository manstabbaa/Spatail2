using System;
using System.Collections.Generic;

namespace Spatail.Core
{
    /// <summary>
    /// Ray casting against things known by their bounds: detected planes, furniture boxes and virtual props.
    /// Devices combine this with their live depth ray cast (and take whichever hit is nearer), so programs can
    /// point at stand-in holograms as well as at the real world. The simulator and tests use it on its own.
    /// </summary>
    public static class GeometryRaycaster
    {
        public static bool Raycast(IEnumerable<RoomEntity> entities, Vec3 origin, Vec3 direction, float maxDistance, out RayHit hit)
        {
            hit = default(RayHit);
            float best = maxDistance;
            bool found = false;
            direction = direction.Normalized;
            if (entities == null) return false;

            foreach (RoomEntity e in entities)
            {
                // A machine's overall bounds would shadow its parts; the parts are what can be pointed at.
                if (e == null || e.kind == EntityKind.Equipment) continue;
                float t;
                Vec3 normal;
                bool ok = EntityKind.IsSurface(e.kind) ? Plane(e, origin, direction, out t, out normal) : Box(e, origin, direction, out t, out normal);
                if (!ok || t < 1e-4f || t >= best) continue;
                best = t;
                found = true;
                hit.position = origin + direction * t;
                hit.normal = normal;
                hit.distance = t;
                hit.label = e.label;
            }
            return found;
        }

        /// <summary>Bounded plane: the floor and ceiling lie in the entity's XZ plane, walls in its XY plane.</summary>
        static bool Plane(RoomEntity e, Vec3 origin, Vec3 direction, out float t, out Vec3 normal)
        {
            t = 0f;
            bool wall = e.kind == EntityKind.Wall;
            Vec3 localNormal = wall ? Vec3.Forward : (e.kind == EntityKind.Ceiling ? new Vec3(0, -1, 0) : Vec3.Up);
            normal = e.rotation * localNormal;
            float denominator = Vec3.Dot(direction, normal);
            // Only the side the normal faces is solid, so content is not stopped by the back of a wall.
            if (denominator > -1e-5f) return false;
            t = Vec3.Dot(e.position - origin, normal) / denominator;
            if (t < 0f) return false;
            Vec3 local = e.rotation.Inverse * (origin + direction * t - e.position);
            float halfX = e.size.x * 0.5f;
            float halfOther = (wall ? e.size.y : e.size.z) * 0.5f;
            float other = wall ? local.y : local.z;
            return Math.Abs(local.x) <= halfX + 1e-3f && Math.Abs(other) <= halfOther + 1e-3f;
        }

        /// <summary>Oriented box (slab method).</summary>
        static bool Box(RoomEntity e, Vec3 origin, Vec3 direction, out float t, out Vec3 normal)
        {
            t = 0f;
            normal = Vec3.Up;
            if (e.size.SqrLength < 1e-8f) return false;
            Quat inverse = e.rotation.Inverse;
            Vec3 o = inverse * (origin - e.position);
            Vec3 d = inverse * direction;
            float[] oo = { o.x, o.y, o.z }, dd = { d.x, d.y, d.z };
            float[] half = { e.size.x * 0.5f, e.size.y * 0.5f, e.size.z * 0.5f };
            float tMin = float.NegativeInfinity, tMax = float.PositiveInfinity;
            int axis = -1;
            float sign = 1f;
            for (int i = 0; i < 3; i++)
            {
                if (Math.Abs(dd[i]) < 1e-7f)
                {
                    if (Math.Abs(oo[i]) > half[i]) return false;
                    continue;
                }
                float t1 = (-half[i] - oo[i]) / dd[i], t2 = (half[i] - oo[i]) / dd[i];
                float near = Math.Min(t1, t2), far = Math.Max(t1, t2);
                if (near > tMin) { tMin = near; axis = i; sign = dd[i] > 0 ? -1f : 1f; }
                if (far < tMax) tMax = far;
                if (tMin > tMax) return false;
            }
            if (tMax < 0f || axis < 0 || tMin < 0f) return false;   // behind, or the origin is inside the box
            t = tMin;
            Vec3 localNormal = axis == 0 ? new Vec3(sign, 0, 0) : (axis == 1 ? new Vec3(0, sign, 0) : new Vec3(0, 0, sign));
            normal = e.rotation * localNormal;
            return true;
        }
    }
}
