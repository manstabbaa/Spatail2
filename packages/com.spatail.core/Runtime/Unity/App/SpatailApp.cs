using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Spatail.Core;
using UnityEngine;

namespace Spatail
{
    public enum AppState
    {
        /// <summary>Waiting for the device to start tracking.</summary>
        Starting,
        Ready,
        /// <summary>A request is being built: sensing, planning, starting the program.</summary>
        Building,
    }

    /// <summary>
    /// Spatail on a device. A request goes in as words; the app captures what the device senses at that moment,
    /// has a program written for it, and runs that program in the space. Platform adapters call
    /// <see cref="Launch"/> with a camera, a sensor, pointers and text or speech input; everything else is shared.
    ///
    /// This class is also the script host: it is what programs' calls to scene, space and user land on.
    /// </summary>
    public sealed class SpatailApp : MonoBehaviour, IScriptHost
    {
        public static SpatailApp Instance { get; private set; }

        public const string StandInResource = "Spatail/pump_station";
        public const string LibraryResource = "Spatail/Script/spatail.js";
        public const string ExamplesResource = "Spatail/Script/Examples/";

        public PlatformServices Platform { get; private set; }
        public SpatailSettings Settings { get; private set; }
        public ExperienceRuntime Runtime { get; private set; }
        public Director Director { get; private set; }
        public RemoteBuilder Planner { get; private set; }
        public SessionLink Session { get; private set; }
        public InteractionSystem Interaction { get; private set; }
        public SceneHost Scene { get; private set; }
        public TwinRenderer Twin { get; private set; }
        public PromptBar PromptBar { get; private set; }

        /// <summary>The stand-in scene definition, and where it currently stands.</summary>
        public SiteDefinition StandIn { get; private set; }
        public Pose3 Station { get; private set; }
        /// <summary>Surfaces from the sensor plus, when the stand-in is on, its parts as props.</summary>
        public RoomModel Room { get; private set; }

        public AppState State { get; private set; }
        public Camera Head => Platform != null ? Platform.Head : null;
        public ISpaceSensor Sensor => Platform != null ? Platform.Sensor : null;
        public bool IsMovingStation { get; private set; }

        public string LastPrompt { get; private set; }
        public BuildResult LastResult { get; private set; }
        /// <summary>The camera frame sent with the last request, for the dev tool to show.</summary>
        public byte[] LastFrameJpeg { get; private set; }
        public string SessionStatus { get; private set; } = "Not sharing";
        public readonly List<string> LogLines = new List<string>();

        public event Action<AppState> StateChanged;
        /// <summary>A request finished: (prompt, result).</summary>
        public event Action<string, BuildResult> Built;
        /// <summary>The set of surfaces or props changed.</summary>
        public event Action RoomChanged;
        public event Action<string> Logged;

        readonly List<RoomEntity> _surfaces = new List<RoomEntity>();
        readonly List<RoomEntity> _props = new List<RoomEntity>();
        float _presenceTimer, _buildingSince;
        Vector3 _lastStationDrag;
        bool _stationPlaced;

        // ---- launch ------------------------------------------------------------------------

        public static SpatailApp Launch(PlatformServices services)
        {
            if (Instance != null) return Instance;
            var go = new GameObject("Spatail");
            DontDestroyOnLoad(go);
            var app = go.AddComponent<SpatailApp>();
            app.Init(services);
            return app;
        }

        void Init(PlatformServices services)
        {
            Instance = this;
            Platform = services;
            Settings = new SpatailSettings();
            Room = new RoomModel();
            Station = Pose3.Identity;

            var standIn = Resources.Load<TextAsset>(StandInResource);
            if (standIn != null) StandIn = SiteDefinition.Parse(standIn.text);

            Interaction = gameObject.AddComponent<InteractionSystem>();
            foreach (PointerSource pointer in services.Pointers)
            {
                Interaction.Register(pointer);
                if (!pointer.ShowRay) continue;
                var visual = new GameObject("Pointer Visual").AddComponent<PointerVisual>();
                visual.transform.SetParent(transform, false);
                visual.Pointer = pointer;
            }
            Interaction.PressedEmpty += pointer => { if (IsMovingStation) SetMovingStation(false); };

            Twin = new GameObject("Stand-in").AddComponent<TwinRenderer>();
            Twin.transform.SetParent(transform, false);
            Twin.Mode = Settings.TwinMode;

            Scene = new GameObject("Experiences").AddComponent<SceneHost>();
            Scene.transform.SetParent(transform, false);

            // The script runtime, with the same library file the browser lab runs.
            var library = Resources.Load<TextAsset>(LibraryResource);
            if (library == null) throw new InvalidOperationException("Runtime library missing at Resources/" + LibraryResource);
            Runtime = new ExperienceRuntime(this, library.text, SdfFont.MetricSet);
            Runtime.Logged += (experience, level, message) => Log((level == "error" ? "! " : "  ") + experience.Title + ": " + message);
            Runtime.Failed += experience => Log("! " + experience.Title + " stopped: " + FirstLine(experience.Error));
            Scene.NodeEvent += (id, type) => Runtime.DispatchEvent(id, type);

            Planner = new RemoteBuilder(new UnityHttpTransport(), () => Settings.PlannerUrl, () => Settings.PlannerToken);
            var demo = new DemoBuilder(id =>
            {
                var asset = Resources.Load<TextAsset>(ExamplesResource + id + ".js");
                return asset != null ? asset.text : null;
            });
            Director = new Director(Runtime, this, demo, Planner) { UseRemote = Settings.UseAi };

            Session = new SessionLink { LocalDevice = services.DeviceName };
            Session.StatusChanged += s => { SessionStatus = s; Log("Session: " + s); };
            Session.PeerJoined += peer => Log(peer.name + " joined" + (string.IsNullOrEmpty(peer.device) ? "." : " from " + peer.device + "."));
            Session.PeerLeft += peer => Log(peer.name + " left.");
            Session.PromptReceived += (text, peer) => Submit(text, peer.name);
            Session.ReplyReceived += OnReplyReceived;
            Session.ConfigReceived += OnConfigReceived;

            if (services.WorldUi)
            {
                PromptBar = new GameObject("Prompt Bar").AddComponent<PromptBar>();
                PromptBar.transform.SetParent(transform, false);
                PromptBar.Init(this);
            }

            if (services.Sensor != null) services.Sensor.Changed += OnSensorChanged;
            Log("Spatail started on " + services.DeviceName + ".");
            StartCoroutine(Boot());
        }

        IEnumerator Boot()
        {
            SetState(AppState.Starting);
            float waited = 0f;
            while (Sensor != null && !Sensor.IsReady)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            Log("Sensing ready after " + waited.ToString("0.0") + " s: " + (Sensor != null ? Sensor.Status : "no sensor"));
            RefreshRoom();
            SetState(AppState.Ready);
        }

        // ---- the space: surfaces from the sensor, props from the stand-in ----------------------------

        void OnSensorChanged()
        {
            if (State == AppState.Starting || IsMovingStation) return;
            RefreshRoom();
        }

        /// <summary>Rebuilds the room model from what the sensor has now. Running programs are not restarted.</summary>
        public void RefreshRoom()
        {
            var raw = new RoomModel { floorY = Sensor != null ? Sensor.FloorY : 0f };
            if (Sensor != null) raw.entities.AddRange(Sensor.Surfaces);
            if (Head != null)
            {
                raw.userPosition = Head.transform.position.ToCore();
                raw.userForward = Head.transform.forward.ToCore();
            }

            bool standIn = StandIn != null && Settings.StandIn;
            if (standIn)
            {
                if (!_stationPlaced)
                {
                    Station = SiteBinder.SuggestStationPose(raw, StandIn);
                    _stationPlaced = true;
                }
                // The stand-in always stands on the device's current floor estimate.
                Station = new Pose3(new Vec3(Station.position.x, raw.floorY, Station.position.z), Station.rotation);
                Room = SiteBinder.Bind(StandIn, raw, Station);
            }
            else Room = raw;

            _surfaces.Clear();
            _props.Clear();
            foreach (RoomEntity e in Room.entities) (e.isVirtual ? _props : _surfaces).Add(e);
            Twin.Rebuild(standIn ? Room : null);
            if (RoomChanged != null) RoomChanged();
        }

        public void SetStandIn(bool on)
        {
            if (Settings.StandIn == on) return;
            Settings.StandIn = on;
            _stationPlaced = false;
            RefreshRoom();
            Log(on ? "Stand-in pump placed in front of you." : "Stand-in removed. Working from what the device senses.");
        }

        public void PlaceStation(Pose3 pose)
        {
            Station = pose;
            _stationPlaced = true;
            RefreshRoom();
        }

        /// <summary>Puts the stand-in back in front of the user.</summary>
        public void ReplaceStation()
        {
            _stationPlaced = false;
            RefreshRoom();
        }

        public void RotateStation(float degrees)
        {
            if (Mathf.Abs(degrees) < 1e-4f) return;
            PlaceStation(new Pose3(Station.position, Quat.Yaw(degrees) * Station.rotation));
        }

        public void SetMovingStation(bool moving)
        {
            if (IsMovingStation == moving || (moving && !Settings.StandIn)) return;
            IsMovingStation = moving;
            Interaction.Suspended = moving;
            if (moving) _lastStationDrag = Station.position.ToUnity();
            Log(moving ? "Point at the floor to move the pump; press to drop it." : "Pump placed.");
            if (!moving) RefreshRoom();
        }

        void UpdateStationMove()
        {
            PointerSource pointer = Interaction.Primary;
            Ray ray;
            if (pointer != null && pointer.TryGetRay(out ray))
            {
                var floor = new Plane(Vector3.up, new Vector3(0, Room.floorY, 0));
                float enter;
                if (floor.Raycast(ray, out enter) && enter < 8f)
                {
                    Vector3 point = ray.GetPoint(enter);
                    if ((point - _lastStationDrag).sqrMagnitude > 0.0004f)
                    {
                        _lastStationDrag = point;
                        PlaceStation(new Pose3(point.ToCore(), Station.rotation));
                    }
                }
            }
            if (Platform.StationRotateInput != null) RotateStation(Platform.StationRotateInput());
        }

        // ---- IScriptHost: what programs can do ---------------------------------------------------

        void IScriptHost.CreateNode(int id, string kind, Dictionary<string, object> props) => Scene.Create(id, kind, props);
        void IScriptHost.UpdateNode(int id, Dictionary<string, object> props) => Scene.UpdateProps(id, props);
        void IScriptHost.SetTransform(int id, Vec3 position, Quat rotation, Vec3 scale) => Scene.SetTransform(id, position, rotation, scale);
        void IScriptHost.DestroyNode(int id) => Scene.DestroyNode(id);

        /// <summary>Whichever is nearer: the real world as the sensor has it, or a stand-in hologram.</summary>
        public bool Raycast(Vec3 origin, Vec3 direction, float maxDistance, out RayHit hit)
        {
            RayHit real = default(RayHit), prop;
            bool hitReal = Sensor != null && Sensor.Raycast(origin, direction, maxDistance, out real);
            bool hitProp = GeometryRaycaster.Raycast(_props, origin, direction, hitReal ? real.distance : maxDistance, out prop);
            hit = hitProp ? prop : real;
            return hitProp || hitReal;
        }

        public float FloorY => Sensor != null ? Sensor.FloorY : 0f;
        public IReadOnlyList<RoomEntity> Surfaces => _surfaces;
        public IReadOnlyList<RoomEntity> Props => _props;

        public void GetUser(out Vec3 position, out Vec3 forward, out Quat rotation)
        {
            Transform head = Head != null ? Head.transform : transform;
            position = head.position.ToCore();
            forward = head.forward.ToCore();
            rotation = head.rotation.ToCore();
        }

        /// <summary>
        /// space.find: look again, now. With the planner connected this captures a fresh frame and asks where the
        /// described thing is; without it, only stand-in props can be found by name.
        /// </summary>
        public void Find(string description, Action<List<RayHit>> done)
        {
            var hits = new List<RayHit>();
            if (!Director.RemoteActive || Sensor == null)
            {
                RoomEntity prop = Room.Resolve(description);
                if (prop != null && prop.isVirtual) hits.Add(new RayHit { position = prop.position, normal = Vec3.Up, distance = Vec3.Distance(prop.position, Head != null ? Head.transform.position.ToCore() : Vec3.Zero), label = prop.label });
                done(hits);
                return;
            }
            Sensor.Capture(async (view, jpeg) =>
            {
                if (view == null || jpeg == null) { done(hits); return; }
                List<LocatedPoint> points = await Planner.LocateAsync(description, jpeg);
                if (this == null) return;
                foreach (LocatedPoint point in points)
                {
                    Vec3 origin, direction;
                    view.Ray(point.u, point.v, out origin, out direction);
                    RayHit hit;
                    if (!Raycast(origin, direction, 15f, out hit)) continue;
                    hit.label = point.label;
                    hits.Add(hit);
                }
                done(hits);
            });
        }

        // ---- requests ------------------------------------------------------------------------

        /// <summary>The entry point for everything the user asks for.</summary>
        public async void Submit(string prompt, string from = null)
        {
            prompt = (prompt ?? "").Trim();
            if (prompt.Length == 0 || State == AppState.Starting) return;

            // A device that has joined someone else's session sends its requests to the host.
            if (from == null && Session.IsActive && !Session.IsHost && Session.Peers.Count > 0)
            {
                LastPrompt = prompt;
                Log("> " + prompt + "  (sent to host)");
                Session.SendPrompt(prompt);
                SetState(AppState.Building);
                return;
            }

            LastPrompt = prompt;
            Log("> " + prompt + (from != null ? "  (from " + from + ")" : ""));
            SetState(AppState.Building);

            BuildResult result;
            try
            {
                Director.UseRemote = Settings.UseAi;
                // Sense first, then build: the frame and the geometry are from this moment, wherever the user is.
                ViewFrame view = null;
                byte[] jpeg = null;
                if (Director.RemoteActive && Sensor != null)
                {
                    var captured = new TaskCompletionSource<bool>();
                    Sensor.Capture((v, j) => { view = v; jpeg = j; captured.TrySetResult(true); });
                    await captured.Task;
                    if (this == null) return;
                    if (Head != null && view != null) ScanEffect.Play(Head.transform.position + Head.transform.forward * 1.5f, Head.transform.right, FloorY);
                }
                LastFrameJpeg = jpeg;
                result = await Director.SubmitAsync(prompt, request =>
                {
                    request.view = view;
                    request.imageJpeg = jpeg;
                });
            }
            catch (Exception ex)
            {
                result = new BuildResult { reply = "Something went wrong while building that.", error = ex.Message, builder = "error" };
            }
            if (this == null) return;

            Finish(prompt, result);
            if (Session.IsActive && Session.IsHost) Session.SendReply(result.reply, result.title, result.builder);
        }

        void Finish(string prompt, BuildResult result)
        {
            LastResult = result;
            if (result.builder != "experience")
            {
                string timing = result.latencyMs > 0 ? " in " + Math.Round(result.latencyMs) + " ms" : "";
                Log("< [" + (result.builder ?? "?") + timing + "] " + result.reply + (string.IsNullOrEmpty(result.title) ? "" : "  (built " + result.title + ")"));
                if (!string.IsNullOrEmpty(result.error)) Log("  note: " + FirstLine(result.error));
            }
            SetState(AppState.Ready);
            if (Built != null) Built(prompt, result);
        }

        public void StopExperience(string id)
        {
            if (Runtime.Stop(id)) Log("Stopped " + id + ".");
        }

        public void ClearExperiences() => Runtime.StopAll();

        /// <summary>Runs a program directly, bypassing the planner. The dev tool uses this to edit and re-run code.</summary>
        public Experience RunProgram(string id, string title, string code)
        {
            Experience existing = Runtime.Get(id);
            return Runtime.Start(id, title, existing != null ? existing.Prompt : "(edited by hand)", code, existing != null ? existing.View : null);
        }

        /// <summary>
        /// Saves the last request (camera frame, depth samples, surfaces, what was said) and what was built for
        /// it to the planner service, as a test case from a real place.
        /// </summary>
        public async void SaveEpisode()
        {
            if (Director.LastRequest == null) { Log("Nothing to save yet: make a request first."); return; }
            if (!Planner.IsConfigured) { Log("Episodes are saved by the planner service. Set its URL first."); return; }
            string name = await Planner.SaveEpisodeAsync(Director.LastRequest, Director.LastResult);
            if (this == null) return;
            Log(name != null ? "Saved episode " + name + "." : "Could not save the episode.");
        }

        public void SetTwinMode(TwinMode mode)
        {
            Twin.Mode = mode;
            Settings.TwinMode = mode;
            Log("Stand-in display: " + mode);
        }

        // ---- session -----------------------------------------------------------------------

        string LocalName
        {
            get
            {
                string n = Settings.DisplayName;
                return string.IsNullOrEmpty(n) ? (Platform.DeviceName == "ios" ? "Dev tool" : "Headset") : n;
            }
        }

        public void HostSession()
        {
            Session.LocalName = LocalName;
            try { Session.Host(); }
            catch (Exception ex) { SessionStatus = "Could not host: " + ex.Message; Log(SessionStatus); }
        }

        public void JoinSession(string address)
        {
            address = (address ?? "").Trim();
            if (address.Length == 0) return;
            string host = address;
            int port;
            int colon = address.LastIndexOf(':');
            if (colon > 0 && int.TryParse(address.Substring(colon + 1), out port)) host = address.Substring(0, colon);
            else port = SessionLink.DefaultPort;
            Settings.SessionAddress = address;
            Session.LocalName = LocalName;
            SessionStatus = "Connecting to " + host + "...";
            Session.Join(host, port);
        }

        public void LeaveSession() => Session.Stop();

        /// <summary>Pushes this device's planner settings to everyone in the session, e.g. from the dev tool to a headset.</summary>
        public void SendPlannerSettingsToPeers()
        {
            if (!Session.IsActive) { Log("Join or host a session first."); return; }
            Session.SendConfig(Settings.PlannerUrl, Settings.PlannerToken, Settings.UseAi);
            Log("Sent planner settings to " + Session.Peers.Count + " peer(s).");
        }

        void OnConfigReceived(string plannerUrl, string plannerToken, bool useAi, Peer peer)
        {
            Settings.PlannerUrl = plannerUrl;
            Settings.PlannerToken = plannerToken;
            Settings.UseAi = useAi;
            Log(peer.name + " set the planner to " + (string.IsNullOrEmpty(plannerUrl) ? "demo only" : plannerUrl) + (useAi ? "" : " (AI off)") + ".");
        }

        void OnReplyReceived(string text, string title, string builder)
        {
            Finish(LastPrompt ?? "", new BuildResult { reply = text, title = title, builder = builder + " (host)" });
        }

        // ---- frame loop --------------------------------------------------------------------

        void Update()
        {
            Session.Pump();
            if (State != AppState.Starting) Runtime.Tick(Time.deltaTime);
            if (IsMovingStation) UpdateStationMove();

            // A request forwarded to a session host that never answers must not leave the UI stuck.
            if (State == AppState.Building && Time.unscaledTime - _buildingSince > 120f)
            {
                Log("No answer after two minutes; giving up on that request.");
                SetState(AppState.Ready);
            }

            if (Session.IsActive && Head != null)
            {
                _presenceTimer += Time.unscaledDeltaTime;
                if (_presenceTimer >= 0.15f)
                {
                    _presenceTimer = 0f;
                    Session.SendPresence(Head.transform.position.ToCore(), Head.transform.forward.ToCore());
                }
            }
        }

        void OnDestroy()
        {
            if (Session != null) Session.Dispose();
            if (Instance == this) Instance = null;
        }

        void OnApplicationQuit()
        {
            if (Session != null) Session.Dispose();
        }

        void SetState(AppState state)
        {
            if (State == state) return;
            State = state;
            if (state == AppState.Building) _buildingSince = Time.unscaledTime;
            if (StateChanged != null) StateChanged(state);
        }

        static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int nl = s.IndexOf('\n');
            return nl < 0 ? s : s.Substring(0, nl);
        }

        public void Log(string line)
        {
            LogLines.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + line);
            if (LogLines.Count > 300) LogLines.RemoveRange(0, LogLines.Count - 300);
            Debug.Log("[Spatail] " + line);
            if (Logged != null) Logged(line);
        }
    }
}
