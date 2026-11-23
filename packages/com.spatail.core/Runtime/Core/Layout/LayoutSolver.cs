using System;
using System.Collections.Generic;

namespace Spatail.Core
{
    /// <summary>Where an element ends up in the room.</summary>
    public struct Placement
    {
        /// <summary>False when the anchor entity is missing; the element should not be shown.</summary>
        public bool valid;
        public Vec3 position;
        /// <summary>Local +Z points away from the viewer, so text laid out in the XY plane reads correctly.</summary>
        public Quat rotation;
        /// <summary>The point on the anchor entity this element refers to. Leader lines end here.</summary>
        public Vec3 target;
        /// <summary>True when <see cref="position"/> is where the content's bottom edge sits rather than its centre.</summary>
        public bool pivotBottom;
        public RoomEntity entity;
    }

    /// <summary>
    /// Turns anchors ("on the floor near the workbench", "above the gate valve") into poses, using what is known about
    /// the room and where the user stands. This is the piece that makes content land somewhere sensible instead of
    /// being dropped in with no sense of the room.
    /// </summary>
    public static class LayoutSolver
    {
        public const float EyeHeight = 1.45f;
        const float Gap = 0.14f;

        public static Placement Place(Element e, RoomModel room)
        {
            var result = new Placement { rotation = Quat.Identity };
            string at = e.anchor.at ?? AnchorMode.Center;

            if (at == AnchorMode.User)
            {
                Vec3 f = room.userForward.Flat.Normalized;
                if (f.SqrLength < 1e-6f) f = Vec3.Forward;
                result.position = room.userPosition + f * 0.9f + new Vec3(0, -0.2f, 0) + Quat.LookRotation(f) * e.anchor.offset;
                result.rotation = FaceUser(result.position, room, 30f);
                result.target = result.position;
                result.valid = true;
                return result;
            }

            RoomEntity entity = room.Get(e.anchor.entity);
            if (entity == null) return result;
            result.entity = entity;
            result.valid = true;
            result.target = entity.position;

            Vec3 toUser = (room.userPosition - entity.position).Flat.Normalized;
            if (toUser.SqrLength < 1e-6f) toUser = -entity.Forward.Flat.Normalized;
            if (toUser.SqrLength < 1e-6f) toUser = new Vec3(0, 0, -1);
            Vec3 offsetWorld = entity.rotation * e.anchor.offset;

            switch (at)
            {
                case AnchorMode.Top:
                    result.position = entity.TopCenter + offsetWorld;
                    result.target = entity.TopCenter;
                    result.pivotBottom = true;
                    result.rotation = FaceUser(result.position, room, 0f);
                    break;

                case AnchorMode.Above:
                    result.position = entity.TopCenter + new Vec3(0, 0.3f, 0) + offsetWorld;
                    result.target = entity.TopCenter;
                    result.pivotBottom = true;
                    result.rotation = FaceUser(result.position, room, 20f);
                    break;

                case AnchorMode.Surface:
                    result.position = entity.TopCenter + offsetWorld;
                    result.target = entity.TopCenter;
                    result.pivotBottom = true;
                    result.rotation = FaceUser(result.position, room, 0f);
                    break;

                case AnchorMode.Callout:
                {
                    float reach = entity.ExtentAlong(toUser) + 0.26f;
                    Vec3 p = entity.position + toUser * reach;
                    // Sit below the part's centre so the card does not cover the thing it is about.
                    p.y = Clamp(entity.position.y - entity.size.y * 0.25f, room.floorY + 0.35f, room.floorY + 1.5f);
                    result.position = p + offsetWorld;
                    result.rotation = FaceUser(result.position, room, 35f);
                    break;
                }

                case AnchorMode.FloorNear:
                {
                    Vec3 footprint = Footprint(e);
                    result.position = FindFloorSpot(entity, room, toUser, footprint) + offsetWorld;
                    result.pivotBottom = true;
                    result.rotation = FaceUser(result.position, room, 0f);
                    break;
                }

                case AnchorMode.Wall:
                {
                    RoomEntity wall = NearestWall(room, entity.position);
                    if (wall == null)
                    {
                        // No wall known: fall back to floating above the entity.
                        result.position = entity.TopCenter + new Vec3(0, 0.3f, 0) + offsetWorld;
                        result.pivotBottom = true;
                        result.rotation = FaceUser(result.position, room, 20f);
                        break;
                    }
                    Vec3 local = wall.rotation.Inverse * (entity.position - wall.position);
                    float halfW = Math.Max(0, wall.size.x * 0.5f - 0.4f);
                    local.x = Clamp(local.x, -halfW, halfW);
                    local.z = 0.02f;
                    Vec3 p = wall.position + wall.rotation * local;
                    p.y = room.floorY + EyeHeight;
                    result.position = p + wall.rotation * e.anchor.offset;
                    result.rotation = Quat.LookRotation(-wall.Forward, Vec3.Up);
                    break;
                }

                default: // Center
                    result.position = entity.position + offsetWorld;
                    result.rotation = e.type == ElementType.Highlight ? entity.rotation : FaceUser(result.position, room, 0f);
                    break;
            }

            if (e.type == ElementType.Gizmo)
            {
                // Gizmos align their local +Y with the part's action axis instead of facing the user.
                Vec3 axis = entity.actionAxis.SqrLength > 0 ? entity.rotation * entity.actionAxis : Vec3.Up;
                result.rotation = Quat.FromTo(Vec3.Up, axis);
                // Centre on where the action happens (the handwheel), not just the top of the bounds.
                result.position = entity.ActionPoint + offsetWorld;
                result.target = result.position;
                result.pivotBottom = false;
            }
            return result;
        }

        /// <summary>World-space centre-line for a flow element.</summary>
        public static List<Vec3> FlowPath(Element e, RoomModel room)
        {
            var result = new List<Vec3>();
            RoomEntity entity = room.Get(e.anchor.entity);
            if (entity == null) return result;
            Vec3 origin = entity.ShapeOrigin;
            foreach (Vec3 p in e.points) result.Add(origin + entity.rotation * p);
            return result;
        }

        /// <summary>Approximate floor area (width, 0, depth) an element needs.</summary>
        public static Vec3 Footprint(Element e)
        {
            float s = e.scale <= 0 ? 1f : e.scale;
            switch (e.type)
            {
                case ElementType.Chart:
                    return new Vec3(ChartWidth(e.values.Count) * s, 0, 0.3f * s);
                case ElementType.Avatar:
                    return new Vec3(0.45f * s, 0, 0.45f * s);
                case ElementType.Card:
                case ElementType.Panel:
                case ElementType.Checklist:
                    return new Vec3(0.5f * s, 0, 0.12f * s);
                default:
                    return new Vec3(0.3f * s, 0, 0.3f * s);
            }
        }

        public static float ChartWidth(int bars) => Math.Max(0.4f, bars * 0.085f + 0.12f);

        /// <summary>
        /// Looks for free floor beside an entity. Tries the side facing the user first, then fans out around the
        /// entity, and takes the first spot that does not collide with anything else that stands on the floor.
        /// </summary>
        static Vec3 FindFloorSpot(RoomEntity entity, RoomModel room, Vec3 toUser, Vec3 footprint)
        {
            float radius = 0.5f * (float)Math.Sqrt(footprint.x * footprint.x + footprint.z * footprint.z);
            float[] angles = { 0f, 35f, -35f, 70f, -70f, 110f, -110f, 150f, -150f, 180f };
            Vec3 best = entity.position;
            float bestClearance = float.NegativeInfinity;

            foreach (float angle in angles)
            {
                Vec3 dir = Quat.Yaw(angle) * toUser;
                float reach = entity.ExtentAlong(dir) + footprint.z * 0.5f + Gap;
                Vec3 spot = entity.position + dir * reach;
                spot.y = room.floorY;
                float clearance = Clearance(spot, radius, entity, room);
                if (clearance >= 0f) return spot;
                if (clearance > bestClearance) { bestClearance = clearance; best = spot; }
            }
            best.y = room.floorY;
            return best;
        }

        /// <summary>Smallest gap between a floor disc and every obstacle; negative when it overlaps something.</summary>
        static float Clearance(Vec3 spot, float radius, RoomEntity self, RoomModel room)
        {
            float min = float.MaxValue;
            foreach (RoomEntity other in room.entities)
            {
                if (other == self || EntityKind.IsSurface(other.kind)) continue;
                if (other.id == self.parentId || other.parentId == self.id) continue;
                // Things mounted well above the floor (a control box on the wall) do not block floor space.
                if (other.position.y - other.size.y * 0.5f > room.floorY + 0.6f) continue;
                Vec3 delta = (spot - other.position).Flat;
                float distance = delta.Length;
                float gap = distance - radius - (distance > 1e-4f ? other.ExtentAlong(delta) : other.FootprintRadius);
                if (gap < min) min = gap;
            }
            // Stay inside the room: keep clear of walls.
            foreach (RoomEntity wall in room.entities)
            {
                if (wall.kind != EntityKind.Wall) continue;
                Vec3 local = wall.rotation.Inverse * (spot - wall.position);
                if (Math.Abs(local.x) > wall.size.x * 0.5f + radius) continue;
                float gap = local.z - radius;
                if (gap < min) min = gap;
            }
            return min;
        }

        static RoomEntity NearestWall(RoomModel room, Vec3 point)
        {
            RoomEntity best = null;
            float bestD = float.MaxValue;
            foreach (RoomEntity wall in room.entities)
            {
                if (wall.kind != EntityKind.Wall) continue;
                Vec3 local = wall.rotation.Inverse * (point - wall.position);
                float slide = Math.Max(0, Math.Abs(local.x) - wall.size.x * 0.5f);
                float d = Math.Abs(local.z) + slide;
                if (d < bestD) { bestD = d; best = wall; }
            }
            return best;
        }

        /// <summary>Rotation that faces content at <paramref name="position"/> toward the user, tilting up or down by at most <paramref name="maxPitch"/> degrees.</summary>
        public static Quat FaceUser(Vec3 position, RoomModel room, float maxPitch)
        {
            Vec3 away = position - room.userPosition;
            Vec3 flat = away.Flat;
            if (flat.SqrLength < 1e-6f) return Quat.LookRotation(room.userForward.Flat.SqrLength > 1e-6f ? room.userForward.Flat : Vec3.Forward);
            Quat yaw = Quat.LookRotation(flat.Normalized, Vec3.Up);
            if (maxPitch <= 0f) return yaw;
            float pitch = (float)(Math.Atan2(-away.y, flat.Length) * 180.0 / Math.PI);
            pitch = Clamp(pitch, -maxPitch, maxPitch);
            return yaw * Quat.AngleAxis(pitch, Vec3.Right);
        }

        static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}
