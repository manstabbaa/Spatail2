using System;
using System.Collections.Generic;

namespace Spatail.Core
{
    /// <summary>
    /// Wire format for builds and plan results. The same JSON is produced by the offline planner, the AI planner
    /// service, the session link and the docs simulator, so this file is the contract between all of them.
    /// </summary>
    public static class SpecJson
    {
        // ---- writing ---------------------------------------------------------------------

        public static Dictionary<string, object> ToJson(Element e)
        {
            var o = new Dictionary<string, object> { { "id", e.id }, { "type", e.type } };
            var anchor = new Dictionary<string, object> { { "entity", e.anchor.entity }, { "at", e.anchor.at } };
            if (e.anchor.offset.SqrLength > 0) anchor["offset"] = Json.FromVec3(e.anchor.offset);
            o["anchor"] = anchor;
            if (e.step != 0) o["step"] = e.step;
            if (!string.IsNullOrEmpty(e.style) && e.style != ElementStyle.Accent) o["style"] = e.style;
            if (Math.Abs(e.scale - 1f) > 1e-4f) o["scale"] = Json.Round(e.scale);
            if (!string.IsNullOrEmpty(e.text)) o["text"] = e.text;
            if (!string.IsNullOrEmpty(e.title)) o["title"] = e.title;
            if (!string.IsNullOrEmpty(e.body)) o["body"] = e.body;
            if (e.number != 0) o["number"] = e.number;
            if (!string.IsNullOrEmpty(e.action)) o["action"] = e.action;
            if (e.points.Count > 0)
            {
                var pts = new List<object>();
                foreach (Vec3 p in e.points) pts.Add(Json.FromVec3(p));
                o["points"] = pts;
                o["radius"] = Json.Round(e.radius);
            }
            if (e.entities.Count > 0) o["entities"] = new List<object>(e.entities);
            if (e.values.Count > 0)
            {
                var vals = new List<object>();
                foreach (float v in e.values) vals.Add(Json.Round(v));
                o["values"] = vals;
            }
            if (e.labels.Count > 0) o["labels"] = new List<object>(e.labels);
            if (!string.IsNullOrEmpty(e.unit)) o["unit"] = e.unit;
            if (e.items.Count > 0)
            {
                var items = new List<object>();
                foreach (ChecklistItem i in e.items)
                    items.Add(new Dictionary<string, object> { { "text", i.text }, { "done", i.done } });
                o["items"] = items;
            }
            if (e.people.Count > 0) o["people"] = new List<object>(e.people);
            if (e.more != 0) o["more"] = e.more;
            if (!string.IsNullOrEmpty(e.name)) o["name"] = e.name;
            return o;
        }

        public static Dictionary<string, object> ToJson(Build b)
        {
            var elements = new List<object>();
            foreach (Element e in b.elements) elements.Add(ToJson(e));
            var o = new Dictionary<string, object> { { "id", b.id }, { "kind", b.kind }, { "title", b.title } };
            if (b.stepCount > 0)
            {
                o["stepCount"] = b.stepCount;
                o["currentStep"] = b.currentStep;
            }
            o["elements"] = elements;
            return o;
        }

        public static Dictionary<string, object> ToJson(PlanOp op)
        {
            var o = new Dictionary<string, object> { { "op", op.op } };
            if (op.op == OpType.Add && op.build != null) o["build"] = ToJson(op.build);
            if (op.op == OpType.Remove || op.op == OpType.Step) o["buildId"] = op.buildId;
            if (op.op == OpType.Step) o["step"] = op.step;
            return o;
        }

        public static Dictionary<string, object> ToJson(PlanResult r)
        {
            var ops = new List<object>();
            foreach (PlanOp op in r.ops) ops.Add(ToJson(op));
            var o = new Dictionary<string, object> { { "reply", r.reply ?? "" }, { "chip", r.chip ?? "" }, { "ops", ops } };
            if (!string.IsNullOrEmpty(r.planner)) o["planner"] = r.planner;
            if (r.latencyMs > 0) o["latencyMs"] = Math.Round(r.latencyMs);
            if (!string.IsNullOrEmpty(r.error)) o["error"] = r.error;
            return o;
        }

        public static string Write(PlanResult r, bool pretty = false) => Json.Write(ToJson(r), pretty);
        public static string Write(Build b, bool pretty = false) => Json.Write(ToJson(b), pretty);

        // ---- reading ---------------------------------------------------------------------

        public static Element ElementFromJson(Dictionary<string, object> o)
        {
            var e = new Element
            {
                id = o.Str("id"),
                type = o.Str("type", ElementType.Label),
                step = o.Int("step"),
                style = o.Str("style", ElementStyle.Accent),
                scale = o.Float("scale", 1f),
                text = o.Str("text"),
                title = o.Str("title"),
                body = o.Str("body"),
                number = o.Int("number"),
                action = o.Str("action"),
                radius = o.Float("radius", 0.07f),
                unit = o.Str("unit"),
                more = o.Int("more"),
                name = o.Str("name"),
                entities = o.StrList("entities"),
                labels = o.StrList("labels"),
                people = o.StrList("people"),
            };
            if (e.scale <= 0) e.scale = 1f;
            var anchor = o.Obj("anchor");
            if (anchor != null)
            {
                e.anchor.entity = anchor.Str("entity");
                e.anchor.at = anchor.Str("at", AnchorMode.Center);
                e.anchor.offset = anchor.Vec("offset");
            }
            var points = o.Arr("points");
            if (points != null) foreach (object p in points) e.points.Add(Json.ToVec3(p));
            var values = o.Arr("values");
            if (values != null) foreach (object v in values) e.values.Add((float)Json.ToDouble(v));
            var items = o.Arr("items");
            if (items != null)
                foreach (object item in items)
                {
                    var d = item as Dictionary<string, object>;
                    if (d != null) e.items.Add(new ChecklistItem { text = d.Str("text", ""), done = d.Bool("done") });
                    else if (item is string) e.items.Add(new ChecklistItem { text = (string)item });
                }
            return e;
        }

        public static Build BuildFromJson(Dictionary<string, object> o)
        {
            var b = new Build
            {
                id = o.Str("id"),
                kind = o.Str("kind", BuildKind.Custom),
                title = o.Str("title"),
                stepCount = o.Int("stepCount"),
                currentStep = o.Int("currentStep"),
            };
            var elements = o.Arr("elements");
            if (elements != null)
                foreach (object item in elements)
                {
                    var d = item as Dictionary<string, object>;
                    if (d != null) b.elements.Add(ElementFromJson(d));
                }
            return b;
        }

        public static PlanResult PlanFromJson(Dictionary<string, object> o)
        {
            var r = new PlanResult
            {
                reply = o.Str("reply", ""),
                chip = o.Str("chip", ""),
                planner = o.Str("planner"),
                latencyMs = o.Num("latencyMs"),
                error = o.Str("error"),
            };
            var ops = o.Arr("ops");
            if (ops != null)
                foreach (object item in ops)
                {
                    var d = item as Dictionary<string, object>;
                    if (d == null) continue;
                    var op = new PlanOp { op = d.Str("op", OpType.Add), buildId = d.Str("buildId"), step = d.Int("step") };
                    var build = d.Obj("build");
                    if (build != null)
                    {
                        op.build = BuildFromJson(build);
                        if (string.IsNullOrEmpty(op.buildId)) op.buildId = op.build.id;
                    }
                    r.ops.Add(op);
                }
            return r;
        }

        public static PlanResult ParsePlan(string json) => PlanFromJson(Json.ParseObject(json));

        // ---- validation ------------------------------------------------------------------

        /// <summary>
        /// Makes a plan safe to render regardless of where it came from: drops elements with unknown types or anchors
        /// that are not in the room, fills in missing ids, and clamps values. Returns human-readable notes about
        /// anything that was changed, which the dev tool surfaces.
        /// </summary>
        public static List<string> Sanitize(PlanResult plan, RoomModel room)
        {
            var notes = new List<string>();
            for (int i = plan.ops.Count - 1; i >= 0; i--)
            {
                PlanOp op = plan.ops[i];
                if (op.op == OpType.Clear) continue;
                if (op.op == OpType.Remove || op.op == OpType.Step)
                {
                    if (string.IsNullOrEmpty(op.buildId)) { notes.Add("Dropped '" + op.op + "' op without a buildId."); plan.ops.RemoveAt(i); }
                    continue;
                }
                if (op.op != OpType.Add || op.build == null)
                {
                    notes.Add("Dropped unknown op '" + op.op + "'.");
                    plan.ops.RemoveAt(i);
                    continue;
                }

                Build b = op.build;
                if (string.IsNullOrEmpty(b.id)) b.id = string.IsNullOrEmpty(b.kind) ? "build" : b.kind;
                if (string.IsNullOrEmpty(b.title)) b.title = "Content";
                op.buildId = b.id;

                var seen = new HashSet<string>();
                int maxStep = 0;
                for (int e = b.elements.Count - 1; e >= 0; e--)
                {
                    Element el = b.elements[e];
                    if (!ElementType.IsKnown(el.type))
                    {
                        notes.Add("Dropped element of unknown type '" + el.type + "'.");
                        b.elements.RemoveAt(e);
                        continue;
                    }
                    bool needsEntity = el.anchor.at != AnchorMode.User;
                    if (needsEntity && room != null && room.Get(el.anchor.entity) == null)
                    {
                        RoomEntity guess = room.Resolve(el.anchor.entity);
                        if (guess != null) el.anchor.entity = guess.id;
                        else
                        {
                            notes.Add("Dropped " + el.type + " anchored to unknown entity '" + el.anchor.entity + "'.");
                            b.elements.RemoveAt(e);
                            continue;
                        }
                    }
                    if (Array.IndexOf(AnchorMode.All, el.anchor.at) < 0) el.anchor.at = AnchorMode.Center;
                    if (el.scale < 0.25f) el.scale = 0.25f;
                    if (el.scale > 4f) el.scale = 4f;
                    if (el.step < 0) el.step = 0;
                    if (el.step > maxStep) maxStep = el.step;
                }
                // Ids are assigned front to back so they are stable for a given plan.
                for (int e = 0; e < b.elements.Count; e++)
                {
                    Element el = b.elements[e];
                    if (string.IsNullOrEmpty(el.id) || seen.Contains(el.id)) el.id = el.type + "-" + (e + 1);
                    seen.Add(el.id);
                }
                if (maxStep > b.stepCount) b.stepCount = maxStep;
                if (b.stepCount > 0)
                {
                    if (b.currentStep < 1) b.currentStep = 1;
                    if (b.currentStep > b.stepCount) b.currentStep = b.stepCount;
                }
                if (b.elements.Count == 0)
                {
                    notes.Add("Dropped build '" + b.id + "' because none of its elements could be placed.");
                    plan.ops.RemoveAt(i);
                }
            }
            return notes;
        }
    }
}
