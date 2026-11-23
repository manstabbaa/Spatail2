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
    ///   dotnet run -- export out-dir    write the lab's data: the runtime library, example programs, fonts and stand-in scene
    /// </summary>
    static class Program
    {
        static int _passed, _failed;
        static readonly List<string> Failures = new List<string>();

        // ---- files ---------------------------------------------------------------------------

        static string Resources()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string candidate = Path.Combine(dir, "packages", "com.spatail.core", "Runtime", "Resources", "Spatail");
                if (Directory.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }
            throw new DirectoryNotFoundException("Resources/Spatail not found above " + AppContext.BaseDirectory);
        }

        static string Library() => File.ReadAllText(Path.Combine(Resources(), "Script", "spatail.js.txt"));
        static string Example(string id)
        {
            string path = Path.Combine(Resources(), "Script", "Examples", id + ".js.txt");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        static SiteDefinition LoadSite() => SiteDefinition.Parse(File.ReadAllText(Path.Combine(Resources(), "pump_station.json")));
        static FontSet LoadFonts()
        {
            var fonts = new FontSet();
            fonts.Add("medium", FontMetrics.Parse(File.ReadAllText(Path.Combine(Resources(), "Fonts", "inter_medium_metrics.json"))));
            fonts.Add("semibold", FontMetrics.Parse(File.ReadAllText(Path.Combine(Resources(), "Fonts", "inter_semibold_metrics.json"))));
            return fonts;
        }

        public static readonly string[] FilmPrompts =
        {
            "Build me a walkthrough for this pump",
            "Show me how the water flows",
            "Has it failed before?",
            "Put this month's output by the bench",
            "What parts do I need?",
            "Bring Sara in",
        };
        static readonly string[] FilmIds = { "walkthrough", "flow", "faults", "dashboard", "checklist", "session" };

        static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "export") return Export(args[1]);

            Run("Vec3 and Quat follow Unity conventions", MathConventions);
            Run("Quat.LookRotation and FromTo", LookRotation);
            Run("Json round-trips values, escapes and vectors", JsonRoundTrip);
            Run("Json rejects malformed input", JsonRejects);
            Run("Stand-in scene loads with parts and fixtures", SiteLoads);
            Run("Binder places parts, snaps the control box to a wall, binds a real table", BinderBehaviour);
            Run("Raycaster: planes, boxes, misses", Raycasts);
            Run("ViewFrame: pixel to ray and back", ViewFrames);
            Run("Font metrics: measuring and wrapping", Fonts);

            Run("Runtime: a program builds nodes, parents them and measures text", RuntimeBasics);
            Run("Runtime: animation, timers and frame callbacks", RuntimeAnimation);
            Run("Runtime: the space API (view, raycast, surfaces, props, placement helpers)", RuntimeSpace);
            Run("Runtime: space.find resolves asynchronously through the host", RuntimeFind);
            Run("Runtime: clicks, hover and bubbling", RuntimeEvents);
            Run("Runtime: stopping removes everything; replacing keeps state", RuntimeLifecycle);
            Run("Runtime: syntax errors, thrown errors and runaway loops are contained", RuntimeFailures);
            Run("Runtime: a failing callback is logged and the program carries on", RuntimeCallbackErrors);
            Run("Runtime: programs cannot reach outside the sandbox", RuntimeSandbox);
            Run("Runtime: node budget", RuntimeNodeBudget);

            Run("Film programs: all six run cleanly in the stand-in room", FilmPrograms);
            Run("Film programs: walkthrough steps by voice, by tapping, and finishes", FilmWalkthrough);
            Run("Film programs: checklist ticks and remembers", FilmChecklist);
            Run("Film programs: chart lands on free floor by the bench", FilmChart);
            Run("Film programs: work in a room with a real table and other walls", FilmOtherRoom);

            Run("Demo builder: routes the film requests, removes, clears, declines the rest", DemoRouting);
            Run("Build request: carries the frame, depth samples and where props appear", RequestShape);
            Run("Remote builder: success, failure, unreadable", RemotePaths);
            Run("Director: voice commands never leave the device", DirectorVoice);
            Run("Director: runs what the planner writes, with the request's view", DirectorRemote);
            Run("Director: a failing program is sent back once for repair", DirectorRepair);
            Run("Director: falls back to the demo when the service is down", DirectorFallback);
            Run("Session link: prompts, replies, presence and settings over TCP", SessionRoundTrip);

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

        // ---- a host that records what programs do ------------------------------------------------

        sealed class TestNode
        {
            public int id;
            public string kind;
            public Dictionary<string, object> props = new Dictionary<string, object>();
            public Vec3 position, scale = Vec3.One;
            public Quat rotation = Quat.Identity;
            public int Parent => props.Int("parent");
        }

        sealed class TestHost : IScriptHost
        {
            public readonly Dictionary<int, TestNode> Nodes = new Dictionary<int, TestNode>();
            public RoomModel Room = new RoomModel();
            public Vec3 UserPosition = new Vec3(0.1f, 1.6f, -2.3f), UserForward = Vec3.Forward;
            public Func<string, List<RayHit>> Finder;
            public readonly List<Action> Deferred = new List<Action>();
            public int Created, Destroyed;

            public void CreateNode(int id, string kind, Dictionary<string, object> props)
            {
                Created++;
                Nodes[id] = new TestNode { id = id, kind = kind, props = props };
            }
            public void UpdateNode(int id, Dictionary<string, object> props)
            {
                foreach (var kv in props) Nodes[id].props[kv.Key] = kv.Value;
            }
            public void SetTransform(int id, Vec3 position, Quat rotation, Vec3 scale)
            {
                TestNode n = Nodes[id];
                n.position = position; n.rotation = rotation; n.scale = scale;
            }
            public void DestroyNode(int id)
            {
                Destroyed++;
                Nodes.Remove(id);
            }
            public bool Raycast(Vec3 origin, Vec3 direction, float maxDistance, out RayHit hit) =>
                GeometryRaycaster.Raycast(Room.entities, origin, direction, maxDistance, out hit);
            public float FloorY => Room.floorY;
            public IReadOnlyList<RoomEntity> Surfaces => Room.entities.Where(e => !e.isVirtual).ToList();
            public IReadOnlyList<RoomEntity> Props => Room.entities.Where(e => e.isVirtual).ToList();
            public void GetUser(out Vec3 position, out Vec3 forward, out Quat rotation)
            {
                position = UserPosition; forward = UserForward; rotation = Quat.LookRotation(UserForward);
            }
            public void Find(string description, Action<List<RayHit>> done)
            {
                // Answer later, as a device would after asking the vision service.
                Deferred.Add(() => done(Finder != null ? Finder(description) : new List<RayHit>()));
            }

            public IEnumerable<TestNode> OfKind(string kind) => Nodes.Values.Where(n => n.kind == kind);
            public TestNode Text(string text) => Nodes.Values.FirstOrDefault(n => n.kind == "text" && n.props.Str("text") == text);
            public Vec3 World(TestNode n)
            {
                Vec3 p = n.position;
                int guard = 0;
                while (n.Parent != 0 && guard++ < 32)
                {
                    TestNode parent = Nodes[n.Parent];
                    p = parent.position + parent.rotation * Vec3.Scale(p, parent.scale);
                    n = parent;
                }
                return p;
            }
            public TestNode Root(TestNode n)
            {
                int guard = 0;
                while (n.Parent != 0 && guard++ < 32) n = Nodes[n.Parent];
                return n;
            }
            /// <summary>The nearest ancestor (or the node itself) that was made interactive.</summary>
            public TestNode Clickable(TestNode n)
            {
                int guard = 0;
                while (n != null && guard++ < 32)
                {
                    if (n.props.Bool("interactive")) return n;
                    n = n.Parent != 0 ? Nodes[n.Parent] : null;
                }
                return null;
            }
        }

        sealed class World
        {
            public TestHost host = new TestHost();
            public ExperienceRuntime runtime;
            public SiteDefinition site;
            public Pose3 station;
            public readonly List<string> log = new List<string>();
            public readonly List<Experience> failed = new List<Experience>();

            public Experience Start(string id, string code, ViewFrame view = null) => runtime.Start(id, id, "test", code, view);

            public void Advance(float seconds, float step = 1f / 30f)
            {
                for (float t = 0; t < seconds; t += step)
                {
                    List<Action> due = host.Deferred.ToList();
                    host.Deferred.Clear();
                    foreach (Action a in due) a();
                    runtime.Tick(step);
                }
            }

            public ViewFrame UserView() =>
                ViewFrame.FromFov(host.UserPosition, Quat.LookRotation(new Vec3(0.05f, -0.35f, 1f)), 60f, 4f / 3f, 1280, 960);

            public void Click(TestNode node)
            {
                TestNode target = host.Clickable(node);
                Check(target != null, "nothing clickable at or above node " + node.id + " (" + node.kind + ")");
                runtime.DispatchEvent(target.id, "click");
            }
        }

        static World MakeWorld(bool standIn = true, bool realTable = false)
        {
            var w = new World { site = LoadSite() };
            RoomModel raw = SimRoom.Create(realTable);
            if (standIn)
            {
                w.station = SiteBinder.SuggestStationPose(raw, w.site);
                w.host.Room = SiteBinder.Bind(w.site, raw, w.station);
            }
            else w.host.Room = raw;
            w.runtime = new ExperienceRuntime(w.host, Library(), LoadFonts());
            w.runtime.Logged += (e, level, message) => w.log.Add(level + ": " + message);
            w.runtime.Failed += e => w.failed.Add(e);
            return w;
        }

        // ---- math, json ----------------------------------------------------------------------

        static void MathConventions()
        {
            Near(Quat.Yaw(90) * Vec3.Forward, Vec3.Right, "yaw 90 turns forward to right");
            Near(Quat.AngleAxis(90, Vec3.Right) * Vec3.Forward, new Vec3(0, -1, 0), "pitch 90 looks down");
            Near(Quat.Euler(0, 0, 90) * Vec3.Up, new Vec3(-1, 0, 0), "roll 90 turns up to -X");
            Quat q = Quat.Euler(20, 50, 70);
            Near(q.Inverse * (q * new Vec3(1, 2, 3)), new Vec3(1, 2, 3), "inverse undoes rotation");
            Near(Vec3.Cross(Vec3.Right, Vec3.Up), Vec3.Forward, "left-handed cross product");
            var pose = new Pose3(new Vec3(1, 0, 2), Quat.Yaw(90));
            Near(pose.TransformPoint(new Vec3(0, 0, 1)), new Vec3(2, 0, 2), "pose transforms points");
            Near(pose.InverseTransformPoint(new Vec3(2, 0, 2)), new Vec3(0, 0, 1), "pose inverse-transforms points");
        }

        static void LookRotation()
        {
            Quat q = Quat.LookRotation(new Vec3(1, 0, 0));
            Near(q * Vec3.Forward, Vec3.Right, "look rotation forward");
            Near(q * Vec3.Up, Vec3.Up, "look rotation keeps up");
            Near(Quat.FromTo(Vec3.Up, Vec3.Right) * Vec3.Up, Vec3.Right, "from-to");
            Near(Quat.FromTo(Vec3.Up, -Vec3.Up) * Vec3.Up, -Vec3.Up, "from-to opposite");
        }

        static void JsonRoundTrip()
        {
            string src = "{\"a\":1,\"b\":[1.5,-2,3e2],\"c\":\"q\\\"uote\\n\\u00e9\",\"d\":true,\"e\":null,\"f\":{\"x\":0.25}}";
            var o = Json.ParseObject(src);
            Check(o.Int("a") == 1 && o.Arr("b").Count == 3 && Json.ToDouble(o.Arr("b")[2]) == 300, "numbers");
            Check(o.Str("c") == "q\"uote\né" && o.Bool("d") && !o.Has("e"), "strings, bools, null");
            var again = Json.ParseObject(Json.Write(o));
            Check(again.Str("c") == o.Str("c") && again.Arr("b").Count == 3, "write/parse round trip");
            Near(Json.ToVec3(Json.Parse("[1,2,3]")), new Vec3(1, 2, 3), "vec from array");
            Near(Json.ToVec3(Json.Parse("{\"x\":1,\"y\":2,\"z\":3}")), new Vec3(1, 2, 3), "vec from object");
        }

        static void JsonRejects()
        {
            foreach (string bad in new[] { "", "{", "{\"a\":}", "[1,2", "{\"a\":1} x", "nul", "{'a':1}" })
            {
                object v;
                Check(!Json.TryParse(bad, out v), "should reject: " + bad);
            }
        }

        // ---- stand-in scene ------------------------------------------------------------------

        static void SiteLoads()
        {
            SiteDefinition site = LoadSite();
            Check(site.equipmentId == "pump-p101" && site.parts.Count == 7 && site.fixtures.Count == 2, "equipment, parts, fixtures");
            Check(site.parts.All(p => p.shapes.Count > 0 && p.size.x > 0), "all parts have geometry and bounds");
            PartDef valve = site.FindPart("gate-valve");
            Check(valve.actionAxis.y == 1 && valve.hasActionPoint, "valve has an action axis and point");
        }

        static void BinderBehaviour()
        {
            SiteDefinition site = LoadSite();
            var station = new Pose3(new Vec3(2, 0, 1), Quat.Yaw(90));
            RoomModel room = SiteBinder.Bind(site, SimRoom.Create(), station);
            RoomEntity valve = room.Get("gate-valve");
            Near(valve.position, new Vec3(2, 0.64f, 1 - 0.64f), "valve follows the station pose");
            Near(valve.ShapeOrigin, station.position, "shape origin is the station origin");

            World w = MakeWorld();
            RoomEntity box = w.host.Room.Get("control-box");
            Near(box.ShapeOrigin.z, SimRoom.BackWallZ, "control box sits on the wall plane");
            Near(box.Forward, new Vec3(0, 0, -1), "control box faces into the room");
            Check(w.host.Room.Get("workbench").isVirtual, "no table in the room: the bench is a stand-in");

            World real = MakeWorld(realTable: true);
            RoomEntity bench = real.host.Room.Get("workbench");
            Check(!bench.isVirtual && bench.shapes == null && real.host.Room.Get("table-1") == null, "a real table becomes the workbench");
        }

        static void Raycasts()
        {
            World w = MakeWorld();
            RayHit hit;
            Check(w.host.Raycast(new Vec3(0, 1.6f, -2), new Vec3(0, -1, 0), 5, out hit), "down hits the floor");
            Near(hit.position, new Vec3(0, 0, -2), "floor point");
            Near(hit.normal, Vec3.Up, "floor normal");
            Near(hit.distance, 1.6f, "floor distance");

            Check(w.host.Raycast(new Vec3(2.5f, 1.5f, -1), new Vec3(0, 0, 1), 10, out hit), "forward hits the back wall");
            Near(hit.position.z, SimRoom.BackWallZ, "wall plane");
            Near(hit.normal, new Vec3(0, 0, -1), "wall normal faces the room");

            RoomEntity motor = w.host.Room.Get("motor");
            Vec3 from = motor.position + new Vec3(0, 0, -2);
            Check(w.host.Raycast(from, new Vec3(0, 0, 1), 10, out hit), "hits the motor");
            Near(hit.position.z, motor.position.z - motor.size.z * 0.5f, "front face of the motor's bounds");
            Check(hit.label == "Motor", "label: " + hit.label);

            Check(!w.host.Raycast(new Vec3(0, 1.6f, -2), new Vec3(0, 1, 0), 10, out hit), "up hits nothing (no ceiling)");
            Check(!w.host.Raycast(new Vec3(0, 1.6f, 2), new Vec3(0, 0, -1), 0.5f, out hit), "the back of a wall is not solid");
        }

        static void ViewFrames()
        {
            var view = ViewFrame.FromFov(new Vec3(1, 1.5f, -2), Quat.Euler(10, 30, 0), 60f, 4f / 3f);
            Vec3 origin, direction;
            view.Ray(0.5f, 0.5f, out origin, out direction);
            Near(direction, view.rotation * Vec3.Forward, "centre pixel looks straight ahead");
            view.Ray(0.9f, 0.5f, out origin, out direction);
            Check(Vec3.Dot(direction, view.rotation * Vec3.Right) > 0, "right of centre looks right");
            view.Ray(0.5f, 0.1f, out origin, out direction);
            Check(Vec3.Dot(direction, view.rotation * Vec3.Up) > 0, "top of the image looks up");

            view.Ray(0.27f, 0.81f, out origin, out direction);
            float u, v;
            Check(view.Project(origin + direction * 3.2f, out u, out v), "point in front projects");
            Near(u, 0.27f, "u round trip");
            Near(v, 0.81f, "v round trip");
            Check(!view.Project(origin - (view.rotation * Vec3.Forward), out u, out v), "points behind the camera do not project");
        }

        static void Fonts()
        {
            FontSet fonts = LoadFonts();
            FontMetrics f = fonts.Get("medium");
            float w, h;
            f.Measure("Close this valve", 0.03f, 0, out w, out h);
            Check(w > 0.2f && w < 0.26f, "one line width plausible: " + w);
            Near(h, f.CapHeight * 0.03f, "one line is cap height tall");
            float w2, h2;
            f.Measure("Turn the handwheel clockwise until it stops.", 0.022f, 0.3f, out w2, out h2);
            Check(w2 <= 0.3f && h2 > h, "wraps within the width");
            Check(f.Wrap("Turn the handwheel clockwise until it stops.", 0.3f / 0.022f).Count == 2, "two lines");
            Check(fonts.Get("semibold").Width("Motor") > f.Width("Motor"), "semibold is wider");
            Check(fonts.Get("no-such-weight") == f, "unknown weight falls back");
        }

        // ---- runtime -------------------------------------------------------------------------

        static void RuntimeBasics()
        {
            World w = MakeWorld();
            Experience e = w.Start("basics", @"
                const g = scene.group({ position: [1, 2, 3] });
                const box = scene.box({ parent: g, size: [0.2, 0.1, 0.4], color: 'accent', position: [0, 0.5, 0], rotation: [0, 90, 0], scale: 2 });
                const label = scene.text({ parent: g, text: 'Hello pump', size: 0.03, weight: 'semibold' });
                const loose = scene.sphere({ radius: 0.1, color: '#ff0000', opacity: 0.5 });
                g.add(loose);
                loose.position = [0, 0, 1];
                log('size', label.width > 0.1, label.height > 0.01, box.worldPosition());
                box.color = 'warning';
            ");
            Check(e.Error == null && e.IsRunning, "runs: " + e.Error);
            Check(w.host.Nodes.Count == 4 && e.NodeCount == 4, "four nodes");
            TestNode group = w.host.OfKind("group").Single(), box = w.host.OfKind("box").Single(), sphere = w.host.OfKind("sphere").Single();
            Near(group.position, new Vec3(1, 2, 3), "group position");
            Check(box.Parent == group.id && sphere.Parent == group.id, "parenting, at creation and by add()");
            Near(box.rotation * Vec3.Forward, Vec3.Right, "euler rotation applied");
            Near(box.scale, new Vec3(2, 2, 2), "uniform scale");
            Check(box.props.Str("color") == "#E8722C", "theme colour resolved, then updated: " + box.props.Str("color"));
            Near(Json.ToVec3(box.props["size"]), new Vec3(0.2f, 0.1f, 0.4f), "kind-specific props pass through");
            Near((float)sphere.props.Num("opacity"), 0.5f, "opacity");
            Near(w.host.World(sphere), new Vec3(1, 2, 4), "world position through the parent");
            Check(w.log.Single() == "info: size true true [1,2.5,3]", "program log: " + string.Join(" | ", w.log));
        }

        static void RuntimeAnimation()
        {
            World w = MakeWorld();
            w.Start("anim", @"
                const a = scene.box({ size: [0.1, 0.1, 0.1] });
                a.animate({ position: [1, 0, 0], scale: 3 }, { duration: 0.5, ease: 'linear' });
                const spinner = scene.box({ size: [0.1, 0.1, 0.1] });
                spinner.spin([0, 1, 0], 90);
                let frames = 0, fired = 0, ticks = 0;
                scene.onFrame(() => frames++);
                scene.after(0.3, () => fired++);
                const stop = scene.every(0.2, () => { ticks++; if (ticks === 3) stop(); });
                scene.wait(0.6).then(() => log('report', frames > 10, fired, ticks));
            ");
            w.Advance(0.25f);
            TestNode a = w.host.Nodes.Values.First();
            Check(a.position.x > 0.3f && a.position.x < 0.7f, "half way: " + a.position.x);
            w.Advance(0.75f);
            Near(a.position, new Vec3(1, 0, 0), "tween finished", 1e-2f);
            Near(a.scale, new Vec3(3, 3, 3), "scale tweened", 1e-2f);
            TestNode spinner = w.host.Nodes.Values.Last();
            Near(spinner.rotation.YawDegrees, 90f, "one second at 90 degrees per second", 4f);
            Check(w.log.Contains("info: report true 1 3"), "timers: " + string.Join(" | ", w.log));
        }

        static void RuntimeSpace()
        {
            World w = MakeWorld();
            ViewFrame view = w.UserView();
            RoomEntity valve = w.host.Room.Get("gate-valve");
            float u, v;
            Check(view.Project(valve.position, out u, out v), "valve is in view");
            string uv = u.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + ", " + v.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
            Experience e = w.Start("space", @"
                const hit = space.view.point(" + uv + @");
                log('view', hit.label, hit.distance > 1 && hit.distance < 4);
                log('sky', space.raycast([0, 1.6, -2], [0, 1, 0]));
                const down = space.raycast([0, 1.6, -2], [0, -1, 0]);
                log('floor', down.position, down.normal, space.floorY);
                log('floorBelow', space.floorBelow([0.4, 1.2, -1.5]));
                log('kinds', space.surfaces().map(s => s.kind).sort().join(','));
                const valve = space.prop('the gate valve');
                log('prop', valve.id, space.prop('bench').id, space.prop('pump').id, space.prop('spaceship'));
                log('action', valve.actionPoint[1] > valve.position[1]);
                const spot = space.freeFloorNear(space.prop('workbench'), 0.45);
                log('spot', spot);
                const facing = quat.rotate(space.faceUser([0, 1, 0]), [0, 0, 1]);
                log('facing', facing[2] > 0.99);
                const wall = space.nearestWall(valve);
                log('wall', wall.surface.id, wall.position[2] > 0.9);
                log('beside', space.beside(valve)[2] < valve.position[2], space.topOf(valve)[1] > valve.position[1]);
                log('head', user.head.position, user.head.forward);
            ", view);
            Check(e.Error == null, "runs: " + e.Error);
            string all = string.Join("\n", w.log);
            Check(all.Contains("info: view Gate valve true"), "view.point finds the valve:\n" + all);
            Check(all.Contains("info: sky null"), "nothing along a ray into the open (the room has no ceiling)");
            Check(all.Contains("info: floor [0,0,-2] [0,1,0] 0"), "raycast down");
            Check(all.Contains("info: floorBelow [0.4,0,-1.5]"), "floor below a point");
            Check(all.Contains("info: kinds floor,wall,wall,wall"), "surfaces are the real ones only");
            Check(all.Contains("info: prop gate-valve workbench pump null"), "props by name, alias and part");
            Check(all.Contains("info: action true"), "action point exposed");
            Check(all.Contains("info: facing true"), "faceUser points content away from the user");
            Check(all.Contains("info: wall wall-back true"), "nearest wall");
            Check(all.Contains("info: beside true true"), "beside and topOf");
            Check(all.Contains("info: head [0.1,1.6,-2.3] [0,0,1]"), "user head");

            // The spot found must actually be clear of everything standing on the floor.
            string spotLine = w.log.First(l => l.StartsWith("info: spot "));
            Vec3 spot = Json.ToVec3(Json.Parse(spotLine.Substring("info: spot ".Length)));
            Near(spot.y, 0f, "on the floor");
            foreach (RoomEntity prop in w.host.Props)
            {
                if (prop.position.y - prop.size.y * 0.5f > 0.6f || prop.kind == EntityKind.Equipment) continue;
                Vec3 local = prop.rotation.Inverse * (spot - prop.position);
                bool inside = Math.Abs(local.x) < prop.size.x * 0.5f && Math.Abs(local.z) < prop.size.z * 0.5f;
                Check(!inside, "free floor spot is inside " + prop.id);
            }
        }

        static void RuntimeFind()
        {
            World w = MakeWorld();
            w.host.Finder = query => query.Contains("extinguisher")
                ? new List<RayHit> { new RayHit { position = new Vec3(3, 1, 0.9f), normal = new Vec3(0, 0, -1), distance = 4, label = "fire extinguisher" } }
                : new List<RayHit>();
            Experience e = w.Start("find", @"
                log('before');
                const hits = await space.find('the fire extinguisher');
                log('found', hits.length, hits[0].label, hits[0].position);
                kit.label('Extinguisher', hits[0]);
                const none = await space.find('a unicorn');
                log('none', none.length);
            ");
            Check(e.IsRunning && w.log.Count == 1, "suspended at the first await");
            w.Advance(0.2f);
            string all = string.Join("\n", w.log);
            Check(all.Contains("info: found 1 fire extinguisher [3,1,0.9]"), "resolved with the host's answer:\n" + all);
            Check(all.Contains("info: none 0"), "empty result resolves too");
            Check(w.host.Text("Extinguisher") != null, "content built after the await");
        }

        static void RuntimeEvents()
        {
            World w = MakeWorld();
            w.Start("events", @"
                const g = scene.group();
                g.onClick(() => log('group clicked'));
                const inner = scene.box({ parent: g, size: [0.1, 0.1, 0.1] });
                const own = scene.box({ size: [0.1, 0.1, 0.1] });
                own.onClick(n => log('own clicked', n.id === own.id));
                own.onHover(inside => log('hover', inside));
                const b = kit.button('Go', () => log('button'));
            ");
            TestNode inner = w.host.OfKind("box").First(n => n.Parent != 0);
            Check(!inner.props.Bool("interactive") && w.host.Nodes[inner.Parent].props.Bool("interactive"), "the group is the hit target");
            Check(w.runtime.DispatchEvent(inner.id, "click"), "a click on a child bubbles to the group");
            TestNode own = w.host.OfKind("box").First(n => n.Parent == 0);
            w.runtime.DispatchEvent(own.id, "click");
            w.runtime.DispatchEvent(own.id, "enter");
            w.runtime.DispatchEvent(own.id, "exit");
            w.Click(w.host.Text("Go"));
            Check(string.Join("|", w.log) == "info: group clicked|info: own clicked true|info: hover true|info: hover false|info: button", "events: " + string.Join("|", w.log));
            Check(!w.runtime.DispatchEvent(99999, "click"), "unknown node");
        }

        static void RuntimeLifecycle()
        {
            World w = MakeWorld();
            const string counter = @"
                state.count = (state.count || 0) + 1;
                scene.text({ text: 'count ' + state.count });
                scene.box({ size: [0.1, 0.1, 0.1] });
            ";
            Experience first = w.Start("counter", counter);
            Experience other = w.Start("other", "scene.sphere({ radius: 0.1 });");
            Check(w.host.Nodes.Count == 3 && w.runtime.Experiences.Count == 2, "two experiences");
            Check(w.runtime.OwnerOf(w.host.OfKind("sphere").Single().id) == other, "node ownership");

            Experience second = w.Start("counter", counter);
            Check(!first.IsRunning && second.IsRunning && w.runtime.Experiences.Count == 2, "same id replaces");
            Check(w.host.Text("count 2") != null && w.host.Text("count 1") == null, "state carried over, old nodes gone");

            Check(w.runtime.Stop("counter") && w.host.Nodes.Count == 1, "stop removes its nodes only");
            Experience third = w.Start("counter", counter);
            Check(w.host.Text("count 1") != null, "an explicit stop forgets state");

            w.Start("self", "scene.box({ size: [1, 1, 1] }); scene.after(0.1, finish);");
            w.Advance(0.3f);
            Check(w.runtime.Get("self") == null, "finish() takes the experience out");
            w.runtime.StopAll();
            Check(w.host.Nodes.Count == 0 && w.runtime.Experiences.Count == 0 && w.host.Created == w.host.Destroyed, "everything cleaned up");
        }

        static void RuntimeFailures()
        {
            World w = MakeWorld();
            Experience syntax = w.Start("syntax", "const x = ;");
            Check(syntax.Error != null && !syntax.IsRunning && w.runtime.Experiences.Count == 0, "syntax error reported");

            Experience thrown = w.Start("thrown", "scene.box({ size: [1, 1, 1] }); const p = space.prop('nothing'); p.position;");
            Check(thrown.Error != null && thrown.Error.Contains("position"), "runtime error names the problem: " + thrown.Error);
            Check(w.host.Nodes.Count == 0, "a failed program leaves nothing behind");

            Experience later = w.Start("later", "await scene.wait(0.1); undefinedFunction();");
            Check(later.IsRunning, "starts fine");
            w.Advance(0.3f);
            Check(!later.IsRunning && later.Error.Contains("undefinedFunction"), "error after an await is caught: " + later.Error);

            Experience loop = w.Start("loop", "while (true) {}");
            Check(loop.Error != null && loop.Error.Contains("too long"), "runaway loop stopped: " + loop.Error);

            Experience frameLoop = w.Start("frameLoop", "scene.onFrame(() => { while (true) {} });");
            w.Advance(0.1f);
            Check(!frameLoop.IsRunning && frameLoop.Error.Contains("too long"), "runaway frame callback stopped");

            Experience deep = w.Start("deep", "function f() { return f(); } f();");
            Check(deep.Error != null, "runaway recursion stopped");
            Check(w.failed.Count == 6 && w.runtime.Experiences.Count == 0, "all six reported as failed: " + w.failed.Count);

            // The runtime is still healthy afterwards.
            Check(w.Start("ok", "scene.box({ size: [1, 1, 1] });").IsRunning, "next program runs");
        }

        static void RuntimeCallbackErrors()
        {
            World w = MakeWorld();
            Experience e = w.Start("callbacks", @"
                let n = 0;
                scene.after(0.05, () => { throw new Error('one bad timer'); });
                scene.onFrame(() => n++);
                scene.after(0.3, () => log('still running', n > 3));
            ");
            w.Advance(0.5f);
            Check(e.IsRunning, "one failing callback does not stop the program");
            Check(w.log.Any(l => l.StartsWith("error: timer") && l.Contains("one bad timer")), "the error is logged");
            Check(w.log.Contains("info: still running true"), "other callbacks kept going");

            Experience noisy = w.Start("noisy", "scene.onFrame(() => { throw new Error('every frame'); });");
            w.Advance(3f);
            Check(!noisy.IsRunning && noisy.Error.Contains("Stopped after"), "a program that fails every frame is stopped: " + noisy.Error);
        }

        static void RuntimeSandbox()
        {
            World w = MakeWorld();
            Experience e = w.Start("sandbox", @"
                log(typeof System, typeof importNamespace, typeof require, typeof process, typeof fetch, typeof XMLHttpRequest, typeof window);
                log(typeof __host.create, Object.keys(__host).sort().join(','));
            ");
            Check(e.Error == null, e.Error);
            Check(w.log[0] == "info: undefined undefined undefined undefined undefined undefined undefined", "no way out: " + w.log[0]);
            Check(w.log[1] == "info: function create,destroy,fail,find,finish,floorY,hasView,log,measure,props,raycast,surfaces,update,user,viewRay,xf",
                "the host surface is exactly this: " + w.log[1]);
        }

        static void RuntimeNodeBudget()
        {
            World w = MakeWorld();
            w.runtime.Limits.MaxNodes = 50;
            Experience e = w.Start("many", "for (let i = 0; i < 60; i++) scene.box({ size: [0.1, 0.1, 0.1] });");
            Check(e.Error != null && e.Error.Contains("too many nodes") && w.host.Nodes.Count == 0, "node budget enforced: " + e.Error);
        }

        // ---- the film's six requests, as programs --------------------------------------------------

        static void FilmPrograms()
        {
            World w = MakeWorld();
            foreach (string id in FilmIds)
            {
                Experience e = w.runtime.Start(id, id, "film", Example(id), w.UserView());
                Check(e.Error == null && e.IsRunning, id + " failed: " + e.Error);
                Check(e.NodeCount >= 5, id + " built only " + e.NodeCount + " nodes");
            }
            w.Advance(3f);
            Check(w.failed.Count == 0, "failed while running: " + string.Join("; ", w.failed.Select(f => f.Id + ": " + f.Error)));
            Check(!w.log.Any(l => l.StartsWith("error")), "errors logged: " + string.Join(" | ", w.log.Where(l => l.StartsWith("error"))));
            Check(w.runtime.Experiences.Count == 6, "all six still running");

            // Everything is inside the room and nothing is left at a degenerate transform.
            foreach (TestNode n in w.host.Nodes.Values)
            {
                Vec3 p = w.host.World(n);
                Check(Math.Abs(p.x) < SimRoom.Width * 0.5f && p.y > -0.05f && p.y < SimRoom.Height && p.z < SimRoom.BackWallZ + 0.05f, n.kind + " " + n.id + " is outside the room at " + p);
                Check(!float.IsNaN(n.rotation.w) && !float.IsNaN(p.x), "NaN transform on " + n.kind);
            }
            foreach (string text in new[] { "Stop and lock out", "Inlet", "Outlet", "42 m³/h", "Last week's faults", "Output this month  ·  ×100 m³", "Parts check", "Coupling insert", "Sara", "+3" })
                Check(w.host.Text(text) != null, "missing text: " + text);
            Check(w.host.OfKind("tube").Any(t => t.props.Has("flow")), "flow tube");
            Check(w.host.OfKind("torus").Count() == 0, "no turn arrow on step 1");
        }

        static void FilmWalkthrough()
        {
            World w = MakeWorld();
            Experience e = w.runtime.Start("walkthrough", "Walkthrough", "film", Example("walkthrough"), null);
            w.Advance(0.5f);
            Check(w.host.Text("Stop and lock out") != null && w.host.Text("1 of 3") != null, "opens on step 1");

            Check(w.runtime.HandleVoice("next"), "\"next\" is handled on the device");
            w.Advance(0.5f);
            Check(w.host.Text("Close this valve") != null && w.host.Text("Stop and lock out") == null, "step 2 replaces step 1");
            TestNode torus = w.host.OfKind("torus").Single();
            RoomEntity valve = w.host.Room.Get("gate-valve");
            Near(w.host.World(torus), valve.ActionPoint, "turn arrow is centred on the handwheel", 1e-2f);
            float before = w.host.Nodes[torus.Parent].rotation.YawDegrees;
            w.Advance(0.5f);
            float turned = w.host.Nodes[torus.Parent].rotation.YawDegrees - before;
            Check(turned > 20f && turned < 60f, "the arrow turns clockwise seen from above: " + turned);

            Check(!w.runtime.HandleVoice("has it failed before"), "a full sentence is not a command");
            Check(w.runtime.HandleVoice("go back"), "back");
            w.Advance(0.2f);
            Check(w.host.Text("Stop and lock out") != null, "back to step 1");

            // Tap pin 3, then the card's buttons.
            w.Click(w.host.Text("3"));
            w.Advance(0.2f);
            Check(w.host.Text("Check the shaft is still") != null && w.host.Text("Done") != null, "pin 3 jumps to the last step");
            w.Click(w.host.Text("Back"));
            w.Advance(0.2f);
            Check(w.host.Text("Close this valve") != null, "Back button");

            // Replacing the experience (as a follow-up request would) resumes on the same step.
            w.runtime.Start("walkthrough", "Walkthrough", "film", Example("walkthrough"), null);
            w.Advance(0.2f);
            Check(w.host.Text("Close this valve") != null, "state survives replacement");

            w.Click(w.host.Text("Next"));
            w.Advance(0.2f);
            w.Click(w.host.Text("Done"));
            w.Advance(0.2f);
            Check(w.runtime.Experiences.Count == 0 && w.host.Nodes.Count == 0, "Done takes the walkthrough out of the room");
        }

        static void FilmChecklist()
        {
            World w = MakeWorld();
            w.runtime.Start("checklist", "Checklist", "film", Example("checklist"), null);
            w.Advance(0.5f);
            TestNode label = w.host.Text("Coupling insert");
            Check(label.props.Str("color") == "#17161D", "not done: ink");
            TestNode row = w.host.Nodes[label.Parent];
            TestNode hit = w.host.Nodes.Values.Single(n => n.Parent == row.id && n.props.Bool("interactive"));
            w.runtime.DispatchEvent(hit.id, "click");
            Check(label.props.Str("color") == "#7B7A8C", "ticked: muted");
            w.runtime.Start("checklist", "Checklist", "film", Example("checklist"), null);
            Check(w.host.Text("Coupling insert").props.Str("color") == "#7B7A8C", "the tick is remembered across replacement");
            RoomEntity bench = w.host.Room.Get("workbench");
            TestNode root = w.host.Root(w.host.Text("Parts check"));
            Check(root.position.y > bench.position.y + bench.size.y * 0.5f + 0.2f, "floats above the bench");
        }

        static void FilmChart()
        {
            World w = MakeWorld();
            w.runtime.Start("dashboard", "Dashboard", "film", Example("dashboard"), null);
            w.Advance(2f);
            TestNode root = w.host.Root(w.host.Text("Output this month  ·  ×100 m³"));
            RoomEntity bench = w.host.Room.Get("workbench"), skid = w.host.Room.Get("baseplate");
            Near(root.position.y, 0f, "stands on the floor");
            Check(Vec3.Distance(root.position.Flat, bench.position.Flat) < 1.6f, "near the bench");
            foreach (RoomEntity blocker in new[] { bench, skid })
            {
                Vec3 local = blocker.rotation.Inverse * (root.position - blocker.position);
                Check(Math.Abs(local.x) > blocker.size.x * 0.5f || Math.Abs(local.z) > blocker.size.z * 0.5f, "chart centre is inside " + blocker.id);
            }
            var bars = w.host.OfKind("box").Where(b => b.Parent != root.id && w.host.Root(b) == root).ToList();
            Check(bars.Count == 10, "ten bars: " + bars.Count);
            Check(bars.All(b => Math.Abs(w.host.Nodes[b.Parent].scale.y - 1f) < 0.02f), "bars have finished growing");
            Check(w.host.OfKind("tube").Any(t => w.host.Root(t) == root), "trend line arrived");
        }

        static void FilmOtherRoom()
        {
            // A different room: a real table to adopt, the station turned and moved. Nothing in the programs
            // knows about this layout.
            var w = new World { site = LoadSite() };
            RoomModel raw = SimRoom.Create(withRealTable: true);
            w.station = new Pose3(new Vec3(1.2f, 0, -1.4f), Quat.Yaw(-70));
            w.host.Room = SiteBinder.Bind(w.site, raw, w.station);
            w.host.UserPosition = new Vec3(-1.5f, 1.7f, -3.2f);
            w.host.UserForward = new Vec3(0.8f, 0, 0.6f);
            w.runtime = new ExperienceRuntime(w.host, Library(), LoadFonts());
            w.runtime.Logged += (e, level, message) => w.log.Add(level + ": " + message);
            foreach (string id in FilmIds)
            {
                Experience e = w.runtime.Start(id, id, "film", Example(id), null);
                Check(e.Error == null, id + " failed in the other room: " + e.Error);
            }
            w.Advance(2f);
            Check(!w.log.Any(l => l.StartsWith("error")), "errors: " + string.Join(" | ", w.log.Where(l => l.StartsWith("error"))));
            RoomEntity bench = w.host.Room.Get("workbench");
            Check(!bench.isVirtual, "bench is the real table");
            TestNode list = w.host.Root(w.host.Text("Parts check"));
            Check(Vec3.Distance(list.position.Flat, bench.position.Flat) < 0.05f, "checklist follows the real table");
            TestNode pin = w.host.Root(w.host.Text("2"));
            Near(pin.position, w.host.Room.Get("gate-valve").TopCenter, "pins follow the moved machine", 1e-2f);
        }

        // ---- building ------------------------------------------------------------------------

        static DemoBuilder Demo() => new DemoBuilder(Example);

        static BuildRequest Request(World w, string prompt)
        {
            var request = new BuildRequest { prompt = prompt, floorY = 0 };
            request.surfaces.AddRange(w.host.Surfaces);
            request.props.AddRange(w.host.Props);
            foreach (Experience e in w.runtime.Experiences)
                request.running.Add(new RunningExperience { id = e.Id, title = e.Title, prompt = e.Prompt, code = e.Code });
            return request;
        }

        static void DemoRouting()
        {
            World w = MakeWorld();
            DemoBuilder demo = Demo();
            for (int i = 0; i < FilmPrompts.Length; i++)
            {
                BuildResult r = demo.Build(Request(w, FilmPrompts[i]));
                Check(r.id == FilmIds[i] && r.HasProgram && r.reply.Length > 0 && r.builder == "demo", FilmPrompts[i] + " -> " + r.id);
            }
            Check(!demo.Build(Request(w, "sing me a song")).HasProgram, "declines what it has no recording for");

            w.runtime.Start("dashboard", "Dashboard", "x", Example("dashboard"), null);
            w.runtime.Start("checklist", "Checklist", "x", Example("checklist"), null);
            BuildResult remove = demo.Build(Request(w, "remove the chart"));
            Check(remove.remove.SequenceEqual(new[] { "dashboard" }) && !remove.HasProgram, "remove by what it is");
            Check(demo.Build(Request(w, "clear everything")).clear, "clear");

            World empty = MakeWorld(standIn: false);
            BuildResult none = demo.Build(Request(empty, FilmPrompts[0]));
            Check(!none.HasProgram && none.reply.Contains("stand-in"), "without the stand-in pump the recording is not played: " + none.reply);
        }

        static void RequestShape()
        {
            World w = MakeWorld();
            BuildRequest request = Request(w, "what is this?");
            request.view = w.UserView();
            request.imageJpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
            request.SampleDepth(w.host);
            request.failure = "ReferenceError: x is not defined";
            request.failedCode = "x();";
            var json = Json.ParseObject(Json.Write(request.ToJson()));

            Check(json.Str("prompt") == "what is this?" && json.Str("image") == "/9j/2Q==", "prompt and base64 image");
            var depth = json.Obj("view").Obj("depth");
            Check(depth.Int("columns") == 8 && depth.Int("rows") == 6 && depth.Arr("metres").Count == 48, "depth grid");
            var metres = depth.Arr("metres").Select(m => Json.ToDouble(m)).ToList();
            Check(metres.Count(m => m > 0) > 30 && metres.Max() < 15, "most rays hit something: " + metres.Count(m => m > 0));
            Check(metres.Skip(40).All(m => m > 0 && m < metres.Take(8).Where(x => x > 0).DefaultIfEmpty(99).Max()), "the bottom row (floor near the feet) is nearer than the top row");
            Check(json.Arr("surfaces").Count == 4, "surfaces");
            var props = json.Arr("props").Cast<Dictionary<string, object>>().ToList();
            var valve = props.Single(p => p.Str("id") == "gate-valve");
            Check(valve.Str("name") == "Gate valve" && valve.Has("about") && valve.Str("partOf") == "pump-p101", "prop fields");
            var uv = valve.Arr("inView");
            Check(uv != null && Json.ToDouble(uv[0]) > 0.5 && Json.ToDouble(uv[1]) > 0.3, "where the hologram appears in the image");
            Check(json.Obj("repair").Str("error").Contains("ReferenceError"), "repair context");
            Check(json.Obj("user").Has("position") && json.Has("floorY") && json.Arr("running").Count == 0, "user, floor, running");
        }

        sealed class FakeHttp : IHttpTransport
        {
            public Func<string, string, HttpResult> handler;
            public readonly List<string> urls = new List<string>(), bodies = new List<string>();
            public Dictionary<string, string> lastHeaders;

            public Task<HttpResult> PostJsonAsync(string url, string json, Dictionary<string, string> headers, int timeoutSeconds)
            {
                urls.Add(url);
                bodies.Add(json);
                lastHeaders = headers;
                return Task.FromResult(handler(url, json));
            }
        }

        static HttpResult Ok(BuildResult r) => new HttpResult { ok = true, status = 200, body = Json.Write(r.ToJson()) };

        static void RemotePaths()
        {
            World w = MakeWorld();
            var http = new FakeHttp();
            var remote = new RemoteBuilder(http, () => "http://planner.local:8787/", () => "secret");
            http.handler = (url, body) => Ok(new BuildResult { reply = "Done.", title = "Labels", id = "labels", code = "scene.box({size:[1,1,1]});", builder = "claude-opus-5-5" });
            BuildResult r = remote.BuildAsync(Request(w, "label it")).Result;
            Check(http.urls[0] == "http://planner.local:8787/v1/build" && http.lastHeaders["Authorization"] == "Bearer secret", "url and auth");
            Check(r.error == null && r.builder == "claude-opus-5-5" && r.HasProgram && r.latencyMs >= 0, "parsed");

            http.handler = (url, body) => new HttpResult { ok = false, status = 502, error = "Bad gateway" };
            Check(remote.BuildAsync(Request(w, "x")).Result.error.Contains("Bad gateway"), "http failure surfaces");
            http.handler = (url, body) => new HttpResult { ok = true, status = 200, body = "<html>" };
            Check(remote.BuildAsync(Request(w, "x")).Result.error.Contains("unreadable"), "garbage surfaces");
            http.handler = (url, body) => { throw new InvalidOperationException("boom"); };
            Check(remote.BuildAsync(Request(w, "x")).Result.error.Contains("boom"), "exceptions surface");

            http.handler = (url, body) => new HttpResult { ok = true, status = 200, body = "{\"points\":[{\"u\":0.4,\"v\":0.6,\"label\":\"valve\"},{\"u\":0.1,\"v\":0.2}]}" };
            List<LocatedPoint> points = remote.LocateAsync("the valve", new byte[] { 1, 2, 3 }).Result;
            Check(http.urls.Last().EndsWith("/v1/locate") && points.Count == 2 && points[0].label == "valve" && points[1].label == "the valve", "locate");
            Near(points[0].u, 0.4f, "u");
            Check(remote.LocateAsync("x", null).Result.Count == 0, "no image, no call");
        }

        static Director MakeDirector(World w, FakeHttp http, string url = "http://planner.local")
        {
            return new Director(w.runtime, w.host, Demo(), new RemoteBuilder(http, () => url));
        }

        static void DirectorVoice()
        {
            World w = MakeWorld();
            var http = new FakeHttp { handler = (u, b) => { throw new Exception("must not be called"); } };
            Director director = MakeDirector(w, http);
            w.runtime.Start("walkthrough", "Walkthrough", "x", Example("walkthrough"), null);
            BuildResult r = director.SubmitAsync("next", null).Result;
            Check(http.urls.Count == 0 && r.builder == "experience" && !r.HasProgram, "handled by the running program");
            w.Advance(0.2f);
            Check(w.host.Text("Close this valve") != null, "and it moved on");
        }

        static void DirectorRemote()
        {
            World w = MakeWorld();
            var http = new FakeHttp();
            Director director = MakeDirector(w, http);
            ViewFrame view = w.UserView();
            float u, v;
            view.Project(w.host.Room.Get("motor").position, out u, out v);
            string uv = u.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + ", " + v.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
            // What a model would write after looking at the frame: point at the thing it saw and build on it.
            http.handler = (url, body) => Ok(new BuildResult
            {
                reply = "That is the drive motor.", title = "Note", id = "note-motor", builder = "claude-opus-5-5",
                code = "const hit = space.view.point(" + uv + "); const at = vec.add(hit.position, [0, 0.3, 0]); kit.label('Drive motor', hit); log('distance', hit.distance > 1);",
            });
            BuildResult r = director.SubmitAsync("what is this?", request => { request.view = view; request.imageJpeg = new byte[] { 1, 2, 3 }; }).Result;
            Check(r.error == null && r.title == "Note", "built: " + r.error);
            var sent = Json.ParseObject(http.bodies[0]);
            Check(sent.Has("image") && sent.Obj("view").Obj("depth").Arr("metres").Count == 48 && sent.Arr("props").Count > 5, "the request carried the live view");
            Experience e = w.runtime.Get("note-motor");
            Check(e != null && e.IsRunning && e.View == view && e.Prompt == "what is this?", "running with the request's view");
            TestNode label = w.host.Root(w.host.Text("Drive motor"));
            Check(Vec3.Distance(label.position, w.host.Room.Get("motor").position) < 0.35f, "anchored where the model pointed: " + label.position);

            http.handler = (url, body) => Ok(new BuildResult { reply = "Removed.", remove = { "note-motor" } });
            director.SubmitAsync("get rid of that", null).Wait();
            Check(w.runtime.Experiences.Count == 0, "the planner can remove experiences");
            Check(Json.ParseObject(http.bodies[1]).Arr("running").Count == 1, "it was told what was running");
        }

        static void DirectorRepair()
        {
            World w = MakeWorld();
            var http = new FakeHttp();
            Director director = MakeDirector(w, http);
            int calls = 0;
            http.handler = (url, body) =>
            {
                calls++;
                var sent = Json.ParseObject(body);
                if (calls == 1) return Ok(new BuildResult { reply = "Here.", title = "Marker", id = "marker", code = "const p = space.prop('flux capacitor'); kit.pin(1, p.position);" });
                Check(sent.Obj("repair").Str("error").Contains("position") && sent.Obj("repair").Str("code").Contains("flux capacitor"), "the failure and the code go back");
                return Ok(new BuildResult { reply = "Here.", title = "Marker", id = "marker", code = "const p = space.prop('motor'); kit.pin(1, space.topOf(p));" });
            };
            BuildResult r = director.SubmitAsync("mark the motor", null).Result;
            Check(calls == 2 && r.error == null && w.runtime.Get("marker") != null && w.host.Text("1") != null, "repaired on the second try: " + r.error);

            calls = 0;
            http.handler = (url, body) => { calls++; return Ok(new BuildResult { reply = "Here.", title = "Broken", id = "broken", code = "nope(" }); };
            r = director.SubmitAsync("break", null).Result;
            Check(calls == 2 && r.error != null && r.title == "" && r.reply.Contains("did not run") && w.runtime.Get("broken") == null, "gives up after one repair and says so: " + r.reply);
        }

        static void DirectorFallback()
        {
            World w = MakeWorld();
            var http = new FakeHttp { handler = (u, b) => new HttpResult { ok = false, status = 500, error = "down" } };
            Director director = MakeDirector(w, http);
            BuildResult r = director.SubmitAsync(FilmPrompts[1], null).Result;
            Check(http.urls.Count == 1 && r.title == "Live flow" && r.error == "Planner service error: down" && r.builder.StartsWith("demo"), "recorded demo with the failure noted");
            Check(w.runtime.Get("flow") != null, "and it is running");

            Director offline = MakeDirector(w, http, url: "");
            r = offline.SubmitAsync(FilmPrompts[4], null).Result;
            Check(http.urls.Count == 1 && r.error == null && r.builder == "demo" && !offline.RemoteActive, "no service configured: demo, no error");
        }

        // ---- session -------------------------------------------------------------------------

        static void SessionRoundTrip()
        {
            using (var host = new SessionLink { LocalName = "Headset", LocalDevice = "quest" })
            using (var client = new SessionLink { LocalName = "Sara", LocalRole = "Reliability engineer", LocalDevice = "ios" })
            {
                Peer joined = null, moved = null, left = null;
                string prompt = null, reply = null, config = null;
                host.PeerJoined += p => joined = p;
                host.PeerMoved += p => moved = p;
                host.PeerLeft += p => left = p;
                host.PromptReceived += (text, p) => prompt = text + " from " + p.name;
                host.ConfigReceived += (url, token, useAi, p) => config = url + "|" + token + "|" + useAi;
                client.ReplyReceived += (text, chip, planner) => reply = text + "|" + chip;

                host.Host(0);
                client.Join("127.0.0.1", host.Port);
                WaitFor(() => { host.Pump(); client.Pump(); return joined != null && client.Peers.Count == 1; }, "handshake");
                Check(joined.name == "Sara" && joined.device == "ios" && client.Peers[0].name == "Headset", "names exchanged");

                client.SendPrompt("show me how the water flows");
                client.SendPresence(new Vec3(1, 1.6f, -2), Vec3.Forward);
                client.SendConfig("http://10.0.0.5:8787", "tok", true);
                WaitFor(() => { host.Pump(); return prompt != null && moved != null && config != null; }, "prompt, presence, settings");
                Check(prompt == "show me how the water flows from Sara" && config == "http://10.0.0.5:8787|tok|True", "payloads");
                Near(moved.position, new Vec3(1, 1.6f, -2), "presence position");

                host.SendReply("In at the inlet.", "Live flow", "demo");
                WaitFor(() => { client.Pump(); return reply != null; }, "reply");
                Check(reply == "In at the inlet.|Live flow", "reply: " + reply);

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

        // ---- export ----------------------------------------------------------------------------

        /// <summary>
        /// Writes everything the browser lab needs to run the same programs with the same library: the runtime
        /// library, the film programs, font metrics and the stand-in scene bound into the simulated room.
        /// </summary>
        static int Export(string dir)
        {
            Directory.CreateDirectory(dir);
            World w = MakeWorld();

            var entities = new List<object>();
            foreach (RoomEntity e in w.host.Room.entities)
            {
                var d = e.ToJson();
                d["origin"] = Json.FromVec3(e.ShapeOrigin);
                if (e.hasActionPoint) d["actionPoint"] = Json.FromVec3(e.ActionPoint);
                if (e.shapes != null)
                {
                    var shapes = new List<object>();
                    foreach (ShapeDef s in e.shapes) shapes.Add(s.ToJson());
                    d["shapes"] = shapes;
                }
                entities.Add(d);
            }

            var programs = new List<object>();
            for (int i = 0; i < FilmIds.Length; i++)
            {
                DemoBuilder.Entry entry = DemoBuilder.Entries.Single(x => x.id == FilmIds[i]);
                programs.Add(new Dictionary<string, object>
                {
                    { "id", entry.id }, { "title", entry.title }, { "prompt", FilmPrompts[i] }, { "reply", entry.reply },
                    { "words", new List<object>(entry.words) }, { "code", Example(entry.id) },
                });
            }

            var fonts = new Dictionary<string, object>();
            foreach (string weight in new[] { "medium", "semibold" })
                fonts[weight] = Json.Parse(File.ReadAllText(Path.Combine(Resources(), "Fonts", "inter_" + weight + "_metrics.json")));

            var bundle = new Dictionary<string, object>
            {
                { "room", new Dictionary<string, object>
                    {
                        { "floorY", 0 },
                        { "user", new Dictionary<string, object> { { "position", Json.FromVec3(w.host.UserPosition) }, { "forward", Json.FromVec3(w.host.UserForward) } } },
                        { "entities", entities },
                    } },
                { "programs", programs },
                { "fonts", fonts },
            };

            File.WriteAllText(Path.Combine(dir, "spatail-data.js"),
                "// Generated by `dotnet run -- export` in tests/Spatail.Core.Tests. Do not edit by hand.\n" +
                "window.SPATAIL_DATA = " + Json.Write(bundle) + ";\n");
            // The library is shipped to the browser byte for byte: the lab runs the file the devices run.
            File.WriteAllText(Path.Combine(dir, "spatail.js"), Library());
            Console.WriteLine("Wrote spatail-data.js (" + entities.Count + " entities, " + programs.Count + " programs) and spatail.js to " + dir);
            return 0;
        }
    }
}
