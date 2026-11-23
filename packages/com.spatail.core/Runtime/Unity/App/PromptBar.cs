using System.Collections.Generic;
using Spatail.Core;
using UnityEngine;

namespace Spatail
{
    /// <summary>
    /// The floating prompt bar from the "Built by you" sequence: a microphone, what you said, and a chip naming what
    /// was built. It hovers below the user's line of sight and drifts back into reach when they turn away.
    /// Above it sit suggested requests; below it, the reply and a few tools.
    /// </summary>
    public sealed class PromptBar : MonoBehaviour
    {
        const float Width = 0.64f, Height = 0.068f, Distance = 0.72f, Drop = 0.36f;
        const string Placeholder = "Just say what you need";

        static readonly string[] Suggestions =
        {
            "Build me a walkthrough for this pump",
            "Show me how the water flows",
            "Has it failed before?",
            "Put this month's output by the bench",
            "What parts do I need?",
            "Bring Sara in",
        };

        SpatailApp _app;
        SpatialText _text, _reply;
        Panel _mic;
        Transform _chip;
        SpatialText _chipText;
        Panel _chipBackground;
        Transform _suggestionRow, _toolRow;
        PillButton _moveButton, _twinButton, _shareButton;
        readonly HashSet<string> _used = new HashSet<string>();

        string _typed = "";
        bool _typing, _listening;
        float _chipTimer;
        Vector3 _targetPosition;
        Quaternion _targetRotation;
        bool _placed;

        public void Init(SpatailApp app)
        {
            _app = app;
            BuildBar();
            RebuildSuggestions();
            BuildTools();
            app.StateChanged += OnState;
            app.Planned += OnPlanned;
            app.Logged += OnLogged;
            Refresh();
        }

        // ---- construction ------------------------------------------------------------------

        void BuildBar()
        {
            Panel.Create(transform, "Bar", new Vector2(Width, Height), Height * 0.5f, Theme.Surface, Theme.AccentSoft, 0.003f);
            var barHit = new GameObject("Bar Hit");
            barHit.transform.SetParent(transform, false);
            Interactable.Add(barHit, new Vector3(Width, Height, 0.02f), BeginTyping);

            // Microphone button.
            const float micSize = 0.046f;
            var mic = new GameObject("Mic");
            mic.transform.SetParent(transform, false);
            mic.transform.localPosition = new Vector3(-Width * 0.5f + 0.011f + micSize * 0.5f, 0, -Theme.Layer);
            _mic = Panel.Disc(mic.transform, "Disc", micSize, Theme.Accent);
            Panel capsule = Panel.Create(mic.transform, "Capsule", new Vector2(0.011f, 0.019f), 0.0055f, Color.white, Color.white, 0f);
            capsule.transform.localPosition = new Vector3(0, 0.004f, -Theme.Layer);
            Panel stand = Panel.Create(mic.transform, "Stand", new Vector2(0.016f, 0.0028f), 0.0014f, Color.white, Color.white, 0f);
            stand.transform.localPosition = new Vector3(0, -0.012f, -Theme.Layer);
            Interactable.Add(mic, new Vector3(micSize * 1.3f, micSize * 1.3f, 0.03f), ToggleListening, new Vector3(0, 0, -0.005f));

            _text = SpatialText.Create(transform, Placeholder, 0.024f, Theme.Muted, TextWeight.Medium);
            _text.transform.localPosition = new Vector3(-Width * 0.5f + 0.011f + micSize + 0.016f, 0, -Theme.Layer);

            // "Built ..." chip at the right end.
            var chip = new GameObject("Chip");
            chip.transform.SetParent(transform, false);
            _chip = chip.transform;
            _chipText = SpatialText.Create(chip.transform, "", 0.018f, Color.white, TextWeight.SemiBold, TextAlign.Center);
            _chipText.transform.localPosition = new Vector3(0, 0, -Theme.Layer * 2f);
            _chipBackground = Panel.Create(chip.transform, "Background", new Vector2(0.1f, 0.04f), 0.02f, Theme.Accent, Theme.Accent, 0f);
            _chipBackground.transform.localPosition = new Vector3(0, 0, -Theme.Layer);
            chip.SetActive(false);

            _reply = SpatialText.Create(transform, "", 0.02f, Theme.Ink, TextWeight.Medium, TextAlign.Center, TextAnchor3D.Top, Width);
            _reply.transform.localPosition = new Vector3(0, -Height * 0.5f - 0.02f, 0);

            var suggestions = new GameObject("Suggestions");
            suggestions.transform.SetParent(transform, false);
            suggestions.transform.localPosition = new Vector3(0, Height * 0.5f + 0.034f, 0);
            _suggestionRow = suggestions.transform;

            var tools = new GameObject("Tools");
            tools.transform.SetParent(transform, false);
            tools.transform.localPosition = new Vector3(0, -Height * 0.5f - 0.105f, 0);
            _toolRow = tools.transform;
        }

        void RebuildSuggestions()
        {
            for (int i = _suggestionRow.childCount - 1; i >= 0; i--) Destroy(_suggestionRow.GetChild(i).gameObject);

            var picks = new List<string>();
            Build stepped = _app.Store.LatestStepped;
            if (stepped != null && stepped.currentStep < stepped.stepCount) picks.Add("Next");
            foreach (string s in Suggestions)
                if (!_used.Contains(s) && picks.Count < 3) picks.Add(s);
            if (picks.Count < 3 && _app.Store.Builds.Count > 0) picks.Add("Clear everything");

            var buttons = new List<PillButton>();
            float total = 0f;
            foreach (string pick in picks)
            {
                string prompt = pick;
                PillButton b = PillButton.Create(_suggestionRow, pick, 0.0165f, Theme.Surface, Theme.AccentSoft, Theme.Accent, () => Submit(prompt), 0.014f, 0.04f);
                buttons.Add(b);
                total += b.Size.x;
            }
            const float gap = 0.012f;
            total += gap * Mathf.Max(0, buttons.Count - 1);
            float x = -total * 0.5f;
            foreach (PillButton b in buttons)
            {
                b.transform.localPosition = new Vector3(x + b.Size.x * 0.5f, 0, 0);
                x += b.Size.x + gap;
            }
        }

        void BuildTools()
        {
            _moveButton = Tool("Move pump", () => _app.SetMovingStation(!_app.IsMovingStation));
            _twinButton = Tool("Twin: " + _app.Twin.Mode, CycleTwin);
            _shareButton = Tool("Share", ToggleShare);
            LayoutTools();
        }

        PillButton Tool(string label, System.Action onClick) =>
            PillButton.Create(_toolRow, label, 0.015f, Theme.SurfaceDim, Theme.AccentSoft, Theme.Muted, onClick, 0.013f, 0.036f);

        void LayoutTools()
        {
            PillButton[] buttons = { _moveButton, _twinButton, _shareButton };
            const float gap = 0.01f;
            float total = -gap;
            foreach (PillButton b in buttons) total += b.Size.x + gap;
            float x = -total * 0.5f;
            foreach (PillButton b in buttons)
            {
                b.transform.localPosition = new Vector3(x + b.Size.x * 0.5f, 0, 0);
                x += b.Size.x + gap;
            }
        }

        void CycleTwin()
        {
            TwinMode next = (TwinMode)(((int)_app.Twin.Mode + 1) % 4);
            _app.SetTwinMode(next);
            _twinButton.SetText("Twin: " + next, 0.013f);
            LayoutTools();
        }

        void ToggleShare()
        {
            if (_app.Session.IsActive) _app.LeaveSession();
            else _app.HostSession();
        }

        // ---- input -------------------------------------------------------------------------

        void Submit(string prompt)
        {
            StopInput();
            _used.Add(prompt);
            _typed = "";
            _app.Submit(prompt);
        }

        public void BeginTyping()
        {
            ITextEntry entry = _app.Platform.TextEntry;
            if (entry == null || _typing) return;
            StopListening();
            _typing = true;
            _typed = "";
            entry.Open("", s => { _typed = s ?? ""; Refresh(); }, s => { _typing = false; Submit(s); }, () => { _typing = false; _typed = ""; Refresh(); });
            Refresh();
        }

        void ToggleListening()
        {
            ISpeechInput speech = _app.Platform.Speech;
            if (speech == null || !speech.IsAvailable) { BeginTyping(); return; }
            if (_listening) { StopListening(); return; }
            if (_typing) { _app.Platform.TextEntry.Close(); _typing = false; }
            _listening = true;
            _typed = "";
            speech.StartListening(
                (text, final) =>
                {
                    _typed = text ?? "";
                    if (final)
                    {
                        _listening = false;
                        if (_typed.Trim().Length > 0) Submit(_typed);
                    }
                    Refresh();
                },
                error =>
                {
                    _listening = false;
                    _app.Log("Speech: " + error);
                    Refresh();
                });
            Refresh();
        }

        void StopListening()
        {
            if (!_listening) return;
            _listening = false;
            ISpeechInput speech = _app.Platform.Speech;
            if (speech != null) speech.StopListening();
        }

        void StopInput()
        {
            StopListening();
            if (_typing && _app.Platform.TextEntry != null) _app.Platform.TextEntry.Close();
            _typing = false;
        }

        // ---- state -------------------------------------------------------------------------

        void OnState(AppState state)
        {
            if (state == AppState.Thinking) HideChip();
            Refresh();
        }

        void OnPlanned(string prompt, PlanResult plan)
        {
            _typed = "";
            _reply.Text = plan.reply ?? "";
            if (!string.IsNullOrEmpty(plan.chip)) ShowChip("Built " + plan.chip);
            RebuildSuggestions();
            Refresh();
        }

        void OnLogged(string line)
        {
            // Session and placement hints reach the user through the reply line.
            if (line.StartsWith("Session:") || line.StartsWith("Point at the floor") || line == "Pump placed.")
            {
                _reply.Text = line;
                _shareButton.SetText(_app.Session.IsActive ? "Stop sharing" : "Share", 0.013f);
                LayoutTools();
            }
        }

        void ShowChip(string label)
        {
            _chipText.Text = label;
            float w = _chipText.Size.x + 0.026f;
            _chipBackground.SetSize(new Vector2(w, 0.04f));
            _chip.localPosition = new Vector3(Width * 0.5f - 0.014f - w * 0.5f, 0, 0);
            _chip.gameObject.SetActive(true);
            _chipTimer = 6f;
        }

        void HideChip() => _chip.gameObject.SetActive(false);

        void Refresh()
        {
            if (_text == null) return;
            string shown;
            Color color = Theme.Ink;
            switch (_app.State)
            {
                case AppState.Starting: shown = "Looking around..."; color = Theme.Muted; break;
                case AppState.Scanning: shown = "Reading the room..."; color = Theme.Muted; break;
                case AppState.Thinking: shown = _app.LastPrompt ?? ""; break;
                default:
                    if (_typed.Length > 0) shown = _typed;
                    else if (_listening) { shown = "Listening..."; color = Theme.Accent; }
                    else if (_typing) { shown = ""; }
                    else if (_chip.gameObject.activeSelf && !string.IsNullOrEmpty(_app.LastPrompt)) shown = _app.LastPrompt;
                    else { shown = Placeholder; color = Theme.Muted; }
                    break;
            }
            _text.Text = Truncate(shown, 44);
            _text.Color = color;
        }

        static string Truncate(string s, int max) => s.Length <= max ? s : "..." + s.Substring(s.Length - max + 3);

        void Update()
        {
            Follow();

            // Caret while typing, pulse while listening or thinking.
            if (_typing && _app.State == AppState.Ready)
            {
                bool caret = Mathf.Repeat(Time.unscaledTime, 1f) < 0.55f;
                _text.Text = Truncate(_typed, 43) + (caret ? "|" : "");
                _text.Color = Theme.Ink;
            }
            bool busy = _listening || _app.State == AppState.Thinking;
            float pulse = busy ? 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 7f) : 1f;
            _mic.transform.parent.localScale = Vector3.one * pulse;
            _mic.SetColors(_listening ? Theme.Warning : Theme.Accent, Theme.Accent);

            if (_chip.gameObject.activeSelf)
            {
                _chipTimer -= Time.unscaledDeltaTime;
                if (_chipTimer <= 0f) { HideChip(); Refresh(); }
            }
            _moveButton.SetColors(_app.IsMovingStation ? Theme.Accent : Theme.SurfaceDim, Theme.AccentSoft, _app.IsMovingStation ? Color.white : Theme.Muted);
        }

        /// <summary>Stay within reach: glide to a spot below the line of sight when the user has turned or walked away.</summary>
        void Follow()
        {
            Camera head = _app.Head;
            if (head == null) return;
            Vector3 forward = head.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) return;
            forward.Normalize();
            Vector3 ideal = head.transform.position + forward * Distance + Vector3.down * Drop;

            bool moved = !_placed || Vector3.Distance(ideal, _targetPosition) > 0.45f;
            if (moved)
            {
                _targetPosition = ideal;
                // Face the user, tipped back so it reads comfortably from above.
                _targetRotation = Quaternion.LookRotation((ideal - head.transform.position).normalized, Vector3.up);
                if (!_placed)
                {
                    transform.position = _targetPosition;
                    transform.rotation = _targetRotation;
                    _placed = true;
                }
            }
            float k = 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, _targetPosition, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, _targetRotation, k);
        }
    }
}
