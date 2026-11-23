using System;

namespace Spatail.Core
{
    /// <summary>
    /// Engine-independent rotation. Component layout and multiplication order match UnityEngine.Quaternion,
    /// so values can be copied across without conversion.
    /// </summary>
    [Serializable]
    public struct Quat : IEquatable<Quat>
    {
        public float x, y, z, w;

        public Quat(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }

        public static readonly Quat Identity = new Quat(0, 0, 0, 1);

        const float Deg2Rad = (float)(Math.PI / 180.0);
        const float Rad2Deg = (float)(180.0 / Math.PI);

        public static Quat AngleAxis(float degrees, Vec3 axis)
        {
            axis = axis.Normalized;
            float h = degrees * Deg2Rad * 0.5f;
            float s = (float)Math.Sin(h);
            return new Quat(axis.x * s, axis.y * s, axis.z * s, (float)Math.Cos(h));
        }

        /// <summary>Rotation about the vertical axis. Positive yaw turns +Z toward +X (Unity convention).</summary>
        public static Quat Yaw(float degrees) => AngleAxis(degrees, Vec3.Up);

        /// <summary>Unity Euler order: Z first, then X, then Y.</summary>
        public static Quat Euler(float xDeg, float yDeg, float zDeg) =>
            AngleAxis(yDeg, Vec3.Up) * AngleAxis(xDeg, Vec3.Right) * AngleAxis(zDeg, Vec3.Forward);

        public static Quat Euler(Vec3 e) => Euler(e.x, e.y, e.z);

        public static Quat operator *(Quat a, Quat b) => new Quat(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y + a.y * b.w + a.z * b.x - a.x * b.z,
            a.w * b.z + a.z * b.w + a.x * b.y - a.y * b.x,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);

        public static Vec3 operator *(Quat q, Vec3 v)
        {
            float x2 = q.x * 2f, y2 = q.y * 2f, z2 = q.z * 2f;
            float xx = q.x * x2, yy = q.y * y2, zz = q.z * z2;
            float xy = q.x * y2, xz = q.x * z2, yz = q.y * z2;
            float wx = q.w * x2, wy = q.w * y2, wz = q.w * z2;
            return new Vec3(
                (1f - (yy + zz)) * v.x + (xy - wz) * v.y + (xz + wy) * v.z,
                (xy + wz) * v.x + (1f - (xx + zz)) * v.y + (yz - wx) * v.z,
                (xz - wy) * v.x + (yz + wx) * v.y + (1f - (xx + yy)) * v.z);
        }

        public Quat Inverse => new Quat(-x, -y, -z, w);

        public Vec3 Forward => this * Vec3.Forward;
        public Vec3 Up => this * Vec3.Up;
        public Vec3 Right => this * Vec3.Right;

        /// <summary>Heading in degrees of this rotation's forward vector projected on the floor.</summary>
        public float YawDegrees
        {
            get
            {
                Vec3 f = Forward;
                return (float)Math.Atan2(f.x, f.z) * Rad2Deg;
            }
        }

        /// <summary>Rotation whose +Z looks along <paramref name="forward"/> with +Y as close to <paramref name="up"/> as possible.</summary>
        public static Quat LookRotation(Vec3 forward, Vec3 up)
        {
            Vec3 f = forward.Normalized;
            if (f.SqrLength < 1e-10f) return Identity;
            Vec3 r = Vec3.Cross(up, f);
            if (r.SqrLength < 1e-10f) r = Vec3.Cross(Math.Abs(f.y) > 0.9f ? Vec3.Forward : Vec3.Up, f);
            r = r.Normalized;
            Vec3 u = Vec3.Cross(f, r);
            return FromBasis(r, u, f);
        }

        public static Quat LookRotation(Vec3 forward) => LookRotation(forward, Vec3.Up);

        /// <summary>Upright rotation (yaw only) facing from <paramref name="from"/> toward <paramref name="to"/>.</summary>
        public static Quat FaceFlat(Vec3 from, Vec3 to)
        {
            Vec3 d = (to - from).Flat;
            return d.SqrLength < 1e-8f ? Identity : LookRotation(d.Normalized, Vec3.Up);
        }

        /// <summary>Shortest rotation taking direction <paramref name="from"/> to direction <paramref name="to"/>.</summary>
        public static Quat FromTo(Vec3 from, Vec3 to)
        {
            Vec3 a = from.Normalized, b = to.Normalized;
            float d = Vec3.Dot(a, b);
            if (d > 0.99999f) return Identity;
            if (d < -0.99999f)
            {
                Vec3 axis = Vec3.Cross(Vec3.Right, a);
                if (axis.SqrLength < 1e-6f) axis = Vec3.Cross(Vec3.Up, a);
                return AngleAxis(180f, axis);
            }
            Vec3 c = Vec3.Cross(a, b);
            var q = new Quat(c.x, c.y, c.z, 1f + d);
            float l = (float)Math.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return new Quat(q.x / l, q.y / l, q.z / l, q.w / l);
        }

        static Quat FromBasis(Vec3 r, Vec3 u, Vec3 f)
        {
            float m00 = r.x, m01 = u.x, m02 = f.x;
            float m10 = r.y, m11 = u.y, m12 = f.y;
            float m20 = r.z, m21 = u.z, m22 = f.z;
            float tr = m00 + m11 + m22;
            if (tr > 0f)
            {
                float s = (float)Math.Sqrt(tr + 1f) * 2f;
                return new Quat((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25f * s);
            }
            if (m00 > m11 && m00 > m22)
            {
                float s = (float)Math.Sqrt(1f + m00 - m11 - m22) * 2f;
                return new Quat(0.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s);
            }
            if (m11 > m22)
            {
                float s = (float)Math.Sqrt(1f + m11 - m00 - m22) * 2f;
                return new Quat((m01 + m10) / s, 0.25f * s, (m12 + m21) / s, (m02 - m20) / s);
            }
            {
                float s = (float)Math.Sqrt(1f + m22 - m00 - m11) * 2f;
                return new Quat((m02 + m20) / s, (m12 + m21) / s, 0.25f * s, (m10 - m01) / s);
            }
        }

        public bool Equals(Quat o) => x == o.x && y == o.y && z == o.z && w == o.w;
        public override bool Equals(object obj) => obj is Quat o && Equals(o);
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2) ^ (w.GetHashCode() >> 1);
        public override string ToString() => "(" + x.ToString("0.###") + ", " + y.ToString("0.###") + ", " + z.ToString("0.###") + ", " + w.ToString("0.###") + ")";
    }

    /// <summary>Position plus rotation.</summary>
    [Serializable]
    public struct Pose3
    {
        public Vec3 position;
        public Quat rotation;

        public Pose3(Vec3 position, Quat rotation) { this.position = position; this.rotation = rotation; }

        public static readonly Pose3 Identity = new Pose3(Vec3.Zero, Quat.Identity);

        public Vec3 TransformPoint(Vec3 local) => position + rotation * local;
        public Vec3 InverseTransformPoint(Vec3 world) => rotation.Inverse * (world - position);
        public Vec3 TransformDirection(Vec3 local) => rotation * local;
        public Vec3 InverseTransformDirection(Vec3 world) => rotation.Inverse * world;

        /// <summary>Compose: the returned pose is <paramref name="local"/> expressed in this pose's parent space.</summary>
        public Pose3 Transform(Pose3 local) => new Pose3(TransformPoint(local.position), rotation * local.rotation);
    }
}
