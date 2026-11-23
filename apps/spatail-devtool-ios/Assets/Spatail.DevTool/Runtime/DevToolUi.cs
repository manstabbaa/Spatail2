using System.Collections.Generic;
using Spatail.Core;
using UnityEngine;

namespace Spatail.DevTool
{
    /// <summary>
    /// The dev tool's screen UI, drawn over the AR view (or over the simulator in the editor).
    ///
    /// Everything a thumb needs sits at the bottom: suggested prompts, the reply, and the prompt row with the tools,
    /// microphone and send buttons. A status strip at the top shows what the app, planner, room and session are
    /// doing. The tools drawer holds the developer tabs: Room, Builds, Spec, Log, Settings and Link.
    ///
    /// Written in IMGUI on purpose: it needs no assets or UI packages and behaves the same under either Unity input
    /// backend. Layout is manual and in points, so text stays crisp on any display density.
    /// </summary>
    public sealed partial class DevToolUi : MonoBehaviour
    {
        static readonly string[] DemoPrompts =
        {
            "Build me a walkthrough for this pump",
            "Show me how the water flows",
            "Has it failed before?",
            "Put this month's output by the bench",
            "What parts do I need?",
            "Bring Sara in",
        };

        const float RowHeight = 44f;      // primary touch targets, in points
        const float ChipHeight = 34f;

        SpatailApp _app;
        ISpeechInput _speech;
        IRoomProvider _room;

        string _prompt = "";
        bool _listening;
        string _notice = "";
        float _noticeUntil;
        bool _drawerOpen;
        readonly HashSet<string> _used = new HashSet<string>();

        Rect _safe;
        readonly List<Rect> _uiRects = new List<Rect>();
        readonly List<Rect> _uiRectsBuilding = new List<Rect>();

        /// <summary>Adds the dev tool UI to a running app. Speech and room may be null (as in the editor simulator).</summary>
        public static DevToolUi Attach(SpatailApp app, ISpeechInput speech, IRoomProvider room)
        {
            var ui = app.gameObject.AddComponent<DevToolUi>();
            ui._app = app;
            ui._speech = speech;
            ui._room = room ?? app.Platform.Room;
            ui.useGUILayout = false;
            ui.LoadSettingsFields();
            app.Planned += ui.OnPlanned;
            return ui;
        }

        /// <summary>True when a screen position (pixels, origin bottom-left, as input reports it) is on the UI.</summary>
        public bool IsOverUi(Vector2 screenPosition)
        {
            var p = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            for (int i = 0; i < _uiRects.Count; i++)
                if (_uiRects[i].Contains(p)) return true;
            return false;
        }

        void OnPlanned(string prompt, PlanResult plan)
        {
            if (!string.IsNullOrEmpty(prompt)) _used.Add(prompt);
        }

        void Update()
        {
            if (_app == null) return;
            PollKeyboard();
            CommitSettingsFields();

            // In the simulator the shared mouse pointer knows nothing about this UI, so hold scene clicks back
            // while the cursor is on it. On device the touch pointer filters its own touches.
            if (_app.Platform.DeviceName == "sim")
                _app.Interaction.Suspended = _app.IsMovingStation || IsOverUi(InputBridge.MousePosition);
        }

        // ---- frame ---------------------------------------------------------------------------

        void OnGUI()
        {
            if (_app == null) return;
            UpdateScale();
            GUI.depth = 0;

            Rect safeArea = Screen.safeArea;
            _safe = new Rect(safeArea.x, Screen.height - safeArea.yMax, safeArea.width, safeArea.height);
            bool repaint = Event.current.type == EventType.Repaint;
            if (repaint) _uiRectsBuilding.Clear();

            float margin = P(10);
            // Stay above the on-screen keyboard while it is up.
            float keyboard = TouchScreenKeyboard.visible ? TouchScreenKeyboard.area.height : 0f;
            float bottom = Mathf.Min(_safe.yMax, Screen.height - keyboard) - margin;
            bool landscape = Screen.width > Screen.height;

            Rect bar = DrawBottomBar(margin, bottom);

            float drawerWidth = 0f;
            if (_drawerOpen)
            {
                Rect drawer;
                if (landscape)
                {
                    drawerWidth = Mathf.Min(P(430), _safe.width * 0.5f);
                    drawer = new Rect(_safe.xMax - margin - drawerWidth, _safe.y + margin, drawerWidth, bar.y - P(8) - _safe.y - margin);
                }
                else
                {
                    float top = _safe.y + margin + P(54) + P(8);
                    float height = Mathf.Min(_safe.height * 0.52f, P(480));
                    height = Mathf.Min(height, bar.y - P(8) - top);
                    drawer = new Rect(_safe.x + margin, bar.y - P(8) - height, _safe.width - margin * 2f, height);
                }
                if (drawer.height > P(120)) DrawDrawer(drawer);
            }

            float stripWidth = _safe.width - margin * 2f - (landscape && _drawerOpen ? drawerWidth + margin : 0f);
            DrawStatusStrip(new Rect(_safe.x + margin, _safe.y + margin, Mathf.Min(stripWidth, P(560)), P(54)));

            if (repaint)
            {
                _uiRects.Clear();
                _uiRects.AddRange(_uiRectsBuilding);
                _focusedControl = GUI.GetNameOfFocusedControl();
            }
        }

        void Claim(Rect rect)
        {
            if (Event.current.type == EventType.Repaint) _uiRectsBuilding.Add(rect);
        }

        // ---- status strip --------------------------------------------------------------------

        void DrawStatusStrip(Rect rect)
        {
            Claim(rect);
            Fill(rect, _strip);
            float pad = P(12);

            Color dot;
            string state;
            switch (_app.State)
            {
                case AppState.Starting: dot = Muted; state = "Starting"; break;
                case AppState.Scanning: dot = Accent; state = "Reading the room"; break;
                case AppState.Thinking: dot = Warning; state = "Building"; break;
                default: dot = Theme.Success; state = _app.IsMovingStation ? "Moving the pump" : "Ready"; break;
            }
            var dotRect = new Rect(rect.x + pad, rect.y + P(13), P(9), P(9));
            if (Event.current.type == EventType.Repaint)
            {
                Color previous = GUI.color;
                GUI.color = dot;
                GUI.DrawTexture(dotRect, Rounded(Color.white, Clear, Mathf.Max(2, Pi(4.5f)), 0));
                GUI.color = previous;
            }

            string planner = _app.Planner != null && _app.Planner.RemoteActive ? "AI planner" : "On-device planner";
            PlanResult last = _app.LastPlan;
            if (last != null && !string.IsNullOrEmpty(last.planner))
                planner += " · last: " + last.planner + (last.latencyMs > 0 ? " " + Mathf.RoundToInt((float)last.latencyMs) + " ms" : "");

            float textX = dotRect.xMax + P(8);
            float textWidth = rect.xMax - pad - textX;
            GUI.Label(new Rect(textX, rect.y + P(7), textWidth, P(20)), state + " · " + planner, _text);
            string room = _room != null ? _room.Status : "No room provider";
            GUI.Label(new Rect(textX, rect.y + P(28), textWidth, P(18)), room + " · " + _app.SessionStatus, _textSmall);

            // The whole strip opens and closes the tools drawer.
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) _drawerOpen = !_drawerOpen;
        }

        // ---- bottom bar ----------------------------------------------------------------------

        Rect DrawBottomBar(float margin, float bottom)
        {
            float pad = P(10);
            float row = P(RowHeight), chip = P(ChipHeight), replyHeight = P(36);
            float height = pad + chip + P(8) + replyHeight + P(6) + row + pad;
            var rect = new Rect(_safe.x + margin, bottom - height, _safe.width - margin * 2f, height);
            Claim(rect);
            Fill(rect, _panel);

            float x = rect.x + pad, width = rect.width - pad * 2f;
            float y = rect.y + pad;

            DrawSuggestions(new Rect(x, y, width, chip));
            y += chip + P(8);
            DrawReply(new Rect(x + P(4), y, width - P(8), replyHeight));
            y += replyHeight + P(6);

            // [tools] [mic] [ prompt ............ ] [send]
            float gap = P(8);
            var tools = new Rect(x, y, P(52), row);
            var mic = new Rect(tools.xMax + gap, y, P(46), row);
            var send = new Rect(x + width - P(58), y, P(58), row);
            var field = new Rect(mic.xMax + gap, y, send.x - gap - mic.xMax - gap, row);

            if (GUI.Button(tools, _drawerOpen ? "Close" : "Tools", _drawerOpen ? _buttonRound : _buttonRoundQuiet)) _drawerOpen = !_drawerOpen;

            bool canSpeak = _speech != null && _speech.IsAvailable;
            if (GUI.Button(mic, _listening ? "Stop" : "Mic", _listening ? _buttonRoundHot : (canSpeak ? _buttonRound : _buttonRoundQuiet)))
                ToggleListening(canSpeak);

            bool ready = _app.State == AppState.Ready || _app.State == AppState.Thinking;
            if (TextInput("prompt", field, ref _prompt, _listening ? "Listening…" : "Just say what you need", TouchScreenKeyboardType.Default, true))
                Send();
            GUI.enabled = ready && _prompt.Trim().Length > 0;
            if (GUI.Button(send, "Send", _buttonPrimary)) Send();
            GUI.enabled = true;
            return rect;
        }

        void DrawSuggestions(Rect rect)
        {
            var picks = new List<string>();
            Build stepped = _app.Store.LatestStepped;
            if (stepped != null)
            {
                if (stepped.currentStep < stepped.stepCount) picks.Add("Next");
                if (stepped.currentStep > 1) picks.Add("Back");
            }
            foreach (string prompt in DemoPrompts)
                if (!_used.Contains(prompt)) picks.Add(prompt);
            if (_app.Store.Builds.Count > 0)
            {
                picks.Add("Make it bigger");
                picks.Add("Clear everything");
            }
            // Prompts already tried stay available at the end of the row.
            foreach (string prompt in DemoPrompts)
                if (_used.Contains(prompt)) picks.Add(prompt);

            ScrollState scroll = BeginScroll("suggestions", rect, true);
            float x = 0f, gap = P(8);
            bool enabled = _app.State == AppState.Ready;
            foreach (string pick in picks)
            {
                float w = WidthOf(_chip, pick);
                GUI.enabled = enabled;
                if (GUI.Button(new Rect(x, 0, w, rect.height), pick, _chip))
                {
                    _prompt = pick;
                    Send();
                }
                GUI.enabled = true;
                x += w + gap;
            }
            EndScroll(scroll, x - gap);
        }

        void DrawReply(Rect rect)
        {
            string reply;
            GUIStyle style = _textWrap;
            PlanResult last = _app.LastPlan;
            string chip = null;

            if (Time.unscaledTime < _noticeUntil) { reply = _notice; style = _textWrapAccent; }
            else if (_app.State == AppState.Thinking) { reply = "Building…  " + Truncate(_app.LastPrompt, 60); style = _textWrapMuted; }
            else if (_app.State == AppState.Starting) { reply = _room != null ? _room.Status : "Starting"; style = _textWrapMuted; }
            else if (_app.State == AppState.Scanning) { reply = "Reading the room…"; style = _textWrapMuted; }
            else if (_app.IsMovingStation) { reply = "Drag on the floor to move the pump, twist with two fingers to turn it, lift to drop."; style = _textWrapAccent; }
            else if (last != null)
            {
                reply = last.reply ?? "";
                if (!string.IsNullOrEmpty(last.chip)) chip = "Built " + last.chip;
            }
            else { reply = "Ask for a walkthrough, the flow, fault history, a dashboard, a checklist, or bring someone in."; style = _textWrapMuted; }

            float chipWidth = 0f;
            if (chip != null)
            {
                chipWidth = WidthOf(_chipBuilt, chip);
                var chipRect = new Rect(rect.xMax - chipWidth, rect.y + (rect.height - P(26)) * 0.5f, chipWidth, P(26));
                GUI.Label(chipRect, chip, _chipBuilt);
                chipWidth += P(8);
            }
            GUI.Label(new Rect(rect.x, rect.y, rect.width - chipWidth, rect.height), reply, style);
        }

        // ---- actions -------------------------------------------------------------------------

        void Send()
        {
            string text = (_prompt ?? "").Trim();
            if (text.Length == 0) return;
            StopListening();
            CloseKeyboard();
            GUI.FocusControl(null);
            _used.Add(text);
            _prompt = "";
            _app.Submit(text);
        }

        void ToggleListening(bool canSpeak)
        {
            if (_listening) { StopListening(); return; }
            if (!canSpeak)
            {
                Notice("Speech is not available here. Tap the prompt and use the keyboard's dictation key.");
                return;
            }
            CloseKeyboard();
            _listening = true;
            _prompt = "";
            _speech.StartListening(
                (text, final) =>
                {
                    _prompt = text ?? "";
                    if (!final) return;
                    _listening = false;
                    if (_prompt.Trim().Length > 0) Send();
                },
                error =>
                {
                    _listening = false;
                    Notice(error);
                    _app.Log("Speech: " + error);
                });
        }

        void StopListening()
        {
            if (!_listening) return;
            _listening = false;
            if (_speech != null) _speech.StopListening();
        }

        void Notice(string message)
        {
            _notice = message ?? "";
            _noticeUntil = Time.unscaledTime + 4f;
        }
    }
}
