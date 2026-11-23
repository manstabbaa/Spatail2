using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spatail.Core;

namespace Spatail.Core.Tests
{
    /// <summary>
    /// Self-contained test runner for the engine-independent core.
    ///   dotnet run                      run all tests
    ///   dotnet run -- export out-dir    write the simulated room and the plans for the demo prompts as JSON
    /// </summary>
    static class Program
    {
        static int _passed, _failed;
        static readonly List<string> Failures = new List<string>();

        static string SitePath()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string candidate = Path.Combine(dir, "packages", "com.spatail.core", "Runtime", "Resources", "Spatail", "pump_station.json");
                if (File.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }
            throw new FileNotFoundException("pump_station.json not found above " + AppContext.BaseDirectory);
        }

        static SiteDefinition LoadSite() => SiteDefinition.Parse(File.ReadAllText(SitePath()));

        public static readonly string[] DemoPrompts =
        {
            "Build me a walkthrough for this pump",
            "Show me how the water flows",
            "Has it failed before?",
            "Put this month's output by the bench",
            "What parts do I need?",
            "Bring Sara in",
        };

        static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "export") return Export(args[1]);

            Run("Vec3 and Quat follow Unity conventions", MathConventions);
            Run("Quat.LookRotation and FromTo", LookRotation);
            Run("Json round-trips values, escapes and vectors", JsonRoundTrip);
            Run("Json rejects malformed input", JsonRejects);
            Run("Site definition loads with parts, fixtures and knowledge", SiteLoads);
            Run("Binder places parts in the station frame", BinderParts);
            Run("Binder snaps the control box to the wall", BinderWall);
            Run("Binder uses a stand-in bench when the room has no table", BinderVirtualBench);
            Run("Binder adopts a real table as the workbench", BinderRealTable);
            Run("Room.Resolve maps phrases to entities", ResolvePhrases);
            Run("Offline planner: walkthrough", PlanWalkthrough);
            Run("Offline planner: flow", PlanFlow);
            Run("Offline planner: fault history", PlanFaults);
            Run("Offline planner: dashboard by the bench", PlanDashboard);
            Run("Offline planner: parts checklist", PlanChecklist);
            Run("Offline planner: bring Sara in", PlanSession);
            Run("Offline planner: step navigation", PlanSteps);
            Run("Offline planner: 'has it failed before' is not navigation", PlanNoFalseNavigation);
            Run("Offline planner: move, resize, remove, clear", PlanEdits);
            Run("Offline planner: describe a part, and unknown requests", PlanDescribeAndUnknown);
            Run("Plans survive a JSON round trip", PlanRoundTrip);
            Run("Sanitize repairs and drops bad content", SanitizeRepairs);
            Run("A plan in the AI service's shape parses, validates and lays out", ServicePlanShape);
            Run("Layout: every demo element gets a sane pose", LayoutSane);
            Run("Layout: chart lands on free floor by the bench", LayoutChart);
            Run("Layout: flow path follows the pipework", LayoutFlow);
            Run("Layout: wall anchor sits on the wall facing the room", LayoutWall);
            Run("BuildStore applies ops and raises events", StoreOps);
            Run("Scan labels retire when real content arrives", LabelsRetire);
            Run("Layout: turn arrow centres on the handwheel", LayoutGizmo);
            Run("Offline planner: 'what is this' uses the pointed-at entity", PlanFocus);
            Run("Remote planner: request shape, success and failure", RemotePlannerPaths);
            Run("Planner router falls back when the service fails", RouterFallback);
            Run("Session link: plans, prompts and presence over TCP", SessionRoundTrip);

            Console.WriteLine();
            Console.WriteLine(_passed + " passed, " + _failed + " failed");
            foreach (string f in Failures) Console.WriteLine("  FAILED: " + f);
            return _failed == 0 ? 0 : 1;
        }

        // ---- harness -----------------------------------------------------------------------

        static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("  ok   " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                Failures.Add(name + " -> " + ex.Message);
                Console.WriteLine("  FAIL " + name + "\n       " + ex.Message);
            }
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        static void Near(float actual, float expected, string message, float tolerance = 1e-3f)
        {
            if (Math.Abs(actual - expected) > tolerance) throw new Exception(message + " (expected " + expected + ", got " + actual + ")");
        }

        static void Near(Vec3 actual, Vec3 expected, string message, float tolerance = 1e-3f)
        {
            if (Vec3.Distance(actual, expected) > tolerance) throw new Exception(message + " (expected " + expected + ", got " + actual + ")");
        }

        // ---- fixtures ----------------------------------------------------------------------

        sealed class World
        {
            public SiteDefinition site;
            public RoomModel room;
            public Pose3 station;
            public BuildStore store = new BuildStore();
            public OfflinePlanner planner = new OfflinePlanner();

            public PlanResult Ask(string prompt)
            {
                PlanResult plan = planner.Plan(new PlanRequest { prompt = prompt, room = room, site = site, builds = store.Builds });
                store.RetireScanLabels(plan);
                store.Apply(plan);
                return plan;
            }
        }

        static World MakeWorld(bool realTable = false)
        {
            var w = new World { site = LoadSite() };
            RoomModel raw = SimRoom.Create(realTable);
            w.station = SiteBinder.SuggestStationPose(raw, w.site);
            w.room = SiteBinder.Bind(w.site, raw, w.station);
            return w;
        }

        // ---- math --------------------------------------------------------------------------

        static void MathConventions()
        {
            Near(Quat.Yaw(90) * Vec3.Forward, Vec3.Right, "yaw 90 turns forward to right");
            Near(Quat.AngleAxis(90, Vec3.Right) * Vec3.Forward, new Vec3(0, -1, 0), "pitch 90 looks down");
            Near(Quat.Euler(0, 0, 90) * Vec3.Up, new Vec3(-1, 0, 0), "roll 90 turns up to -X");
            Near(Quat.Euler(90, 0, 0) * Vec3.Up, Vec3.Forward, "pitch 90 turns up to forward");
            Quat q = Quat.Euler(20, 50, 70);
            Near(q.Inverse * (q * new Vec3(1, 2, 3)), new Vec3(1, 2, 3), "inverse undoes rotation");
            Near((Quat.Yaw(30) * Quat.Yaw(60)) * Vec3.Forward, Vec3.Right, "rotations compose");
            Near(Vec3.Cross(Vec3.Right, Vec3.Up), Vec3.Forward, "left-handed cross product");
            var pose = new Pose3(new Vec3(1, 0, 2), Quat.Yaw(90));
            Near(pose.TransformPoint(new Vec3(0, 0, 1)), new Vec3(2, 0, 2), "pose transforms points");
            Near(pose.InverseTransformPoint(new Vec3(2, 0, 2)), new Vec3(0, 0, 1), "pose inverse-transforms points");
            Near(Quat.Yaw(40).YawDegrees, 40f, "yaw round trip", 1e-2f);
        }

        static void LookRotation()
        {
            Quat q = Quat.LookRotation(new Vec3(1, 0, 0));
            Near(q * Vec3.Forward, Vec3.Right, "look rotation forward");
            Near(q * Vec3.Up, Vec3.Up, "look rotation keeps up");
            Quat down = Quat.LookRotation(new Vec3(0, -1, 0.001f));
            Near((down * Vec3.Forward).y, -1f, "look straight down", 1e-2f);
            Near(Quat.FromTo(Vec3.Up, Vec3.Right) * Vec3.Up, Vec3.Right, "from-to");
            Near(Quat.FromTo(Vec3.Up, -Vec3.Up) * Vec3.Up, -Vec3.Up, "from-to opposite");
            Near(Quat.FaceFlat(Vec3.Zero, new Vec3(0, 5, 3)) * Vec3.Forward, Vec3.Forward, "face flat ignores height");
        }

        // ---- json --------------------------------------------------------------------------

        static void JsonRoundTrip()
        {
            string src = "{\"a\":1,\"b\":[1.5,-2,3e2],\"c\":\"q\\\"uote\\n\\u00e9\",\"d\":true,\"e\":null,\"f\":{\"x\":0.25}}";
            var o = Json.ParseObject(src);
            Check(o.Int("a") == 1, "int");
            Check(o.Arr("b").Count == 3 && Json.ToDouble(o.Arr("b")[2]) == 300, "array + exponent");
            Check(o.Str("c") == "q\"uote\né", "escapes");
            Check(o.Bool("d") && !o.Has("e"), "bool/null");
            Near(o.Obj("f").Float("x"), 0.25f, "nested");
            var again = Json.ParseObject(Json.Write(o));
            Check(again.Str("c") == o.Str("c") && again.Arr("b").Count == 3, "write/parse round trip");
            Near(Json.ToVec3(Json.Parse("[1,2,3]")), new Vec3(1, 2, 3), "vec from array");
            Near(Json.ToVec3(Json.Parse("{\"x\":1,\"y\":2,\"z\":3}")), new Vec3(1, 2, 3), "vec from object");
            Check(Json.Write(new Dictionary<string, object> { { "n", 2.5f }, { "i", 3 } }) == "{\"n\":2.5,\"i\":3}", "number formatting");
            Check(Json.Write(Json.Parse(Json.Write(o, true))) == Json.Write(o), "pretty output parses to the same value");
        }

        static void JsonRejects()
        {
            foreach (string bad in new[] { "", "{", "{\"a\":}", "[1,2", "{\"a\":1} x", "nul", "{'a':1}" })
            {
                object v;
                Check(!Json.TryParse(bad, out v), "should reject: " + bad);
            }
        }

        // ---- site + binder -----------------------------------------------------------------

        static void SiteLoads()
        {
            SiteDefinition site = LoadSite();
            Check(site.equipmentId == "pump-p101", "equipment id");
            Check(site.parts.Count == 7, "seven parts, got " + site.parts.Count);
            Check(site.fixtures.Count == 2, "two fixtures");
            Check(site.FindPart("gate-valve").actionAxis.y == 1, "valve axis");
            Check(site.parts.All(p => p.shapes.Count > 0 && p.size.x > 0), "all parts have geometry and bounds");
            Check(site.knowledge.procedures.Count == 2 && site.knowledge.procedures[0].steps.Count == 3, "procedures");
            Check(site.knowledge.flows[0].path.Count == 3, "flow path");
            Check(site.knowledge.faults.Count == 2 && site.knowledge.metrics[0].values.Count == 10, "faults + metrics");
            Check(site.knowledge.spares.Count == 3 && site.knowledge.sparesMore == 3, "spares");
            Check(site.knowledge.people.Any(p => p.name == "Sara"), "people");
            // Every knowledge record points at something that exists.
            var ids = new HashSet<string>(site.parts.Select(p => p.id).Concat(site.fixtures.Select(p => p.id)));
            foreach (var proc in site.knowledge.procedures)
                foreach (var step in proc.steps) Check(ids.Contains(step.entity), "procedure step entity " + step.entity);
            foreach (var f in site.knowledge.faults) Check(ids.Contains(f.entity), "fault entity " + f.entity);
            foreach (var f in site.knowledge.flows)
                foreach (string h in f.highlight) Check(ids.Contains(h), "flow highlight " + h);
        }

        static void BinderParts()
        {
            SiteDefinition site = LoadSite();
            var station = new Pose3(new Vec3(2, 0, 1), Quat.Yaw(90));
            RoomModel room = SiteBinder.Bind(site, SimRoom.Create(), station);
            RoomEntity valve = room.Get("gate-valve");
            Check(valve != null && valve.kind == EntityKind.Part && valve.parentId == "pump-p101", "valve bound as part");
            // Station +X maps to world -Z under a 90 degree yaw.
            Near(valve.position, new Vec3(2, 0.64f, 1 - 0.64f), "valve position follows the station pose");
            Near(valve.ShapeOrigin, station.position, "shape origin is the station origin");
            Near(valve.rotation * valve.actionAxis, Vec3.Up, "action axis stays vertical");
        }

        static void BinderWall()
        {
            World w = MakeWorld();
            RoomEntity box = w.room.Get("control-box");
            Check(box != null && box.kind == EntityKind.Fixture, "control box bound");
            Near(box.ShapeOrigin.z, SimRoom.BackWallZ, "control box sits on the wall plane");
            Near(box.Forward, new Vec3(0, 0, -1), "control box faces into the room");
            Near(box.position.y, 1.2f, "control box height");
            // No wall within reach: stands where the site puts it.
            var open = new RoomModel();
            open.entities.Add(new RoomEntity { id = "floor", kind = EntityKind.Floor, size = new Vec3(10, 0, 10) });
            RoomModel bound = SiteBinder.Bind(w.site, open, Pose3.Identity);
            Near(bound.Get("control-box").ShapeOrigin, new Vec3(-0.95f, 1.2f, 0.92f), "free-standing fallback");
            Near(bound.Get("control-box").Forward, new Vec3(0, 0, -1), "free-standing faces the front of the station");
        }

        static void BinderVirtualBench()
        {
            World w = MakeWorld();
            RoomEntity bench = w.room.Get("workbench");
            Check(bench != null && bench.isVirtual && bench.shapes != null && bench.kind == EntityKind.Table, "virtual bench");
            Near(bench.position.y, 0.47f, "bench centre height");
            Check(bench.position.x < w.room.Get("motor").position.x - 0.8f, "bench is to the left of the motor");
        }

        static void BinderRealTable()
        {
            World w = MakeWorld(realTable: true);
            RoomEntity bench = w.room.Get("workbench");
            Check(bench != null && !bench.isVirtual && bench.shapes == null, "bench bound to the real table");
            Near(bench.size, new Vec3(1.4f, 0.75f, 0.7f), "takes the real table's size");
            Check(w.room.Get("table-1") == null, "the raw table is absorbed, not duplicated");
            Check(w.room.OfKind(EntityKind.Table).Count == 1, "exactly one table");
        }

        static void ResolvePhrases()
        {
            World w = MakeWorld();
            Check(w.room.Resolve("the gate valve").id == "gate-valve", "gate valve");
            Check(w.room.Resolve("valve").id == "gate-valve", "valve");
            Check(w.room.Resolve("bench").id == "workbench", "bench");
            Check(w.room.Resolve("pump").id == "pump", "pump means the part");
            Check(w.room.Resolve("motor").id == "motor", "motor");
            Check(w.room.Resolve("control panel").id == "control-box", "alias");
            Check(w.room.Resolve("pump-p101").id == "pump-p101", "id");
            Check(w.room.Resolve("spaceship") == null, "unknown");
        }

        // ---- planner -----------------------------------------------------------------------

        static void PlanWalkthrough()
        {
            World w = MakeWorld();
            PlanResult plan = w.Ask(DemoPrompts[0]);
            Check(plan.chip == "Walkthrough", "chip is " + plan.chip);
            Build b = w.store.Get("walkthrough");
            Check(b != null && b.stepCount == 3 && b.currentStep == 1, "three steps, starting at one");
            Check(b.elements.Count(e => e.type == ElementType.Pin) == 3, "three pins");
            Element card2 = b.elements.First(e => e.type == ElementType.Card && e.step == 2);
            Check(card2.title == "Close this valve" && card2.anchor.entity == "gate-valve", "step 2 card");
            Element gizmo = b.elements.First(e => e.type == ElementType.Gizmo);
            Check(gizmo.step == 2 && gizmo.action == "turn-cw" && gizmo.anchor.entity == "gate-valve", "turn arrow on the valve");
            Check(b.elements.Where(e => e.type == ElementType.Pin).All(e => e.step == 0), "pins are always visible");
            Check(b.IsVisible(b.elements.First(e => e.id == "card-1")) && !b.IsVisible(card2), "only the current step's card shows");
        }

        static void PlanFlow()
        {
            World w = MakeWorld();
            PlanResult plan = w.Ask(DemoPrompts[1]);
            Check(plan.chip == "Live flow", "chip");
            Build b = w.store.Get("flow");
            Element flow = b.elements.First(e => e.type == ElementType.Flow);
            Check(flow.points.Count == 3 && flow.entities.SequenceEqual(new[] { "inlet", "outlet" }), "path + highlights");
            Check(b.elements.Any(e => e.type == ElementType.Label && e.text == "Inlet"), "inlet label");
            Check(b.elements.Any(e => e.type == ElementType.Label && e.text == "Outlet"), "outlet label");
        }

        static void PlanFaults()
        {
            World w = MakeWorld();
            PlanResult plan = w.Ask(DemoPrompts[2]);
            Check(plan.chip == "Fault history", "chip");
            Build b = w.store.Get("faults");
            var highlights = b.elements.Where(e => e.type == ElementType.Highlight).ToList();
            Check(highlights.Count == 2 && highlights.All(h => h.style == ElementStyle.Warning), "two warning highlights");
            Check(highlights.Select(h => h.anchor.entity).SequenceEqual(new[] { "motor", "coupling" }), "on the motor and coupling");
            Check(b.elements.Any(e => e.type == ElementType.Label && e.text == "Last week's faults"), "title label");
            Check(plan.reply.Contains("2 faults"), "reply summarises: " + plan.reply);
        }

        static void PlanDashboard()
        {
            World w = MakeWorld();
            PlanResult plan = w.Ask(DemoPrompts[3]);
            Check(plan.chip == "Dashboard", "chip");
            Element chart = w.store.Get("dashboard").elements.Single();
            Check(chart.type == ElementType.Chart && chart.title == "Output this month" && chart.values.Count == 10, "chart content");
            Check(chart.anchor.entity == "workbench" && chart.anchor.at == AnchorMode.FloorNear, "by the bench, on the floor");
            // A different metric and placement.
            w.Ask("show energy consumption on the bench");
            chart = w.store.Get("dashboard").elements.Single();
            Check(chart.title == "Energy this month" && chart.anchor.at == AnchorMode.Surface, "metric and surface placement");
        }

        static void PlanChecklist()
        {
            World w = MakeWorld();
            PlanResult plan = w.Ask(DemoPrompts[4]);
            Check(plan.chip == "Checklist", "chip");
            Element list = w.store.Get("checklist").elements.Single();
            Check(list.title == "Parts check" && list.items.Count == 3, "items");
            Check(list.items[0].done && list.items[1].done && !list.items[2].done, "tick state");
            Check(list.people.SequenceEqual(new[] { "M", "J" }) && list.more == 3, "people");
            Check(list.anchor.entity == "workbench" && list.anchor.at == AnchorMode.Above, "above the bench");
        }

        static void PlanSession()
        {
            World w = MakeWorld();
            PlanResult plan = w.Ask(DemoPrompts[5]);
            Check(plan.chip == "Shared session", "chip");
            Element avatar = w.store.Get("session").elements.Single();
            Check(avatar.type == ElementType.Avatar && avatar.name == "Sara" && avatar.text == "S", "Sara");
        }

        static void PlanSteps()
        {
            World w = MakeWorld();
            w.Ask(DemoPrompts[0]);
            PlanResult next = w.Ask("next");
            Check(next.ops.Single().op == OpType.Step && w.store.Get("walkthrough").currentStep == 2, "next -> step 2");
            Check(next.reply.Contains("Close this valve"), "reply names the step: " + next.reply);
            w.Ask("next step");
            Check(w.store.Get("walkthrough").currentStep == 3, "next step -> 3");
            PlanResult end = w.Ask("next");
            Check(end.ops.Count == 0 && end.reply.Contains("last step"), "stops at the end");
            w.Ask("go back");
            Check(w.store.Get("walkthrough").currentStep == 2, "back -> 2");
            w.Ask("go to step one");
            Check(w.store.Get("walkthrough").currentStep == 1, "step one");
            w.Ask("step 3");
            Check(w.store.Get("walkthrough").currentStep == 3, "step 3");
        }

        static void PlanNoFalseNavigation()
        {
            World w = MakeWorld();
            w.Ask(DemoPrompts[0]);
            w.Ask("next");
            PlanResult plan = w.Ask(DemoPrompts[2]);
            Check(plan.chip == "Fault history", "built fault history, got: " + plan.reply);
            Check(w.store.Get("walkthrough").currentStep == 2, "walkthrough did not move");
        }

        static void PlanEdits()
        {
            World w = MakeWorld();
            w.Ask(DemoPrompts[3]);
            w.Ask(DemoPrompts[4]);

            w.Ask("make the chart bigger");
            Near(w.store.Get("dashboard").elements[0].scale, 1.35f, "chart grew");
            Near(w.store.Get("checklist").elements[0].scale, 1f, "checklist untouched");

            w.Ask("move the chart next to the motor");
            Element chart = w.store.Get("dashboard").elements[0];
            Check(chart.anchor.entity == "motor" && chart.anchor.at == AnchorMode.FloorNear, "chart moved to the motor");
            Near(chart.scale, 1.35f, "scale survives a move");

            w.Ask("put it on the wall");
            Check(w.store.Get("dashboard").elements[0].anchor.at == AnchorMode.Wall, "'it' is the build just touched; now on the wall");
            Check(w.store.Get("checklist").elements[0].anchor.at == AnchorMode.Above, "the checklist stayed where it was");

            w.Ask("remove the checklist");
            Check(w.store.Get("checklist") == null && w.store.Get("dashboard") != null, "checklist removed only");

            w.Ask("clear everything");
            Check(w.store.Builds.Count == 0, "cleared");
        }

        static void PlanDescribeAndUnknown()
        {
            World w = MakeWorld();
            PlanResult plan = w.Ask("what is the gate valve?");
            Element panel = w.store.Get("note").elements.First(e => e.type == ElementType.Panel);
            Check(panel.title == "Gate valve" && panel.body.Contains("clockwise"), "describes the valve");
            PlanResult unknown = w.Ask("sing me a song");
            Check(unknown.ops.Count == 0 && unknown.reply.Length > 0 && unknown.chip == "", "unknown request explains what is possible");
            PlanResult labels = w.Ask("label everything");
            Check(w.store.Get("labels").elements.Count == 5, "five scan labels, got " + w.store.Get("labels").elements.Count);
        }

        static void PlanRoundTrip()
        {
            World w = MakeWorld();
            foreach (string prompt in DemoPrompts)
            {
                PlanResult plan = w.planner.Plan(new PlanRequest { prompt = prompt, room = w.room, site = w.site, builds = w.store.Builds });
                string json = SpecJson.Write(plan);
                PlanResult back = SpecJson.ParsePlan(json);
                Check(SpecJson.Write(back) == json, "round trip differs for: " + prompt);
                Check(back.ops[0].build.elements.Count == plan.ops[0].build.elements.Count, "element count");
            }
        }

        static void SanitizeRepairs()
        {
            World w = MakeWorld();
            string json = @"{""reply"":""ok"",""chip"":""Custom"",""ops"":[
              {""op"":""add"",""build"":{""id"":"""",""kind"":""custom"",""title"":"""",""elements"":[
                {""type"":""label"",""text"":""A"",""anchor"":{""entity"":""the motor"",""at"":""top""}},
                {""type"":""hologram"",""anchor"":{""entity"":""motor""}},
                {""type"":""label"",""text"":""B"",""anchor"":{""entity"":""warp-core"",""at"":""top""}},
                {""type"":""card"",""title"":""T"",""step"":2,""scale"":99,""anchor"":{""entity"":""pump"",""at"":""sideways""}},
                {""type"":""panel"",""title"":""Free"",""anchor"":{""entity"":"""",""at"":""user""}}]}},
              {""op"":""teleport""},
              {""op"":""remove""},
              {""op"":""add"",""build"":{""id"":""empty"",""elements"":[{""type"":""label"",""anchor"":{""entity"":""nope""}}]}}]}";
            PlanResult plan = SpecJson.ParsePlan(json);
            List<string> notes = SpecJson.Sanitize(plan, w.room);
            Check(plan.ops.Count == 1, "only the valid op survives, got " + plan.ops.Count);
            Build b = plan.ops[0].build;
            Check(b.id == "custom" && b.title == "Content", "missing id/title filled");
            Check(b.elements.Count == 3, "bad elements dropped, got " + b.elements.Count);
            Check(b.elements[0].anchor.entity == "motor", "phrase resolved to an entity id");
            Check(b.elements[1].anchor.at == AnchorMode.Center && b.elements[1].scale == 4f, "bad anchor mode and scale clamped");
            Check(b.elements.All(e => !string.IsNullOrEmpty(e.id)) && b.elements.Select(e => e.id).Distinct().Count() == 3, "unique ids");
            Check(b.stepCount == 2 && b.currentStep == 1, "step count inferred");
            Check(notes.Count >= 5, "notes explain what changed: " + notes.Count);
        }

        static void ServicePlanShape()
        {
            // Exactly what the planner service's schema makes the model emit: vectors as {x,y,z}, every field present.
            World w = MakeWorld();
            string json = @"{""reply"":""Two steps."",""chip"":""Walkthrough"",""planner"":""claude-opus-5-5"",""latencyMs"":2100,""ops"":[
              {""op"":""remove"",""buildId"":""labels""},
              {""op"":""add"",""build"":{""id"":""walkthrough"",""kind"":""walkthrough"",""title"":""Walkthrough"",""stepCount"":2,""currentStep"":1,""elements"":[
                {""id"":""pin-1"",""type"":""pin"",""number"":1,""text"":"""",""step"":0,""style"":""accent"",""scale"":1,""anchor"":{""entity"":""gate-valve"",""at"":""top"",""offset"":{""x"":0,""y"":0,""z"":0}}},
                {""id"":""card-1"",""type"":""card"",""number"":1,""title"":""Close this valve"",""body"":""Clockwise until it stops."",""step"":1,""style"":""accent"",""scale"":1,""anchor"":{""entity"":""gate-valve"",""at"":""callout"",""offset"":{""x"":0,""y"":0.1,""z"":0}}},
                {""id"":""gizmo-1"",""type"":""gizmo"",""action"":""turn-cw"",""step"":1,""style"":""accent"",""scale"":1,""anchor"":{""entity"":""gate-valve"",""at"":""top"",""offset"":{""x"":0,""y"":0,""z"":0}}},
                {""id"":""flow-1"",""type"":""flow"",""points"":[{""x"":1.3,""y"":0.46,""z"":0},{""x"":0.2,""y"":0.46,""z"":0}],""radius"":0.07,""entities"":[""inlet""],""text"":"""",""step"":2,""style"":""accent"",""scale"":1,""anchor"":{""entity"":""pump-p101"",""at"":""center"",""offset"":{""x"":0,""y"":0,""z"":0}}},
                {""id"":""chart-1"",""type"":""chart"",""title"":""Output"",""unit"":""m3"",""values"":[1,2,3],""labels"":[],""step"":2,""style"":""accent"",""scale"":1.2,""anchor"":{""entity"":""workbench"",""at"":""floor-near"",""offset"":{""x"":0,""y"":0,""z"":0}}},
                {""id"":""list-1"",""type"":""checklist"",""title"":""Parts"",""items"":[{""text"":""Seal"",""done"":true}],""people"":[""M""],""more"":0,""step"":2,""style"":""accent"",""scale"":1,""anchor"":{""entity"":""workbench"",""at"":""above"",""offset"":{""x"":0,""y"":0,""z"":0}}}]}}]}";
            PlanResult plan = SpecJson.ParsePlan(json);
            List<string> notes = SpecJson.Sanitize(plan, w.room);
            Check(notes.Count == 0, "a well-formed service plan needs no repairs: " + string.Join("; ", notes));
            Check(plan.planner == "claude-opus-5-5" && plan.ops.Count == 2, "metadata and ops");
            Build b = plan.ops[1].build;
            Check(b.elements.Count == 6 && b.stepCount == 2 && b.currentStep == 1, "build parsed");
            Near(b.elements[1].anchor.offset, new Vec3(0, 0.1f, 0), "object-form offset");
            Near(b.elements[3].points[0], new Vec3(1.3f, 0.46f, 0), "object-form flow points");
            Check(b.elements[5].items[0].done && b.elements[5].people[0] == "M", "checklist fields");
            Near(b.elements[4].scale, 1.2f, "scale");
            foreach (Element e in b.elements) Check(LayoutSolver.Place(e, w.room).valid, e.id + " places");
            w.store.Add(BuildFactory.Labels(w.room));
            w.store.Apply(plan);
            Check(w.store.Get("labels") == null && w.store.Get("walkthrough") != null, "applies");
        }

        // ---- layout ------------------------------------------------------------------------

        static void LayoutSane()
        {
            World w = MakeWorld();
            foreach (string prompt in DemoPrompts) w.Ask(prompt);
            int count = 0;
            foreach (Build b in w.store.Builds)
                foreach (Element e in b.elements)
                {
                    Placement p = LayoutSolver.Place(e, w.room);
                    Check(p.valid, e.id + " has no placement");
                    Check(p.position.y >= -0.01f && p.position.y < SimRoom.Height, e.id + " height out of range: " + p.position);
                    Check(Math.Abs(p.position.x) < SimRoom.Width * 0.5f && p.position.z < SimRoom.BackWallZ + 0.01f, e.id + " is outside the room: " + p.position);
                    Near((p.rotation * Vec3.Forward).Length, 1f, e.id + " rotation is not unit");
                    if (e.type != ElementType.Gizmo && e.type != ElementType.Highlight && e.type != ElementType.Flow)
                    {
                        Vec3 away = (p.position - w.room.userPosition).Flat.Normalized;
                        Check(Vec3.Dot((p.rotation * Vec3.Forward).Flat.Normalized, away) > 0.95f, e.id + " does not face the user");
                    }
                    count++;
                }
            Check(count >= 20, "expected at least 20 elements across the demo, got " + count);
        }

        static void LayoutChart()
        {
            World w = MakeWorld();
            w.Ask(DemoPrompts[3]);
            Element chart = w.store.Get("dashboard").elements[0];
            Placement p = LayoutSolver.Place(chart, w.room);
            RoomEntity bench = w.room.Get("workbench");
            Near(p.position.y, 0f, "chart stands on the floor");
            Check(p.position.z < bench.position.z - bench.size.z * 0.5f, "chart is in front of the bench (toward the user)");
            Check(Vec3.Distance(p.position.Flat, bench.position.Flat) < 1.2f, "chart is close to the bench");
            // It must not overlap the pump skid.
            RoomEntity skid = w.room.Get("pump-p101");
            Vec3 local = skid.rotation.Inverse * (p.position - skid.position);
            float halfChart = LayoutSolver.ChartWidth(chart.values.Count) * 0.5f;
            Check(Math.Abs(local.x) > skid.size.x * 0.5f + halfChart * 0.5f || Math.Abs(local.z) > skid.size.z * 0.5f, "chart clears the skid");
        }

        static void LayoutFlow()
        {
            World w = MakeWorld();
            w.Ask(DemoPrompts[1]);
            Element flow = w.store.Get("flow").elements.First(e => e.type == ElementType.Flow);
            List<Vec3> path = LayoutSolver.FlowPath(flow, w.room);
            Check(path.Count == 3, "three points");
            Near(path[0], w.station.TransformPoint(new Vec3(1.3f, 0.46f, 0)), "starts at the inlet end");
            Near(path[2], w.station.TransformPoint(new Vec3(0.2f, 1.26f, 0)), "ends above the outlet");
            RoomEntity inlet = w.room.Get("inlet");
            Near(path[0].y, inlet.position.y, "runs along the pipe centre-line");
        }

        static void LayoutWall()
        {
            World w = MakeWorld();
            var e = new Element { type = ElementType.Panel, anchor = new Anchor { entity = "pump-p101", at = AnchorMode.Wall } };
            Placement p = LayoutSolver.Place(e, w.room);
            Near(p.position.z, SimRoom.BackWallZ - 0.02f, "just off the back wall");
            Near(p.position.y, LayoutSolver.EyeHeight, "eye height");
            Near(p.rotation * Vec3.Forward, new Vec3(0, 0, 1), "faces into the room (forward points into the wall)");
        }

        // ---- store -------------------------------------------------------------------------

        static void StoreOps()
        {
            var store = new BuildStore();
            var log = new List<string>();
            store.Added += b => log.Add("+" + b.id);
            store.Removed += b => log.Add("-" + b.id);
            store.Changed += b => log.Add("~" + b.id);

            World w = MakeWorld();
            Build walk = BuildFactory.Walkthrough(w.site.knowledge.procedures[0], w.room);
            store.Add(walk);
            store.Add(BuildFactory.Checklist(w.site.knowledge, "workbench", AnchorMode.Above));
            store.Add(walk.Clone());
            Check(store.Builds.Count == 2, "re-adding replaces");
            Check(string.Join(" ", log) == "+walkthrough +checklist -walkthrough +walkthrough", "events: " + string.Join(" ", log));
            log.Clear();

            Check(store.SetStep("walkthrough", 2) && !store.SetStep("walkthrough", 2) && !store.SetStep("checklist", 2), "step changes once");
            Check(store.SetStep("walkthrough", 99) && store.Get("walkthrough").currentStep == 3, "step clamps");
            Check(store.ToggleChecklistItem("checklist", "checklist-spares", 2) && store.Get("checklist").elements[0].items[2].done, "toggle");
            Check(string.Join(" ", log) == "~walkthrough ~walkthrough ~checklist", "change events: " + string.Join(" ", log));

            PlanResult snapshot = store.Snapshot();
            var other = new BuildStore();
            other.Apply(snapshot);
            Check(other.Builds.Count == 2 && other.Get("walkthrough").currentStep == 3 && other.Get("checklist").elements[0].items[2].done, "snapshot restores state");
            Check(store.LatestStepped.id == "walkthrough" && store.FindByKind(BuildKind.Checklist) != null, "queries");
            store.Clear();
            Check(store.Builds.Count == 0, "clear");
        }

        static void LabelsRetire()
        {
            World w = MakeWorld();
            w.store.Add(BuildFactory.Labels(w.room));
            PlanResult plan = w.Ask(DemoPrompts[0]);
            Check(plan.ops[0].op == OpType.Remove && plan.ops[0].buildId == "labels", "plan carries the removal");
            Check(w.store.Get("labels") == null && w.store.Get("walkthrough") != null, "labels gone, walkthrough in");
            PlanResult again = w.Ask("label everything");
            Check(w.store.Get("labels") != null && w.store.Get("walkthrough") != null, "asking for labels brings them back alongside");
            PlanResult step = w.Ask("next");
            Check(step.ops.Count == 1 && w.store.Get("labels") != null, "navigation leaves labels alone");
        }

        static void LayoutGizmo()
        {
            World w = MakeWorld();
            w.Ask(DemoPrompts[0]);
            Element gizmo = w.store.Get("walkthrough").elements.First(e => e.type == ElementType.Gizmo);
            Placement p = LayoutSolver.Place(gizmo, w.room);
            Near(p.position, w.station.TransformPoint(new Vec3(0.64f, 0.85f, 0)), "on the handwheel");
            Near(p.rotation * Vec3.Up, Vec3.Up, "axis vertical");
        }

        static void PlanFocus()
        {
            World w = MakeWorld();
            PlanResult plan = w.planner.Plan(new PlanRequest { prompt = "what is this?", room = w.room, site = w.site, builds = w.store.Builds, focusEntityId = "motor" });
            Check(plan.ops.Count == 1 && plan.ops[0].build.elements[0].title == "Motor", "describes the motor");
            plan = w.planner.Plan(new PlanRequest { prompt = "what is this?", room = w.room, site = w.site, builds = w.store.Builds });
            Check(plan.ops.Count == 0, "without a focus there is nothing to describe");
        }

        // ---- remote planner ----------------------------------------------------------------

        sealed class FakeHttp : IHttpTransport
        {
            public Func<string, string, HttpResult> handler;
            public string lastUrl, lastBody;
            public Dictionary<string, string> lastHeaders;
            public int calls;

            public Task<HttpResult> PostJsonAsync(string url, string json, Dictionary<string, string> headers, int timeoutSeconds)
            {
                calls++;
                lastUrl = url; lastBody = json; lastHeaders = headers;
                return Task.FromResult(handler(url, json));
            }
        }

        static void RemotePlannerPaths()
        {
            World w = MakeWorld();
            var http = new FakeHttp();
            var remote = new RemotePlanner(http, () => "http://relay.local:8787/", () => "secret");
            var request = new PlanRequest { prompt = "label the motor", room = w.room, site = w.site, builds = w.store.Builds };

            http.handler = (url, body) => new HttpResult
            {
                ok = true, status = 200,
                body = @"{""reply"":""Done."",""chip"":""Labels"",""planner"":""claude-opus-5-5"",""ops"":[{""op"":""add"",""build"":{""id"":""labels"",""kind"":""labels"",""title"":""Labels"",
                         ""elements"":[{""id"":""l1"",""type"":""label"",""text"":""Motor"",""anchor"":{""entity"":""motor"",""at"":""top""}}]}}]}",
            };
            PlanResult plan = remote.PlanAsync(request).Result;
            Check(http.lastUrl == "http://relay.local:8787/v1/plan", "url: " + http.lastUrl);
            Check(http.lastHeaders["Authorization"] == "Bearer secret", "auth header");
            var sent = Json.ParseObject(http.lastBody);
            Check(sent.Str("prompt") == "label the motor", "prompt sent");
            Check(sent.Obj("room").Arr("entities").Count == w.room.entities.Count, "room sent");
            Check(sent.Obj("knowledge").Arr("procedures").Count == 2, "knowledge sent");
            Check(sent.Arr("builds").Count == 0, "builds sent");
            Check(plan.error == null && plan.planner == "claude-opus-5-5" && plan.ops.Count == 1, "plan parsed");

            http.handler = (url, body) => new HttpResult { ok = false, status = 502, error = "Bad gateway" };
            Check(remote.PlanAsync(request).Result.error.Contains("Bad gateway"), "http failure surfaces");
            http.handler = (url, body) => new HttpResult { ok = true, status = 200, body = "<html>oops</html>" };
            Check(remote.PlanAsync(request).Result.error.Contains("unreadable"), "garbage surfaces");
            http.handler = (url, body) => { throw new InvalidOperationException("boom"); };
            Check(remote.PlanAsync(request).Result.error.Contains("boom"), "exceptions surface");
        }

        static void RouterFallback()
        {
            World w = MakeWorld();
            var http = new FakeHttp { handler = (u, b) => new HttpResult { ok = false, status = 500, error = "down" } };
            string url = "http://relay.local";
            var router = new PlannerRouter(new OfflinePlanner(), new RemotePlanner(http, () => url));
            var request = new PlanRequest { prompt = DemoPrompts[0], room = w.room, site = w.site, builds = w.store.Builds };

            PlanResult plan = router.PlanAsync(request).Result;
            Check(http.calls == 1 && plan.chip == "Walkthrough" && plan.error == "Planner service error: down", "falls back with the error noted");
            Check(plan.planner.StartsWith("offline"), "planner name: " + plan.planner);
            w.store.Apply(plan);

            request = new PlanRequest { prompt = "next", room = w.room, site = w.site, builds = w.store.Builds };
            plan = router.PlanAsync(request).Result;
            Check(http.calls == 1 && plan.ops[0].op == OpType.Step, "navigation never hits the network");

            url = "";
            request = new PlanRequest { prompt = DemoPrompts[1], room = w.room, site = w.site, builds = w.store.Builds };
            plan = router.PlanAsync(request).Result;
            Check(http.calls == 1 && plan.error == null && plan.planner == "offline", "unconfigured -> offline, no error");
        }

        // ---- session -----------------------------------------------------------------------

        static void SessionRoundTrip()
        {
            using (var host = new SessionLink { LocalName = "Headset", LocalDevice = "quest" })
            using (var client = new SessionLink { LocalName = "Sara", LocalRole = "Reliability engineer", LocalDevice = "ios" })
            {
                Peer joined = null, moved = null;
                string prompt = null, reply = null;
                PlanResult received = null;
                host.PeerJoined += p => joined = p;
                host.PeerMoved += p => moved = p;
                host.PromptReceived += (text, p) => prompt = text + " from " + p.name;
                client.PlanReceived += (plan, p) => received = plan;
                client.ReplyReceived += (text, chip, planner) => reply = text + "|" + chip;

                host.Host(0);
                client.Join("127.0.0.1", host.Port);
                WaitFor(() => { host.Pump(); client.Pump(); return joined != null && client.Peers.Count == 1; }, "handshake");
                Check(joined.name == "Sara" && joined.device == "ios" && client.Peers[0].name == "Headset", "names exchanged");

                client.SendPrompt("show me how the water flows");
                client.SendPresence(new Vec3(1, 1.6f, -2), Vec3.Forward);
                WaitFor(() => { host.Pump(); return prompt != null && moved != null; }, "prompt + presence");
                Check(prompt == "show me how the water flows from Sara", "prompt: " + prompt);
                Near(moved.position, new Vec3(1, 1.6f, -2), "presence position");

                World w = MakeWorld();
                PlanResult plan = w.Ask(prompt.Substring(0, prompt.IndexOf(" from ", StringComparison.Ordinal)));
                host.SendPlan(plan);
                host.SendReply(plan.reply, plan.chip, plan.planner);
                WaitFor(() => { client.Pump(); return received != null && reply != null; }, "plan + reply");
                Check(received.ops[0].build.id == "flow" && received.ops[0].build.elements.Count == plan.ops[0].build.elements.Count, "plan arrived intact");
                Check(reply.EndsWith("|Live flow"), "reply: " + reply);

                string config = null;
                host.ConfigReceived += (url, token, useAi, p) => config = url + "|" + token + "|" + useAi + "|" + p.name;
                client.SendConfig("http://10.0.0.5:8787", "tok", true);
                WaitFor(() => { host.Pump(); return config != null; }, "config");
                Check(config == "http://10.0.0.5:8787|tok|True|Sara", "config: " + config);

                Peer left = null;
                host.PeerLeft += p => left = p;
                client.Stop();
                WaitFor(() => { host.Pump(); return left != null; }, "peer left");
            }
        }

        static void WaitFor(Func<bool> condition, string what)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return;
                Thread.Sleep(10);
            }
            throw new Exception("timed out waiting for " + what);
        }

        // ---- export ------------------------------------------------------------------------

        /// <summary>
        /// Writes what the shipping planner and binder produce for the demo, for the docs simulator to replay:
        /// the bound room (with hologram shapes), and one plan per demo prompt plus a few follow-ups.
        /// </summary>
        static int Export(string dir)
        {
            Directory.CreateDirectory(dir);
            World w = MakeWorld();

            var roomJson = w.room.ToJson();
            var entities = roomJson.Arr("entities");
            for (int i = 0; i < w.room.entities.Count; i++)
            {
                RoomEntity e = w.room.entities[i];
                var d = (Dictionary<string, object>)entities[i];
                d["shapeOrigin"] = Json.FromVec3(e.ShapeOrigin);
                d["scanLabel"] = e.scanLabel;
                if (e.shapes != null)
                {
                    var shapes = new List<object>();
                    foreach (ShapeDef s in e.shapes) shapes.Add(s.ToJson());
                    d["shapes"] = shapes;
                }
            }
            roomJson["station"] = new Dictionary<string, object>
            {
                { "position", Json.FromVec3(w.station.position) }, { "yaw", Json.Round(w.station.rotation.YawDegrees) },
            };

            var script = new List<string>(DemoPrompts);
            script.InsertRange(1, new[] { "next", "next" });
            script.AddRange(new[] { "make the chart bigger", "move the chart next to the motor", "what is the gate valve?", "label everything", "remove the checklist", "clear everything" });

            var steps = new List<object>();
            // Scan labels first: this is what the room looks like right after the scan.
            steps.Add(Entry("(scan)", new PlanResult { reply = "Here is what I can see.", chip = "Labels", planner = "offline", ops = { PlanOp.Add(BuildFactory.Labels(w.room)) } }, w));
            w.store.Add(BuildFactory.Labels(w.room));
            foreach (string prompt in script)
            {
                PlanResult plan = w.planner.Plan(new PlanRequest { prompt = prompt, room = w.room, site = w.site, builds = w.store.Builds });
                w.store.RetireScanLabels(plan);
                steps.Add(Entry(prompt, plan, w));
                w.store.Apply(plan);
            }

            // A library of stand-alone requests, each planned against an empty room, so the docs can answer
            // free-typed prompts in any order by replaying the nearest one.
            string[] standalone =
            {
                DemoPrompts[0], DemoPrompts[1], DemoPrompts[2], DemoPrompts[3], DemoPrompts[4], DemoPrompts[5],
                "Walk me through the start-up check", "Show energy this month on the bench", "Put this month's output on the wall",
                "Put the parts checklist on the wall", "Bring Marco in", "Label everything",
                "What is the motor?", "What is the pump?", "What is the gate valve?", "What is the coupling?",
                "What is the control box?", "What is the outlet?", "What is the inlet pipe?", "What is the workbench?",
            };
            var library = new List<object>();
            foreach (string prompt in standalone)
            {
                World fresh = MakeWorld();
                PlanResult plan = fresh.planner.Plan(new PlanRequest { prompt = prompt, room = fresh.room, site = fresh.site, builds = fresh.store.Builds });
                if (plan.ops.Count == 0) throw new Exception("library prompt produced nothing: " + prompt);
                library.Add(Entry(prompt, plan, fresh));
            }
            // What the planner says when it cannot help, so the docs show the real wording.
            string fallback = w.planner.Plan(new PlanRequest { prompt = "sing me a song", room = w.room, site = w.site, builds = w.store.Builds }).reply;

            File.WriteAllText(Path.Combine(dir, "room.json"), Json.Write(roomJson, true));
            File.WriteAllText(Path.Combine(dir, "plans.json"), Json.Write(steps, true));
            var bundle = new Dictionary<string, object> { { "room", roomJson }, { "plans", steps }, { "library", library }, { "fallbackReply", fallback } };
            File.WriteAllText(Path.Combine(dir, "spatail-data.js"),
                "// Generated by `dotnet run -- export` in tests/Spatail.Core.Tests. Do not edit by hand.\n" +
                "window.SPATAIL_DATA = " + Json.Write(bundle) + ";\n");
            Console.WriteLine("Wrote room.json (" + w.room.entities.Count + " entities), plans.json (" + steps.Count + " plans) and spatail-data.js (" + library.Count + " library plans) to " + dir);
            return 0;
        }

        static Dictionary<string, object> Entry(string prompt, PlanResult plan, World w)
        {
            // Resolve every element with the shipping layout solver so the docs can show exactly where it lands.
            var placements = new Dictionary<string, object>();
            foreach (PlanOp op in plan.ops)
            {
                if (op.build == null) continue;
                foreach (Element e in op.build.elements)
                {
                    Placement p = LayoutSolver.Place(e, w.room);
                    var entry = new Dictionary<string, object>
                    {
                        { "position", Json.FromVec3(p.position) },
                        { "rotation", new List<object> { Json.Round(p.rotation.x), Json.Round(p.rotation.y), Json.Round(p.rotation.z), Json.Round(p.rotation.w) } },
                        { "target", Json.FromVec3(p.target) },
                        { "pivotBottom", p.pivotBottom },
                    };
                    if (e.type == ElementType.Flow)
                    {
                        var path = new List<object>();
                        foreach (Vec3 pt in LayoutSolver.FlowPath(e, w.room)) path.Add(Json.FromVec3(pt));
                        entry["path"] = path;
                    }
                    placements[op.build.id + "/" + e.id] = entry;
                }
            }
            // Timing differs run to run; leave it out so the exported files are reproducible.
            plan.latencyMs = 0;
            return new Dictionary<string, object> { { "prompt", prompt }, { "plan", SpecJson.ToJson(plan) }, { "placements", placements } };
        }
    }
}
