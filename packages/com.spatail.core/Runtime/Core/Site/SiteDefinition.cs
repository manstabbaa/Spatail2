using System.Collections.Generic;

namespace Spatail.Core
{
    /// <summary>One primitive of a stand-in hologram. Authored in the owning part's shape frame.</summary>
    public sealed class ShapeDef
    {
        /// <summary>box | cyl | torus | sphere</summary>
        public string type = "box";
        /// <summary>Centre position.</summary>
        public Vec3 p;
        /// <summary>Euler rotation in degrees (Unity order).</summary>
        public Vec3 r;
        /// <summary>
        /// box: full size. cyl: (radius, length along local Y, top radius or 0 for same). torus: (ring radius, tube radius, 0),
        /// ring lies in the local XZ plane. sphere: (radius, 0, 0).
        /// </summary>
        public Vec3 s;
        /// <summary>body | shade | dark | glass. Purely a rendering hint.</summary>
        public string tone = "body";

        public static ShapeDef FromJson(Dictionary<string, object> o) => new ShapeDef
        {
            type = o.Str("t", "box"),
            p = o.Vec("p"),
            r = o.Vec("r"),
            s = o.Vec("s", Vec3.One),
            tone = o.Str("tone", "body"),
        };

        public Dictionary<string, object> ToJson()
        {
            var o = new Dictionary<string, object> { { "t", type }, { "p", Json.FromVec3(p) }, { "s", Json.FromVec3(s) } };
            if (r.SqrLength > 0) o["r"] = Json.FromVec3(r);
            if (tone != "body") o["tone"] = tone;
            return o;
        }
    }

    /// <summary>A named component of the equipment, or a fixture that belongs to the site.</summary>
    public sealed class PartDef
    {
        public string id;
        public string label;
        public List<string> aliases = new List<string>();
        public string about;
        /// <summary>Centre of the part's bounds in its shape frame.</summary>
        public Vec3 center;
        public Vec3 size;
        public Vec3 actionAxis;
        /// <summary>Where on the part the action happens (a handwheel, a button), in the shape frame. Optional.</summary>
        public Vec3 actionPoint;
        public bool hasActionPoint;
        /// <summary>Whether the room scan announces this part with a label.</summary>
        public bool scanLabel;
        public List<ShapeDef> shapes = new List<ShapeDef>();

        // Fixture-only fields -----------------------------------------------------------------
        /// <summary>"" (rigidly part of the equipment), "wall" (snap to the nearest wall) or "table" (bind to a real table).</summary>
        public string mount = "";
        /// <summary>Where the fixture's shape frame sits relative to the station origin when free-standing.</summary>
        public Vec3 offset;
        public float yaw;

        public static PartDef FromJson(Dictionary<string, object> o)
        {
            var p = new PartDef
            {
                id = o.Str("id"),
                label = o.Str("label"),
                about = o.Str("about"),
                center = o.Vec("center"),
                size = o.Vec("size"),
                actionAxis = o.Vec("actionAxis"),
                actionPoint = o.Vec("actionPoint"),
                hasActionPoint = o.Has("actionPoint"),
                scanLabel = o.Bool("scanLabel"),
                mount = o.Str("mount", ""),
                offset = o.Vec("offset"),
                yaw = o.Float("yaw"),
                aliases = o.StrList("aliases"),
            };
            var shapes = o.Arr("shapes");
            if (shapes != null)
                foreach (object s in shapes)
                {
                    var d = s as Dictionary<string, object>;
                    if (d != null) p.shapes.Add(ShapeDef.FromJson(d));
                }
            return p;
        }
    }

    /// <summary>
    /// A stand-in scene: a machine and its fixtures as simple holograms with named parts. It exists so Spatail can
    /// be shown and tested where there is no real equipment. In real use nothing is defined ahead of time; the
    /// planner works from what the device senses.
    /// </summary>
    public sealed class SiteDefinition
    {
        public string id;
        public string name;
        public string equipmentId = "equipment";
        public string equipmentLabel;
        public string equipmentAbout;
        public List<string> equipmentAliases = new List<string>();
        public Vec3 equipmentCenter;
        public Vec3 equipmentSize;
        public List<PartDef> parts = new List<PartDef>();
        public List<PartDef> fixtures = new List<PartDef>();

        public static SiteDefinition Parse(string json)
        {
            var o = Json.ParseObject(json);
            var site = new SiteDefinition { id = o.Str("id"), name = o.Str("name") };
            var eq = o.Obj("equipment");
            if (eq != null)
            {
                site.equipmentId = eq.Str("id", "equipment");
                site.equipmentLabel = eq.Str("label", site.name);
                site.equipmentAbout = eq.Str("about");
                site.equipmentAliases = eq.StrList("aliases");
                site.equipmentCenter = eq.Vec("center");
                site.equipmentSize = eq.Vec("size");
                var parts = eq.Arr("parts");
                if (parts != null)
                    foreach (object p in parts)
                    {
                        var d = p as Dictionary<string, object>;
                        if (d != null) site.parts.Add(PartDef.FromJson(d));
                    }
            }
            var fixtures = o.Arr("fixtures");
            if (fixtures != null)
                foreach (object f in fixtures)
                {
                    var d = f as Dictionary<string, object>;
                    if (d != null) site.fixtures.Add(PartDef.FromJson(d));
                }
            return site;
        }

        public PartDef FindPart(string id)
        {
            foreach (PartDef p in parts) if (p.id == id) return p;
            foreach (PartDef p in fixtures) if (p.id == id) return p;
            return null;
        }
    }
}
