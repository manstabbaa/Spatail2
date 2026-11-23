using System;
using System.Collections.Generic;

namespace Spatail.Core
{
    /// <summary>
    /// Merges what the device sees (floor, walls, tables) with what the organisation knows (the registered equipment
    /// and its fixtures) into the single <see cref="RoomModel"/> the planner and layout solver work from.
    /// Fixtures bind to real furniture when the room has it and fall back to stand-in holograms when it does not.
    /// </summary>
    public static class SiteBinder
    {
        /// <summary>A wall-mounted fixture snaps to a real wall when one is at most this far from its nominal spot.</summary>
        public const float WallSnapDistance = 2.5f;
        /// <summary>A table fixture adopts a real table when one is at most this far from the station.</summary>
        public const float TableBindDistance = 5f;

        public static RoomModel Bind(SiteDefinition site, RoomModel raw, Pose3 station)
        {
            var room = new RoomModel
            {
                userPosition = raw.userPosition,
                userForward = raw.userForward,
                floorY = raw.floorY,
            };
            var claimed = new HashSet<RoomEntity>();

            // Equipment root and its parts sit rigidly in the station frame.
            var equipment = new RoomEntity
            {
                id = site.equipmentId,
                kind = EntityKind.Equipment,
                label = site.equipmentLabel,
                about = site.equipmentAbout,
                aliases = new List<string>(site.equipmentAliases),
                position = station.TransformPoint(site.equipmentCenter),
                rotation = station.rotation,
                size = site.equipmentSize,
                localCenter = site.equipmentCenter,
                isVirtual = true,
            };
            var bound = new List<RoomEntity> { equipment };

            foreach (PartDef part in site.parts)
            {
                RoomEntity e = FromPart(part, EntityKind.Part, station);
                e.parentId = equipment.id;
                bound.Add(e);
            }

            foreach (PartDef fixture in site.fixtures)
            {
                switch (fixture.mount)
                {
                    case "wall": bound.Add(BindToWall(fixture, raw, station)); break;
                    case "table": bound.Add(BindToTable(fixture, raw, station, claimed)); break;
                    default: bound.Add(FromPart(fixture, EntityKind.Fixture, station)); break;
                }
            }

            foreach (RoomEntity e in raw.entities)
                if (!claimed.Contains(e)) room.entities.Add(e);
            room.entities.AddRange(bound);
            return room;
        }

        static RoomEntity FromPart(PartDef part, string kind, Pose3 frame)
        {
            return new RoomEntity
            {
                id = part.id,
                kind = kind,
                label = part.label,
                about = part.about,
                aliases = new List<string>(part.aliases),
                position = frame.TransformPoint(part.center),
                rotation = frame.rotation,
                size = part.size,
                localCenter = part.center,
                actionAxis = part.actionAxis,
                actionOffset = part.hasActionPoint ? part.actionPoint - part.center : Vec3.Zero,
                hasActionPoint = part.hasActionPoint,
                scanLabel = part.scanLabel,
                shapes = part.shapes,
                isVirtual = true,
            };
        }

        static RoomEntity BindToWall(PartDef fixture, RoomModel raw, Pose3 station)
        {
            Vec3 nominal = station.TransformPoint(fixture.offset);
            RoomEntity bestWall = null;
            float bestDistance = WallSnapDistance;
            Vec3 bestPoint = nominal;

            foreach (RoomEntity wall in raw.entities)
            {
                if (wall.kind != EntityKind.Wall) continue;
                Vec3 local = wall.rotation.Inverse * (nominal - wall.position);
                float margin = fixture.size.x * 0.5f + 0.05f;
                float halfW = wall.size.x * 0.5f - margin;
                if (halfW < 0) continue;
                float distance = Math.Abs(local.z);
                // Sliding along the wall to stay inside it costs distance too, so nearer-fitting walls win.
                float slide = Math.Max(0, Math.Abs(local.x) - halfW);
                float cost = distance + slide;
                if (cost >= bestDistance) continue;
                bestDistance = cost;
                bestWall = wall;
                float x = Math.Max(-halfW, Math.Min(halfW, local.x));
                bestPoint = wall.position + wall.rotation * new Vec3(x, local.y, 0);
            }

            Pose3 frame;
            if (bestWall != null)
            {
                frame = new Pose3(bestPoint, bestWall.rotation);
            }
            else
            {
                // No wall nearby: stand the fixture where the site definition puts it, facing the same way a wall would.
                frame = new Pose3(nominal, station.rotation * Quat.Yaw(180f + fixture.yaw));
            }
            RoomEntity e = FromPart(fixture, EntityKind.Fixture, frame);
            return e;
        }

        static RoomEntity BindToTable(PartDef fixture, RoomModel raw, Pose3 station, HashSet<RoomEntity> claimed)
        {
            RoomEntity real = null;
            float best = TableBindDistance;
            foreach (RoomEntity t in raw.entities)
            {
                if (t.kind != EntityKind.Table || t.isVirtual || claimed.Contains(t)) continue;
                float d = Vec3.Distance(t.position.Flat, station.position.Flat);
                if (d < best) { best = d; real = t; }
            }

            if (real != null)
            {
                claimed.Add(real);
                return new RoomEntity
                {
                    id = fixture.id,
                    kind = EntityKind.Table,
                    label = fixture.label,
                    about = fixture.about,
                    aliases = new List<string>(fixture.aliases),
                    position = real.position,
                    rotation = real.rotation,
                    size = real.size,
                    scanLabel = fixture.scanLabel,
                    isVirtual = false,
                };
            }

            Vec3 origin = station.TransformPoint(fixture.offset);
            origin.y = raw.floorY;
            var frame = new Pose3(origin, station.rotation * Quat.Yaw(fixture.yaw));
            RoomEntity e = FromPart(fixture, EntityKind.Table, frame);
            return e;
        }

        /// <summary>
        /// Picks a sensible default pose for the station: on the floor a comfortable distance in front of the user,
        /// turned so its front faces them. When a wall is close behind that spot the station backs up square to it.
        /// </summary>
        public static Pose3 SuggestStationPose(RoomModel raw, SiteDefinition site, float distance = 1.9f)
        {
            Vec3 forward = raw.userForward.Flat.Normalized;
            if (forward.SqrLength < 1e-6f) forward = Vec3.Forward;
            Vec3 spot = raw.userPosition + forward * distance;
            spot.y = raw.floorY;
            Quat rotation = Quat.LookRotation(forward, Vec3.Up);

            // Square up against a wall if one is close behind the spot (the station's +Z is its back).
            float depth = site.equipmentSize.z * 0.5f + 0.45f;
            RoomEntity bestWall = null;
            float bestGap = 1.6f;
            foreach (RoomEntity wall in raw.entities)
            {
                if (wall.kind != EntityKind.Wall) continue;
                Vec3 local = wall.rotation.Inverse * (spot - wall.position);
                if (Math.Abs(local.x) > wall.size.x * 0.5f) continue;
                if (local.z < 0) continue; // behind the wall
                if (Vec3.Dot(wall.Forward, forward) > -0.5f) continue; // the user is not facing this wall
                if (local.z < bestGap) { bestGap = local.z; bestWall = wall; }
            }
            if (bestWall != null)
            {
                Vec3 local = bestWall.rotation.Inverse * (spot - bestWall.position);
                spot = bestWall.position + bestWall.rotation * new Vec3(local.x, local.y, depth);
                spot.y = raw.floorY;
                rotation = Quat.LookRotation(-bestWall.Forward.Flat.Normalized, Vec3.Up);
            }
            return new Pose3(spot, rotation);
        }
    }
}
