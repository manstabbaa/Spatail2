using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meta.XR;
using Spatail.Core;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Spatail.Quest
{
    /// <summary>
    /// What a Quest senses, live. Nothing is loaded from a stored room scan and Space Setup is never opened: the
    /// headset reads the place as the wearer moves through it.
    ///
    /// - Rays are cast into the headset's live depth (MR Utility Kit's <see cref="EnvironmentRaycastManager"/>).
    /// - Floors, walls and table tops grow in a <see cref="SurfaceTracker"/> fed by a sparse grid of those rays
    ///   through the wearer's view, a few times a second.
    /// - The camera frame the planner looks at comes from the passthrough camera
    ///   (<see cref="PassthroughCameraAccess"/>), with the lens intrinsics and the camera pose of that frame.
    ///
    /// Every part degrades on its own. Without depth (older headsets, or the Spatial data permission refused) rays
    /// fall back to what has been tracked and then to a flat floor; without the camera the planner is told where the
    /// wearer is looking but gets no image. <see cref="Status"/> always says which of these is the case.
    /// </summary>
    public sealed class QuestSensor : MonoBehaviour, ISpaceSensor
    {
        public OVRCameraRig Rig;

        /// <summary>Long side of the image sent to the planner, in pixels.</summary>
        public int CaptureLongSide = 1024;
        /// <summary>Flip the captured image if a device delivers it upside down.</summary>
        public bool FlipCapturedImage;
        /// <summary>Scan passes per second while the wearer is moving.</summary>
        public float ScansPerSecond = 4f;
        /// <summary>Most depth rays cast for scanning in any one frame.</summary>
        public int RaysPerFrame = 12;

        // A pass covers the middle of the view. Rays that leave the depth camera's frustum simply report so.
        const int GridColumns = 8, GridRows = 6;
        const float GridHalfWidth = 0.62f, GridHalfHeight = 0.5f;   // tan of the half angles: about 32 and 27 degrees
        const float ScanRange = 6f;
        const float ChangedInterval = 0.75f;
        const float TrackingTimeout = 5f;
        const float CaptureTimeout = 4f;

        enum Feature { Unsupported, WaitingForPermission, Denied, Starting, On }

        readonly SurfaceTracker _tracker = new SurfaceTracker();
        readonly List<Action<ViewFrame, byte[]>> _captureWaiters = new List<Action<ViewFrame, byte[]>>();
        readonly List<EncodeJob> _encoding = new List<EncodeJob>();

        EnvironmentRaycastManager _raycaster;
        PassthroughCameraAccess _camera;
        Feature _depth = Feature.Starting, _cameraState = Feature.Starting;
        Feature _saidDepth = Feature.Starting, _saidCamera = Feature.Starting;
        bool _surveyed, _saidCameraFailed;
        bool _tracking, _trackingTimedOut, _capturing, _cameraFailed;
        int _cameraRetries, _captureId;
        float _cameraRetryAt, _captureStarted;

        // Scanning.
        int _scanIndex = -1;
        float _nextScan, _jitterX, _jitterY;
        Vector3 _scanOrigin, _lastScanPosition;
        Quaternion _scanRotation, _lastScanRotation = Quaternion.identity;
        int _stillPasses;

        // Changed, at most once per interval.
        bool _changedPending;
        float _lastChanged = -10f;

        sealed class EncodeJob
        {
            public int Id;
            public ViewFrame View;
            public Task<byte[]> Task;
            public byte[] Raw;
            public int Width, Height;
        }

        // ---- ISpaceSensor ------------------------------------------------------------------------

        /// <summary>Ready as soon as head tracking runs. Depth and the camera join when they are available.</summary>
        public bool IsReady => _tracking || _trackingTimedOut;

        public event Action Changed;

        public IReadOnlyList<RoomEntity> Surfaces => _tracker.Surfaces;

        /// <summary>The tracked floor, or the tracking origin's floor level until one has been seen.</summary>
        public float FloorY => _tracker.FloorY;

        public string Status
        {
            get
            {
                if (!IsReady) return "Waiting for head tracking";
                string depth;
                switch (_depth)
                {
                    case Feature.On: depth = "Depth sensing on"; break;
                    case Feature.Starting: depth = "Depth sensing starting"; break;
                    case Feature.WaitingForPermission: depth = "Waiting for the Spatial data permission"; break;
                    case Feature.Denied: depth = "Spatial data permission denied: using a flat floor"; break;
                    default: depth = "No depth sensing on this headset: using a flat floor"; break;
                }
                int count = _tracker.Surfaces.Count;
                if (_depth == Feature.On || count > 0) depth += ", " + count + (count == 1 ? " surface" : " surfaces") + " so far";

                string camera;
                switch (_cameraState)
                {
                    case Feature.On:
                        camera = "Camera on (" + _camera.CurrentResolution.x + "x" + _camera.CurrentResolution.y + ")";
                        break;
                    case Feature.Starting: camera = _cameraFailed ? "Camera could not start: the planner cannot see" : "Camera starting"; break;
                    case Feature.WaitingForPermission: camera = "Waiting for the camera permission"; break;
                    case Feature.Denied: camera = "Camera permission denied: the planner cannot see"; break;
                    default: camera = "No passthrough camera on this headset: the planner cannot see"; break;
                }
                return (_trackingTimedOut && !_tracking ? "Head tracking has not started. " : "") + depth + ". " + camera + ".";
            }
        }

        /// <summary>
        /// Live depth first. Where depth has no answer (outside the depth camera's view, not started, not supported)
        /// the surfaces tracked so far, and finally a flat floor at the current estimate.
        /// </summary>
        public bool Raycast(Vec3 origin, Vec3 direction, float maxDistance, out RayHit hit)
        {
            direction = direction.Normalized;
            if (DepthActive)
            {
                EnvironmentRaycastHit depthHit;
                if (_raycaster.Raycast(new Ray(origin.ToUnity(), direction.ToUnity()), out depthHit, Mathf.Min(maxDistance, 100f)))
                {
                    Vec3 point = depthHit.point.ToCore();
                    Vec3 normal = depthHit.normal.ToCore();
                    // The runtime sometimes has a point but no orientation for it; face the ray in that case.
                    if (normal.SqrLength < 0.25f) normal = direction * -1f;
                    hit = new RayHit { position = point, normal = normal.Normalized, distance = Vec3.Distance(origin, point) };
                    return true;
                }
            }

            if (GeometryRaycaster.Raycast(_tracker.Surfaces, origin, direction, maxDistance, out hit)) return true;

            if (direction.y < -1e-4f)
            {
                float t = (FloorY - origin.y) / direction.y;
                if (t > 0f && t <= maxDistance)
                {
                    hit = new RayHit { position = origin + direction * t, normal = Vec3.Up, distance = t, label = "floor" };
                    return true;
                }
            }
            hit = default(RayHit);
            return false;
        }

        /// <summary>
        /// The passthrough camera's latest frame as a JPEG, with the view it was taken from. When there is no camera
        /// (unsupported, refused, not started) the callback gets the head's view and no image.
        /// </summary>
        public void Capture(Action<ViewFrame, byte[]> done)
        {
            if (done == null) return;
            if (_camera == null || !_camera.isActiveAndEnabled || !_camera.IsPlaying)
            {
                done(HeadView(), null);
                return;
            }
            _captureWaiters.Add(done);
            if (_capturing) return;
            _capturing = true;
            _captureStarted = Time.unscaledTime;
            StartCoroutine(CaptureFrame(++_captureId));
        }

        // ---- start-up ----------------------------------------------------------------------------

        IEnumerator Start()
        {
            // Sensing is per-device, so wait until the runtime is up before asking what this one can do. The answers
            // are cached by the SDK, and asking too early would cache "no".
            float waited = 0f;
            while (!HeadTracked && waited < TrackingTimeout)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            _tracking = HeadTracked;
            _trackingTimedOut = !_tracking;
            if (_trackingTimedOut) Debug.LogWarning("[Spatail] Head tracking did not start within " + TrackingTimeout + " s; carrying on.");

            bool depthSupported = Supported(() => EnvironmentRaycastManager.IsSupported, "depth ray casting");
            bool cameraSupported = Supported(() => PassthroughCameraAccess.IsSupported, "the passthrough camera");
            _depth = depthSupported ? Feature.Starting : Feature.Unsupported;
            _cameraState = cameraSupported ? Feature.Starting : Feature.Unsupported;
            Debug.Log("[Spatail] This device: depth ray casting " + (depthSupported ? "supported" : "not supported") + ", passthrough camera " + (cameraSupported ? "supported" : "not supported") + ".");

            // Both SDK components only wait for their permission; neither asks for it. Create them first so they
            // start by themselves the moment the wearer answers, then ask.
            if (depthSupported) CreateRaycaster();
            if (cameraSupported) CreateCamera();
            _surveyed = true;
            yield return RequestPermissions(depthSupported, cameraSupported);
        }

        static bool HeadTracked => OVRPlugin.initialized && OVRPlugin.positionTracked;

        static bool Supported(Func<bool> query, string what)
        {
            try
            {
                return query();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Spatail] Could not tell whether this headset supports " + what + ": " + ex.Message);
                return false;
            }
        }

        void CreateRaycaster()
        {
            var host = new GameObject("Depth Raycaster");
            host.SetActive(false);
            host.transform.SetParent(transform, false);
            _raycaster = host.AddComponent<EnvironmentRaycastManager>();
            if (Rig != null) _raycaster.CustomTrackingSpace = Rig.trackingSpace;
            host.SetActive(true);
        }

        void CreateCamera()
        {
            var host = new GameObject("Passthrough Camera");
            host.SetActive(false);
            host.transform.SetParent(transform, false);
            _camera = host.AddComponent<PassthroughCameraAccess>();
            _camera.CameraPosition = PassthroughCameraAccess.CameraPositionType.Left;
            _camera.RequestedResolution = new Vector2Int(1280, 960);
            // The frame rate can only be set while the component is disabled. Frames are only needed on request.
            _camera.enabled = false;
            _camera.MaxFramerate = 15;
            _camera.enabled = true;
            host.SetActive(true);
        }

        IEnumerator RequestPermissions(bool depth, bool camera)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var wanted = new List<string>();
            if (depth && !Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission)) wanted.Add(OVRPermissionsRequester.ScenePermission);
            if (camera && !Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.PassthroughCameraAccessPermission)) wanted.Add(OVRPermissionsRequester.PassthroughCameraAccessPermission);
            if (wanted.Count == 0) yield break;

            if (wanted.Contains(OVRPermissionsRequester.ScenePermission)) _depth = Feature.WaitingForPermission;
            if (wanted.Contains(OVRPermissionsRequester.PassthroughCameraAccessPermission)) _cameraState = Feature.WaitingForPermission;

            int answered = 0;
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => answered++;
            callbacks.PermissionDenied += _ => answered++;
            // One request for both, so the system shows the dialogs one after the other.
            Permission.RequestUserPermissions(wanted.ToArray(), callbacks);

            // The callbacks do not arrive if another permission dialog was already open, so also poll, and stop
            // waiting after two minutes rather than reporting "waiting" forever.
            float waited = 0f;
            while (answered < wanted.Count && waited < 120f)
            {
                // Each part starts as soon as its own permission is there, whatever the other dialog is doing.
                if (_depth == Feature.WaitingForPermission && Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission)) _depth = Feature.Starting;
                if (_cameraState == Feature.WaitingForPermission && Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.PassthroughCameraAccessPermission)) _cameraState = Feature.Starting;
                if (_depth != Feature.WaitingForPermission && _cameraState != Feature.WaitingForPermission) break;
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            // Let the last dialog close and the grant settle before reading the result.
            yield return null;
            if (_depth == Feature.WaitingForPermission)
                _depth = Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission) ? Feature.Starting : Feature.Denied;
            if (_cameraState == Feature.WaitingForPermission)
                _cameraState = Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.PassthroughCameraAccessPermission) ? Feature.Starting : Feature.Denied;
            RaiseChangedSoon();
#else
            // In the Editor (Link or the Meta XR Simulator) there is nothing to ask for.
            yield break;
#endif
        }

        // ---- every frame -------------------------------------------------------------------------

        void OnEnable() => _tracker.Changed += RaiseChangedSoon;
        void OnDisable() => _tracker.Changed -= RaiseChangedSoon;

        void RaiseChangedSoon() => _changedPending = true;

        void Update()
        {
            if (!_tracking && _trackingTimedOut) _tracking = HeadTracked;
            if (Rig != null) _tracker.SetFloorHint(Rig.trackingSpace.position.y);

            UpdateFeatureStates();
            if (DepthActive) Scan();
            Announce();
            FinishEncodings();
            // Never leave a request hanging on the camera: whoever asked is waiting to build.
            if (_capturing && Time.unscaledTime - _captureStarted > CaptureTimeout)
            {
                Debug.LogWarning("[Spatail] The camera frame did not arrive within " + CaptureTimeout + " s; continuing without an image.");
                DeliverCapture(_captureId, HeadView(), null);
            }

            if (_changedPending && Time.unscaledTime - _lastChanged >= ChangedInterval)
            {
                _changedPending = false;
                _lastChanged = Time.unscaledTime;
                if (Changed != null) Changed();
            }
        }

        /// <summary>Says what is running whenever that changes, in the Console and in Spatail's own log.</summary>
        void Announce()
        {
            if (!IsReady || !_surveyed) return;
            if (_depth == _saidDepth && _cameraState == _saidCamera && _cameraFailed == _saidCameraFailed) return;
            _saidDepth = _depth;
            _saidCamera = _cameraState;
            _saidCameraFailed = _cameraFailed;
            string line = "Sensing: " + Status;
            Debug.Log("[Spatail] " + line);
            SpatailApp app = SpatailApp.Instance;
            if (app != null) app.Log(line);
        }

        bool DepthActive => _raycaster != null && _raycaster.isActiveAndEnabled && (_depth == Feature.On || _depth == Feature.Starting);

        void UpdateFeatureStates()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // A permission granted later, from the system settings, takes effect without a restart.
            if (_depth == Feature.Denied && Time.frameCount % 90 == 0 && Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission)) _depth = Feature.Starting;
            if (_cameraState == Feature.Denied && Time.frameCount % 90 == 0 && Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.PassthroughCameraAccessPermission)) _cameraState = Feature.Starting;
#endif
            if (_camera == null) return;
            if (_cameraState == Feature.Starting)
            {
                if (_camera.IsPlaying)
                {
                    _cameraState = Feature.On;
                    _cameraFailed = false;
                    _cameraRetries = 0;
                    RaiseChangedSoon();
                }
                // The component switches itself off when the camera cannot be opened.
                else if (!_camera.enabled)
                {
                    if (!_cameraFailed)
                    {
                        _cameraFailed = true;
                        _cameraRetryAt = Time.unscaledTime + 10f;
                        Debug.LogWarning("[Spatail] The passthrough camera could not be started.");
                    }
                    // On a headset the camera can be busy for a moment; in the Editor a retry would only repeat the error.
                    else if (Application.isMobilePlatform && _cameraRetries < 3 && Time.unscaledTime >= _cameraRetryAt)
                    {
                        _cameraRetries++;
                        _cameraRetryAt = Time.unscaledTime + 10f;
                        _camera.enabled = true;
                    }
                }
            }
            // Pausing the app stops the camera; it starts again by itself on resume.
            else if (_cameraState == Feature.On && !_camera.IsPlaying) _cameraState = Feature.Starting;
        }

        // ---- surfaces from depth -----------------------------------------------------------------

        /// <summary>
        /// A grid of depth rays through the wearer's view, spread over a few frames. Each pass is frozen at the head
        /// pose it started with and offset a little from the last, so over time the samples cover the view evenly.
        /// </summary>
        void Scan()
        {
            Transform head = Rig != null ? Rig.centerEyeAnchor : null;
            if (head == null) return;

            if (_scanIndex < 0)
            {
                if (Time.unscaledTime < _nextScan) return;
                _nextScan = Time.unscaledTime + 1f / Mathf.Max(0.5f, ScansPerSecond);

                // Standing still and looking at the same thing adds nothing after a few passes.
                bool moved = Vector3.Distance(head.position, _lastScanPosition) > 0.03f || Quaternion.Angle(head.rotation, _lastScanRotation) > 2f;
                if (moved) _stillPasses = 0;
                else if (_depth == Feature.On && ++_stillPasses > 4) return;
                _lastScanPosition = head.position;
                _lastScanRotation = head.rotation;

                _scanOrigin = head.position;
                _scanRotation = head.rotation;
                _jitterX = UnityEngine.Random.value;
                _jitterY = UnityEngine.Random.value;
                _scanIndex = 0;
            }

            int budget = Mathf.Max(1, RaysPerFrame);
            const int total = GridColumns * GridRows;
            while (budget-- > 0 && _scanIndex < total)
            {
                int column = _scanIndex % GridColumns, row = _scanIndex / GridColumns;
                _scanIndex++;
                float x = ((column + _jitterX) / GridColumns * 2f - 1f) * GridHalfWidth;
                float y = ((row + _jitterY) / GridRows * 2f - 1f) * GridHalfHeight;
                Vector3 direction = _scanRotation * new Vector3(x, y, 1f).normalized;

                EnvironmentRaycastHit hit;
                bool found = _raycaster.Raycast(new Ray(_scanOrigin, direction), out hit, ScanRange);
                if (hit.status == EnvironmentRaycastHitStatus.NotReady || hit.status == EnvironmentRaycastHitStatus.NotSupported)
                {
                    // Not running yet (or no longer): try again on the next pass instead of spending the budget.
                    if (_depth == Feature.On) _depth = Feature.Starting;
                    _scanIndex = -1;
                    return;
                }
                if (_depth != Feature.On)
                {
                    _depth = Feature.On;
                    RaiseChangedSoon();
                }
                if (found && hit.normalConfidence > 0f) _tracker.AddSample(hit.point.ToCore(), hit.normal.ToCore());
            }
            if (_scanIndex >= total) _scanIndex = -1;
        }

        // ---- camera frames -----------------------------------------------------------------------

        ViewFrame HeadView()
        {
            Transform head = Rig != null ? Rig.centerEyeAnchor : transform;
            Camera eye = head.GetComponent<Camera>();
            float fov = eye != null ? eye.fieldOfView : 90f;
            float aspect = eye != null && eye.aspect > 0.01f ? eye.aspect : 1f;
            return ViewFrame.FromFov(head.position.ToCore(), head.rotation.ToCore(), fov, aspect);
        }

        /// <summary>
        /// The view of the camera frame that is current now: the camera's pose at that frame's timestamp, and the
        /// frustum from the lens intrinsics. The SDK's own viewport-to-ray conversion is sampled at two opposite
        /// corners, which accounts for the principal point and for the sensor crop at the current resolution.
        /// </summary>
        ViewFrame CameraView(int width, int height)
        {
            Pose pose = _camera.GetCameraPose();
            // Viewport space has its origin at the bottom left; ViewFrame's image space at the top left.
            Vector3 bottomLeft = _camera.ViewportPointToRay(new Vector2(0f, 0f), Pose.identity).direction;
            Vector3 topRight = _camera.ViewportPointToRay(new Vector2(1f, 1f), Pose.identity).direction;
            if (bottomLeft.z < 1e-4f || topRight.z < 1e-4f) return null;
            float left = bottomLeft.x / bottomLeft.z, right = topRight.x / topRight.z;
            float bottom = bottomLeft.y / bottomLeft.z, top = topRight.y / topRight.z;
            if (right - left < 1e-4f || top - bottom < 1e-4f) return null;
            return new ViewFrame
            {
                position = pose.position.ToCore(),
                rotation = pose.rotation.ToCore(),
                tanHalfX = (right - left) * 0.5f,
                tanHalfY = (top - bottom) * 0.5f,
                centerU = -left / (right - left),
                centerV = top / (top - bottom),
                width = width,
                height = height,
            };
        }

        IEnumerator CaptureFrame(int id)
        {
            // Take a frame that arrived this frame, so the pose read below belongs to the pixels read back.
            float waited = 0f;
            while (_camera != null && _camera.IsPlaying && !_camera.IsUpdatedThisFrame && waited < 0.5f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (_camera == null || !_camera.IsPlaying)
            {
                DeliverCapture(id, HeadView(), null);
                yield break;
            }

            Vector2Int source = _camera.CurrentResolution;
            float scale = Mathf.Min(1f, CaptureLongSide / (float)Mathf.Max(1, Mathf.Max(source.x, source.y)));
            int width = Mathf.Max(16, Mathf.RoundToInt(source.x * scale)), height = Mathf.Max(16, Mathf.RoundToInt(source.y * scale));
            ViewFrame view = CameraView(width, height);
            if (view == null)
            {
                DeliverCapture(id, HeadView(), null);
                yield break;
            }

            // The camera texture is filled on the render thread during this frame; read it once the frame is drawn.
            yield return new WaitForEndOfFrame();
            Texture texture = _camera != null && _camera.IsPlaying ? _camera.GetTexture() : null;
            if (texture == null)
            {
                DeliverCapture(id, HeadView(), null);
                yield break;
            }

            RenderTexture small = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            if (FlipCapturedImage) Graphics.Blit(texture, small, new Vector2(1f, -1f), new Vector2(0f, 1f));
            else Graphics.Blit(texture, small);
            RenderTexture.active = previous;

            // Read back without stalling the frame, then compress on a worker thread.
            AsyncGPUReadback.Request(small, 0, TextureFormat.RGBA32, request =>
            {
                RenderTexture.ReleaseTemporary(small);
                if (this == null) return;
                if (request.hasError)
                {
                    Debug.LogWarning("[Spatail] Reading the camera frame back failed.");
                    DeliverCapture(id, view, null);
                    return;
                }
                byte[] raw = request.GetData<byte>().ToArray();
                var job = new EncodeJob { Id = id, View = view, Raw = raw, Width = width, Height = height };
                job.Task = Task.Run(() => ImageConversion.EncodeArrayToJPG(raw, GraphicsFormat.R8G8B8A8_SRGB, (uint)width, (uint)height, 0, 80));
                _encoding.Add(job);
            });
        }

        void FinishEncodings()
        {
            for (int i = _encoding.Count - 1; i >= 0; i--)
            {
                EncodeJob job = _encoding[i];
                if (!job.Task.IsCompleted) continue;
                _encoding.RemoveAt(i);
                byte[] jpeg = null;
                if (job.Task.Status == TaskStatus.RanToCompletion) jpeg = job.Task.Result;
                else
                {
                    // If this Unity version refuses to compress off the main thread, do it here instead.
                    try
                    {
                        jpeg = ImageConversion.EncodeArrayToJPG(job.Raw, GraphicsFormat.R8G8B8A8_SRGB, (uint)job.Width, (uint)job.Height, 0, 80);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[Spatail] Compressing the camera frame failed: " + ex.Message);
                    }
                }
                DeliverCapture(job.Id, job.View, jpeg);
            }
        }

        void DeliverCapture(int id, ViewFrame view, byte[] jpeg)
        {
            // A capture that was given up on (see Update) may still finish later; its result is no longer wanted.
            if (!_capturing || id != _captureId) return;
            _capturing = false;
            var waiters = _captureWaiters.ToArray();
            _captureWaiters.Clear();
            foreach (Action<ViewFrame, byte[]> done in waiters)
            {
                try
                {
                    done(view, jpeg);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }
    }
}
