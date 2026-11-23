using UnityEngine;

namespace Spatail
{
    /// <summary>User-adjustable settings, persisted on the device with PlayerPrefs.</summary>
    public sealed class SpatailSettings
    {
        const string Prefix = "spatail.";

        /// <summary>Base URL of the Spatail planner service, such as http://192.168.1.20:8787. Empty = offline only.</summary>
        public string PlannerUrl
        {
            get => PlayerPrefs.GetString(Prefix + "plannerUrl", "");
            set { PlayerPrefs.SetString(Prefix + "plannerUrl", (value ?? "").Trim()); PlayerPrefs.Save(); }
        }

        /// <summary>Shared secret for the planner service, if it requires one. This is not a model API key.</summary>
        public string PlannerToken
        {
            get => PlayerPrefs.GetString(Prefix + "plannerToken", "");
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
