using System.Collections.Generic;
using System.Text;
using Spatail.Core;
using UnityEngine;

namespace Spatail.DevTool
{
    /// <summary>The tools drawer: six tabs of developer tooling over the running app.</summary>
    public sealed partial class DevToolUi
    {
        static readonly string[] TabNames = { "Room", "Builds", "Spec", "Log", "Settings", "Link" };
        static readonly TwinMode[] TwinModes = { TwinMode.Solid, TwinMode.Ghost, TwinMode.Occluder, TwinMode.Hidden };

        int _tab;
        float _cy, _cw;      // layout cursor and width inside the current tab, in pixels

        // Cached text for the heavier tabs.
        PlanResult _specPlan;
        string _specJson = "", _specShown = "";
        int _logCount = -1;
        string _logText = "";
        string _localAddress = "";
        float _localAddressAt = -100f;

        // Editable copies of settings; written back when the user finishes typing.
        string _fieldUrl = "", _fieldToken = "", _fieldName = "", _fieldJoin = "";

        void LoadSettingsFields()
        {
            _fieldUrl = _app.Settings.PlannerUrl;
            _fieldToken = _app.Settings.PlannerToken;
            _fieldName = _app.Settings.DisplayName;
            _fieldJoin = _app.Settings.SessionAddress;
        }

        void CommitSettingsFields()
        {
            SpatailSettings settings = _app.Settings;
            if (!IsEditing("settings.url") && _fieldUrl != settings.PlannerUrl) { settings.PlannerUrl = _fieldUrl; _fieldUrl = settings.PlannerUrl; }
            if (!IsEditing("settings.token") && _fieldToken != settings.PlannerToken) { settings.PlannerToken = _fieldToken; _fieldToken = settings.PlannerToken; }
            if (!IsEditing("settings.name") && _fieldName != settings.DisplayName) { settings.DisplayName = _fieldName; _fieldName = settings.DisplayName; }
        }

        // ---- frame ---------------------------------------------------------------------------

        void DrawDrawer(Rect rect)
        {
            Claim(rect);
            Fill(rect, _panel);
            float pad = P(10);

            // Tab bar.
            float tabHeight = P(ChipHeight);
            float tabWidth = (rect.width - pad * 2f) / TabNames.Length;
            for (int i = 0; i < TabNames.Length; i++)
            {
                var r = new Rect(rect.x + pad + i * tabWidth, rect.y + pad, tabWidth, tabHeight);
                if (GUI.Button(r, TabNames[i], i == _tab ? _tabOn : _tabOff)) _tab = i;
            }

            var area = new Rect(rect.x + pad, rect.y + pad + tabHeight + P(8), rect.width - pad * 2f, rect.height - pad * 2f - tabHeight - P(8));
            ScrollState scroll = BeginScroll("tab" + _tab, area, false);
            _cy = 0f;
            _cw = area.width;
            switch (_tab)
            {
                case 0: DrawRoomTab(); break;
                case 1: DrawBuildsTab(); break;
                case 2: DrawSpecTab(); break;
                case 3: DrawLogTab(scroll, area.height); break;
                case 4: DrawSettingsTab(); break;
                default: DrawLinkTab(); break;
            }
            EndScroll(scroll, _cy);
        }

        Rect Next(float heightPoints, float gapPoints = 6f)
        {
            var r = new Rect(0, _cy, _cw, P(heightPoints));
            _cy += P(heightPoints) + P(gapPoints);
            return r;
        }

        void Heading(string text) => GUI.Label(Next(22, 4), text, _textBold);

        void Line(string text, GUIStyle style = null) => GUI.Label(Next(20, 2), text, style ?? _textMuted);

        /// <summary>Splits a row into equal buttons and returns the index pressed, or -1.</summary>
        int ButtonRow(Rect row, string[] labels, GUIStyle[] styles)
        {
            float gap = P(8);
            float w = (row.width - gap * (labels.Length - 1)) / labels.Length;
            int pressed = -1;
            for (int i = 0; i < labels.Length; i++)
                if (GUI.Button(new Rect(row.x + i * (w + gap), row.y, w, row.height), labels[i], styles[i])) pressed = i;
            return pressed;
        }

        // ---- Room ----------------------------------------------------------------------------

        void DrawRoomTab()
        {
            RoomModel room = _app.Room;
            Heading("What Spatail sees");
            if (room == null || room.entities.Count == 0)
            {
                Line("Nothing yet. " + (_room != null ? _room.Status : ""));
                return;
            }
            Vec3 user = room.userPosition, station = _app.Station.position;
            Line(room.entities.Count + " entities · floor at y = " + room.floorY.ToString("0.00") + " m");
            Line("You: " + user.x.ToString("0.0") + ", " + user.y.ToString("0.0") + ", " + user.z.ToString("0.0") +
                 "   Pump: " + station.x.ToString("0.0") + ", " + station.z.ToString("0.0") + " heading " + Mathf.RoundToInt(_app.Station.rotation.YawDegrees) + "°");
            _cy += P(4);

            foreach (RoomEntity e in room.entities)
            {
                Rect card = Next(50, 6);
                Fill(card, _card);
                float pad = P(10);
                string kind = e.kind + (EntityKind.IsSurface(e.kind) || e.kind == EntityKind.Equipment ? "" : (e.isVirtual ? " · stand-in" : " · real"));
                float badgeWidth = WidthOf(_badge, kind);
                var badge = new Rect(card.xMax - pad - badgeWidth, card.y + P(8), badgeWidth, P(20));
                GUI.Label(badge, kind, _badge);
                GUI.Label(new Rect(card.x + pad, card.y + P(6), badge.x - card.x - pad * 2f, P(20)), string.IsNullOrEmpty(e.label) ? e.id : e.label, _textBold);
                string size = e.size.x.ToString("0.00") + " × " + e.size.y.ToString("0.00") + " × " + e.size.z.ToString("0.00") + " m";
                GUI.Label(new Rect(card.x + pad, card.y + P(27), card.width - pad * 2f, P(18)), e.id + " · " + size, _textSmall);
            }
        }

        // ---- Builds --------------------------------------------------------------------------

        void DrawBuildsTab()
        {
            IReadOnlyList<Build> builds = _app.Store.Builds;
            Heading("In the room now");
            if (builds.Count == 0)
            {
                Line("Nothing has been built yet. Try a suggestion below.");
                return;
            }

            // Copy first: the buttons below change the store.
            var snapshot = new List<Build>(builds);
            foreach (Build b in snapshot)
            {
                bool stepped = b.stepCount > 0;
                Rect card = Next(stepped ? 102 : 96, 8);
                Fill(card, _card);
                float pad = P(10);
                float inner = card.width - pad * 2f;
                GUI.Label(new Rect(card.x + pad, card.y + P(6), inner, P(20)), b.title + "  ·  " + b.kind, _textBold);
                GUI.Label(new Rect(card.x + pad, card.y + P(26), inner, P(18)), Summarise(b), _textSmall);

                var row = new Rect(card.x + pad, card.yMax - pad - P(RowHeight), inner, P(RowHeight));
                if (stepped)
                {
                    float w = (row.width - P(16)) / 4f;
                    GUI.enabled = b.currentStep > 1;
                    if (GUI.Button(new Rect(row.x, row.y, w, row.height), "Prev", _buttonQuiet)) _app.SetStep(b.id, b.currentStep - 1);
                    GUI.enabled = true;
                    var label = new GUIStyle(_text) { alignment = TextAnchor.MiddleCenter };
                    GUI.Label(new Rect(row.x + w, row.y, w + P(8), row.height), b.currentStep + " of " + b.stepCount, label);
                    GUI.enabled = b.currentStep < b.stepCount;
                    if (GUI.Button(new Rect(row.x + w * 2f + P(8), row.y, w, row.height), "Next", _buttonQuiet)) _app.SetStep(b.id, b.currentStep + 1);
                    GUI.enabled = true;
                    if (GUI.Button(new Rect(row.xMax - w, row.y, w, row.height), "Remove", _buttonDanger)) _app.RemoveBuild(b.id);
                }
                else
                {
                    float w = Mathf.Min(P(120), row.width);
                    if (GUI.Button(new Rect(row.xMax - w, row.y, w, row.height), "Remove", _buttonDanger)) _app.RemoveBuild(b.id);
                }
            }
            if (GUI.Button(Next(RowHeight), "Clear everything", _buttonSecondary)) _app.ClearBuilds();
        }

        static string Summarise(Build b)
        {
            var counts = new Dictionary<string, int>();
            foreach (Element e in b.elements)
            {
                int n;
                counts.TryGetValue(e.type, out n);
                counts[e.type] = n + 1;
            }
            var sb = new StringBuilder();
            sb.Append(b.elements.Count).Append(b.elements.Count == 1 ? " element: " : " elements: ");
            bool first = true;
            foreach (var pair in counts)
            {
                if (!first) sb.Append(", ");
                first = false;
                sb.Append(pair.Value).Append(' ').Append(pair.Key);
            }
            return sb.ToString();
        }

        // ---- Spec ----------------------------------------------------------------------------

        void DrawSpecTab()
        {
            PlanResult plan = _app.LastPlan;
            Heading("Last plan");
            if (plan == null)
            {
                Line("No plan yet. Send a request and its spec appears here.");
                return;
            }
            if (!ReferenceEquals(plan, _specPlan))
            {
                _specPlan = plan;
                _specJson = SpecJson.Write(plan, true);
                // IMGUI cannot lay out very long strings; show the head and keep the whole thing for Copy.
                const int limit = 9000;
                _specShown = _specJson.Length <= limit ? _specJson : _specJson.Substring(0, limit) + "\n… (" + (_specJson.Length - limit) + " more characters; use Copy)";
            }
            Line("\"" + Truncate(_app.LastPrompt, 70) + "\"", _text);
            string timing = plan.latencyMs > 0 ? Mathf.RoundToInt((float)plan.latencyMs) + " ms" : "";
            Line((plan.planner ?? "") + (timing.Length > 0 ? " · " + timing : "") + " · " + plan.ops.Count + (plan.ops.Count == 1 ? " op" : " ops"));
            if (!string.IsNullOrEmpty(plan.error)) Line("Note: " + Truncate(plan.error, 90), _textAccent);

            if (GUI.Button(Next(RowHeight, 8), "Copy JSON", _buttonSecondary))
            {
                GUIUtility.systemCopyBuffer = _specJson;
                Notice("Plan JSON copied.");
            }
            float height = _textMono.CalcHeight(new GUIContent(_specShown), _cw);
            GUI.Label(new Rect(0, _cy, _cw, height), _specShown, _textMono);
            _cy += height + P(8);
        }

        // ---- Log -----------------------------------------------------------------------------

        void DrawLogTab(ScrollState scroll, float viewHeight)
        {
            List<string> lines = _app.LogLines;
            bool grew = lines.Count != _logCount;
            if (grew)
            {
                _logCount = lines.Count;
                var sb = new StringBuilder();
                for (int i = Mathf.Max(0, lines.Count - 120); i < lines.Count; i++) sb.Append(lines[i]).Append('\n');
                _logText = sb.ToString();
            }
            Heading("Log");
            if (GUI.Button(Next(RowHeight, 8), "Copy log", _buttonSecondary))
            {
                GUIUtility.systemCopyBuffer = string.Join("\n", lines.ToArray());
                Notice("Log copied.");
            }
            float height = _textMono.CalcHeight(new GUIContent(_logText), _cw);
            GUI.Label(new Rect(0, _cy, _cw, height), _logText, _textMono);
            _cy += height + P(8);
            // Follow new lines unless the user is in the middle of scrolling back.
            if (grew && !scroll.pressed) scroll.position = Mathf.Max(0f, _cy - viewHeight);
        }

        // ---- Settings ------------------------------------------------------------------------

        void DrawSettingsTab()
        {
            SpatailSettings settings = _app.Settings;

            Heading("AI planner");
            Line("Address of the Spatail planner service on your network.");
            TextInput("settings.url", Next(RowHeight), ref _fieldUrl, "http://192.168.1.20:8787", TouchScreenKeyboardType.URL);
            TextInput("settings.token", Next(RowHeight), ref _fieldToken, "Access token (optional)", TouchScreenKeyboardType.Default, false, true);
            bool ai = settings.UseAi;
            int choice = ButtonRow(Next(ChipHeight, 10), new[] { "AI when available", "On-device only" }, new[] { ai ? _segmentOn : _segmentOff, ai ? _segmentOff : _segmentOn });
            if (choice >= 0) settings.UseAi = choice == 0;

            Heading("You");
            TextInput("settings.name", Next(RowHeight, 10), ref _fieldName, "Name shown to others in a session", TouchScreenKeyboardType.Default);

            Heading("Twin");
            Line("Solid when the real pump is not here; Ghost or Occluder when it is.");
            var styles = new GUIStyle[TwinModes.Length];
            var labels = new string[TwinModes.Length];
            for (int i = 0; i < TwinModes.Length; i++)
            {
                labels[i] = TwinModes[i].ToString();
                styles[i] = _app.Twin.Mode == TwinModes[i] ? _segmentOn : _segmentOff;
            }
            int mode = ButtonRow(Next(ChipHeight, 10), labels, styles);
            if (mode >= 0) _app.SetTwinMode(TwinModes[mode]);

            Heading("Pump placement");
            bool moving = _app.IsMovingStation;
            int action = ButtonRow(Next(RowHeight), new[] { moving ? "Drop here" : "Move pump", "In front of me" }, new[] { moving ? _buttonPrimary : _buttonSecondary, _buttonSecondary });
            if (action == 0)
            {
                _app.SetMovingStation(!moving);
                if (!moving) _drawerOpen = false; // get out of the way so the floor is reachable
            }
            else if (action == 1) PlaceInFront();
            int turn = ButtonRow(Next(RowHeight, 10), new[] { "Turn left 15°", "Turn right 15°" }, new[] { _buttonQuiet, _buttonQuiet });
            if (turn == 0) _app.RotateStation(-15f);
            else if (turn == 1) _app.RotateStation(15f);

            Heading("Content");
            if (GUI.Button(Next(RowHeight, 10), "Clear everything", _buttonDanger)) _app.ClearBuilds();

            Line("Spatail DevTool " + Application.version + " · " + _app.Platform.DeviceName + " · site: " + _app.Site.name, _textSmall);
        }

        void PlaceInFront()
        {
            if (_app.Platform.Room == null) return;
            RoomModel raw = _app.Platform.Room.Capture();
            if (_app.Head != null)
            {
                raw.userPosition = _app.Head.transform.position.ToCore();
                raw.userForward = _app.Head.transform.forward.ToCore();
            }
            _app.PlaceStation(SiteBinder.SuggestStationPose(raw, _app.Site));
            Notice("Pump placed in front of you.");
        }

        // ---- Link ----------------------------------------------------------------------------

        void DrawLinkTab()
        {
            SessionLink session = _app.Session;
            Heading("Shared session");
            Line(_app.SessionStatus, _text);

            if (Time.unscaledTime - _localAddressAt > 5f)
            {
                _localAddressAt = Time.unscaledTime;
                _localAddress = SessionLink.LocalAddress();
            }
            Line("This device: " + _localAddress + (session.IsActive && session.IsHost ? ":" + session.Port : ""));
            _cy += P(4);

            if (session.IsActive)
            {
                List<Peer> peers = session.Peers;
                Heading(peers.Count == 0 ? (session.IsHost ? "Waiting for someone to join" : "Connecting") : "In the session");
                foreach (Peer peer in peers)
                {
                    Rect card = Next(46, 6);
                    Fill(card, _card);
                    float pad = P(10);
                    GUI.Label(new Rect(card.x + pad, card.y + P(5), card.width - pad * 2f, P(20)), peer.name, _textBold);
                    string detail = (string.IsNullOrEmpty(peer.device) ? "device" : peer.device) + (string.IsNullOrEmpty(peer.role) ? "" : " · " + peer.role) + (peer.hasPose ? " · tracked" : "");
                    GUI.Label(new Rect(card.x + pad, card.y + P(24), card.width - pad * 2f, P(18)), detail, _textSmall);
                }
                if (!session.IsHost) Line("Requests you send are planned by the host and appear for everyone.");
                if (GUI.Button(Next(RowHeight), session.IsHost ? "Stop hosting" : "Leave session", _buttonDanger)) _app.LeaveSession();
                return;
            }

            Heading("Join a headset");
            Line("On the headset choose Share, then enter the address it shows.");
            bool go = TextInput("link.join", Next(RowHeight), ref _fieldJoin, "192.168.1.30:" + SessionLink.DefaultPort, TouchScreenKeyboardType.NumbersAndPunctuation);
            GUI.enabled = _fieldJoin.Trim().Length > 0;
            if (GUI.Button(Next(RowHeight, 14), "Join", _buttonPrimary) || (go && _fieldJoin.Trim().Length > 0))
            {
                CloseKeyboard();
                _app.JoinSession(_fieldJoin);
            }
            GUI.enabled = true;

            Heading("Or host from this device");
            Line("Others join using the address above.");
            if (GUI.Button(Next(RowHeight), "Host a session", _buttonSecondary)) _app.HostSession();
        }
    }
}
