using System;
using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace Spatail.Quest.EditorTools
{
    /// <summary>
    /// <c>Spatail > Record Demo (Play Mode)</c>: enters Play mode, waits for Spatail to be ready, sends the demo
    /// requests one after another with pauses, and records the Game view to <c>Recordings/spatail-&lt;time&gt;.mp4</c>
    /// with Unity Recorder.
    ///
    /// It records whatever Play mode is running: the built-in desktop simulator when no XR runtime is active, or the
    /// Quest platform when the Editor is driven by the Meta XR Simulator or a headset over Link. The recording is of
    /// Unity's Game view, so it shows what Unity renders. Passthrough is composited by the XR runtime, outside
    /// Unity, and therefore is not in it (see the README).
    ///
    /// Entering Play mode reloads scripts, so the run is tracked in SessionState and picked up again afterwards.
    /// </summary>
    [InitializeOnLoad]
    public static class SpatailDemoRecorder
    {
        public const int Width = 1920, Height = 1080;
        public const float FrameRate = 30f;

        struct Step
        {
            public string Prompt;
            public float Hold;   // seconds of recorded time to stay on the result
            public Step(string prompt, float hold) { Prompt = prompt; Hold = hold; }
        }

        // The six requests from the concept film; "next" steps through the walkthrough it starts with.
        static readonly Step[] Script =
        {
            new Step("Build me a walkthrough for this pump", 6f),
            new Step("next", 4f),
            new Step("next", 4f),
            new Step("Show me how the water flows", 7f),
            new Step("Has it failed before?", 6f),
            new Step("Put this month's output by the bench", 6f),
            new Step("What parts do I need?", 6f),
            new Step("Bring Sara in", 7f),
        };

        const string ActiveKey = "Spatail.RecordDemo.Active";
        const float ReadyTimeout = 90f;    // real seconds to wait for the app to start
        const float BuildTimeout = 180f;   // real seconds to wait for one request (the planner can take a while)
        const float LeadIn = 1.5f, LeadOut = 2f;

        enum Phase { WaitingForApp, LeadIn, Submit, WaitingForResult, Holding, LeadOut, Done }

        static Phase _phase;
        static int _step;
        static double _phaseStartedReal;
        static float _phaseStartedGame;
        static RecorderController _controller;
        static RecorderControllerSettings _controllerSettings;
        static MovieRecorderSettings _movieSettings;
        static string _outputFile;

        static SpatailDemoRecorder()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            // This runs again after the script reload that entering Play mode causes.
            if (SessionState.GetBool(ActiveKey, false))
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) Begin();
                else SessionState.SetBool(ActiveKey, false);
            }
        }

        [MenuItem("Spatail/Record Demo (Play Mode)", priority = 40)]
        public static void Record()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Spatail", "Leave Play mode first. The recording starts Play mode itself.", "OK");
                return;
            }
            SessionState.SetBool(ActiveKey, true);
            Begin();
            EditorApplication.EnterPlaymode();
        }

        [MenuItem("Spatail/Record Demo (Play Mode)", true)]
        static bool RecordAvailable() => !EditorApplication.isCompiling;

        /// <summary>True when the Meta XR Simulator has been activated for this Editor (Window > Meta > Meta XR Simulator > Activate).</summary>
        public static bool MetaXrSimulatorActive
        {
            get
            {
                // Activating the simulator points the OpenXR loader at its runtime through this variable
                // (Meta.XR.Simulator.Editor.XRSimUtils.IsSimulatorActivated).
                string runtime = Environment.GetEnvironmentVariable("XR_SELECTED_RUNTIME_JSON");
                return !string.IsNullOrEmpty(runtime) && runtime.Replace('\\', '/').EndsWith("/meta_openxr_simulator.json", StringComparison.OrdinalIgnoreCase);
            }
        }

        static void Begin()
        {
            _phase = Phase.WaitingForApp;
            _step = 0;
            _phaseStartedReal = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingPlayMode || !SessionState.GetBool(ActiveKey, false)) return;
            // Play mode was stopped by hand, or the run is over: either way, close the file properly.
            Finish(_phase == Phase.Done ? null : "Play mode ended before the script finished.");
        }

        static void Enter(Phase phase)
        {
            _phase = phase;
            _phaseStartedReal = EditorApplication.timeSinceStartup;
            _phaseStartedGame = Time.time;
        }

        static double RealSeconds => EditorApplication.timeSinceStartup - _phaseStartedReal;
        // Recorded time: with a constant frame rate the Recorder advances game time by exactly one frame per frame.
        static float RecordedSeconds => Time.time - _phaseStartedGame;

        static void Tick()
        {
            if (!SessionState.GetBool(ActiveKey, false))
            {
                EditorApplication.update -= Tick;
                return;
            }
            if (!EditorApplication.isPlaying)
            {
                // Still on the way into Play mode. Give up if it never arrives (for example a compile error).
                if (!EditorApplication.isPlayingOrWillChangePlaymode && RealSeconds > 15) Finish("Play mode did not start.");
                return;
            }

            SpatailApp app = SpatailApp.Instance;
            switch (_phase)
            {
                case Phase.WaitingForApp:
                    if (app != null && app.State == AppState.Ready)
                    {
                        if (!StartRecording(app)) { Finish("The recorder could not be started; see the Console."); ExitPlayMode(); return; }
                        Enter(Phase.LeadIn);
                    }
                    else if (RealSeconds > ReadyTimeout)
                    {
                        Finish("Spatail did not become ready within " + ReadyTimeout + " s.");
                        ExitPlayMode();
                    }
                    break;

                case Phase.LeadIn:
                    if (RecordedSeconds >= LeadIn) Enter(Phase.Submit);
                    break;

                case Phase.Submit:
                    if (app == null) { Finish("Spatail stopped."); ExitPlayMode(); return; }
                    Debug.Log("[Spatail] Recording: \"" + Script[_step].Prompt + "\"");
                    app.Submit(Script[_step].Prompt);
                    Enter(Phase.WaitingForResult);
                    break;

                case Phase.WaitingForResult:
                    if (app == null || app.State == AppState.Ready) Enter(Phase.Holding);
                    else if (RealSeconds > BuildTimeout)
                    {
                        Debug.LogWarning("[Spatail] Recording: \"" + Script[_step].Prompt + "\" did not finish within " + BuildTimeout + " s; moving on.");
                        Enter(Phase.Holding);
                    }
                    break;

                case Phase.Holding:
                    if (RecordedSeconds < Script[_step].Hold) break;
                    _step++;
                    Enter(_step < Script.Length ? Phase.Submit : Phase.LeadOut);
                    break;

                case Phase.LeadOut:
                    if (RecordedSeconds < LeadOut) break;
                    _phase = Phase.Done;
                    Finish(null);
                    ExitPlayMode();
                    break;
            }
        }

        static bool StartRecording(SpatailApp app)
        {
            string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Recordings");
            Directory.CreateDirectory(folder);
            // The Recorder adds the extension itself.
            string basePath = Path.Combine(folder, "spatail-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")).Replace('\\', '/');

            _controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            _movieSettings = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            _movieSettings.name = "Spatail Demo";
            _movieSettings.Enabled = true;
            var encoder = new CoreEncoderSettings { EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High };
#if UNITY_EDITOR_LINUX
            // Unity's built-in encoder has no H.264 on Linux; WebM is what it offers there.
            encoder.Codec = CoreEncoderSettings.OutputCodec.WEBM;
            _outputFile = basePath + ".webm";
#else
            encoder.Codec = CoreEncoderSettings.OutputCodec.MP4;
            _outputFile = basePath + ".mp4";
#endif
            _movieSettings.EncoderSettings = encoder;
            _movieSettings.CaptureAudio = false;
            _movieSettings.ImageInputSettings = new GameViewInputSettings { OutputWidth = Width, OutputHeight = Height };
            _movieSettings.OutputFile = basePath;

            _controllerSettings.AddRecorderSettings(_movieSettings);
            _controllerSettings.SetRecordModeToManual();
            _controllerSettings.FrameRatePlayback = FrameRatePlayback.Constant;
            _controllerSettings.FrameRate = FrameRate;
            // Play at the recorded speed, so what is on screen while recording is what ends up in the file.
            _controllerSettings.CapFrameRate = true;

            _controller = new RecorderController(_controllerSettings);
            try
            {
                _controller.PrepareRecording();
                if (!_controller.StartRecording()) return false;
            }
            catch (Exception ex)
            {
                Debug.LogError("[Spatail] Recording could not start: " + ex.Message);
                return false;
            }

            string device = app.Platform != null ? app.Platform.DeviceName : "?";
            string mode = device == "sim" ? "the built-in desktop simulator"
                : MetaXrSimulatorActive ? "the Quest platform on the Meta XR Simulator"
                : "the Quest platform on the active XR runtime";
            Debug.Log("[Spatail] Recording " + mode + " to " + _outputFile + "\n  Sensing: " + (app.Sensor != null ? app.Sensor.Status : "none"));
            return true;
        }

        static void Finish(string problem)
        {
            SessionState.SetBool(ActiveKey, false);
            EditorApplication.update -= Tick;

            bool recorded = _controller != null;
            if (_controller != null)
            {
                try { _controller.StopRecording(); }
                catch (Exception ex) { Debug.LogWarning("[Spatail] Stopping the recorder failed: " + ex.Message); }
                _controller = null;
            }
            if (_movieSettings != null) UnityEngine.Object.DestroyImmediate(_movieSettings);
            if (_controllerSettings != null) UnityEngine.Object.DestroyImmediate(_controllerSettings);
            _movieSettings = null;
            _controllerSettings = null;

            if (problem != null) Debug.LogError("[Spatail] Recording stopped early: " + problem);
            if (!recorded) return;

            SpatailApp app = SpatailApp.Instance;
            if (app != null && app.Sensor != null) Debug.Log("[Spatail] Sensing at the end of the recording: " + app.Sensor.Status);
            Debug.Log("[Spatail] Recording saved to " + _outputFile);
            string file = _outputFile;
            // The file is closed a moment after the recorder stops; show it once the Editor is idle again.
            EditorApplication.delayCall += () => { if (File.Exists(file)) EditorUtility.RevealInFinder(file); };
        }

        static void ExitPlayMode()
        {
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        }
    }
}
