using System;
using System.Collections;
using System.Collections.Generic;
using Spatail.Core;
using UnityEngine;

namespace Spatail
{
    public enum AppState
    {
        /// <summary>Waiting for the device to understand the room.</summary>
        Starting,
        /// <summary>Playing the scan and placing the station.</summary>
        Scanning,
        Ready,
        /// <summary>A request is with the planner.</summary>
        Thinking,
    }

    /// <summary>
    /// The heart of Spatail on a device. It owns the room model, the registered site, the planner and the set of
    /// builds, and it is the one place requests go through: say something, get content in the room.
    /// Platform adapters call <see cref="Launch"/> with their services; everything else is shared.
    /// </summary>
    public sealed class SpatailApp : MonoBehaviour
    {
        public static SpatailApp Instance { get; private set; }

        public const string DefaultSite = "Spatail/pump_station";

        public PlatformServices Platform { get; private set; }
        public SpatailSettings Settings { get; private set; }
        public SiteDefinition Site { get; private set; }
        public RoomModel Room { get; private set; }
        public Pose3 Station { get; private set; }
        public BuildStore Store { get; private set; }
        public PlannerRouter Planner { get; private set; }
        public SessionLink Session { get; private set; }
        public InteractionSystem Interaction { get; private set; }
        public TwinRenderer Twin { get; private set; }
        public ContentRenderer Content { get; private set; }
        public PromptBar PromptBar { get; private set; }

        public AppState State { get; private set; }
        public Camera Head => Platform != null ? Platform.Head : null;
        public bool IsMovingStation { get; private set; }

        public string LastPrompt { get; private set; }
        public PlanResult LastPlan { get; private set; }
        public string SessionStatus { get; private set; } = "Not sharing";
        /// <summary>Recent log lines, newest last. The dev tool shows these.</summary>
        public readonly List<string> LogLines = new List<string>();

        public event Action<AppState> StateChanged;
        /// <summary>A request finished: (prompt, plan). Raised for local, remote and session-delivered plans.</summary>
        public event Action<string, PlanResult> Planned;
        /// <summary>The room model was rebuilt (station moved, surfaces changed).</summary>
        public event Action RoomChanged;
        public event Action<string> Logged;

        float _presenceTimer;
        float _thinkingSince;
        Vector3 _lastStationDrag;
        bool _suppressBroadcast;

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
            Store = new BuildStore();

            var siteAsset = Resources.Load<TextAsset>(DefaultSite);
            if (siteAsset == null) throw new InvalidOperationException("Site definition missing at Resources/" + DefaultSite);
            Site = SiteDefinition.Parse(siteAsset.text);

            var remote = new RemotePlanner(new UnityHttpTransport(), () => Settings.PlannerUrl, () => Settings.PlannerToken);
            Planner = new PlannerRouter(new OfflinePlanner(), remote) { UseRemote = Settings.UseAi };

            Room = new RoomModel();
            Station = Pose3.Identity;

            Interaction = gameObject.AddComponent<InteractionSystem>();
            foreach (PointerSource pointer in services.Pointers)
            {
                Interaction.Register(pointer);
                if (pointer.ShowRay)
                {
                    var visual = new GameObject("Pointer Visual").AddComponent<PointerVisual>();
                    visual.transform.SetParent(transform, false);
                    visual.Pointer = pointer;
                }
            }
            Interaction.PressedEmpty += OnPressedEmpty;

            Twin = new GameObject("Twin").AddComponent<TwinRenderer>();
            Twin.transform.SetParent(transform, false);
            Twin.Mode = Settings.TwinMode;

            Content = new GameObject("Content").AddComponent<ContentRenderer>();
            Content.transform.SetParent(transform, false);
            Content.Init(this);

            Session = new SessionLink { LocalDevice = services.DeviceName };
            Session.StatusChanged += s => { SessionStatus = s; Log("Session: " + s); };
            Session.PeerJoined += OnPeerJoined;
            Session.PeerLeft += p => Log(p.name + " left the session.");
            Session.PlanReceived += OnPlanReceived;
            Session.PromptReceived += (text, peer) => Submit(text, peer.name);
            Session.ReplyReceived += OnReplyReceived;

            if (services.WorldUi)
            {
                PromptBar = new GameObject("Prompt Bar").AddComponent<PromptBar>();
                PromptBar.transform.SetParent(transform, false);
                PromptBar.Init(this);
            }

            if (services.Room != null) services.Room.Changed += OnRoomProviderChanged;
            Log("Spatail started on " + services.DeviceName + ". Site: " + Site.name + ".");
            StartCoroutine(Boot());
        }

        IEnumerator Boot()
        {
            SetState(AppState.Starting);
            float waited = 0f;
            while (Platform.Room != null && !Platform.Room.IsReady)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            Log("Room ready after " + waited.ToString("0.0") + " s: " + (Platform.Room != null ? Platform.Room.Status : "no provider"));

            SetState(AppState.Scanning);
            RoomModel raw = CaptureRaw();
            Station = SiteBinder.SuggestStationPose(raw, Site);
            Rebind(raw);

            if (Head != null) ScanEffect.Play(Station.position.ToUnity(), Head.transform.right, Room.floorY);
            yield return new WaitForSeconds(ScanEffect.Duration * 0.75f);

            // What the scan recognised: the labels from the opening of the "Built by you" sequence.
            Store.Add(BuildFactory.Labels(Room));
            SetState(AppState.Ready);
        }

        // ---- room --------------------------------------------------------------------------

        RoomModel CaptureRaw()
        {
            RoomModel raw = Platform.Room != null ? Platform.Room.Capture() : new RoomModel();
            if (Head != null)
            {
                raw.userPosition = Head.transform.position.ToCore();
                raw.userForward = Head.transform.forward.ToCore();
            }
            return raw;
        }

        void OnRoomProviderChanged()
        {
            if (State == AppState.Starting || IsMovingStation) return;
            Rebind(CaptureRaw());
        }

        /// <summary>Rebuilds the room model from the device's latest view of the room and re-lays out all content.</summary>
        public void Rebind() => Rebind(CaptureRaw());

        void Rebind(RoomModel raw)
        {
            Room = SiteBinder.Bind(Site, raw, Station);
            Twin.Rebuild(Room);
            Content.Relayout();
            if (RoomChanged != null) RoomChanged();
        }

        /// <summary>Puts the station (the registered equipment's frame) somewhere new.</summary>
        public void PlaceStation(Pose3 pose)
        {
            Station = pose;
            Rebind();
        }

        public void RotateStation(float degrees)
        {
            if (Mathf.Abs(degrees) < 1e-4f) return;
            Station = new Pose3(Station.position, Quat.Yaw(degrees) * Station.rotation);
            Rebind();
        }

        /// <summary>Enters or leaves the mode where the station follows the pointer along the floor.</summary>
        public void SetMovingStation(bool moving)
        {
            if (IsMovingStation == moving) return;
            IsMovingStation = moving;
            Interaction.Suspended = moving;
            if (moving) _lastStationDrag = Station.position.ToUnity();
            Log(moving ? "Point at the floor to move the pump; press to drop it." : "Pump placed.");
            if (!moving) Rebind();
        }

        void OnPressedEmpty(PointerSource pointer)
        {
            if (IsMovingStation) SetMovingStation(false);
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
                        Station = new Pose3(point.ToCore(), Station.rotation);
                        Rebind();
                    }
                }
            }
            if (Platform.StationRotateInput != null) RotateStation(Platform.StationRotateInput());
        }

        // ---- requests ----------------------------------------------------------------------

        /// <summary>
        /// The entry point for everything the user asks for. Plans the request, applies the result to the room and
        /// shares it with anyone in the session.
        /// </summary>
        public async void Submit(string prompt, string from = null)
        {
            prompt = (prompt ?? "").Trim();
            if (prompt.Length == 0 || State == AppState.Starting || State == AppState.Scanning) return;

            // A device that has joined someone else's session sends its requests to the host, which plans them
            // once for everybody.
            if (from == null && Session.IsActive && !Session.IsHost && Session.Peers.Count > 0)
            {
                LastPrompt = prompt;
                Log("> " + prompt + "  (sent to host)");
                Session.SendPrompt(prompt);
                SetState(AppState.Thinking);
                return;
            }

            LastPrompt = prompt;
            Log("> " + prompt + (from != null ? "  (from " + from + ")" : ""));
            SetState(AppState.Thinking);

            Room.userPosition = Head != null ? Head.transform.position.ToCore() : Room.userPosition;
            Room.userForward = Head != null ? Head.transform.forward.ToCore() : Room.userForward;
            var request = new PlanRequest
            {
                prompt = prompt,
                room = Room,
                site = Site,
                builds = Store.Builds,
                focusEntityId = Interaction.FocusEntityId,
            };

            PlanResult plan;
            try
            {
                Planner.UseRemote = Settings.UseAi;
                plan = await Planner.PlanAsync(request);
            }
            catch (Exception ex)
            {
                plan = new PlanResult { reply = "Something went wrong while planning that.", chip = "", error = ex.Message, planner = "error" };
            }
            if (this == null) return; // destroyed while waiting

            Apply(plan);
            Finish(prompt, plan);
            if (Session.IsActive && Session.IsHost) Session.SendReply(plan.reply, plan.chip, plan.planner);
        }

        void Finish(string prompt, PlanResult plan)
        {
            LastPlan = plan;
            string timing = plan.latencyMs > 0 ? " in " + Math.Round(plan.latencyMs) + " ms" : "";
            Log("< [" + (plan.planner ?? "?") + timing + "] " + plan.reply + (string.IsNullOrEmpty(plan.chip) ? "" : "  (built " + plan.chip + ")"));
            if (!string.IsNullOrEmpty(plan.error)) Log("  note: " + plan.error);
            SetState(AppState.Ready);
            if (Planned != null) Planned(prompt, plan);
        }

        /// <summary>Applies a plan to the room and, unless it came from the session, shares it.</summary>
        public void Apply(PlanResult plan)
        {
            if (plan == null) return;
            Store.RetireScanLabels(plan);
            Store.Apply(plan);
            if (!_suppressBroadcast && Session.IsActive && plan.ops.Count > 0) Session.SendPlan(plan);
        }

        void ApplyLocalOp(PlanOp op)
        {
            var plan = new PlanResult { reply = "", chip = "", planner = "local" };
            plan.ops.Add(op);
            Apply(plan);
        }

        public void SetStep(string buildId, int step)
        {
            Build b = Store.Get(buildId);
            if (b == null || b.stepCount == 0) return;
            ApplyLocalOp(PlanOp.Step(buildId, Mathf.Clamp(step, 1, b.stepCount)));
        }

        public void RemoveBuild(string buildId) => ApplyLocalOp(PlanOp.Remove(buildId));

        public void ClearBuilds() => ApplyLocalOp(PlanOp.Clear());

        public void ToggleChecklistItem(string buildId, string elementId, int index)
        {
            if (!Store.ToggleChecklistItem(buildId, elementId, index)) return;
            // Ticks are not an op of their own; peers get the updated build.
            Build b = Store.Get(buildId);
            if (b != null && Session.IsActive)
            {
                var plan = new PlanResult { reply = "", chip = "", planner = "local" };
                plan.ops.Add(PlanOp.Add(b.Clone()));
                Session.SendPlan(plan);
            }
        }

        public void SetTwinMode(TwinMode mode)
        {
            Twin.Mode = mode;
            Settings.TwinMode = mode;
            Log("Twin display: " + mode);
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
            int port = SessionLink.DefaultPort;
            int colon = address.LastIndexOf(':');
            if (colon > 0 && int.TryParse(address.Substring(colon + 1), out port)) host = address.Substring(0, colon);
            else port = SessionLink.DefaultPort;
            Settings.SessionAddress = address;
            Session.LocalName = LocalName;
            SessionStatus = "Connecting to " + host + "...";
            Session.Join(host, port);
        }

        public void LeaveSession() => Session.Stop();

        void OnPeerJoined(Peer peer)
        {
            Log(peer.name + " joined the session" + (string.IsNullOrEmpty(peer.device) ? "." : " from " + peer.device + "."));
            // Bring the newcomer up to date with everything already in the room.
            if (Session.IsHost) Session.SendPlan(Store.Snapshot());
        }

        void OnPlanReceived(PlanResult plan, Peer peer)
        {
            SpecJson.Sanitize(plan, Room);
            _suppressBroadcast = true;
            Apply(plan);
            _suppressBroadcast = false;
        }

        void OnReplyReceived(string text, string chip, string planner)
        {
            var plan = new PlanResult { reply = text, chip = chip, planner = planner + " (host)" };
            Finish(LastPrompt ?? "", plan);
        }

        // ---- frame loop --------------------------------------------------------------------

        void Update()
        {
            Session.Pump();
            if (Head != null && Room != null)
            {
                Room.userPosition = Head.transform.position.ToCore();
                Room.userForward = Head.transform.forward.ToCore();
            }
            if (IsMovingStation) UpdateStationMove();
            // A request forwarded to a session host that never answers must not leave the UI stuck.
            if (State == AppState.Thinking && Time.unscaledTime - _thinkingSince > 60f)
            {
                Log("No answer after 60 s; giving up on that request.");
                SetState(AppState.Ready);
            }

            if (Session.IsActive && Head != null)
            {
                _presenceTimer += Time.unscaledDeltaTime;
                if (_presenceTimer >= 0.15f)
                {
                    _presenceTimer = 0f;
                    Session.SendPresence(
                        Station.InverseTransformPoint(Head.transform.position.ToCore()),
                        Station.InverseTransformDirection(Head.transform.forward.ToCore()));
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
            if (state == AppState.Thinking) _thinkingSince = Time.unscaledTime;
            if (StateChanged != null) StateChanged(state);
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
