using UnityEngine;

namespace Spatail
{
    /// <summary>User-adjustable settings, persisted on the device with PlayerPrefs.</summary>
    public sealed class SpatailSettings
    {
        const string Prefix = "spatail.";

        /// <summary>
        /// Optional build-time defaults. Put a file at Assets/Resources/SpatailConfig.json in the app project:
        /// { "plannerUrl": "http://192.168.1.20:8787", "plannerToken": "" }. Values saved on the device win.
        /// </summary>
        public const string ConfigResource = "SpatailConfig";

        static string _defaultUrl, _defaultToken;
        static bool _defaultsLoaded;

        static void LoadDefaults()
        {
            if (_defaultsLoaded) return;
            _defaultsLoaded = true;
            _defaultUrl = "";
            _defaultToken = "";
            var asset = Resources.Load<TextAsset>(ConfigResource);
            if (asset == null) return;
            object parsed;
            if (!Spatail.Core.Json.TryParse(asset.text, out parsed)) { Debug.LogWarning("[Spatail] " + ConfigResource + ".json is not valid JSON; ignoring it."); return; }
            var o = parsed as System.Collections.Generic.Dictionary<string, object>;
            if (o == null) return;
            _defaultUrl = Spatail.Core.Json.Str(o, "plannerUrl", "").Trim();
            _defaultToken = Spatail.Core.Json.Str(o, "plannerToken", "").Trim();
        }

        /// <summary>Base URL of the Spatail planner service, such as http://192.168.1.20:8787. Empty = offline only.</summary>
        public string PlannerUrl
        {
            get { LoadDefaults(); return PlayerPrefs.GetString(Prefix + "plannerUrl", _defaultUrl); }
            set { PlayerPrefs.SetString(Prefix + "plannerUrl", (value ?? "").Trim()); PlayerPrefs.Save(); }
        }

        /// <summary>Shared secret for the planner service, if it requires one. This is not a model API key.</summary>
        public string PlannerToken
        {
            get { LoadDefaults(); return PlayerPrefs.GetString(Prefix + "plannerToken", _defaultToken); }
            set { PlayerPrefs.SetString(Prefix + "plannerToken", (value ?? "").Trim()); PlayerPrefs.Save(); }
        }

        public bool UseAi
        {
            get => PlayerPrefs.GetInt(Prefix + "useAi", 1) == 1;
            set { PlayerPrefs.SetInt(Prefix + "useAi", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Name shown to other people in a shared session.</summary>
        public string DisplayName
        {
            get => PlayerPrefs.GetString(Prefix + "displayName", "");
            set { PlayerPrefs.SetString(Prefix + "displayName", (value ?? "").Trim()); PlayerPrefs.Save(); }
        }

        /// <summary>
        /// Whether the stand-in pump hologram is in the room. It is a demo prop for places with no real equipment;
        /// turn it off to work purely from what the device senses.
        /// </summary>
        public bool StandIn
        {
            get => PlayerPrefs.GetInt(Prefix + "standIn", 1) == 1;
            set { PlayerPrefs.SetInt(Prefix + "standIn", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public TwinMode TwinMode
        {
            get => (TwinMode)PlayerPrefs.GetInt(Prefix + "twinMode", (int)TwinMode.Solid);
            set { PlayerPrefs.SetInt(Prefix + "twinMode", (int)value); PlayerPrefs.Save(); }
        }

        /// <summary>Last address joined from this device.</summary>
        public string SessionAddress
        {
            get => PlayerPrefs.GetString(Prefix + "sessionAddress", "");
            set { PlayerPrefs.SetString(Prefix + "sessionAddress", (value ?? "").Trim()); PlayerPrefs.Save(); }
        }
    }
}
