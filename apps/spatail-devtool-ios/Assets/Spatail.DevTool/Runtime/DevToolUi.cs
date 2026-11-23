using System.Collections.Generic;
using Spatail.Core;
using UnityEngine;

namespace Spatail.DevTool
{
    /// <summary>
    /// The dev tool's screen UI, drawn over the AR view (or over the simulator in the editor).
    ///
    /// Everything a thumb needs sits at the bottom: suggested prompts, the reply, and the prompt row with the tools,
    /// microphone and send buttons. A status strip at the top shows what the app, builder, sensor and session are
    /// doing. The tools drawer holds the developer tabs: Space, Experiences, Code, Log, Settings and Link.
    ///
    /// Written in IMGUI on purpose: it needs no assets or UI packages and behaves the same under either Unity input
    /// backend. Layout is manual and in points, so text stays crisp on any display density.
    /// </summary>
    public sealed partial class DevToolUi : MonoBehaviour
    {
        // The same suggestions, under the same rule, as the headset's prompt bar (PromptBar in the shared package).
        /// <summary>Requests the recorded demo can answer next to the stand-in pump.</summary>
        static readonly string[] StandInPrompts =
        {
            "Build me a walkthrough for this pump",
            "Show me how the water flows",
            "Has it failed before?",
            "Put this month's output by the bench",
            "What parts do I need?",
            "Bring Sara in",
        };

        /// <summary>The kind of thing to ask anywhere, once the planner is connected.</summary>
        static readonly string[] OpenPrompts =
        {
            "What am I looking at?",
            "How far is that from here?",
            "Mark a two metre zone around this",
        };

        const float RowHeight = 44f;      // primary touch targets, in points
        const float ChipHeight = 34f;

        SpatailApp _app;
        ISpeechInput _speech;
        ISpaceSensor _sensor;
        /// <summary>The ARKit sensor when running on a device; null in the editor simulator.</summary>
        ArSensor _ar;

        string _prompt = "";
        bool _listening;
        string _notice = "";
        float _noticeUntil;
        bool _drawerOpen;
        readonly HashSet<string> _used = new HashSet<string>();

        Rect _safe;
        readonly List<Rect> _uiRects = new List<Rect>();
        readonly List<Rect> _uiRectsBuilding = new List<Rect>();
        readonly List<string> _picks = new List<string>();

        /// <summary>Adds the dev tool UI to a running app. Speech and sensor may be null (as in the editor simulator).</summary>
        public static DevToolUi Attach(SpatailApp app, ISpeechInput speech, ISpaceSensor sensor)
        {
            var ui = app.gameObject.AddComponent<DevToolUi>();
            ui._app = app;
            ui._speech = speech;
            ui._sensor = sensor ?? app.Sensor;
            ui._ar = ui._sensor as ArSensor;
            ui.useGUILayout = false;
            ui.LoadSettingsFields();
            app.Built += ui.OnBuilt;
            app.Runtime.Failed += ui.OnFailed;
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

        void OnBuilt(string prompt, BuildResult result)
        {
            if (!string.IsNullOrEmpty(prompt)) _used.Add(prompt);
            // Show the program that was just written when the Code tab is next opened.
            if (result != null && result.HasProgram && !string.IsNullOrEmpty(result.id)) SelectCode(result.id);
        }

        void OnFailed(Experience experience)
        {
            _failures.Insert(0, experience);
            if (_failures.Count > 5) _failures.RemoveAt(_failures.Count - 1);
            // If its code is open, show why it stopped next to it.
            if (experience.Id == _codeId) _codeError = experience.Error;
        }

        string SensorStatus => _sensor != null ? _sensor.Status : "No sensor";

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
                case AppState.Building: dot = Warning; state = "Building"; break;
                default: dot = Theme.Success; state = _app.IsMovingStation ? "Moving the stand-in" : "Ready"; break;
            }
            var dotRect = new Rect(rect.x + pad, rect.y + P(13), P(9), P(9));
            if (Event.current.type == EventType.Repaint)
            {
                Color previous = GUI.color;
                GUI.color = dot;
                GUI.DrawTexture(dotRect, Rounded(Color.white, Clear, Mathf.Max(2, Pi(4.5f)), 0));
                GUI.color = previous;
            }

            // Who builds: the planner service when it is connected, else the recorded demo, which only knows the stand-in.
            string builder = _app.Director.RemoteActive ? "AI builder"
                : (_app.Settings.StandIn ? "Recorded demo (no planner)" : "No planner connected");
            BuildResult last = _app.LastResult;
            if (last != null && !string.IsNullOrEmpty(last.builder))
                builder += " · last: " + last.builder + (last.latencyMs > 0 ? " " + Mathf.RoundToInt((float)last.latencyMs) + " ms" : "");

            float textX = dotRect.xMax + P(8);
            float textWidth = rect.xMax - pad - textX;
            GUI.Label(new Rect(textX, rect.y + P(7), textWidth, P(20)), state + " · " + builder, _text);
            GUI.Label(new Rect(textX, rect.y + P(28), textWidth, P(18)), SensorStatus + " · " + _app.SessionStatus, _textSmall);

            // The whole strip opens and closes the tools drawer.
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) _drawerOpen = !_drawerOpen;
        }

        // ---- bottom bar ----------------------------------------------------------------------

        Rect DrawBottomBar(float margin, float bottom)
        {
            float pad = P(10);
            CollectSuggestions();
            float row = P(RowHeight), chip = _picks.Count > 0 ? P(ChipHeight) : 0f, replyHeight = P(36);
            float chipGap = chip > 0f ? P(8) : 0f;
            float height = pad + chip + chipGap + replyHeight + P(6) + row + pad;
            var rect = new Rect(_safe.x + margin, bottom - height, _safe.width - margin * 2f, height);
            Claim(rect);
            Fill(rect, _panel);

            float x = rect.x + pad, width = rect.width - pad * 2f;
            float y = rect.y + pad;

            if (chip > 0f) DrawSuggestions(new Rect(x, y, width, chip));
            y += chip + chipGap;
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

            bool ready = _app.State != AppState.Starting;
            if (TextInput("prompt", field, ref _prompt, _listening ? "Listening…" : "Just say what you need", TouchScreenKeyboardType.Default, true))
                Send();
            GUI.enabled = ready && _prompt.Trim().Length > 0;
            if (GUI.Button(send, "Send", _buttonPrimary)) Send();
            GUI.enabled = true;
            return rect;
        }

        /// <summary>
        /// With the stand-in in the room, what the recorded demo can do; otherwise, once the planner is connected,
        /// the open-ended kind of request it handles; otherwise nothing, because nothing would answer.
        /// </summary>
        void CollectSuggestions()
        {
            _picks.Clear();
            string[] source = _app.Settings.StandIn ? StandInPrompts : (_app.Director.RemoteActive ? OpenPrompts : null);
            if (source == null) return;
            foreach (string prompt in source)
                if (!_used.Contains(prompt)) _picks.Add(prompt);
            // Prompts already tried stay available at the end of the row.
            foreach (string prompt in source)
                if (_used.Contains(prompt)) _picks.Add(prompt);
        }

        void DrawSuggestions(Rect rect)
        {
            ScrollState scroll = BeginScroll("suggestions", rect, true);
            float x = 0f, gap = P(8);
            bool enabled = _app.State == AppState.Ready;
            string chosen = null;
            foreach (string pick in _picks)
            {
                float w = WidthOf(_chip, pick);
                GUI.enabled = enabled;
                if (GUI.Button(new Rect(x, 0, w, rect.height), pick, _chip)) chosen = pick;
                GUI.enabled = true;
                x += w + gap;
            }
            EndScroll(scroll, x - gap);
            if (chosen != null)
            {
                _prompt = chosen;
                Send();
            }
        }

        void DrawReply(Rect rect)
        {
            string reply;
            GUIStyle style = _textWrap;
            BuildResult last = _app.LastResult;
            string chip = null;

            if (Time.unscaledTime < _noticeUntil) { reply = _notice; style = _textWrapAccent; }
            else if (_app.State == AppState.Building) { reply = "Looking and building…  " + Truncate(_app.LastPrompt, 60); style = _textWrapMuted; }
            else if (_app.State == AppState.Starting) { reply = SensorStatus; style = _textWrapMuted; }
            else if (_app.IsMovingStation) { reply = "Drag on the floor to move the stand-in, twist with two fingers to turn it, lift to drop."; style = _textWrapAccent; }
            else if (last != null && last.builder == "experience")
            {
                // A running program handled a spoken command itself; there is no reply text.
                reply = "\u201C" + Truncate(_app.LastPrompt, 60) + "\u201D  handled by the running experience.";
                style = _textWrapMuted;
            }
            else if (last != null)
            {
                reply = last.reply ?? "";
                if (!string.IsNullOrEmpty(last.error) && reply.Length == 0) reply = last.error;
                if (last.HasProgram && !string.IsNullOrEmpty(last.title)) chip = "Built " + last.title;
            }
            else if (_app.Director.RemoteActive) { reply = "Point the phone at something and say what you need."; style = _textWrapMuted; }
            else if (_app.Settings.StandIn) { reply = "No planner connected: the recorded demo answers the suggestions above. Set the planner address in Tools, Settings."; style = _textWrapMuted; }
            else { reply = "Nothing can build yet. Set the planner address in Tools, Settings."; style = _textWrapMuted; }

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
