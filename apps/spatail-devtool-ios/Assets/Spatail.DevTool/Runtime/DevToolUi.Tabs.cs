using System.Collections.Generic;
using System.Text;
using Spatail.Core;
using UnityEngine;

namespace Spatail.DevTool
{
    /// <summary>
    /// The tools drawer: six tabs of developer tooling over the running app. Space is what the device senses now,
    /// Experiences is what is running, Code is the program behind one of them, open for editing.
    /// </summary>
    public sealed partial class DevToolUi
    {
        static readonly string[] TabNames = { "Space", "Experiences", "Code", "Log", "Settings", "Link" };
        static readonly TwinMode[] TwinModes = { TwinMode.Solid, TwinMode.Ghost, TwinMode.Occluder, TwinMode.Hidden };
        const int CodeTab = 2;
        const string ScratchId = "scratch";
        const string StarterProgram =
            "// A program draws with scene, reads the place with space, and listens with voice.\n" +
            "const ahead = vec.add(user.head.position, vec.scale(vec.normalize(vec.flat(user.head.forward)), 1.2));\n" +
            "const hit = space.raycast(user.head.position, user.head.forward, 6);\n" +
            "kit.label(hit ? 'This is ' + hit.distance.toFixed(2) + ' m away' : 'Nothing sensed here yet', hit ? hit.position : ahead);\n";

        int _tab;
        float _cy, _cw;      // layout cursor and width inside the current tab, in pixels
        readonly float[] _tabWidths = new float[6];

        // Cached text and textures for the heavier tabs.
        int _logCount = -1;
        string _logText = "";
        string _localAddress = "";
        float _localAddressAt = -100f;
        byte[] _frameBytes;
        Texture2D _frameTexture;

        // Programs that stopped with an error, most recent first; the runtime forgets them, the dev tool does not.
        readonly List<Experience> _failures = new List<Experience>();

        // The program open in the Code tab.
        string _codeId, _codeTitle = "", _codeText = "", _codeLoaded = "", _codeError;

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

        void OnDestroy()
        {
            if (_frameTexture != null) Destroy(_frameTexture);
        }

        // ---- frame ---------------------------------------------------------------------------

        void DrawDrawer(Rect rect)
        {
            Claim(rect);
            Fill(rect, _panel);
            float pad = P(10);

            // Tab bar: each tab as wide as its name needs, scaled together to fill the row.
            float tabHeight = P(ChipHeight);
            float available = rect.width - pad * 2f, natural = 0f;
            for (int i = 0; i < TabNames.Length; i++)
            {
                _tabWidths[i] = WidthOf(_tabOn, TabNames[i]) + P(12);
                natural += _tabWidths[i];
            }
            float tabX = rect.x + pad;
            for (int i = 0; i < TabNames.Length; i++)
            {
                float width = _tabWidths[i] * available / natural;
                if (GUI.Button(new Rect(tabX, rect.y + pad, width, tabHeight), TabNames[i], i == _tab ? _tabOn : _tabOff) && _tab != i)
                {
                    _tab = i;
                    CloseKeyboard();
                    GUI.FocusControl(null);
                }
                tabX += width;
            }

            var area = new Rect(rect.x + pad, rect.y + pad + tabHeight + P(8), rect.width - pad * 2f, rect.height - pad * 2f - tabHeight - P(8));
            ScrollState scroll = BeginScroll("tab" + _tab, area, false);
            _cy = 0f;
            _cw = area.width;
            switch (_tab)
            {
                case 0: DrawSpaceTab(); break;
                case 1: DrawExperiencesTab(); break;
                case CodeTab: DrawCodeTab(); break;
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

        /// <summary>Text that wraps over as many lines as it needs.</summary>
        void Wrapped(string text, GUIStyle style, float gapPoints = 6f)
        {
            float height = style.CalcHeight(new GUIContent(text), _cw);
            GUI.Label(new Rect(0, _cy, _cw, height), text, style);
            _cy += height + P(gapPoints);
        }

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

        // ---- Space ---------------------------------------------------------------------------

        void DrawSpaceTab()
        {
            Heading("What the device senses now");
            Wrapped(SensorStatus, _textWrap, 2);
            float floorY = _app.FloorY;
            bool floorGuessed = _ar != null && _ar.FloorIsEstimated;
            Line("Floor at y = " + floorY.ToString("0.00") + " m" + (floorGuessed ? " (a guess until a floor is found)" : ""));
            if (_app.Head != null)
            {
                Vector3 head = _app.Head.transform.position;
                Line("You: " + head.x.ToString("0.0") + ", " + head.y.ToString("0.0") + ", " + head.z.ToString("0.0") +
                     " · " + (head.y - floorY).ToString("0.00") + " m above the floor");
            }
            if (_ar != null) Wrapped("Ray casts answered by: " + _ar.RaycastInfo, _textWrapMuted);
            _cy += P(4);

            Heading("Last frame sent");
            byte[] jpeg = _app.LastFrameJpeg;
            if (jpeg == null || jpeg.Length == 0)
            {
                Wrapped(_app.Director.RemoteActive
                    ? "None yet. A frame is captured each time a request goes to the planner."
                    : "None. Frames are only captured when the planner is connected.", _textWrapMuted);
            }
            else
            {
                if (!ReferenceEquals(jpeg, _frameBytes))
                {
                    _frameBytes = jpeg;
                    if (_frameTexture == null) _frameTexture = new Texture2D(2, 2, TextureFormat.RGB24, false) { name = "Spatail Frame Preview" };
                    _frameTexture.LoadImage(jpeg);
                }
                float aspect = _frameTexture.height > 0 ? (float)_frameTexture.width / _frameTexture.height : 1.33f;
                float height = Mathf.Min(P(200), _cw / aspect);
                var picture = new Rect(0, _cy, height * aspect, height);
                GUI.DrawTexture(picture, _frameTexture, ScaleMode.ScaleToFit, false);
                _cy += height + P(6);
                Line(_frameTexture.width + " × " + _frameTexture.height + " px · " + (jpeg.Length / 1024) + " KB", _textSmall);
            }
            if (_ar != null)
            {
                Wrapped(_ar.FrameInfo, _textWrapMuted, 4);
                // How the camera image is turned upright cannot be checked without a device, so it can be put right here.
                Wrapped("The picture should be upright, as you saw it. If it is not, turn or flip it; that applies from the next request.", _textWrapMuted);
                bool auto = _ar.FrameRotation < 0;
                int pressed = ButtonRow(Next(ChipHeight, 10),
                    new[] { auto ? "Auto (" + _ar.AppliedRotation + "°)" : "Auto", "Turn 90°", _ar.FrameFlip ? "Flipped" : "Flip" },
                    new[] { auto ? _segmentOn : _segmentOff, auto ? _segmentOff : _segmentOn, _ar.FrameFlip ? _segmentOn : _segmentOff });
                if (pressed == 0) _ar.FrameRotation = -1;
                else if (pressed == 1) _ar.FrameRotation = (_ar.AppliedRotation + 90) % 360;
                else if (pressed == 2) _ar.FrameFlip = !_ar.FrameFlip;
            }

            IReadOnlyList<RoomEntity> surfaces = _app.Surfaces;
            Heading("Surfaces found so far (" + surfaces.Count + ")");
            if (surfaces.Count == 0) Wrapped("None yet. Nothing is known about a place in advance; surfaces appear as the device sees them.", _textWrapMuted);
            foreach (RoomEntity e in surfaces) EntityCard(e);

            IReadOnlyList<RoomEntity> props = _app.Props;
            if (props.Count > 0)
            {
                Vec3 station = _app.Station.position;
                Heading("Stand-in props (" + props.Count + ")");
                Line("Placed by the app, not sensed. At " + station.x.ToString("0.0") + ", " + station.z.ToString("0.0") +
                     ", heading " + Mathf.RoundToInt(_app.Station.rotation.YawDegrees) + "°");
                foreach (RoomEntity e in props) EntityCard(e);
            }
        }

        void EntityCard(RoomEntity e)
        {
            Rect card = Next(50, 6);
            Fill(card, _card);
            float pad = P(10);
            float badgeWidth = WidthOf(_badge, e.kind);
            var badge = new Rect(card.xMax - pad - badgeWidth, card.y + P(8), badgeWidth, P(20));
            GUI.Label(badge, e.kind, _badge);
            GUI.Label(new Rect(card.x + pad, card.y + P(6), badge.x - card.x - pad * 2f, P(20)), string.IsNullOrEmpty(e.label) ? e.id : e.label, _textBold);
            string size = e.size.x.ToString("0.00") + " × " + e.size.y.ToString("0.00") + " × " + e.size.z.ToString("0.00") + " m";
            GUI.Label(new Rect(card.x + pad, card.y + P(27), card.width - pad * 2f, P(18)), e.id + " · " + size, _textSmall);
        }

        // ---- Experiences ---------------------------------------------------------------------

        void DrawExperiencesTab()
        {
            Heading("Running now");
            // Copy first: the buttons below change the list.
            var running = new List<Experience>(_app.Runtime.Experiences);
            if (running.Count == 0) Wrapped("Nothing is running. Ask for something below, or write a program in the Code tab.", _textWrapMuted);

            float pad = P(10);
            foreach (Experience exp in running)
            {
                int logLines = Mathf.Min(3, exp.Log.Count);
                Rect card = Next(6 + 20 + 18 + logLines * 16 + 6 + RowHeight + 10, 8);
                Fill(card, _card);
                float inner = card.width - pad * 2f;
                float y = card.y + P(6);
                string nodes = exp.NodeCount + (exp.NodeCount == 1 ? " node" : " nodes");
                GUI.Label(new Rect(card.x + pad, y, inner, P(20)), exp.Title + "  ·  " + nodes, _textBold);
                y += P(20);
                GUI.Label(new Rect(card.x + pad, y, inner, P(18)), "\u201C" + Truncate(exp.Prompt, 70) + "\u201D", _textSmall);
                y += P(18);
                for (int i = exp.Log.Count - logLines; i < exp.Log.Count; i++)
                {
                    bool error = exp.Log[i].StartsWith("! ");
                    GUI.Label(new Rect(card.x + pad, y, inner, P(16)), Truncate(exp.Log[i], 90), error ? _textAccent : _textSmall);
                    y += P(16);
                }

                var row = new Rect(card.x + pad, card.yMax - pad - P(RowHeight), inner, P(RowHeight));
                int pressed = ButtonRow(row, new[] { "Code", "Stop" }, new[] { _buttonQuiet, _buttonDanger });
                if (pressed == 0)
                {
                    SelectCode(exp.Id);
                    _tab = CodeTab;
                }
                else if (pressed == 1) _app.StopExperience(exp.Id);
            }
            if (running.Count > 1 && GUI.Button(Next(RowHeight, 10), "Stop everything", _buttonSecondary)) _app.ClearExperiences();

            if (_failures.Count == 0) return;
            Heading("Stopped with an error");
            int open = -1;
            for (int f = 0; f < _failures.Count; f++)
            {
                Experience failed = _failures[f];
                string error = Truncate(failed.Error ?? "Unknown error", 400);
                float errorHeight = _textWrapAccent.CalcHeight(new GUIContent(error), _cw - pad * 2f);
                var card = new Rect(0, _cy, _cw, P(6 + 20 + 6 + RowHeight + 10) + errorHeight);
                _cy += card.height + P(8);
                Fill(card, _card);
                float inner = card.width - pad * 2f;
                GUI.Label(new Rect(card.x + pad, card.y + P(6), inner, P(20)), failed.Title, _textBold);
                GUI.Label(new Rect(card.x + pad, card.y + P(26), inner, errorHeight), error, _textWrapAccent);
                if (GUI.Button(new Rect(card.x + pad, card.yMax - pad - P(RowHeight), inner, P(RowHeight)), "Open in Code", _buttonQuiet)) open = f;
            }
            if (open >= 0)
            {
                LoadCode(_failures[open]);
                _tab = CodeTab;
            }
            if (GUI.Button(Next(RowHeight, 10), "Forget these", _buttonQuiet)) _failures.Clear();
        }

        // ---- Code ----------------------------------------------------------------------------

        bool CodeEdited => _codeText != _codeLoaded;

        /// <summary>Opens a running (or recently failed) program in the Code tab, unless edits are in progress there.</summary>
        void SelectCode(string id)
        {
            if (CodeEdited && _codeId != id) return;
            Experience exp = _app.Runtime.Get(id);
            if (exp == null)
                foreach (Experience failed in _failures)
                    if (failed.Id == id) { exp = failed; break; }
            if (exp != null) LoadCode(exp);
        }

        void LoadCode(Experience exp)
        {
            CloseKeyboard();
            _codeId = exp.Id;
            _codeTitle = exp.Title ?? "";
            _codeText = _codeLoaded = exp.Code ?? "";
            _codeError = exp.IsRunning ? null : exp.Error;
        }

        void NewProgram()
        {
            CloseKeyboard();
            _codeId = ScratchId;
            _codeTitle = "Scratch";
            _codeLoaded = "";
            _codeText = StarterProgram;
            _codeError = null;
        }

        void RunCode()
        {
            CloseKeyboard();
            GUI.FocusControl(null);
            Experience started = _app.RunProgram(_codeId, _codeTitle, _codeText);
            if (started == null) { _codeError = "The program did not start."; return; }
            _codeLoaded = _codeText;
            // A program that fails while starting is torn down straight away and carries the reason.
            if (!started.IsRunning || !string.IsNullOrEmpty(started.Error))
            {
                _codeError = string.IsNullOrEmpty(started.Error) ? "The program stopped while starting." : started.Error;
                Notice("The program stopped with an error.");
                return;
            }
            _codeError = null;
            Notice("Running " + started.Title + " · " + started.NodeCount + (started.NodeCount == 1 ? " node." : " nodes."));
        }

        void DrawCodeTab()
        {
            IReadOnlyList<Experience> running = _app.Runtime.Experiences;
            if (_codeId == null)
            {
                if (running.Count > 0) LoadCode(running[running.Count - 1]); else NewProgram();
            }

            Heading("Program");
            // Which program: every running experience, plus a blank one to write from scratch.
            Rect chips = Next(ChipHeight, 8);
            ScrollState scroll = BeginScroll("code.pick", chips, true);
            float x = 0f, gap = P(8);
            Experience pick = null;
            bool pickNew = false;
            foreach (Experience exp in running)
            {
                float w = WidthOf(_chip, exp.Title);
                if (GUI.Button(new Rect(x, 0, w, chips.height), exp.Title, exp.Id == _codeId ? _chipBuilt : _chip)) pick = exp;
                x += w + gap;
            }
            float newWidth = WidthOf(_chip, "New");
            if (GUI.Button(new Rect(x, 0, newWidth, chips.height), "New", _codeId == ScratchId && _app.Runtime.Get(ScratchId) == null ? _chipBuilt : _chip)) pickNew = true;
            x += newWidth;
            EndScroll(scroll, x);
            if (pick != null) LoadCode(pick);
            else if (pickNew) NewProgram();

            Experience current = _app.Runtime.Get(_codeId);
            string state = current != null ? "running · " + current.NodeCount + (current.NodeCount == 1 ? " node" : " nodes") : "not running";
            Line(_codeId + " · " + state + (CodeEdited ? " · edited" : ""));

            bool touch = TouchScreenKeyboard.isSupported;
            string[] labels = touch ? new[] { "Run", "Edit", "Copy", "Paste" } : new[] { "Run", "Copy", "Paste" };
            GUIStyle[] styles = touch ? new[] { _buttonPrimary, _buttonSecondary, _buttonQuiet, _buttonQuiet } : new[] { _buttonPrimary, _buttonQuiet, _buttonQuiet };
            int pressed = ButtonRow(Next(RowHeight, 8), labels, styles);
            string action = pressed >= 0 ? labels[pressed] : null;
            if (action == "Run") RunCode();
            else if (action == "Edit") EditMultiline("code", _codeText);
            else if (action == "Copy")
            {
                GUIUtility.systemCopyBuffer = _codeText;
                Notice("Program copied.");
            }
            else if (action == "Paste")
            {
                string pasted = GUIUtility.systemCopyBuffer;
                if (string.IsNullOrEmpty(pasted)) Notice("The clipboard is empty.");
                else
                {
                    CloseKeyboard();
                    _codeText = pasted;
                    Notice("Pasted. Press Run to start it.");
                }
            }

            if (!string.IsNullOrEmpty(_codeError)) Wrapped(Truncate(_codeError, 600), _textWrapAccent, 8);
            if (touch) Wrapped("Edit opens the keyboard on the whole program. For bigger changes, copy it to an editor and paste it back.", _textWrapMuted);
            MultilineInput("code", ref _codeText);
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
            int choice = ButtonRow(Next(ChipHeight, 10), new[] { "AI on", "AI off" }, new[] { ai ? _segmentOn : _segmentOff, ai ? _segmentOff : _segmentOn });
            if (choice >= 0) settings.UseAi = choice == 0;

            Heading("You");
            TextInput("settings.name", Next(RowHeight, 10), ref _fieldName, "Name shown to others in a session", TouchScreenKeyboardType.Default);

            Heading("Stand-in");
            Wrapped("A virtual pump station for demos and tests where there is no machine. It is drawn by the app, not sensed; turn it off to work with the place as it is.", _textWrapMuted);
            bool standIn = settings.StandIn;
            int standInChoice = ButtonRow(Next(ChipHeight, 10), new[] { "Stand-in on", "Stand-in off" }, new[] { standIn ? _segmentOn : _segmentOff, standIn ? _segmentOff : _segmentOn });
            if (standInChoice >= 0 && (standInChoice == 0) != standIn)
            {
                if (_app.IsMovingStation) _app.SetMovingStation(false);
                _app.SetStandIn(standInChoice == 0);
            }

            if (settings.StandIn)
            {
                Line("How it is drawn");
                var styles = new GUIStyle[TwinModes.Length];
                var labels = new string[TwinModes.Length];
                for (int i = 0; i < TwinModes.Length; i++)
                {
                    labels[i] = TwinModes[i].ToString();
                    styles[i] = settings.TwinMode == TwinModes[i] ? _segmentOn : _segmentOff;
                }
                int mode = ButtonRow(Next(ChipHeight, 10), labels, styles);
                if (mode >= 0) _app.SetTwinMode(TwinModes[mode]);

                Line("Where it stands");
                bool moving = _app.IsMovingStation;
                int action = ButtonRow(Next(RowHeight), new[] { moving ? "Drop here" : "Move", "In front of me" }, new[] { moving ? _buttonPrimary : _buttonSecondary, _buttonSecondary });
                if (action == 0)
                {
                    _app.SetMovingStation(!moving);
                    if (!moving) _drawerOpen = false; // get out of the way so the floor is reachable
                }
                else if (action == 1)
                {
                    _app.ReplaceStation();
                    Notice("Stand-in placed in front of you.");
                }
                int turn = ButtonRow(Next(RowHeight, 10), new[] { "Turn left 15°", "Turn right 15°" }, new[] { _buttonQuiet, _buttonQuiet });
                if (turn == 0) _app.RotateStation(-15f);
                else if (turn == 1) _app.RotateStation(15f);
            }

            Heading("Content");
            if (GUI.Button(Next(RowHeight), "Clear everything", _buttonDanger))
            {
                _app.ClearExperiences();
                Notice("Everything was removed.");
            }
            Wrapped("Save episode sends the last request (camera frame, depth samples, surfaces, what was said) and what was built for it to the planner service, to keep as a test case from a real place.", _textWrapMuted);
            GUI.enabled = _app.Director.LastRequest != null;
            if (GUI.Button(Next(RowHeight, 10), "Save episode", _buttonSecondary))
            {
                _app.SaveEpisode();
                Notice(_app.Planner.IsConfigured ? "Saving the episode; the Log tab shows the result." : "Set the planner address first.");
            }
            GUI.enabled = true;

            Line("Spatail DevTool " + Application.version + " · " + _app.Platform.DeviceName, _textSmall);
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
                // Typing a URL in a headset is slow; set the planner up here and push it across.
                if (peers.Count > 0 && GUI.Button(Next(RowHeight, 8), "Send my planner settings to the others", _buttonSecondary)) _app.SendPlannerSettingsToPeers();
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
