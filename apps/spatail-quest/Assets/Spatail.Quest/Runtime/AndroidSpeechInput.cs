using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Android;

namespace Spatail.Quest
{
    /// <summary>
    /// Speech-to-text through Android's <c>android.speech.SpeechRecognizer</c>.
    ///
    /// EXPERIMENTAL AND UNTESTED ON HORIZON OS. Quest headsets run an Android build without Google services, and it
    /// is not confirmed that they ship a system <c>RecognitionService</c>. Everything here is therefore guarded:
    /// <see cref="IsAvailable"/> is only true when the OS reports that speech recognition is available, and when it
    /// is false the prompt bar falls back to the system keyboard (which has its own dictation key). Nothing in this
    /// class runs in the Editor.
    ///
    /// All SpeechRecognizer calls are made on the Android UI thread, as the API requires, and results are handed back
    /// to Unity's main thread through a queue drained in <see cref="Update"/>.
    /// </summary>
    public sealed class AndroidSpeechInput : MonoBehaviour, ISpeechInput
    {
        readonly Queue<Action> _mainThread = new Queue<Action>();
        Action<string, bool> _onResult;
        Action<string> _onError;
        bool _availabilityChecked, _available;

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject _activity, _recognizer;
        Listener _listener;
#endif

        public bool IsListening { get; private set; }

        public bool IsAvailable
        {
            get
            {
                if (_availabilityChecked) return _available;
                _availabilityChecked = true;
#if UNITY_ANDROID && !UNITY_EDITOR
                try
                {
                    using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                        _activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                    using (var recognizerClass = new AndroidJavaClass("android.speech.SpeechRecognizer"))
                        _available = _activity != null && recognizerClass.CallStatic<bool>("isRecognitionAvailable", _activity);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Spatail] Speech recognition check failed: " + ex.Message);
                    _available = false;
                }
                Debug.Log("[Spatail] Android speech recognition available: " + _available);
#else
                _available = false;
#endif
                return _available;
            }
        }

        public void StartListening(Action<string, bool> onResult, Action<string> onError)
        {
            _onResult = onResult;
            _onError = onError;
            if (!IsAvailable)
            {
                if (onError != null) onError("Speech recognition is not available on this device.");
                return;
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                // Ask once; the user taps the microphone again after answering.
                Permission.RequestUserPermission(Permission.Microphone);
                if (onError != null) onError("Allow the microphone, then tap the mic again.");
                return;
            }
            IsListening = true;
            RunOnUiThread(BeginOnUiThread);
#endif
        }

        public void StopListening()
        {
            if (!IsListening) return;
            IsListening = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            RunOnUiThread(() =>
            {
                try { if (_recognizer != null) _recognizer.Call("stopListening"); }
                catch (Exception ex) { Debug.LogWarning("[Spatail] stopListening failed: " + ex.Message); }
            });
#endif
        }

        void Update()
        {
            while (true)
            {
                Action action;
                lock (_mainThread)
                {
                    if (_mainThread.Count == 0) break;
                    action = _mainThread.Dequeue();
                }
                action();
            }
        }

        void Post(Action action)
        {
            lock (_mainThread) _mainThread.Enqueue(action);
        }

        void DeliverResult(string text, bool final)
        {
            Post(() =>
            {
                if (final) IsListening = false;
                if (_onResult != null) _onResult(text ?? "", final);
            });
        }

        void DeliverError(string message)
        {
            Post(() =>
            {
                IsListening = false;
                if (_onError != null) _onError(message);
            });
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        void RunOnUiThread(Action action)
        {
            _activity.Call("runOnUiThread", new AndroidJavaRunnable(action));
        }

        void BeginOnUiThread()
        {
            try
            {
                if (_recognizer == null)
                {
                    using (var recognizerClass = new AndroidJavaClass("android.speech.SpeechRecognizer"))
                        _recognizer = recognizerClass.CallStatic<AndroidJavaObject>("createSpeechRecognizer", _activity);
                    _listener = new Listener(this);
                    _recognizer.Call("setRecognitionListener", _listener);
                }
                using (var intent = new AndroidJavaObject("android.content.Intent", "android.speech.action.RECOGNIZE_SPEECH"))
                {
                    intent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.LANGUAGE_MODEL", "free_form").Dispose();
                    intent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.PARTIAL_RESULTS", true).Dispose();
                    intent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.MAX_RESULTS", 1).Dispose();
                    _recognizer.Call("startListening", intent);
                }
            }
            catch (Exception ex)
            {
                DeliverError("Could not start speech recognition: " + ex.Message);
            }
        }

        void OnDestroy()
        {
            if (_recognizer == null) return;
            AndroidJavaObject recognizer = _recognizer;
            _recognizer = null;
            try
            {
                RunOnUiThread(() =>
                {
                    try { recognizer.Call("destroy"); recognizer.Dispose(); }
                    catch (Exception) { }
                });
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Receives <c>android.speech.RecognitionListener</c> callbacks. Dispatching by method name in
        /// <see cref="Invoke(string, object[])"/> avoids relying on reflection to match every Java signature.
        /// </summary>
        sealed class Listener : AndroidJavaProxy
        {
            readonly AndroidSpeechInput _owner;

            public Listener(AndroidSpeechInput owner) : base("android.speech.RecognitionListener")
            {
                _owner = owner;
            }

            public override AndroidJavaObject Invoke(string methodName, object[] args)
            {
                switch (methodName)
                {
                    case "onPartialResults":
                        _owner.DeliverResult(FirstResult(args), false);
                        return null;
                    case "onResults":
                        _owner.DeliverResult(FirstResult(args), true);
                        return null;
                    case "onError":
                        _owner.DeliverError(Describe(args != null && args.Length > 0 && args[0] is int ? (int)args[0] : -1));
                        return null;
                    case "onReadyForSpeech":
                    case "onBeginningOfSpeech":
                    case "onRmsChanged":
                    case "onBufferReceived":
                    case "onEndOfSpeech":
                    case "onEvent":
                        return null;
                    default:
                        // equals, hashCode, toString
                        return base.Invoke(methodName, args);
                }
            }

            static string FirstResult(object[] args)
            {
                var bundle = args != null && args.Length > 0 ? args[0] as AndroidJavaObject : null;
                if (bundle == null) return "";
                using (AndroidJavaObject list = bundle.Call<AndroidJavaObject>("getStringArrayList", "results_recognition"))
                {
                    if (list == null || list.Call<int>("size") == 0) return "";
                    return list.Call<string>("get", 0) ?? "";
                }
            }

            static string Describe(int code)
            {
                switch (code)
                {
                    case 1: return "Network timeout.";
                    case 2: return "Network error.";
                    case 3: return "Microphone error.";
                    case 4: return "Speech service error.";
                    case 5: return "Speech recognition was interrupted.";
                    case 6: return "I did not hear anything.";
                    case 7: return "I did not catch that.";
                    case 8: return "Speech recognition is busy.";
                    case 9: return "Microphone permission is missing.";
                    default: return "Speech recognition failed (" + code + ").";
                }
            }
        }
#endif
    }
}
