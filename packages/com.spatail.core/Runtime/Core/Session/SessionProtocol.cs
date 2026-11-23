using System.Collections.Generic;

namespace Spatail.Core
{
    /// <summary>
    /// Messages exchanged between linked devices. One JSON object per line over TCP.
    /// Today the link carries requests (a phone can drive a headset), replies, presence and settings. Programs
    /// are not mirrored between devices yet: each is written against one device's view of the space.
    /// </summary>
    public static class SessionMessage
    {
        /// <summary>First message in each direction. Carries the sender's display name and role.</summary>
        public const string Hello = "hello";
        /// <summary>A prompt the sender wants the host to plan and execute.</summary>
        public const string Prompt = "prompt";
        /// <summary>Head position and gaze of a participant, in the sender's world frame.</summary>
        public const string Presence = "presence";
        /// <summary>A participant left.</summary>
        public const string Bye = "bye";
        /// <summary>Host's status line for the prompt it just handled.</summary>
        public const string Reply = "reply";

        /// <summary>Planner settings pushed from one device to another, so a headset can be configured from the dev tool.</summary>
        public const string Config = "config";

        public static string MakeConfig(string plannerUrl, string plannerToken, bool useAi) => Json.Write(new Dictionary<string, object>
        {
            { "type", Config }, { "plannerUrl", plannerUrl ?? "" }, { "plannerToken", plannerToken ?? "" }, { "useAi", useAi },
        });

        public static string MakeHello(string name, string role, string device) => Json.Write(new Dictionary<string, object>
        {
            { "type", Hello }, { "name", name }, { "role", role }, { "device", device }, { "protocol", 1 },
        });

        public static string MakePrompt(string text, string from) => Json.Write(new Dictionary<string, object>
        {
            { "type", Prompt }, { "text", text }, { "from", from },
        });

        public static string MakePresence(string name, Vec3 position, Vec3 forward) => Json.Write(new Dictionary<string, object>
        {
            { "type", Presence }, { "name", name }, { "position", Json.FromVec3(position) }, { "forward", Json.FromVec3(forward) },
        });

        public static string MakeBye(string name) => Json.Write(new Dictionary<string, object> { { "type", Bye }, { "name", name } });

        public static string MakeReply(string text, string chip, string planner) => Json.Write(new Dictionary<string, object>
        {
            { "type", Reply }, { "text", text ?? "" }, { "chip", chip ?? "" }, { "planner", planner ?? "" },
        });
    }

    /// <summary>Someone else in the session.</summary>
    public sealed class Peer
    {
        public string name = "Guest";
        public string role = "";
        public string device = "";
        /// <summary>Head position in the sender's world frame. Valid once <see cref="hasPose"/> is true.</summary>
        public Vec3 position;
        public Vec3 forward = Vec3.Forward;
        public bool hasPose;
    }
}
