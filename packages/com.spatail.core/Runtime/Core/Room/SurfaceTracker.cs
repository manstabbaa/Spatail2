using System;
using System.Collections.Generic;

namespace Spatail.Core
{
    /// <summary>
    /// Finds floors, walls and table tops as the user looks around, from nothing but points where rays met the
    /// world. Devices that can cast into live depth but have no plane detection of their own (a headset that has
    /// not been through a room scan) feed it a sparse grid of hits a few times a second; it grows flat patches out
    /// of the samples that agree with each other.
    ///
    /// It starts empty and only ever knows what has been looked at. That is the intended behaviour: the place is
    /// understood as it is encountered, not ahead of time.
    /// </summary>
    public sealed class SurfaceTracker
    {
        sealed class Patch
        {
            public bool horizontal;
            public Vec3 normal;         // unit; for walls, horizontal and facing the viewer
            public float offset;        // height for horizontal patches, dot(normal, p) for walls
            public float minA, maxA;    // extent along X (horizontal) or along the wall's tangent
            public float minB, maxB;    // extent along Z (horizontal) or along Y (wall)
            public int samples;
            public string id;
            public float reportedArea;
            public bool reported;
            public Vec3 Tangent => Vec3.Cross(Vec3.Up, normal).Normalized;
            public float Area => Math.Max(0, maxA - minA) * Math.Max(0, maxB - minB);
        }

        readonly List<Patch> _patches = new List<Patch>();
        readonly List<RoomEntity> _surfaces = new List<RoomEntity>();
        int _walls, _tables;
        bool _dirty;
        float _floorHint;
        bool _hasFloorHint;

        /// <summary>Samples needed before a patch counts as a surface.</summary>
        public int MinSamples = 6;
        /// <summary>Smallest extent (metres) in both directions before a patch counts.</summary>
        public float MinExtent = 0.35f;
        /// <summary>How far a sample may be from a patch's edge and still extend it.</summary>
        public float JoinDistance = 0.7f;

        public event Action Changed;

        public IReadOnlyList<RoomEntity> Surfaces
        {
            get
            {
                if (_dirty) Rebuild();
                return _surfaces;
            }
        }

        /// <summary>The lowest floor-like patch, or the hint from the device's tracking origin until one is found.</summary>
        public float FloorY
        {
            get
            {
                Patch floor = Floor();
                return floor != null ? floor.offset : _floorHint;
            }
        }

        public bool HasFloor => Floor() != null;

        /// <summary>The device's own idea of floor height (for example a floor-level tracking origin).</summary>
        public void SetFloorHint(float y)
        {
            _floorHint = y;
            _hasFloorHint = true;
        }

        public void Clear()
        {
            _patches.Clear();
            _walls = _tables = 0;
            _dirty = true;
        }

        /// <summary>One point where a ray met the world, with the surface normal there.</summary>
        public void AddSample(Vec3 position, Vec3 normal)
        {
            normal = normal.Normalized;
            if (normal.SqrLength < 0.5f) return;
            bool up = normal.y > 0.85f;
            bool vertical = Math.Abs(normal.y) < 0.3f;
            if (!up && !vertical) return;   // slopes and ceilings are not tracked

            Patch patch = up ? JoinHorizontal(position) : JoinVertical(position, normal.Flat.Normalized);
            patch.samples++;
            Absorb(patch);

            bool qualifies = patch.samples >= MinSamples && patch.maxA - patch.minA >= MinExtent && patch.maxB - patch.minB >= MinExtent;
            if (!qualifies) return;
            if (!patch.reported)
            {
                patch.reported = true;
                patch.reportedArea = patch.Area;
                Notify();
            }
            else if (patch.Area > patch.reportedArea * 1.3f + 0.05f)
            {
                patch.reportedArea = patch.Area;
                Notify();
            }
        }

        void Notify()
        {
            _dirty = true;
            if (Changed != null) Changed();
        }

        /// <summary>
        /// Two patches of the same plane that have grown close to each other are one surface seen from two places.
        /// </summary>
        void Absorb(Patch patch)
        {
            for (int i = _patches.Count - 1; i >= 0; i--)
            {
                Patch other = _patches[i];
                if (other == patch || other.horizontal != patch.horizontal) continue;
                if (patch.horizontal)
                {
                    if (Math.Abs(other.offset - patch.offset) > 0.06f) continue;
                }
                else
                {
                    if (Vec3.Dot(other.normal, patch.normal) < 0.97f || Math.Abs(other.offset - patch.offset) > 0.08f) continue;
                }
                // Both extents are in the same frame: X/Z for horizontal patches, tangent/Y for walls on one plane.
                float otherMinA = other.minA, otherMaxA = other.maxA;
                if (!patch.horizontal && Vec3.Dot(other.Tangent, patch.Tangent) < 0)
                {
                    otherMinA = -other.maxA;
                    otherMaxA = -other.minA;
                }
                bool apartA = otherMinA > patch.maxA + JoinDistance || otherMaxA < patch.minA - JoinDistance;
                bool apartB = patch.horizontal && (other.minB > patch.maxB + JoinDistance || other.maxB < patch.minB - JoinDistance);
                if (apartA || apartB) continue;

                patch.offset = (patch.offset * patch.samples + other.offset * other.samples) / Math.Max(1, patch.samples + other.samples);
                patch.minA = Math.Min(patch.minA, otherMinA); patch.maxA = Math.Max(patch.maxA, otherMaxA);
                patch.minB = Math.Min(patch.minB, other.minB); patch.maxB = Math.Max(patch.maxB, other.maxB);
                patch.samples += other.samples;
                // Keep the identity people may already be anchored to.
                if (other.reported && (!patch.reported || other.reportedArea > patch.reportedArea))
                {
                    patch.id = other.id ?? patch.id;
                    patch.reported = true;
                    patch.reportedArea = Math.Max(patch.reportedArea, other.reportedArea);
                }
                _patches.RemoveAt(i);
                _dirty = true;
            }
        }

        Patch JoinHorizontal(Vec3 p)
        {
            foreach (Patch patch in _patches)
            {
                if (!patch.horizontal || Math.Abs(p.y - patch.offset) > 0.06f) continue;
                if (p.x < patch.minA - JoinDistance || p.x > patch.maxA + JoinDistance || p.z < patch.minB - JoinDistance || p.z > patch.maxB + JoinDistance) continue;
                patch.offset += (p.y - patch.offset) / (patch.samples + 1);
                patch.minA = Math.Min(patch.minA, p.x); patch.maxA = Math.Max(patch.maxA, p.x);
                patch.minB = Math.Min(patch.minB, p.z); patch.maxB = Math.Max(patch.maxB, p.z);
                return patch;
            }
            var created = new Patch { horizontal = true, normal = Vec3.Up, offset = p.y, minA = p.x, maxA = p.x, minB = p.z, maxB = p.z };
            _patches.Add(created);
            return created;
        }

        Patch JoinVertical(Vec3 p, Vec3 normal)
        {
            foreach (Patch patch in _patches)
            {
                if (patch.horizontal || Vec3.Dot(patch.normal, normal) < 0.97f) continue;
                if (Math.Abs(Vec3.Dot(patch.normal, p) - patch.offset) > 0.08f) continue;
                float a = Vec3.Dot(patch.Tangent, p);
                if (a < patch.minA - JoinDistance || a > patch.maxA + JoinDistance) continue;
                patch.offset += (Vec3.Dot(patch.normal, p) - patch.offset) / (patch.samples + 1);
                patch.minA = Math.Min(patch.minA, a); patch.maxA = Math.Max(patch.maxA, a);
                patch.minB = Math.Min(patch.minB, p.y); patch.maxB = Math.Max(patch.maxB, p.y);
                return patch;
            }
            var created = new Patch { horizontal = false, normal = normal, offset = Vec3.Dot(normal, p), minB = p.y, maxB = p.y };
            created.minA = created.maxA = Vec3.Dot(created.Tangent, p);
            _patches.Add(created);
            return created;
        }

        Patch Floor()
        {
            Patch best = null;
            foreach (Patch patch in _patches)
            {
                if (!patch.horizontal || !patch.reported) continue;
                // With a hint from the device, anything well above it is furniture, not floor.
                if (_hasFloorHint && patch.offset > _floorHint + 0.3f) continue;
                if (best == null || patch.offset < best.offset - 0.05f || (Math.Abs(patch.offset - best.offset) <= 0.05f && patch.Area > best.Area)) best = patch;
            }
            return best;
        }

        void Rebuild()
        {
            _dirty = false;
            _surfaces.Clear();
            Patch floor = Floor();
            float floorY = floor != null ? floor.offset : _floorHint;

            foreach (Patch patch in _patches)
            {
                if (!patch.reported) continue;
                if (patch.horizontal)
                {
                    bool isFloor = patch == floor || Math.Abs(patch.offset - floorY) < 0.1f;
                    float height = patch.offset - floorY;
                    if (!isFloor && (height < 0.3f || height > 1.4f)) continue;   // steps, shelves and the like are left alone
                    var centre = new Vec3((patch.minA + patch.maxA) * 0.5f, patch.offset, (patch.minB + patch.maxB) * 0.5f);
                    float width = patch.maxA - patch.minA, depth = patch.maxB - patch.minB;
                    if (isFloor)
                    {
                        if (patch.id == null) patch.id = patch == floor ? "floor" : "floor-" + (_surfaces.Count + 1);
                        _surfaces.Add(new RoomEntity { id = patch.id, kind = EntityKind.Floor, label = "Floor", position = centre, size = new Vec3(width, 0, depth) });
                    }
                    else
                    {
                        if (patch.id == null || !patch.id.StartsWith("table")) patch.id = "table-" + (++_tables);
                        // A table is a volume from the floor to its top.
                        _surfaces.Add(new RoomEntity
                        {
                            id = patch.id, kind = EntityKind.Table, label = "Table",
                            position = new Vec3(centre.x, floorY + height * 0.5f, centre.z), size = new Vec3(width, height, depth),
                        });
                    }
                }
                else
                {
                    if (patch.id == null) patch.id = "wall-" + (++_walls);
                    Vec3 tangent = patch.Tangent;
                    float a = (patch.minA + patch.maxA) * 0.5f, y = (patch.minB + patch.maxB) * 0.5f;
                    Vec3 centre = patch.normal * patch.offset + tangent * a + new Vec3(0, y, 0);
                    _surfaces.Add(new RoomEntity
                    {
                        id = patch.id, kind = EntityKind.Wall, label = "Wall",
                        position = centre, rotation = Quat.LookRotation(patch.normal, Vec3.Up),
                        size = new Vec3(patch.maxA - patch.minA, patch.maxB - patch.minB, 0.02f),
                    });
                }
            }
        }
    }
}
