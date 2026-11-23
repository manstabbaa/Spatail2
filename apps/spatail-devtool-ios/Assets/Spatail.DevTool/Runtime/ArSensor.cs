using System;
using System.Collections.Generic;
using Spatail.Core;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Spatail.DevTool
{
    /// <summary>
    /// What an iPhone or iPad senses, live, through ARKit. Nothing about the place is known in advance and nothing
    /// is required before requests can be made except that ARKit is tracking.
    ///
    /// <b>Ray casts.</b> ARKit's provider only casts natively from a point on the screen
    /// (ARKitRaycastSubsystem registers supportsWorldBasedRaycast = false), so a ray is answered by the first of
    /// these that hits:
    /// 1. the LiDAR mesh, where the device has one: real geometry for any ray;
    /// 2. ARKit's own ray cast through the matching screen point, when the ray starts at the camera: detected planes
    ///    and ARKit's estimate of the surface where no plane has been detected yet;
    /// 3. the planes detected so far, tested within their outlines: any ray;
    /// 4. what ARKit answered for nearby directions when the frame the ray belongs to was captured: approximate, for
    ///    rays cast later through an image position after the phone has moved;
    /// 5. the surfaces below, a plane's bounding rectangle, and the detected floor beyond its mapped part.
    ///
    /// <b>Surfaces.</b> The planes found so far, as floor, walls, tables, seats, doors, windows and ceiling.
    ///
    /// <b>Capture.</b> The camera image as a JPEG turned upright for how the phone is held, with a view frame from
    /// the camera's intrinsics so a position in the picture maps to a ray in the world.
    /// </summary>
    public sealed class ArSensor : MonoBehaviour, ISpaceSensor
    {
        public ARPlaneManager Planes;
        public ARRaycastManager Raycasts;
        public ARCameraManager CameraManager;
        public Camera Camera;

        const float ScanInterval = 0.5f;
        const float MinRaiseInterval = 2f;
        const float FloorStep = 0.03f;
        const float AssumedPhoneHeight = 1.35f;
        const float DefaultRange = 30f;
        const int LongSide = 1024;
        const int JpegQuality = 80;
        /// <summary>How far a ray may start from the camera and still be answered through the screen.</summary>
        const float AtCamera = 0.03f;
        const string RotationKey = "spatail.devtool.frameRotation";
        const string FlipKey = "spatail.devtool.frameFlip";

        public event Action Changed;

        // ---- surfaces ----------------------------------------------------------------------------
        List<RoomEntity> _surfaces = new List<RoomEntity>();
        readonly List<ARPlane> _horizontal = new List<ARPlane>();
        bool _hasFloor, _classificationSeen, _hasFloorGuess;
        float _floorY, _floorGuess;
        float _nextScan, _lastRaise = -100f;
        string _announced = "", _pending;

        // ---- ray casts ---------------------------------------------------------------------------
        enum Source { Mesh, Arkit, Planes, Memory, Surfaces, Miss }
        static readonly string[] SourceNames = { "mesh", "ARKit", "planes", "remembered", "surfaces", "nothing" };
        readonly int[] _answered = new int[6];
        readonly List<ARRaycastHit> _hits = new List<ARRaycastHit>();
        ArMeshRaycaster _mesh;

        struct Sample { public Vector3 direction, position, normal; }
        sealed class FrameMemory
        {
            public Vector3 origin;
            public readonly List<Sample> samples = new List<Sample>();
        }
        readonly List<FrameMemory> _frames = new List<FrameMemory>();
        const int FramesKept = 4, SamplesPerFrame = 400;

        // ---- capture -----------------------------------------------------------------------------
        NativeArray<byte> _pixels;
        Color32[] _colors;
        Texture2D _texture;
        int _rotationOverride = -1;
        bool _flip;

        /// <summary>What the last captured frame was, for the dev tool: size, rotation, field of view, self-check.</summary>
        public string FrameInfo { get; private set; } = "No frame captured yet";

        /// <summary>Degrees the camera image is turned clockwise to stand upright: -1 follows the screen orientation.</summary>
        public int FrameRotation
        {
            get => _rotationOverride;
            set
            {
                _rotationOverride = value < 0 ? -1 : ((value / 90) % 4) * 90;
                PlayerPrefs.SetInt(RotationKey, _rotationOverride);
            }
        }

        /// <summary>Set if captured frames arrive upside down (the image buffer's first row is the bottom one).</summary>
        public bool FrameFlip
        {
            get => _flip;
            set { _flip = value; PlayerPrefs.SetInt(FlipKey, value ? 1 : 0); }
        }

        /// <summary>The rotation currently applied to captured frames.</summary>
        public int AppliedRotation => _rotationOverride >= 0 ? _rotationOverride : RotationForScreen();

        public bool FloorIsEstimated => !_hasFloor;
        public int MeshChunks => _mesh != null ? _mesh.ChunkCount : 0;

        /// <summary>How ray casts have been answered since launch, by source.</summary>
        public string RaycastInfo
        {
            get
            {
                var text = new System.Text.StringBuilder();
                for (int i = 0; i < _answered.Length; i++)
                {
                    if (_answered[i] == 0) continue;
                    if (text.Length > 0) text.Append(" · ");
                    text.Append(SourceNames[i]).Append(' ').Append(_answered[i]);
                }
                return text.Length > 0 ? text.ToString() : "none yet";
            }
        }

        void Awake()
        {
            _rotationOverride = PlayerPrefs.GetInt(RotationKey, -1);
            _flip = PlayerPrefs.GetInt(FlipKey, 0) == 1;
        }

        /// <summary>Gives the sensor the LiDAR mesh to cast into. Optional.</summary>
        public void UseMesh(ARMeshManager meshes)
        {
            if (meshes != null) _mesh = new ArMeshRaycaster(meshes);
        }

        void OnDestroy()
        {
            if (_pixels.IsCreated) _pixels.Dispose();
            if (_texture != null) Destroy(_texture);
        }

        // ---- status ------------------------------------------------------------------------------

        public bool IsReady => ARSession.state == ARSessionState.SessionTracking;

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
                int count = _surfaces.Count;
                string surfaces = count == 0 ? "no surfaces yet" : count + (count == 1 ? " surface" : " surfaces");
                return "Tracking · " + surfaces + (_hasFloor ? "" : " · floor estimated") + (MeshChunks > 0 ? " · LiDAR mesh" : "");
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

        // ---- surfaces ----------------------------------------------------------------------------

        public IReadOnlyList<RoomEntity> Surfaces => _surfaces;

        /// <summary>The detected floor's height, or until one is detected a guess from how a phone is usually held.</summary>
        public float FloorY => _hasFloor ? _floorY : (_hasFloorGuess ? _floorGuess : (Camera != null ? Camera.transform.position.y - AssumedPhoneHeight : 0f));

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

        /// <summary>Rebuilds the surface list from the planes ARKit has now; returns a signature of its material content.</summary>
        string Scan()
        {
            var found = new List<RoomEntity>();
            Vector3 viewer = Camera != null ? Camera.transform.position : Vector3.zero;
            if (IsReady && !_hasFloorGuess)
            {
                _floorGuess = viewer.y - AssumedPhoneHeight;
                _hasFloorGuess = true;
            }

            // ---- floor: the largest plane ARKit calls floor, else the lowest sizeable one well below the phone ----
            _horizontal.Clear();
            ARPlane floorPlane = null;
            float floorArea = 0f;
            if (Planes != null)
            {
                foreach (ARPlane plane in Planes.trackables)
                {
                    if (!Usable(plane)) continue;
                    if (plane.classifications != PlaneClassifications.None) _classificationSeen = true;
                    if (plane.alignment != PlaneAlignment.HorizontalUp) continue;
                    _horizontal.Add(plane);
                    if ((plane.classifications & PlaneClassifications.Floor) == 0) continue;
                    float area = plane.size.x * plane.size.y;
                    if (area > floorArea) { floorArea = area; floorPlane = plane; }
                }
            }
            if (floorPlane == null)
            {
                float lowest = float.MaxValue;
                foreach (ARPlane plane in _horizontal)
                {
                    const PlaneClassifications furniture = PlaneClassifications.Table | PlaneClassifications.SeatOfAnyType | PlaneClassifications.Ceiling;
                    if ((plane.classifications & furniture) != 0) continue;
                    if (plane.size.x * plane.size.y < 0.4f) continue;
                    float y = plane.center.y;
                    if (y > viewer.y - 0.6f) continue;
                    if (y < lowest) { lowest = y; floorPlane = plane; }
                }
            }
            _hasFloor = floorPlane != null;
            if (_hasFloor) _floorY = floorPlane.center.y;
            float floorY = FloorY;

            int walls = 0, tables = 0, others = 0;
            float mappedArea = 0f;
            if (Planes != null)
            {
                foreach (ARPlane plane in Planes.trackables)
                {
                    if (!Usable(plane)) continue;
                    PlaneClassifications cls = plane.classifications;
                    Vector3 centre = plane.center;
                    Vector2 size = plane.size;
                    RoomEntity entity = null;

                    if (plane.alignment == PlaneAlignment.Vertical)
                    {
                        entity = Upright(plane, cls, viewer);
                        if (entity != null && entity.kind == EntityKind.Wall) walls++; else if (entity != null) others++;
                    }
                    else if (plane.alignment == PlaneAlignment.HorizontalUp)
                    {
                        float height = centre.y - floorY;
                        bool isFloor = plane == floorPlane || (cls & PlaneClassifications.Floor) != 0 ||
                                       (_hasFloor && Mathf.Abs(height) < 0.06f && size.x * size.y > 0.3f);
                        bool isTable = (cls & PlaneClassifications.Table) != 0;
                        bool isSeat = (cls & PlaneClassifications.SeatOfAnyType) != 0;
                        // Where ARKit does not classify planes, a waist-high surface of some size is taken to be a table.
                        if (!_classificationSeen && !isFloor && height > 0.45f && height < 1.15f && size.x * size.y > 0.2f) isTable = true;

                        if (isFloor)
                        {
                            entity = new RoomEntity
                            {
                                id = plane == floorPlane ? "floor" : "floor-" + ShortId(plane),
                                kind = EntityKind.Floor, label = "Floor",
                                position = new Vec3(centre.x, centre.y, centre.z),
                                rotation = Flat(plane.transform.forward).ToCore(),
                                size = new Vec3(size.x, 0f, size.y),
                            };
                        }
                        else if ((isTable || isSeat) && height > 0.15f)
                        {
                            // Furniture is reported as the volume it occupies, from the floor to its top.
                            entity = new RoomEntity
                            {
                                id = (isTable ? "table-" : "seat-") + ShortId(plane),
                                kind = isTable ? EntityKind.Table : EntityKind.Seat,
                                label = isTable ? "Table" : "Seat",
                                position = new Vec3(centre.x, floorY + height * 0.5f, centre.z),
                                rotation = Flat(plane.transform.forward).ToCore(),
                                size = new Vec3(size.x, height, size.y),
                            };
                            if (isTable) tables++; else others++;
                        }
                    }
                    else if (plane.alignment == PlaneAlignment.HorizontalDown && (cls & PlaneClassifications.Ceiling) != 0)
                    {
                        entity = new RoomEntity
                        {
                            id = "ceiling-" + ShortId(plane), kind = EntityKind.Ceiling, label = "Ceiling",
                            position = centre.ToCore(),
                            rotation = Flat(plane.transform.forward).ToCore(),
                            size = new Vec3(size.x, 0f, size.y),
                        };
                        others++;
                    }

                    if (entity == null) continue;
                    // The main floor goes first: it is the one most code asks for.
                    if (entity.id == "floor") found.Insert(0, entity); else found.Add(entity);
                    mappedArea += size.x * size.y;
                }
            }

            _surfaces = found;
            if (!IsReady) return "not-tracking";
            // Planes are refined all the time; only material changes are announced: the floor appearing or moving a
            // few centimetres, a surface gained or lost, or the mapped area growing by a couple of square metres.
            return (_hasFloor ? "f" : "e") + Mathf.RoundToInt(floorY / FloorStep) + "|w" + walls + "|t" + tables + "|o" + others +
                   "|a" + Mathf.RoundToInt(mappedArea / 2f);
        }

        static bool Usable(ARPlane plane)
        {
            return plane != null && plane.subsumedBy == null && plane.trackingState != TrackingState.None &&
                   plane.size.x > 0.05f && plane.size.y > 0.05f;
        }

        /// <summary>
        /// A vertical plane as a wall, door or window: the patch ARKit has mapped, no more, facing into the room
        /// along its local +Z.
        /// </summary>
        static RoomEntity Upright(ARPlane plane, PlaneClassifications cls, Vector3 viewer)
        {
            // In-plane axes are local X and Z; whichever is closer to vertical carries the height.
            Transform t = plane.transform;
            bool xIsVertical = Mathf.Abs(t.right.y) > Mathf.Abs(t.forward.y);
            float width = xIsVertical ? plane.size.y : plane.size.x;
            float height = xIsVertical ? plane.size.x : plane.size.y;

            string kind = EntityKind.Wall, label = "Wall";
            if ((cls & PlaneClassifications.DoorFrame) != 0) { kind = EntityKind.Door; label = "Door"; }
            else if ((cls & PlaneClassifications.WindowFrame) != 0) { kind = EntityKind.Window; label = "Window"; }
            else if (cls != PlaneClassifications.None && (cls & PlaneClassifications.WallFace) == 0) return null; // wall art and the like
            else if (cls == PlaneClassifications.None && (width < 0.5f || height < 0.5f)) return null;

            Vector3 normal = plane.normal;
            normal.y = 0f;
            if (normal.sqrMagnitude < 1e-4f) return null;
            normal.Normalize();
            Vector3 centre = plane.center;
            // The user is on the room side of a wall, so it faces them.
            Vector3 toViewer = viewer - centre;
            toViewer.y = 0f;
            if (Vector3.Dot(normal, toViewer) < 0f) normal = -normal;

            return new RoomEntity
            {
                id = kind + "-" + ShortId(plane), kind = kind, label = label,
                position = centre.ToCore(),
                rotation = Quaternion.LookRotation(normal, Vector3.up).ToCore(),
                size = new Vec3(width, height, 0.02f),
            };
        }

        static Quaternion Flat(Vector3 forward)
        {
            forward.y = 0f;
            return forward.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.identity;
        }

        static string ShortId(ARPlane plane)
        {
            string id = plane.trackableId.ToString();
            return id.Length > 6 ? id.Substring(id.Length - 6).ToLowerInvariant() : id.ToLowerInvariant();
        }

        // ---- ray casts ---------------------------------------------------------------------------

        public bool Raycast(Vec3 origin, Vec3 direction, float maxDistance, out RayHit hit)
        {
            hit = default(RayHit);
            Vector3 o = origin.ToUnity(), d = direction.ToUnity();
            if (!IsReady || d.sqrMagnitude < 1e-12f) return false;
            d.Normalize();
            if (maxDistance <= 0f || float.IsInfinity(maxDistance) || float.IsNaN(maxDistance)) maxDistance = DefaultRange;

            Vector3 point, normal;
            float distance;
            string label = null;
            Source source;

            if (_mesh != null && _mesh.Raycast(o, d, maxDistance, out point, out normal, out distance))
            {
                source = Source.Mesh;
            }
            else if (ThroughScreen(o, d, maxDistance, out point, out normal, out distance))
            {
                source = Source.Arkit;
                Remember(o, d, point, normal);
            }
            else if (AgainstPlanes(o, d, maxDistance, TrackableType.PlaneWithinPolygon, out point, out normal, out distance))
            {
                source = Source.Planes;
            }
            else if (Recall(o, d, maxDistance, out point, out normal, out distance))
            {
                source = Source.Memory;
            }
            else if (AgainstSurfaces(origin, direction.Normalized, o, d, maxDistance, out point, out normal, out distance, out label))
            {
                source = Source.Surfaces;
            }
            else
            {
                _answered[(int)Source.Miss]++;
                return false;
            }

            _answered[(int)source]++;
            hit = new RayHit { position = point.ToCore(), normal = normal.ToCore(), distance = distance, label = label };
            return true;
        }

        /// <summary>
        /// ARKit's own ray cast, which the provider offers only through a screen point. Used when the ray starts at
        /// the camera and passes through the screen: detected planes first, with ARKit's estimate of the surface
        /// where none has been detected, then feature points.
        /// </summary>
        bool ThroughScreen(Vector3 origin, Vector3 direction, float maxDistance, out Vector3 point, out Vector3 normal, out float distance)
        {
            point = normal = Vector3.zero;
            distance = 0f;
            if (Raycasts == null || Camera == null) return false;
            if ((origin - Camera.transform.position).sqrMagnitude > AtCamera * AtCamera) return false;
            Vector3 screen = Camera.WorldToScreenPoint(origin + direction * 5f);
            if (screen.z <= 0f || screen.x < 0f || screen.y < 0f || screen.x > Screen.width || screen.y > Screen.height) return false;
            return ScreenPointRaycast(new Vector2(screen.x, screen.y), origin, direction, maxDistance, out point, out normal, out distance);
        }

        bool ScreenPointRaycast(Vector2 screenPoint, Vector3 origin, Vector3 direction, float maxDistance, out Vector3 point, out Vector3 normal, out float distance)
        {
            point = normal = Vector3.zero;
            distance = 0f;
            bool featurePoint = false;
            if (!Raycasts.Raycast(screenPoint, _hits, TrackableType.PlaneWithinPolygon | TrackableType.PlaneEstimated) || _hits.Count == 0)
            {
                if (!Raycasts.Raycast(screenPoint, _hits, TrackableType.FeaturePoint) || _hits.Count == 0) return false;
                featurePoint = true;
            }
            // Results are sorted nearest first (ARRaycastManager.TransformAndDisposeNativeHitResults).
            Pose pose = _hits[0].pose;
            point = pose.position;
            distance = Vector3.Dot(point - origin, direction);
            if (distance <= 0.02f || distance > maxDistance) return false;
            // A plane hit's pose has the surface normal as its up axis; a feature point has no surface, so it faces the ray.
            normal = featurePoint ? -direction : pose.up;
            if (Vector3.Dot(normal, direction) > 0f) normal = -normal;
            return true;
        }

        /// <summary>
        /// Any world ray against the planes detected so far. ARKit does not cast world rays natively, so
        /// ARRaycastManager answers this from ARPlaneManager's plane geometry.
        /// </summary>
        bool AgainstPlanes(Vector3 origin, Vector3 direction, float maxDistance, TrackableType types, out Vector3 point, out Vector3 normal, out float distance)
        {
            point = normal = Vector3.zero;
            distance = 0f;
            if (Raycasts == null || !Raycasts.Raycast(new Ray(origin, direction), _hits, types) || _hits.Count == 0) return false;
            ARRaycastHit nearest = _hits[0];
            if (nearest.distance <= 0.02f || nearest.distance > maxDistance) return false;
            point = nearest.pose.position;
            distance = nearest.distance;
            normal = nearest.pose.up;
            if (Vector3.Dot(normal, direction) > 0f) normal = -normal;
            return true;
        }

        /// <summary>
        /// Last resort: the surface entities (furniture as volumes), a plane's bounding rectangle, and the detected
        /// floor continued beyond the part mapped so far. Whichever is nearest.
        /// </summary>
        bool AgainstSurfaces(Vec3 coreOrigin, Vec3 coreDirection, Vector3 origin, Vector3 direction, float maxDistance,
            out Vector3 point, out Vector3 normal, out float distance, out string label)
        {
            point = normal = Vector3.zero;
            distance = maxDistance;
            label = null;
            bool found = false;

            RayHit entityHit;
            if (GeometryRaycaster.Raycast(_surfaces, coreOrigin, coreDirection, distance, out entityHit))
            {
                point = entityHit.position.ToUnity();
                normal = entityHit.normal.ToUnity();
                distance = entityHit.distance;
                label = entityHit.label;
                found = true;
            }
            Vector3 p, n;
            float t;
            if (AgainstPlanes(origin, direction, distance, TrackableType.PlaneWithinBounds, out p, out n, out t) && t < distance)
            {
                point = p; normal = n; distance = t; label = null;
                found = true;
            }
            if (!found && _hasFloor && direction.y < -0.05f)
            {
                t = (_floorY - origin.y) / direction.y;
                if (t > 0.02f && t <= maxDistance)
                {
                    point = origin + direction * t;
                    normal = Vector3.up;
                    distance = t;
                    label = "floor";
                    found = true;
                }
            }
            return found;
        }

        // What ARKit answered through the screen for each captured frame, kept so that a ray cast later through the
        // same frame (space.view.point after the phone has moved) still lands near the right place.

        FrameMemory MemoryFor(Vector3 origin)
        {
            for (int i = _frames.Count - 1; i >= 0; i--)
                if ((_frames[i].origin - origin).sqrMagnitude < 1e-6f) return _frames[i];
            return null;
        }

        void Remember(Vector3 origin, Vector3 direction, Vector3 point, Vector3 normal)
        {
            FrameMemory frame = MemoryFor(origin);
            if (frame == null || frame.samples.Count >= SamplesPerFrame) return;
            frame.samples.Add(new Sample { direction = direction, position = point, normal = normal });
        }

        bool Recall(Vector3 origin, Vector3 direction, float maxDistance, out Vector3 point, out Vector3 normal, out float distance)
        {
            point = normal = Vector3.zero;
            distance = 0f;
            FrameMemory frame = MemoryFor(origin);
            if (frame == null || frame.samples.Count == 0) return false;

            int best = -1;
            float bestDot = 0.9925f; // within about seven degrees
            for (int i = 0; i < frame.samples.Count; i++)
            {
                float dot = Vector3.Dot(frame.samples[i].direction, direction);
                if (dot > bestDot) { bestDot = dot; best = i; }
            }
            if (best < 0) return false;

            // Continue the remembered surface to this ray, unless that takes it implausibly far from the sample.
            Sample sample = frame.samples[best];
            float sampleDistance = Vector3.Dot(sample.position - origin, direction);
            float facing = Vector3.Dot(direction, sample.normal);
            distance = Mathf.Abs(facing) > 0.1f ? Vector3.Dot(sample.position - origin, sample.normal) / facing : sampleDistance;
            if (distance < sampleDistance * 0.6f || distance > sampleDistance * 1.6f) distance = sampleDistance;
            if (distance <= 0.02f || distance > maxDistance) return false;
            point = origin + direction * distance;
            normal = sample.normal;
            return true;
        }

        /// <summary>Starts remembering a captured frame, seeded with a grid of ARKit's answers across the screen.</summary>
        void BeginMemory(Vector3 origin)
        {
            if (MemoryFor(origin) != null) return;
            var frame = new FrameMemory { origin = origin };
            _frames.Add(frame);
            if (_frames.Count > FramesKept) _frames.RemoveAt(0);

            // With a LiDAR mesh every ray is answered exactly, now or later; nothing needs remembering.
            if (!IsReady || Raycasts == null || Camera == null || MeshChunks > 0) return;
            const int columns = 7, rows = 5;
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    var screenPoint = new Vector2(Screen.width * (column + 0.5f) / columns, Screen.height * (row + 0.5f) / rows);
                    Vector3 direction = Camera.ScreenPointToRay(screenPoint).direction;
                    Vector3 point, normal;
                    float distance;
                    if (ScreenPointRaycast(screenPoint, origin, direction, DefaultRange, out point, out normal, out distance))
                        frame.samples.Add(new Sample { direction = direction, position = point, normal = normal });
                }
            }
        }

        // ---- capture -----------------------------------------------------------------------------

        public void Capture(Action<ViewFrame, byte[]> done)
        {
            if (done == null) return;
            ViewFrame view = null;
            byte[] jpeg = null;
            try
            {
                jpeg = GrabFrame(out view);
            }
            catch (Exception ex)
            {
                FrameInfo = "The camera image could not be read: " + ex.Message;
                jpeg = null;
            }
            // Without an image the request still carries where the phone was and what the screen showed.
            if (view == null || jpeg == null) view = ScreenFrame();
            if (view != null) BeginMemory(view.position.ToUnity());
            done(view, jpeg);
        }

        /// <summary>A view frame for what is on screen, from the projection ARKit gives the camera.</summary>
        ViewFrame ScreenFrame()
        {
            if (Camera == null) return null;
            Matrix4x4 projection = Camera.projectionMatrix;
            var view = new ViewFrame { position = Camera.transform.position.ToCore(), rotation = Camera.transform.rotation.ToCore() };
            if (projection.m00 > 1e-4f && projection.m11 > 1e-4f)
            {
                view.tanHalfX = 1f / projection.m00;
                view.tanHalfY = 1f / projection.m11;
            }
            return view;
        }

        /// <summary>
        /// The latest camera image as an upright JPEG, with the view frame that maps its pixels to rays.
        ///
        /// ARKit delivers the image as the sensor sees it, which is landscape with the top of the picture towards
        /// the side of the phone that is up in <c>ScreenOrientation.LandscapeLeft</c>. The pixels are turned to match
        /// how the phone is held now, and the intrinsics (focal length and principal point, which describe the
        /// unturned image) are turned with them.
        /// </summary>
        byte[] GrabFrame(out ViewFrame view)
        {
            view = null;
            if (CameraManager == null || Camera == null) { FrameInfo = "No AR camera"; return null; }
            XRCpuImage image;
            if (!CameraManager.TryAcquireLatestCpuImage(out image)) { FrameInfo = "No camera image is available yet"; return null; }

            int nativeWidth, nativeHeight, w, h;
            try
            {
                nativeWidth = image.width;
                nativeHeight = image.height;
                float scale = Mathf.Min(1f, (float)LongSide / Mathf.Max(nativeWidth, nativeHeight));
                w = Mathf.Max(1, Mathf.RoundToInt(nativeWidth * scale));
                h = Mathf.Max(1, Mathf.RoundToInt(nativeHeight * scale));
                var conversion = new XRCpuImage.ConversionParams
                {
                    inputRect = new RectInt(0, 0, nativeWidth, nativeHeight),
                    outputDimensions = new Vector2Int(w, h),
                    outputFormat = TextureFormat.RGBA32,
                    transformation = XRCpuImage.Transformation.None,
                };
                int bytes = image.GetConvertedDataSize(conversion);
                if (bytes < w * h * 4) { FrameInfo = "Unexpected camera image size"; return null; }
                if (!_pixels.IsCreated || _pixels.Length != bytes)
                {
                    if (_pixels.IsCreated) _pixels.Dispose();
                    _pixels = new NativeArray<byte>(bytes, Allocator.Persistent);
                }
                image.Convert(conversion, new NativeSlice<byte>(_pixels));
            }
            finally
            {
                // The image holds a native buffer; ARKit stops delivering frames if these are not released.
                image.Dispose();
            }

            // ---- turn the pixels upright ----
            int rotation = AppliedRotation;
            bool sideways = rotation == 90 || rotation == 270;
            int ow = sideways ? h : w, oh = sideways ? w : h;
            if (_colors == null || _colors.Length != ow * oh) _colors = new Color32[ow * oh];
            for (int yd = 0; yd < oh; yd++)
            {
                // Texture rows run bottom to top; yd counts down from the top of the upright picture.
                int outRow = (oh - 1 - yd) * ow;
                for (int xd = 0; xd < ow; xd++)
                {
                    int x, y;
                    if (rotation == 90) { x = yd; y = h - 1 - xd; }
                    else if (rotation == 180) { x = w - 1 - xd; y = h - 1 - yd; }
                    else if (rotation == 270) { x = w - 1 - yd; y = xd; }
                    else { x = xd; y = yd; }
                    if (_flip) y = h - 1 - y;
                    int i = (y * w + x) * 4;
                    _colors[outRow + xd] = new Color32(_pixels[i], _pixels[i + 1], _pixels[i + 2], 255);
                }
            }
            if (_texture == null || _texture.width != ow || _texture.height != oh)
            {
                if (_texture != null) Destroy(_texture);
                _texture = new Texture2D(ow, oh, TextureFormat.RGBA32, false) { name = "Spatail Camera Frame" };
            }
            _texture.SetPixels32(_colors);
            byte[] jpeg = _texture.EncodeToJPG(JpegQuality);

            // ---- the view frame, from the intrinsics turned the same way ----
            view = new ViewFrame
            {
                position = Camera.transform.position.ToCore(),
                rotation = Camera.transform.rotation.ToCore(),
                width = ow, height = oh,
            };
            string basis;
            XRCameraIntrinsics intrinsics;
            if (CameraManager.TryGetIntrinsics(out intrinsics) && intrinsics.focalLength.x > 1f && intrinsics.focalLength.y > 1f)
            {
                // Intrinsics are in pixels of the full-size image, principal point from its top-left corner.
                Vector2Int resolution = intrinsics.resolution;
                float sx = (float)w / (resolution.x > 0 ? resolution.x : nativeWidth);
                float sy = (float)h / (resolution.y > 0 ? resolution.y : nativeHeight);
                float fx = intrinsics.focalLength.x * sx, fy = intrinsics.focalLength.y * sy;
                float cx = intrinsics.principalPoint.x * sx, cy = intrinsics.principalPoint.y * sy;
                float ufx, ufy, ucx, ucy;
                if (rotation == 90) { ufx = fy; ufy = fx; ucx = h - cy; ucy = cx; }
                else if (rotation == 180) { ufx = fx; ufy = fy; ucx = w - cx; ucy = h - cy; }
                else if (rotation == 270) { ufx = fy; ufy = fx; ucx = cy; ucy = w - cx; }
                else { ufx = fx; ufy = fy; ucx = cx; ucy = cy; }
                view.tanHalfX = ow / (2f * ufx);
                view.tanHalfY = oh / (2f * ufy);
                view.centerU = ucx / ow;
                view.centerV = ucy / oh;
                basis = "intrinsics " + DisplayCheck(view);
            }
            else
            {
                // No intrinsics: the screen shows this picture scaled to fill it, so its projection gives the scale.
                Matrix4x4 projection = Camera.projectionMatrix;
                float imageAspect = (float)ow / oh;
                bool heightFits = Screen.height > 0 && (float)Screen.width / Screen.height <= imageAspect;
                if (projection.m00 > 1e-4f && projection.m11 > 1e-4f)
                {
                    if (heightFits) { view.tanHalfY = 1f / projection.m11; view.tanHalfX = view.tanHalfY * imageAspect; }
                    else { view.tanHalfX = 1f / projection.m00; view.tanHalfY = view.tanHalfX / imageAspect; }
                }
                basis = "no intrinsics, field of view taken from the display";
            }

            FrameInfo = ow + "×" + oh + " px (camera " + nativeWidth + "×" + nativeHeight + ", turned " + rotation + "°" + (_flip ? ", flipped" : "") + ") · " +
                        Degrees(view.tanHalfX) + "° × " + Degrees(view.tanHalfY) + "° · " + basis;
            return jpeg;
        }

        /// <summary>
        /// Compares the field of view computed from the intrinsics with the one ARKit gives the on-screen camera.
        /// The screen shows the same picture scaled to fill it, so in the direction that fits they must agree; if
        /// they do not, the picture has been turned the wrong way or the intrinsics mean something else.
        /// </summary>
        string DisplayCheck(ViewFrame view)
        {
            Matrix4x4 projection = Camera.projectionMatrix;
            if (projection.m00 <= 1e-4f || projection.m11 <= 1e-4f || Screen.height <= 0) return "(not checked)";
            bool heightFits = (float)Screen.width / Screen.height <= (float)view.width / view.height;
            float shown = heightFits ? 1f / projection.m11 : 1f / projection.m00;
            float computed = heightFits ? view.tanHalfY : view.tanHalfX;
            if (Mathf.Abs(computed / shown - 1f) < 0.04f) return "agree with the display";
            return "DISAGREE with the display (" + Degrees(computed) + "° against " + Degrees(shown) + "°)";
        }

        static int Degrees(float tanHalf) => Mathf.RoundToInt(2f * Mathf.Atan(tanHalf) * Mathf.Rad2Deg);

        /// <summary>Clockwise turn that stands the sensor's landscape image upright for the current screen orientation.</summary>
        static int RotationForScreen()
        {
            switch (Screen.orientation)
            {
                case ScreenOrientation.Portrait: return 90;
                case ScreenOrientation.PortraitUpsideDown: return 270;
                case ScreenOrientation.LandscapeRight: return 180;
                default: return 0;
            }
        }
    }
}
