using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Spatail.DevTool
{
    /// <summary>
    /// Speech-to-text for the prompt field, backed by the native SFSpeechRecognizer plugin (SpatailSpeech.mm).
    /// Native callbacks are queued and replayed on Unity's main loop. Outside an iOS player the component reports
    /// itself unavailable and the UI falls back to the keyboard, which has its own dictation key.
    /// </summary>
    public sealed class IosSpeechInput : MonoBehaviour, ISpeechInput
    {
        const int Partial = 0, Final = 1, Error = 2, Stopped = 3;

        struct NativeEvent
        {
            public int kind;
            public string text;
        }

        static readonly Queue<NativeEvent> Events = new Queue<NativeEvent>();
        static readonly object Gate = new object();

        Action<string, bool> _onResult;
        Action<string> _onError;
        bool _listening;

        /// <summary>BCP 47 language tag such as "en-US". Empty uses the device language.</summary>
        public string Locale = "";

#if UNITY_IOS && !UNITY_EDITOR
        delegate void NativeCallback(int kind, IntPtr text);

        [DllImport("__Internal")] static extern int SpatailSpeech_IsSupported();
        [DllImport("__Internal")] static extern void SpatailSpeech_Start(string locale, NativeCallback callback);
        [DllImport("__Internal")] static extern void SpatailSpeech_Stop();

        [AOT.MonoPInvokeCallback(typeof(NativeCallback))]
        static void OnNative(int kind, IntPtr text)
        {
            string value = text != IntPtr.Zero ? Marshal.PtrToStringUTF8(text) : "";
            lock (Gate) Events.Enqueue(new NativeEvent { kind = kind, text = value ?? "" });
        }

        public bool IsAvailable => SpatailSpeech_IsSupported() != 0;
#else
        public bool IsAvailable => false;
#endif

        public bool IsListening => _listening;

        public void StartListening(Action<string, bool> onResult, Action<string> onError)
        {
            _onResult = onResult;
            _onError = onError;
#if UNITY_IOS && !UNITY_EDITOR
            lock (Gate) Events.Clear();
            _listening = true;
            SpatailSpeech_Start(Locale ?? "", OnNative);
#else
            if (onError != null) onError("Speech recognition is only available on an iOS device.");
#endif
        }

        public void StopListening()
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (_listening) SpatailSpeech_Stop();
#endif
        }

        void Update()
        {
            while (true)
            {
                NativeEvent e;
                lock (Gate)
                {
                    if (Events.Count == 0) break;
                    e = Events.Dequeue();
                }
                switch (e.kind)
                {
                    case Partial:
                        if (_onResult != null) _onResult(e.text, false);
                        break;
                    case Final:
                        _listening = false;
                        if (_onResult != null) _onResult(e.text, true);
                        break;
                    case Error:
                        _listening = false;
                        if (_onError != null) _onError(e.text);
                        break;
                    case Stopped:
                        _listening = false;
                        if (_onResult != null) _onResult("", true);
                        break;
                }
            }
        }

        void OnDisable()
        {
            StopListening();
        }
    }
}
