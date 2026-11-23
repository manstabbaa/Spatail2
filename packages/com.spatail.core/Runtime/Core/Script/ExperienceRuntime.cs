using System;
using System.Collections.Generic;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Jint.Runtime.Interop;

namespace Spatail.Core
{
    /// <summary>One program running in the room: the code the planner wrote for one request, and what it has built.</summary>
    public sealed class Experience
    {
        public string Id { get; internal set; }
        public string Title { get; internal set; }
        /// <summary>What the user asked for.</summary>
        public string Prompt { get; internal set; }
        public string Code { get; internal set; }
        public ViewFrame View { get; internal set; }
        public bool IsRunning { get; internal set; }
        /// <summary>Set when the program failed to start or threw while running. The planner can be shown this to repair it.</summary>
        public string Error { get; internal set; }
        public int NodeCount => Nodes.Count;
        /// <summary>The program's own log lines and any errors from its callbacks, newest last.</summary>
        public readonly List<string> Log = new List<string>();

        internal Engine Engine;
        internal readonly HashSet<int> Nodes = new HashSet<int>();
        internal bool FinishRequested;
        internal int ErrorCount;
        internal int Budget;
    }

    /// <summary>Guard rails for generated code. Programs get a generous but finite budget per call.</summary>
    public sealed class ScriptLimits
    {
        /// <summary>Statements a program may execute while starting up.</summary>
        public int StartStatements = 3_000_000;
        /// <summary>Statements per frame, event or callback afterwards.</summary>
        public int CallStatements = 400_000;
        public int MaxRecursion = 200;
        /// <summary>Nodes one experience may create.</summary>
        public int MaxNodes = 4000;
        /// <summary>Callback errors after which an experience is stopped rather than failing every frame.</summary>
        public int MaxErrors = 30;
    }

    /// <summary>
    /// Runs the programs the planner writes. Each experience gets its own JavaScript engine with the Spatail
    /// library loaded and nothing else: no file, network or reflection access, only the host functions below.
    /// This is the piece that lets a request become whatever software it needs to be, instead of a choice from a
    /// fixed list of content types.
    /// </summary>
    public sealed class ExperienceRuntime
    {
        readonly IScriptHost _host;
        readonly string _library;
        readonly FontSet _fonts;
        readonly List<Experience> _experiences = new List<Experience>();
        readonly Dictionary<int, Experience> _nodeOwner = new Dictionary<int, Experience>();
        readonly Dictionary<string, string> _savedState = new Dictionary<string, string>();
        int _nextNode = 1;
        double _time;

        public ScriptLimits Limits = new ScriptLimits();

        public IReadOnlyList<Experience> Experiences => _experiences;

        public event Action<Experience> Started;
        public event Action<Experience> Stopped;
        /// <summary>A program could not start, or was stopped after repeated errors. <see cref="Experience.Error"/> says why.</summary>
        public event Action<Experience> Failed;
        /// <summary>(experience, level, message) for everything a program logs.</summary>
        public event Action<Experience, string, string> Logged;

        /// <param name="librarySource">The Spatail runtime library (spatail.js).</param>
        public ExperienceRuntime(IScriptHost host, string librarySource, FontSet fonts)
        {
            _host = host;
            _library = librarySource;
            _fonts = fonts ?? new FontSet();
        }

        public Experience Get(string id)
        {
            foreach (Experience e in _experiences) if (e.Id == id) return e;
            return null;
        }

        /// <summary>
        /// Starts a program. If an experience with the same id is running it is replaced, and whatever it kept in
        /// <c>state</c> is handed to the new version.
        /// </summary>
        public Experience Start(string id, string title, string prompt, string code, ViewFrame view)
        {
            if (string.IsNullOrEmpty(id)) id = "experience-" + (_experiences.Count + 1);
            Stop(id, keepState: true);

            var exp = new Experience { Id = id, Title = string.IsNullOrEmpty(title) ? "Experience" : title, Prompt = prompt, Code = code ?? "", View = view, IsRunning = true };
            _experiences.Add(exp);

            try
            {
                exp.Budget = Limits.StartStatements;
                exp.Engine = CreateEngine(exp);
                string saved;
                if (_savedState.TryGetValue(id, out saved)) exp.Engine.SetValue("__initialState", saved);
                exp.Engine.Execute(_library);
                // The program body runs inside an async function so it can await, and so a rejected promise is
                // reported instead of vanishing.
                exp.Engine.Execute(
                    "(async function () {\n" + exp.Code + "\n})().catch(function (e) { __host.fail(__describe(e)); });");
                Drain(exp);
            }
            catch (Exception ex)
            {
                Fail(exp, Describe(ex));
                return exp;
            }

            // The program's own top-level promise may have rejected during start-up.
            if (exp.Error != null)
            {
                Fail(exp, exp.Error);
                return exp;
            }
            // Start-up is allowed more work than a frame.
            exp.Budget = Limits.CallStatements;
            if (Started != null) Started(exp);
            return exp;
        }

        public bool Stop(string id) => Stop(id, keepState: false);

        bool Stop(string id, bool keepState)
        {
            Experience exp = Get(id);
            if (exp == null) return false;
            if (keepState && exp.Engine != null && exp.Error == null)
            {
                try { _savedState[id] = TypeConverter.ToString(exp.Engine.Invoke("__saveState")); }
                catch (Exception) { _savedState.Remove(id); }
            }
            else if (!keepState) _savedState.Remove(id);
            Teardown(exp);
            _experiences.Remove(exp);
            if (Stopped != null) Stopped(exp);
            return true;
        }

        public void StopAll()
        {
            foreach (Experience exp in new List<Experience>(_experiences)) Stop(exp.Id);
        }

        /// <summary>Advances every running program by one frame.</summary>
        public void Tick(float deltaTime)
        {
            _time += deltaTime;
            for (int i = _experiences.Count - 1; i >= 0; i--)
            {
                Experience exp = _experiences[i];
                if (!exp.IsRunning) continue;
                Call(exp, "__tick", (double)deltaTime, _time);
                if (!exp.IsRunning) continue;
                if (exp.Error != null) Fail(exp, exp.Error);
                else if (exp.FinishRequested) Stop(exp.Id);
            }
        }

        /// <summary>Delivers a pointer event ("click", "enter", "exit") on a node to the program that owns it.</summary>
        public bool DispatchEvent(int nodeId, string type)
        {
            Experience exp;
            if (!_nodeOwner.TryGetValue(nodeId, out exp) || !exp.IsRunning) return false;
            JsValue result = Call(exp, "__event", (double)nodeId, type);
            return result != null && result.IsBoolean() && result.AsBoolean();
        }

        /// <summary>
        /// Offers a short utterance ("next", "back") to the running programs. Returns true if one of them handled
        /// it, in which case the request does not need to go to the planner at all.
        /// </summary>
        public bool HandleVoice(string text)
        {
            for (int i = _experiences.Count - 1; i >= 0; i--)
            {
                Experience exp = _experiences[i];
                if (!exp.IsRunning) continue;
                JsValue result = Call(exp, "__voice", text ?? "");
                if (result != null && result.IsBoolean() && result.AsBoolean()) return true;
            }
            return false;
        }

        public Experience OwnerOf(int nodeId)
        {
            Experience exp;
            return _nodeOwner.TryGetValue(nodeId, out exp) ? exp : null;
        }

        // ---- engine plumbing ---------------------------------------------------------------

        JsValue Call(Experience exp, string function, params object[] arguments)
        {
            try
            {
                var args = new JsValue[arguments.Length];
                for (int i = 0; i < arguments.Length; i++)
                    args[i] = arguments[i] is string ? (JsValue)(string)arguments[i] : (JsValue)(double)arguments[i];
                JsValue result = exp.Engine.Invoke(function, args);
                Drain(exp);
                return result;
            }
            catch (Exception ex)
            {
                // The library catches errors inside callbacks; reaching here means the budget ran out or the
                // engine itself gave up, and continuing would repeat it every frame.
                Fail(exp, Describe(ex));
                return null;
            }
        }

        /// <summary>Runs promise continuations queued by the last call (await, then).</summary>
        static void Drain(Experience exp)
        {
            exp.Engine.Advanced.ProcessTasks();
        }

        void Fail(Experience exp, string message)
        {
            if (exp.Error != message) AddLog(exp, "error", message);
            exp.Error = message;
            Teardown(exp);
            _experiences.Remove(exp);
            _savedState.Remove(exp.Id);
            if (Failed != null) Failed(exp);
        }

        void Teardown(Experience exp)
        {
            exp.IsRunning = false;
            foreach (int id in exp.Nodes)
            {
                _nodeOwner.Remove(id);
                try { _host.DestroyNode(id); } catch (Exception) { /* a host hiccup must not block teardown */ }
            }
            exp.Nodes.Clear();
            exp.Engine = null;
        }

        void AddLog(Experience exp, string level, string message)
        {
            exp.Log.Add((level == "error" ? "! " : "") + message);
            if (exp.Log.Count > 200) exp.Log.RemoveRange(0, exp.Log.Count - 200);
            if (Logged != null) Logged(exp, level, message);
        }

        static string Describe(Exception ex)
        {
            if (ex is StatementsCountOverflowException) return "The program ran too long without finishing (statement budget exceeded).";
            if (ex is RecursionDepthOverflowException) return "The program recursed too deeply.";
            var js = ex as JavaScriptException;
            if (js != null)
            {
                string stack = js.JavaScriptStackTrace;
                return js.Message + (string.IsNullOrEmpty(stack) ? "" : "\n" + stack);
            }
            return ex.GetType().Name + ": " + ex.Message;
        }

        Engine CreateEngine(Experience exp)
        {
            var engine = new Engine(options =>
            {
                options.LimitRecursion(Limits.MaxRecursion);
                options.Strict(false);
                options.Constraint(new StatementBudget(exp));
            });

            var host = new JsObject(engine);
            Action<string, Func<JsValue[], JsValue>> define = (name, fn) =>
                host.Set(name, new ClrFunction(engine, name, (self, args) => fn(args)));

            define("create", args =>
            {
                if (exp.Nodes.Count >= Limits.MaxNodes) throw new JavaScriptException("This experience has created too many nodes (" + Limits.MaxNodes + ").");
                int id = _nextNode++;
                exp.Nodes.Add(id);
                _nodeOwner[id] = exp;
                _host.CreateNode(id, Str(args, 0), Props(args, 1));
                return id;
            });
            define("update", args =>
            {
                int id = Int(args, 0);
                if (exp.Nodes.Contains(id)) _host.UpdateNode(id, Props(args, 1));
                return JsValue.Undefined;
            });
            define("xf", args =>
            {
                int id = Int(args, 0);
                if (exp.Nodes.Contains(id))
                    _host.SetTransform(id,
                        new Vec3(Num(args, 1), Num(args, 2), Num(args, 3)),
                        new Quat(Num(args, 4), Num(args, 5), Num(args, 6), Num(args, 7, 1f)),
                        new Vec3(Num(args, 8, 1f), Num(args, 9, 1f), Num(args, 10, 1f)));
                return JsValue.Undefined;
            });
            define("destroy", args =>
            {
                int id = Int(args, 0);
                if (exp.Nodes.Remove(id))
                {
                    _nodeOwner.Remove(id);
                    _host.DestroyNode(id);
                }
                return JsValue.Undefined;
            });
            define("measure", args =>
            {
                float w, h;
                _fonts.Get(Str(args, 2)).Measure(Str(args, 0), Num(args, 1, 0.022f), Num(args, 3), out w, out h);
                return new JsArray(engine, new JsValue[] { (double)w, (double)h });
            });
            define("raycast", args =>
            {
                RayHit hit;
                var direction = new Vec3(Num(args, 3), Num(args, 4), Num(args, 5));
                if (direction.SqrLength < 1e-10f) return JsValue.Null;
                bool found = _host.Raycast(new Vec3(Num(args, 0), Num(args, 1), Num(args, 2)), direction.Normalized, Num(args, 6, 10f), out hit);
                return found ? (JsValue)Json.Write(HitJson(hit)) : JsValue.Null;
            });
            define("hasView", args => exp.View != null);
            define("viewRay", args =>
            {
                if (exp.View == null) return JsValue.Null;
                Vec3 origin, direction;
                exp.View.Ray(Num(args, 0), Num(args, 1), out origin, out direction);
                return Json.Write(new Dictionary<string, object> { { "origin", Json.FromVec3(origin) }, { "direction", Json.FromVec3(direction) } });
            });
            define("floorY", args => (double)_host.FloorY);
            define("surfaces", args => Json.Write(EntityList(_host.Surfaces, false)));
            define("props", args => Json.Write(EntityList(_host.Props, true)));
            define("user", args =>
            {
                Vec3 p, f;
                Quat r;
                _host.GetUser(out p, out f, out r);
                return new JsArray(engine, new JsValue[]
                {
                    Clean(p.x), Clean(p.y), Clean(p.z), Clean(f.x), Clean(f.y), Clean(f.z), Clean(r.x), Clean(r.y), Clean(r.z), Clean(r.w),
                });
            });
            define("find", args =>
            {
                string query = Str(args, 0);
                double callback = Num(args, 1);
                _host.Find(query, hits =>
                {
                    if (!exp.IsRunning || exp.Engine == null) return;
                    var list = new List<object>();
                    if (hits != null) foreach (RayHit hit in hits) list.Add(HitJson(hit));
                    Call(exp, "__resolve", callback, Json.Write(list));
                });
                return JsValue.Undefined;
            });
            define("log", args =>
            {
                string level = Str(args, 0);
                if (level == "error" && ++exp.ErrorCount > Limits.MaxErrors)
                    throw new JavaScriptException("Stopped after " + Limits.MaxErrors + " errors. Last: " + Str(args, 1));
                AddLog(exp, level, Str(args, 1));
                return JsValue.Undefined;
            });
            define("fail", args =>
            {
                // Raised from the program's top-level promise; the runtime stops the experience after this call.
                exp.Error = Str(args, 0);
                AddLog(exp, "error", exp.Error);
                return JsValue.Undefined;
            });
            define("finish", args =>
            {
                exp.FinishRequested = true;
                return JsValue.Undefined;
            });

            engine.SetValue("__host", host);
            return engine;
        }

        // ---- marshalling -------------------------------------------------------------------

        /// <summary>Floats widen to doubles with noise in the low digits; trim it so programs see 0.1, not 0.10000000149.</summary>
        static double Clean(float f) => Math.Round(f, 5);

        static string Str(JsValue[] args, int i) => i < args.Length && !args[i].IsUndefined() && !args[i].IsNull() ? TypeConverter.ToString(args[i]) : "";

        static float Num(JsValue[] args, int i, float fallback = 0f)
        {
            if (i >= args.Length || args[i].IsUndefined() || args[i].IsNull()) return fallback;
            double d = TypeConverter.ToNumber(args[i]);
            return double.IsNaN(d) || double.IsInfinity(d) ? fallback : (float)d;
        }

        static int Int(JsValue[] args, int i) => (int)Num(args, i);

        static Dictionary<string, object> Props(JsValue[] args, int i)
        {
            string json = Str(args, i);
            object parsed;
            if (json.Length > 0 && Json.TryParse(json, out parsed))
            {
                var d = parsed as Dictionary<string, object>;
                if (d != null) return d;
            }
            return new Dictionary<string, object>();
        }

        static Dictionary<string, object> HitJson(RayHit hit)
        {
            var o = new Dictionary<string, object>
            {
                { "position", Json.FromVec3(hit.position) }, { "normal", Json.FromVec3(hit.normal) }, { "distance", Json.Round(hit.distance) },
            };
            if (!string.IsNullOrEmpty(hit.label)) o["label"] = hit.label;
            return o;
        }

        static List<object> EntityList(IReadOnlyList<RoomEntity> entities, bool asProps)
        {
            var list = new List<object>();
            if (entities == null) return list;
            foreach (RoomEntity e in entities)
            {
                var o = new Dictionary<string, object>
                {
                    { "id", e.id }, { "kind", e.kind },
                    { "position", Json.FromVec3(e.position) },
                    { "rotation", new List<object> { Json.Round(e.rotation.x), Json.Round(e.rotation.y), Json.Round(e.rotation.z), Json.Round(e.rotation.w) } },
                    { "size", Json.FromVec3(e.size) },
                };
                if (asProps)
                {
                    o["name"] = e.label ?? e.id;
                    // Floor-level origin of the frame the prop was authored in; handy for paths given in its own coordinates.
                    o["origin"] = Json.FromVec3(e.ShapeOrigin);
                    if (e.aliases.Count > 0) o["aliases"] = new List<object>(e.aliases);
                    if (!string.IsNullOrEmpty(e.about)) o["about"] = e.about;
                    if (!string.IsNullOrEmpty(e.parentId)) o["parent"] = e.parentId;
                    if (e.hasActionPoint) o["actionPoint"] = Json.FromVec3(e.ActionPoint);
                    if (e.actionAxis.SqrLength > 0) o["actionAxis"] = Json.FromVec3(e.rotation * e.actionAxis);
                }
                else
                {
                    o["label"] = e.label ?? e.kind;
                }
                list.Add(o);
            }
            return list;
        }

        /// <summary>Stops a call once it has executed its allowance of statements.</summary>
        sealed class StatementBudget : Constraint
        {
            readonly Experience _experience;
            int _count;

            public StatementBudget(Experience experience) { _experience = experience; }

            public override void Reset() => _count = 0;

            public override void Check()
            {
                if (++_count > _experience.Budget) throw new StatementsCountOverflowException();
            }
        }
    }
}
