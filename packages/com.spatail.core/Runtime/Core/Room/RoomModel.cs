using System;
using System.Collections.Generic;

namespace Spatail.Core
{
    /// <summary>Semantic categories for things Spatail knows about in the room.</summary>
    public static class EntityKind
    {
        public const string Floor = "floor";
        public const string Wall = "wall";
        public const string Ceiling = "ceiling";
        public const string Table = "table";
        public const string Seat = "seat";
        public const string Storage = "storage";
        public const string Door = "door";
        public const string Window = "window";
        public const string Screen = "screen";
        /// <summary>A registered machine (the digital twin's root).</summary>
        public const string Equipment = "equipment";
        /// <summary>A named component of a piece of equipment, such as a valve or motor.</summary>
        public const string Part = "part";
        /// <summary>Supporting furniture that belongs to the site definition, such as a control box.</summary>
        public const string Fixture = "fixture";
        public const string Other = "other";

        public static bool IsSurface(string kind) => kind == Floor || kind == Wall || kind == Ceiling;
    }

    /// <summary>
    /// One thing in the room: a surface, a piece of furniture, or a part of registered equipment.
    /// <see cref="position"/> is the centre of the bounding box, <see cref="size"/> its full extent along the
    /// entity's local axes. Walls face into the room along local +Z.
    /// </summary>
    public sealed class RoomEntity
    {
        public string id;
        public string kind = EntityKind.Other;
        public string label;
        public List<string> aliases = new List<string>();
        public string about;
        public Vec3 position;
        public Quat rotation = Quat.Identity;
        public Vec3 size;
        public string parentId;
        /// <summary>True when Spatail is drawing a stand-in hologram because nothing real is bound to this entity.</summary>
        public bool isVirtual;
        /// <summary>Preferred axis for rotate/turn gizmos, in entity-local space. Zero when not applicable.</summary>
        public Vec3 actionAxis;
        /// <summary>Where the action happens relative to the centre of the bounds, in entity-local space.</summary>
        public Vec3 actionOffset;
        public bool hasActionPoint;
        /// <summary>World position gizmos should centre on: the action point when known, else the top of the bounds.</summary>
        public Vec3 ActionPoint => hasActionPoint ? WorldPoint(actionOffset) : TopCenter;
        /// <summary>Centre of the bounds inside the frame the hologram shapes were authored in.</summary>
        public Vec3 localCenter;
        /// <summary>Stand-in hologram geometry, or null when the entity is a real object. Not serialized.</summary>
        public List<ShapeDef> shapes;
        /// <summary>Whether the room scan announces this entity with a label.</summary>
        public bool scanLabel;

        /// <summary>World position of the origin of the frame the shapes were authored in.</summary>
        public Vec3 ShapeOrigin => position - rotation * localCenter;

        public Pose3 Pose => new Pose3(position, rotation);
        public Vec3 Up => rotation * Vec3.Up;
        public Vec3 Forward => rotation * Vec3.Forward;
        public Vec3 Right => rotation * Vec3.Right;

        /// <summary>Entity-local offset (metres) to world space.</summary>
        public Vec3 WorldPoint(Vec3 local) => position + rotation * local;

        public Vec3 TopCenter => position + rotation * new Vec3(0, size.y * 0.5f, 0);
        public Vec3 BottomCenter => position + rotation * new Vec3(0, -size.y * 0.5f, 0);

        /// <summary>Radius of the footprint on the floor plane.</summary>
        public float FootprintRadius => 0.5f * (float)Math.Sqrt(size.x * size.x + size.z * size.z);

        /// <summary>Half-extent of the bounds along a world direction.</summary>
        public float ExtentAlong(Vec3 worldDir)
        {
            Vec3 d = rotation.Inverse * worldDir.Normalized;
            return 0.5f * (Math.Abs(d.x) * size.x + Math.Abs(d.y) * size.y + Math.Abs(d.z) * size.z);
        }

        public Dictionary<string, object> ToJson()
        {
            var o = new Dictionary<string, object>
            {
                { "id", id }, { "kind", kind }, { "label", label },
                { "position", Json.FromVec3(position) },
                { "rotation", new List<object> { Json.Round(rotation.x), Json.Round(rotation.y), Json.Round(rotation.z), Json.Round(rotation.w) } },
                { "size", Json.FromVec3(size) },
            };
            if (aliases.Count > 0) o["aliases"] = new List<object>(aliases);
            if (!string.IsNullOrEmpty(about)) o["about"] = about;
            if (!string.IsNullOrEmpty(parentId)) o["parent"] = parentId;
            if (isVirtual) o["virtual"] = true;
            if (actionAxis.SqrLength > 0) o["actionAxis"] = Json.FromVec3(actionAxis);
            if (hasActionPoint) o["actionOffset"] = Json.FromVec3(actionOffset);
            return o;
        }

        public static RoomEntity FromJson(Dictionary<string, object> o)
        {
            var e = new RoomEntity
            {
                id = o.Str("id"),
                kind = o.Str("kind", EntityKind.Other),
                label = o.Str("label"),
                about = o.Str("about"),
                position = o.Vec("position"),
                size = o.Vec("size"),
                parentId = o.Str("parent"),
                isVirtual = o.Bool("virtual"),
                actionAxis = o.Vec("actionAxis"),
                actionOffset = o.Vec("actionOffset"),
                hasActionPoint = o.Has("actionOffset"),
                aliases = o.StrList("aliases"),
            };
            var r = o.Arr("rotation");
            if (r != null && r.Count >= 4)
                e.rotation = new Quat((float)Json.ToDouble(r[0]), (float)Json.ToDouble(r[1]), (float)Json.ToDouble(r[2]), (float)Json.ToDouble(r[3]));
            return e;
        }
    }

    /// <summary>
    /// Spatail's picture of the room: every surface, piece of furniture and equipment part it can anchor content to,
    /// plus where the user is standing. This is the context handed to the planner.
    /// </summary>
    public sealed class RoomModel
    {
        public readonly List<RoomEntity> entities = new List<RoomEntity>();
        public Vec3 userPosition = new Vec3(0, 1.6f, -2f);
        public Vec3 userForward = Vec3.Forward;
        public float floorY;

        public RoomEntity Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < entities.Count; i++)
                if (entities[i].id == id) return entities[i];
            return null;
        }

        public List<RoomEntity> OfKind(string kind)
        {
            var result = new List<RoomEntity>();
            for (int i = 0; i < entities.Count; i++)
                if (entities[i].kind == kind) result.Add(entities[i]);
            return result;
        }

        public RoomEntity FirstOfKind(string kind)
        {
            for (int i = 0; i < entities.Count; i++)
                if (entities[i].kind == kind) return entities[i];
            return null;
        }

        /// <summary>Entities people would name out loud: equipment, parts, fixtures and furniture.</summary>
        public List<RoomEntity> Nameable()
        {
            var result = new List<RoomEntity>();
            for (int i = 0; i < entities.Count; i++)
                if (!EntityKind.IsSurface(entities[i].kind) && !string.IsNullOrEmpty(entities[i].label)) result.Add(entities[i]);
            return result;
        }

        /// <summary>
        /// Finds the entity a phrase refers to ("the gate valve", "bench", "motor"). Accepts ids, labels and aliases;
        /// returns null when nothing in the room matches.
        /// </summary>
        public RoomEntity Resolve(string phrase)
        {
            if (string.IsNullOrEmpty(phrase)) return null;
            string p = TextMatch.Normalize(phrase);
            if (p.Length == 0) return null;
            RoomEntity exact = Get(phrase.Trim());
            if (exact != null) return exact;

            RoomEntity best = null;
            int bestScore = 0;
            for (int i = 0; i < entities.Count; i++)
            {
                RoomEntity e = entities[i];
                int score = Math.Max(TextMatch.Score(p, e.label), TextMatch.Score(p, e.id));
                for (int a = 0; a < e.aliases.Count; a++) score = Math.Max(score, TextMatch.Score(p, e.aliases[a]));
                if (score == 0 && TextMatch.ContainsWord(p, e.kind)) score = 1;
                if (score > bestScore) { bestScore = score; best = e; }
            }
            return best;
        }

        /// <summary>Nearest entity of a kind to a point, or null.</summary>
        public RoomEntity Nearest(string kind, Vec3 point, float maxDistance = float.MaxValue)
        {
            RoomEntity best = null;
            float bestD = maxDistance;
            for (int i = 0; i < entities.Count; i++)
            {
                if (entities[i].kind != kind) continue;
                float d = Vec3.Distance(entities[i].position, point);
                if (d < bestD) { bestD = d; best = entities[i]; }
            }
            return best;
        }

        public Dictionary<string, object> ToJson()
        {
            var list = new List<object>();
            for (int i = 0; i < entities.Count; i++) list.Add(entities[i].ToJson());
            return new Dictionary<string, object>
            {
                { "floorY", Json.Round(floorY) },
                { "user", new Dictionary<string, object> { { "position", Json.FromVec3(userPosition) }, { "forward", Json.FromVec3(userForward) } } },
                { "entities", list },
            };
        }

        public static RoomModel FromJson(Dictionary<string, object> o)
        {
            var m = new RoomModel { floorY = o.Float("floorY") };
            var user = o.Obj("user");
            if (user != null)
            {
                m.userPosition = user.Vec("position", m.userPosition);
                m.userForward = user.Vec("forward", m.userForward);
            }
            var list = o.Arr("entities");
            if (list != null)
                foreach (object item in list)
                {
                    var d = item as Dictionary<string, object>;
                    if (d != null) m.entities.Add(RoomEntity.FromJson(d));
                }
            return m;
        }
    }

    /// <summary>Forgiving text comparison used to map spoken phrases onto entity names.</summary>
    public static class TextMatch
    {
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            bool space = false;
            foreach (char raw in s)
            {
                char c = char.ToLowerInvariant(raw);
                if (char.IsLetterOrDigit(c))
                {
                    if (space && sb.Length > 0) sb.Append(' ');
                    space = false;
                    sb.Append(c);
                }
                else space = true;
            }
            return sb.ToString();
        }

        /// <summary>True when <paramref name="word"/> (may be several words) appears on word boundaries in the normalized phrase.</summary>
        public static bool ContainsWord(string normalizedPhrase, string word)
        {
            string w = Normalize(word);
            if (w.Length == 0 || normalizedPhrase.Length == 0) return false;
            int idx = 0;
            while ((idx = normalizedPhrase.IndexOf(w, idx, StringComparison.Ordinal)) >= 0)
            {
                bool startOk = idx == 0 || normalizedPhrase[idx - 1] == ' ';
                int end = idx + w.Length;
                bool endOk = end == normalizedPhrase.Length || normalizedPhrase[end] == ' ' ||
                             // tolerate simple plurals: "valves" matches "valve"
                             (normalizedPhrase[end] == 's' && (end + 1 == normalizedPhrase.Length || normalizedPhrase[end + 1] == ' '));
                if (startOk && endOk) return true;
                idx = end;
            }
            return false;
        }

        public static bool ContainsAny(string normalizedPhrase, params string[] words)
        {
            for (int i = 0; i < words.Length; i++)
                if (ContainsWord(normalizedPhrase, words[i])) return true;
            return false;
        }

        /// <summary>
        /// 0 when the name is not mentioned. Otherwise higher is better: full-name matches beat single-word matches,
        /// and longer names beat shorter ones so "gate valve" wins over "valve".
        /// </summary>
        public static int Score(string normalizedPhrase, string name)
        {
            string n = Normalize(name);
            if (n.Length == 0) return 0;
            if (normalizedPhrase == n) return 1000 + n.Length;
            if (ContainsWord(normalizedPhrase, n)) return 500 + n.Length;
            // The phrase is a fragment of the name ("valve" for "gate valve").
            if (ContainsWord(n, normalizedPhrase)) return 200 + normalizedPhrase.Length;
            // Any significant word of the name appears in the phrase.
            int best = 0;
            foreach (string w in n.Split(' '))
                if (w.Length >= 4 && ContainsWord(normalizedPhrase, w)) best = Math.Max(best, 50 + w.Length);
            return best;
        }
    }
}
