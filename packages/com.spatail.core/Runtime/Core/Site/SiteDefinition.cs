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

    public sealed class ProcedureStep
    {
        public string entity;
        public string title;
        public string body;
        /// <summary>turn-cw | turn-ccw | press | inspect | none</summary>
        public string action = "none";
    }

    public sealed class Procedure
    {
        public string id;
        public string title;
        public List<string> aliases = new List<string>();
        public List<ProcedureStep> steps = new List<ProcedureStep>();
    }

    public sealed class FlowDef
    {
        public string id;
        public string title;
        public string medium;
        public string rate;
        public List<string> aliases = new List<string>();
        /// <summary>Centre-line of the flow in the station frame, upstream to downstream.</summary>
        public List<Vec3> path = new List<Vec3>();
        public float radius = 0.07f;
        /// <summary>Entity ids whose volumes light up while the flow is shown.</summary>
        public List<string> highlight = new List<string>();
        public string inletEntity;
        public string outletEntity;
    }

    public sealed class FaultRecord
    {
        public string entity;
        public string title;
        public string when;
        public string severity = "medium";
        public string detail;
    }

    public sealed class MetricDef
    {
        public string id;
        public string title;
        public string unit;
        public List<string> aliases = new List<string>();
        public List<float> values = new List<float>();
        public List<string> labels = new List<string>();
    }

    public sealed class SpareItem
    {
        public string text;
        public bool done;
    }

    public sealed class Person
    {
        public string name;
        public string initial;
        public string role;
    }

    /// <summary>
    /// What the organisation knows about this equipment. In production these records come from manuals, the CMMS
    /// and the historian; the planner turns them into spatial content on request.
    /// </summary>
    public sealed class Knowledge
    {
        public List<Procedure> procedures = new List<Procedure>();
        public List<FlowDef> flows = new List<FlowDef>();
        public List<FaultRecord> faults = new List<FaultRecord>();
        public string faultsTitle = "Recent faults";
        public List<MetricDef> metrics = new List<MetricDef>();
        public string sparesTitle = "Parts check";
        public List<SpareItem> spares = new List<SpareItem>();
        /// <summary>Initials of the people already on the job, and how many more are not shown.</summary>
        public List<string> sparesPeople = new List<string>();
        public int sparesMore;
        public List<Person> people = new List<Person>();
        /// <summary>The original JSON, forwarded verbatim to the AI planner as context.</summary>
        public Dictionary<string, object> raw = new Dictionary<string, object>();

        public Person FindPerson(string normalizedPhrase)
        {
            foreach (Person p in people)
                if (TextMatch.ContainsWord(normalizedPhrase, p.name)) return p;
            return null;
        }

        public static Knowledge FromJson(Dictionary<string, object> o)
        {
            var k = new Knowledge();
            if (o == null) return k;
            k.raw = o;
            foreach (var d in Objects(o.Arr("procedures")))
            {
                var proc = new Procedure { id = d.Str("id"), title = d.Str("title"), aliases = d.StrList("aliases") };
                foreach (var s in Objects(d.Arr("steps")))
                    proc.steps.Add(new ProcedureStep { entity = s.Str("entity"), title = s.Str("title"), body = s.Str("body"), action = s.Str("action", "none") });
                k.procedures.Add(proc);
            }
            foreach (var d in Objects(o.Arr("flows")))
            {
                var f = new FlowDef
                {
                    id = d.Str("id"), title = d.Str("title"), medium = d.Str("medium"), rate = d.Str("rate"),
                    aliases = d.StrList("aliases"), radius = d.Float("radius", 0.07f), highlight = d.StrList("highlight"),
                    inletEntity = d.Str("inlet"), outletEntity = d.Str("outlet"),
                };
                var path = d.Arr("path");
                if (path != null) foreach (object pt in path) f.path.Add(Json.ToVec3(pt));
                k.flows.Add(f);
            }
            var faults = o.Obj("faults");
            if (faults != null)
            {
                k.faultsTitle = faults.Str("title", k.faultsTitle);
                foreach (var d in Objects(faults.Arr("records")))
                    k.faults.Add(new FaultRecord { entity = d.Str("entity"), title = d.Str("title"), when = d.Str("when"), severity = d.Str("severity", "medium"), detail = d.Str("detail") });
            }
            foreach (var d in Objects(o.Arr("metrics")))
            {
                var m = new MetricDef { id = d.Str("id"), title = d.Str("title"), unit = d.Str("unit"), aliases = d.StrList("aliases"), labels = d.StrList("labels") };
                var values = d.Arr("values");
                if (values != null) foreach (object v in values) m.values.Add((float)Json.ToDouble(v));
                k.metrics.Add(m);
            }
            var spares = o.Obj("spares");
            if (spares != null)
            {
                k.sparesTitle = spares.Str("title", k.sparesTitle);
                k.sparesPeople = spares.StrList("people");
                k.sparesMore = spares.Int("more");
                foreach (var d in Objects(spares.Arr("items")))
                    k.spares.Add(new SpareItem { text = d.Str("text"), done = d.Bool("done") });
            }
            foreach (var d in Objects(o.Arr("people")))
            {
                string name = d.Str("name", "");
                k.people.Add(new Person { name = name, initial = d.Str("initial", name.Length > 0 ? name.Substring(0, 1) : "?"), role = d.Str("role") });
            }
            return k;
        }

        static IEnumerable<Dictionary<string, object>> Objects(List<object> list)
        {
            if (list == null) yield break;
            foreach (object item in list)
            {
                var d = item as Dictionary<string, object>;
                if (d != null) yield return d;
            }
        }
    }

    /// <summary>
    /// A registered site: one piece of equipment (the digital twin), its supporting fixtures, and what is known about it.
    /// Loaded from JSON so new equipment can be added without touching code.
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
        public Knowledge knowledge = new Knowledge();

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
            site.knowledge = Knowledge.FromJson(o.Obj("knowledge"));
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
