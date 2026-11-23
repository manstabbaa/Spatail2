using System;
using System.Collections;
using System.Threading.Tasks;
using Meta.XR.MRUtilityKit;
using Spatail.Core;
using UnityEngine;
using UnityEngine.Android;

namespace Spatail.Quest
{
    /// <summary>
    /// The room as the headset knows it, through Meta's MR Utility Kit: the floor, walls, tables and other furniture
    /// captured by Space Setup, translated into Spatail's <see cref="RoomModel"/>.
    ///
    /// MRUK conventions this relies on (see MRUKAnchor.cs): a plane lies in the anchor's local XY plane with local +Z
    /// as its normal, and walls face into the room; a volume's anchor sits on its top face with local +Z pointing up,
    /// so <c>VolumeBounds.size.z</c> is the height.
    ///
    /// If there is no room scan, or the user declines the spatial data permission, Spatail still works on a flat
    /// floor at the tracking origin and <see cref="Status"/> says what to do about it.
    /// </summary>
    public sealed class MrukRoomProvider : MonoBehaviour, IRoomProvider
    {
        public OVRCameraRig Rig;

        public bool IsReady { get; private set; }
        public string Status { get; private set; } = "Starting";
        public event Action Changed;

        /// <summary>The outcome of the last attempt to load the room from the device.</summary>
        public MRUK.LoadDeviceResult LastResult { get; private set; } = MRUK.LoadDeviceResult.NotInitialized;

        bool _loading;
        float _notifyAt = -1f;
        int _walls, _tables, _others;

        IEnumerator Start()
        {
            MRUK mruk = MRUK.Instance;
            if (mruk == null)
            {
                Finish("Room understanding is unavailable. Using a flat floor.");
                yield break;
            }
            mruk.SceneLoadedEvent.AddListener(OnSceneChanged);
            mruk.RoomCreatedEvent.AddListener(OnRoomChanged);
            mruk.RoomUpdatedEvent.AddListener(OnRoomChanged);
            mruk.RoomRemovedEvent.AddListener(OnRoomChanged);

            // Give the XR runtime a moment to come up before asking it for anything.
            float waited = 0f;
            while (!OVRManager.OVRManagerinitialized && waited < 5f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            yield return RequestScenePermission();
            yield return Load(true);
        }

        /// <summary>Tries again to load the room, for example after the user has run Space Setup.</summary>
        public void Reload()
        {
            if (!_loading && isActiveAndEnabled) StartCoroutine(Load(true));
        }

        IEnumerator RequestScenePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            string permission = OVRPermissionsRequester.ScenePermission;
            if (Permission.HasUserAuthorizedPermission(permission)) yield break;

            Status = "Waiting for permission to use your room layout";
            bool answered = false;
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => answered = true;
            callbacks.PermissionDenied += _ => answered = true;
            Permission.RequestUserPermission(permission, callbacks);

            // The callbacks do not arrive if another permission dialog was already open, so also poll, and give up
            // waiting after two minutes rather than hanging forever.
            float waited = 0f;
            while (!answered && !Permission.HasUserAuthorizedPermission(permission) && waited < 120f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
#else
            yield break;
#endif
        }

        IEnumerator Load(bool offerSpaceSetup)
        {
            _loading = true;
            Status = "Loading your room";

            Task<MRUK.LoadDeviceResult> task = null;
            try
            {
                // When no room has been captured, MRUK opens Space Setup itself and then tries once more.
                task = MRUK.Instance.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: offerSpaceSetup);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Spatail] MRUK could not start loading the room: " + ex.Message);
            }

            if (task != null)
            {
                while (!task.IsCompleted) yield return null;
                LastResult = task.IsFaulted || task.IsCanceled ? MRUK.LoadDeviceResult.Failure : task.Result;
            }
            else
            {
                LastResult = MRUK.LoadDeviceResult.Failure;
            }
            _loading = false;

            switch (LastResult)
            {
                case MRUK.LoadDeviceResult.Success:
                    Capture();
                    Finish(Describe());
                    break;
                case MRUK.LoadDeviceResult.NoScenePermission:
                    Finish("Spatail is not allowed to use your room layout. Turn on Spatial data for Spatail in Settings > Privacy and safety > App permissions. Using a flat floor for now.");
                    break;
                case MRUK.LoadDeviceResult.NoRoomsFound:
                    Finish("No room scan found. Run Space Setup in Settings > Environment setup, then reopen Spatail. Using a flat floor for now.");
                    break;
                default:
                    Finish("Could not load the room (" + LastResult + "). Using a flat floor for now.");
                    break;
            }
        }

        void Finish(string status)
        {
            Status = status;
            bool wasReady = IsReady;
            IsReady = true;
            Debug.Log("[Spatail] Room: " + status);
            if (wasReady && Changed != null) Changed();
        }

        void OnSceneChanged() => _notifyAt = Time.unscaledTime + 0.5f;
        void OnRoomChanged(MRUKRoom room) => _notifyAt = Time.unscaledTime + 0.5f;

        void Update()
        {
            // Room events arrive in bursts while MRUK updates anchors; tell the app once things settle.
            if (_notifyAt < 0f || Time.unscaledTime < _notifyAt || !IsReady || _loading) return;
            _notifyAt = -1f;
            Capture();
            Status = Describe();
            if (Changed != null) Changed();
        }

        string Describe()
        {
            if (_walls == 0 && _tables == 0 && _others == 0) return "Flat floor only";
            return "Room loaded: " + Count(_walls, "wall") + ", " + Count(_tables, "table") + ", " + Count(_others, "other object");
        }

        static string Count(int n, string noun) => n + " " + noun + (n == 1 ? "" : "s");

        // ---- capture -----------------------------------------------------------------------

        public RoomModel Capture()
        {
            var model = new RoomModel();
            _walls = _tables = _others = 0;
            float floorY = Rig != null && Rig.trackingSpace != null ? Rig.trackingSpace.position.y : 0f;
            bool haveFloor = false;

            MRUK mruk = MRUK.Instance;
            if (mruk != null && mruk.Rooms.Count > 0)
            {
                MRUKRoom current = null;
                try { current = mruk.GetCurrentRoom(); }
                catch (Exception) { /* native library not ready; fall back to the first room */ }
                if (current == null) current = mruk.Rooms[0];
                if (current.FloorAnchors.Count > 0) floorY = current.FloorAnchors[0].transform.position.y;

                int index = 0;
                foreach (MRUKRoom room in mruk.Rooms)
                {
                    if (room == null) continue;
                    foreach (MRUKAnchor anchor in room.Anchors)
                    {
                        if (anchor == null) continue;
                        RoomEntity entity = Convert(anchor, floorY);
                        if (entity == null) continue;
                        index++;
                        switch (entity.kind)
                        {
                            case EntityKind.Floor:
                                entity.id = haveFloor ? "floor-" + index : "floor";
                                haveFloor = true;
                                break;
                            case EntityKind.Ceiling: entity.id = "ceiling-" + index; break;
                            case EntityKind.Wall: entity.id = "wall-" + (++_walls); break;
                            case EntityKind.Table: entity.id = "table-" + (++_tables); break;
                            default: entity.id = entity.kind + "-" + (++_others); break;
                        }
                        model.entities.Add(entity);
                    }
                }
            }

            model.floorY = floorY;
            if (!haveFloor) model.entities.Insert(0, FallbackFloor(floorY));
            return model;
        }

        /// <summary>A generous flat floor under the user, for when the headset has no room scan to offer.</summary>
        RoomEntity FallbackFloor(float floorY)
        {
            Vector3 centre = Rig != null && Rig.centerEyeAnchor != null ? Rig.centerEyeAnchor.position : Vector3.zero;
            return new RoomEntity
            {
                id = "floor",
                kind = EntityKind.Floor,
                label = "Floor",
                position = new Vec3(centre.x, floorY, centre.z),
                size = new Vec3(8f, 0f, 8f),
            };
        }

        static RoomEntity Convert(MRUKAnchor anchor, float floorY)
        {
            const MRUKAnchor.SceneLabels wallLabels =
                MRUKAnchor.SceneLabels.WALL_FACE | MRUKAnchor.SceneLabels.INVISIBLE_WALL_FACE | MRUKAnchor.SceneLabels.INNER_WALL_FACE;
            const MRUKAnchor.SceneLabels wallMounted =
                MRUKAnchor.SceneLabels.DOOR_FRAME | MRUKAnchor.SceneLabels.WINDOW_FRAME | MRUKAnchor.SceneLabels.WALL_ART;

            if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.GLOBAL_MESH)) return null;
            Transform t = anchor.transform;

            if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.FLOOR) || anchor.HasAnyLabel(MRUKAnchor.SceneLabels.CEILING))
            {
                if (!anchor.PlaneRect.HasValue) return null;
                Rect rect = anchor.PlaneRect.Value;
                bool floor = anchor.HasAnyLabel(MRUKAnchor.SceneLabels.FLOOR);
                // The plane's local Y is horizontal for floors and ceilings; use it as the entity's forward.
                return new RoomEntity
                {
                    kind = floor ? EntityKind.Floor : EntityKind.Ceiling,
                    label = floor ? "Floor" : "Ceiling",
                    position = t.TransformPoint(new Vector3(rect.center.x, rect.center.y, 0f)).ToCore(),
                    rotation = Upright(t.up).ToCore(),
                    size = new Vec3(rect.width, 0f, rect.height),
                };
            }

            if (anchor.HasAnyLabel(wallLabels) || anchor.HasAnyLabel(wallMounted))
            {
                if (!anchor.PlaneRect.HasValue) return null;
                Rect rect = anchor.PlaneRect.Value;
                Vector3 normal = t.forward;
                normal.y = 0f;
                if (normal.sqrMagnitude < 1e-4f) return null;
                bool wall = anchor.HasAnyLabel(wallLabels);
                string kind = wall ? EntityKind.Wall
                    : anchor.HasAnyLabel(MRUKAnchor.SceneLabels.DOOR_FRAME) ? EntityKind.Door
                    : anchor.HasAnyLabel(MRUKAnchor.SceneLabels.WINDOW_FRAME) ? EntityKind.Window
                    : EntityKind.Other;
                return new RoomEntity
                {
                    kind = kind,
                    label = wall ? "Wall" : kind == EntityKind.Door ? "Door" : kind == EntityKind.Window ? "Window" : "Wall art",
                    position = t.TransformPoint(new Vector3(rect.center.x, rect.center.y, 0f)).ToCore(),
                    // Spatail walls face into the room along +Z, which is what MRUK's plane normal already does.
                    rotation = Quaternion.LookRotation(normal.normalized, Vector3.up).ToCore(),
                    size = new Vec3(rect.width, rect.height, 0.02f),
                };
            }

            // Everything else is furniture: a volume, or failing that a horizontal top surface.
            string furnitureKind, furnitureLabel;
            Classify(anchor, out furnitureKind, out furnitureLabel);

            if (anchor.VolumeBounds.HasValue)
            {
                Bounds bounds = anchor.VolumeBounds.Value;
                Vector3 centre = t.TransformPoint(bounds.center);
                if (Mathf.Abs(t.forward.y) > 0.7f)
                {
                    // Usual case: local Z is vertical, local X and Y span the footprint.
                    return new RoomEntity
                    {
                        kind = furnitureKind,
                        label = furnitureLabel,
                        position = centre.ToCore(),
                        rotation = Upright(t.up).ToCore(),
                        size = new Vec3(bounds.size.x, bounds.size.z, bounds.size.y),
                    };
                }
                // Unexpected orientation: fall back to a world-aligned box around the volume.
                Bounds world = WorldBounds(t, bounds);
                return new RoomEntity
                {
                    kind = furnitureKind,
                    label = furnitureLabel,
                    position = world.center.ToCore(),
                    size = world.size.ToCore(),
                };
            }

            if (anchor.PlaneRect.HasValue && Mathf.Abs(t.forward.y) > 0.7f)
            {
                // A table top without a volume: treat everything from the floor up to the top as the table.
                Rect rect = anchor.PlaneRect.Value;
                Vector3 top = t.TransformPoint(new Vector3(rect.center.x, rect.center.y, 0f));
                float height = Mathf.Max(0.05f, top.y - floorY);
                return new RoomEntity
                {
                    kind = furnitureKind,
                    label = furnitureLabel,
                    position = new Vec3(top.x, top.y - height * 0.5f, top.z),
                    rotation = Upright(t.up).ToCore(),
                    size = new Vec3(rect.width, height, rect.height),
                };
            }
            return null;
        }

        static void Classify(MRUKAnchor anchor, out string kind, out string label)
        {
            if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE)) { kind = EntityKind.Table; label = "Table"; }
            else if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.COUCH)) { kind = EntityKind.Seat; label = "Couch"; }
            else if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.BED)) { kind = EntityKind.Seat; label = "Bed"; }
            else if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.STORAGE)) { kind = EntityKind.Storage; label = "Storage"; }
            else if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.SCREEN)) { kind = EntityKind.Screen; label = "Screen"; }
            else if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.LAMP)) { kind = EntityKind.Other; label = "Lamp"; }
            else if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.PLANT)) { kind = EntityKind.Other; label = "Plant"; }
            else { kind = EntityKind.Other; label = "Object"; }
        }

        /// <summary>Yaw-only rotation whose forward follows a (roughly horizontal) direction.</summary>
        static Quaternion Upright(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude < 1e-4f ? Quaternion.identity : Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        static Bounds WorldBounds(Transform t, Bounds local)
        {
            var world = new Bounds(t.TransformPoint(local.center), Vector3.zero);
            Vector3 e = local.extents;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                world.Encapsulate(t.TransformPoint(local.center + corner));
            }
            return world;
        }
    }
}
