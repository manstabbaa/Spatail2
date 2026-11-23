using System;
using System.Collections.Generic;
using Spatail.Core;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Spatail.DevTool
{
    /// <summary>
    /// Turns AR Foundation planes into Spatail's room model: a floor, walls, tables and seats.
    ///
    /// Plane classification is used where the device provides it (ARKit does on A12 and newer); on devices that do
    /// not classify, size and height heuristics stand in. Planes are refined continuously, so the provider only
    /// announces a change when something material happens: the floor appears or shifts by a few centimetres, or a
    /// wall, table or seat is gained or lost.
    /// </summary>
    public sealed class ArRoomProvider : MonoBehaviour, IRoomProvider
    {
        public ARPlaneManager Planes;
        public Camera Camera;

        const float ScanInterval = 0.5f;
        const float MinRaiseInterval = 2f;
        const float FloorStep = 0.03f;
        /// <summary>If tracking is running but no floor plane has shown up after this long, assume one.</summary>
        const float EstimateFloorAfter = 10f;
        const float AssumedPhoneHeight = 1.35f;

        public event Action Changed;

        readonly List<ARPlane> _floorCandidates = new List<ARPlane>();
        RoomModel _latest = new RoomModel();
        bool _hasFloor, _floorIsEstimated, _classificationSeen;
        float _estimatedFloorY;
        float _trackingSince = -1f;
        float _nextScan, _lastRaise = -100f;
        string _announced = "";
        string _pending;
        int _walls, _tables;

        public bool IsReady => _hasFloor;

        public string Status
        {
            get
            {
                switch (ARSession.state)
                {
                    case ARSessionState.None:
                    case ARSessionState.CheckingAvailability:
                        return "Starting AR";
                    case ARSessionState.Unsupported:
                        return "AR is not supported on this device";
                    case ARSessionState.NeedsInstall:
                    case ARSessionState.Installing:
                        return "Installing AR support";
                    case ARSessionState.Ready:
                    case ARSessionState.SessionInitializing:
                        return Coaching(ARSession.notTrackingReason);
                }
                if (!_hasFloor) return "Move the phone slowly and point it at the floor";
                string floor = _floorIsEstimated ? "Floor estimated, keep scanning" : "Floor found";
                return floor + " · " + Count(_walls, "wall") + " · " + Count(_tables, "table");
            }
        }

        static string Coaching(NotTrackingReason reason)
        {
            switch (reason)
            {
                case NotTrackingReason.InsufficientLight: return "Too dark to track; find more light";
                case NotTrackingReason.InsufficientFeatures: return "Point at a surface with more detail";
                case NotTrackingReason.ExcessiveMotion: return "Move the phone more slowly";
                case NotTrackingReason.Relocalizing: return "Finding its place again; look around";
                case NotTrackingReason.CameraUnavailable: return "The camera is not available";
                case NotTrackingReason.Unsupported: return "Tracking is not supported here";
                default: return "Starting the camera; move the phone around";
            }
        }

        static string Count(int n, string noun) => n + " " + noun + (n == 1 ? "" : "s");

        public RoomModel Capture()
        {
            Scan();
            return _latest;
        }

        void Update()
        {
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + ScanInterval;
            string signature = Scan();

            if (signature != _announced) _pending = signature;
            if (_pending != null && Time.unscaledTime - _lastRaise >= MinRaiseInterval)
            {
                _announced = _pending;
                _pending = null;
                _lastRaise = Time.unscaledTime;
                if (Changed != null) Changed();
            }
        }

        /// <summary>Rebuilds the room model from the current planes and returns a signature of its material content.</summary>
        string Scan()
        {
            var room = new RoomModel();
            Vector3 cameraPosition = Camera != null ? Camera.transform.position : Vector3.zero;
            if (Camera != null)
            {
                room.userPosition = cameraPosition.ToCore();
                room.userForward = Camera.transform.forward.ToCore();
            }

            bool tracking = ARSession.state == ARSessionState.SessionTracking;
            if (tracking && _trackingSince < 0f) _trackingSince = Time.unscaledTime;

            // ---- floor -----------------------------------------------------------------------
            _floorCandidates.Clear();
            ARPlane floorPlane = null;
            float floorArea = 0f;
            if (Planes != null)
            {
                foreach (ARPlane plane in Planes.trackables)
                {
                    if (!Usable(plane)) continue;
                    if (plane.classifications != PlaneClassifications.None) _classificationSeen = true;
                    if (plane.alignment != PlaneAlignment.HorizontalUp) continue;
                    _floorCandidates.Add(plane);
                    if ((plane.classifications & PlaneClassifications.Floor) == 0) continue;
                    float area = plane.size.x * plane.size.y;
                    if (area > floorArea) { floorArea = area; floorPlane = plane; }
                }
            }
            if (floorPlane == null)
            {
                // No plane is labelled as floor: take the lowest sizeable horizontal plane well below the phone.
                float lowest = float.MaxValue;
                foreach (ARPlane plane in _floorCandidates)
                {
                    const PlaneClassifications furniture = PlaneClassifications.Table | PlaneClassifications.SeatOfAnyType | PlaneClassifications.Ceiling;
                    if ((plane.classifications & furniture) != 0) continue;
                    if (plane.size.x * plane.size.y < 0.4f) continue;
                    float y = plane.center.y;
                    if (y > cameraPosition.y - 0.6f) continue;
                    if (y < lowest) { lowest = y; floorPlane = plane; }
                }
            }

            float floorY;
            if (floorPlane != null)
            {
                _hasFloor = true;
                _floorIsEstimated = false;
                Vector3 centre = floorPlane.center;
                floorY = centre.y;
                room.entities.Add(new RoomEntity
                {
                    id = "floor", kind = EntityKind.Floor, label = "Floor",
                    position = new Vec3(centre.x, floorY, centre.z),
                    rotation = FlatRotation(floorPlane.transform.forward).ToCore(),
                    size = new Vec3(floorPlane.size.x, 0f, floorPlane.size.y),
                });
            }
            else if (_floorIsEstimated || (tracking && Time.unscaledTime - _trackingSince > EstimateFloorAfter))
            {
                // Tracking works but no floor plane has been found. Assume one so the user is not left waiting;
                // the real floor replaces it as soon as it is detected.
                if (!_floorIsEstimated) _estimatedFloorY = cameraPosition.y - AssumedPhoneHeight;
                _hasFloor = true;
                _floorIsEstimated = true;
                floorY = _estimatedFloorY;
                room.entities.Add(new RoomEntity
                {
                    id = "floor", kind = EntityKind.Floor, label = "Floor (estimated)",
                    position = new Vec3(cameraPosition.x, floorY, cameraPosition.z),
                    size = new Vec3(6f, 0f, 6f),
                });
            }
            else
            {
                _hasFloor = false;
                floorY = cameraPosition.y - AssumedPhoneHeight;
            }
            room.floorY = floorY;

            // ---- walls and furniture -------------------------------------------------------------
            int walls = 0, tables = 0, seats = 0;
            if (Planes != null)
            {
                foreach (ARPlane plane in Planes.trackables)
                {
                    if (!Usable(plane) || plane == floorPlane) continue;
                    PlaneClassifications cls = plane.classifications;

                    if (plane.alignment == PlaneAlignment.Vertical)
                    {
                        RoomEntity wall = MakeWall(plane, cls, floorY, cameraPosition);
                        if (wall == null) continue;
                        room.entities.Add(wall);
                        walls++;
                    }
                    else if (plane.alignment == PlaneAlignment.HorizontalUp)
                    {
                        float height = plane.center.y - floorY;
                        float area = plane.size.x * plane.size.y;
                        bool isTable = (cls & PlaneClassifications.Table) != 0;
                        bool isSeat = (cls & PlaneClassifications.SeatOfAnyType) != 0;
                        // Without classification, a waist-high horizontal surface of some size is treated as a table.
                        if (!_classificationSeen && cls == PlaneClassifications.None && height > 0.45f && height < 1.15f && area > 0.2f) isTable = true;
                        if ((!isTable && !isSeat) || height < 0.15f) continue;

                        Vector3 centre = plane.center;
                        room.entities.Add(new RoomEntity
                        {
                            id = (isTable ? "table-" : "seat-") + ShortId(plane),
                            kind = isTable ? EntityKind.Table : EntityKind.Seat,
                            label = isTable ? "Table" : "Seat",
                            position = new Vec3(centre.x, floorY + height * 0.5f, centre.z),
                            rotation = FlatRotation(plane.transform.forward).ToCore(),
                            size = new Vec3(plane.size.x, height, plane.size.y),
                        });
                        if (isTable) tables++; else seats++;
                    }
                }
            }

            _walls = walls;
            _tables = tables;
            _latest = room;

            if (!_hasFloor) return "no-floor";
            return (_floorIsEstimated ? "e" : "f") + Mathf.RoundToInt(floorY / FloorStep) + "|w" + walls + "|t" + tables + "|s" + seats;
        }

        static bool Usable(ARPlane plane)
        {
            return plane != null && plane.subsumedBy == null && plane.trackingState != TrackingState.None &&
                   plane.size.x > 0.05f && plane.size.y > 0.05f;
        }

        /// <summary>
        /// A vertical plane as a wall entity: centred on the plane, facing into the room along local +Z, and extended
        /// down to the floor and up to at least standing height, since detected wall patches rarely cover the wall.
        /// </summary>
        static RoomEntity MakeWall(ARPlane plane, PlaneClassifications cls, float floorY, Vector3 viewer)
        {
            // In-plane axes: local X and Z. Whichever is closer to vertical carries the height.
            Transform t = plane.transform;
            bool xIsVertical = Mathf.Abs(t.right.y) > Mathf.Abs(t.forward.y);
            float width = xIsVertical ? plane.size.y : plane.size.x;
            float height = xIsVertical ? plane.size.x : plane.size.y;

            if (cls != PlaneClassifications.None)
            {
                if ((cls & PlaneClassifications.WallFace) == 0) return null; // doors, windows, wall art
            }
            else if (width < 1f || height < 0.6f)
            {
                return null;
            }

            Vector3 normal = plane.normal;
            normal.y = 0f;
            if (normal.sqrMagnitude < 1e-4f) return null;
            normal.Normalize();

            Vector3 centre = plane.center;
            // The user is always on the room side of a wall, so make the wall face them.
            Vector3 toViewer = viewer - centre;
            toViewer.y = 0f;
            if (Vector3.Dot(normal, toViewer) < 0f) normal = -normal;
            float bottom = Mathf.Min(centre.y - height * 0.5f, floorY);
            float top = Mathf.Max(centre.y + height * 0.5f, floorY + 2.2f);
            return new RoomEntity
            {
                id = "wall-" + ShortId(plane),
                kind = EntityKind.Wall,
                label = "Wall",
                position = new Vec3(centre.x, (top + bottom) * 0.5f, centre.z),
                rotation = Quaternion.LookRotation(normal, Vector3.up).ToCore(),
                size = new Vec3(width, top - bottom, 0.02f),
            };
        }

        static Quaternion FlatRotation(Vector3 forward)
        {
            forward.y = 0f;
            return forward.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.identity;
        }

        static string ShortId(ARPlane plane)
        {
            string id = plane.trackableId.ToString();
            return id.Length > 6 ? id.Substring(id.Length - 6).ToLowerInvariant() : id.ToLowerInvariant();
        }
    }
}
