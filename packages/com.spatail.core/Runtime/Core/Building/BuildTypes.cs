using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Spatail.Core
{
    /// <summary>
    /// One request, with what the device senses at the moment it is made. Nothing here is prepared in advance:
    /// the camera frame, the depth samples and the surfaces are whatever the device has right now, wherever the
    /// user happens to be standing.
    /// </summary>
    public sealed class BuildRequest
    {
        public string prompt;
        /// <summary>Where the camera was when <see cref="imageJpeg"/> was taken. Null when the device has no camera frame.</summary>
        public ViewFrame view;
        /// <summary>The camera frame as JPEG bytes, or null.</summary>
        public byte[] imageJpeg;
        /// <summary>Coarse grid of distances (metres) along the view, row by row from the top-left; -1 where nothing was hit.</summary>
        public List<float> depth = new List<float>();
        public int depthColumns, depthRows;
        public List<RoomEntity> surfaces = new List<RoomEntity>();
        public List<RoomEntity> props = new List<RoomEntity>();
        public float floorY;
        public Vec3 userPosition;
        public Vec3 userForward = Vec3.Forward;
        /// <summary>What is already running, so follow-ups can change or remove it.</summary>
        public List<RunningExperience> running = new List<RunningExperience>();
        /// <summary>Set when asking the planner to fix a program it wrote that failed.</summary>
        public string failedCode;
        public string failure;

        public const int DefaultDepthColumns = 8, DefaultDepthRows = 6;

        /// <summary>Samples the live geometry along a grid of rays through the view, to give the planner a sense of scale.</summary>
        public void SampleDepth(IScriptHost host, int columns = DefaultDepthColumns, int rows = DefaultDepthRows)
        {
            depth.Clear();
            depthColumns = columns;
            depthRows = rows;
            if (view == null) { depthColumns = depthRows = 0; return; }
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < columns; c++)
                {
                    Vec3 origin, direction;
                    view.Ray((c + 0.5f) / columns, (r + 0.5f) / rows, out origin, out direction);
                    RayHit hit;
                    depth.Add(host.Raycast(origin, direction, 15f, out hit) ? hit.distance : -1f);
                }
        }

        public Dictionary<string, object> ToJson()
        {
            var o = new Dictionary<string, object> { { "prompt", prompt ?? "" } };
            if (view != null)
            {
                var v = new Dictionary<string, object> { { "width", view.width }, { "height", view.height } };
                if (depth.Count > 0)
                {
                    var d = new List<object>();
                    foreach (float m in depth) d.Add(Math.Round(m, 2));
                    v["depth"] = new Dictionary<string, object> { { "columns", depthColumns }, { "rows", depthRows }, { "metres", d } };
                }
                o["view"] = v;
            }
            if (imageJpeg != null && imageJpeg.Length > 0) o["image"] = Convert.ToBase64String(imageJpeg);

            o["user"] = new Dictionary<string, object> { { "position", Json.FromVec3(userPosition) }, { "forward", Json.FromVec3(userForward) } };
            o["floorY"] = Json.Round(floorY);

            var surfaceList = new List<object>();
            foreach (RoomEntity e in surfaces)
                surfaceList.Add(new Dictionary<string, object>
                {
                    { "id", e.id }, { "kind", e.kind }, { "position", Json.FromVec3(e.position) }, { "size", Json.FromVec3(e.size) },
                    { "facing", Json.FromVec3(e.kind == EntityKind.Wall ? e.Forward : e.Up) },
                });
            o["surfaces"] = surfaceList;

            var propList = new List<object>();
            foreach (RoomEntity e in props)
            {
                var p = new Dictionary<string, object> { { "id", e.id }, { "name", e.label ?? e.id }, { "position", Json.FromVec3(e.position) }, { "size", Json.FromVec3(e.size) } };
                if (e.aliases.Count > 0) p["aliases"] = new List<object>(e.aliases);
                if (!string.IsNullOrEmpty(e.about)) p["about"] = e.about;
                if (!string.IsNullOrEmpty(e.parentId)) p["partOf"] = e.parentId;
                // Holograms are not in the camera image, so say where each one appears in it.
                float u, v;
                if (view != null && view.Project(e.position, out u, out v) && u >= 0 && u <= 1 && v >= 0 && v <= 1)
                    p["inView"] = new List<object> { Math.Round(u, 3), Math.Round(v, 3) };
                propList.Add(p);
            }
            o["props"] = propList;

            var runningList = new List<object>();
            foreach (RunningExperience r in running)
                runningList.Add(new Dictionary<string, object> { { "id", r.id }, { "title", r.title }, { "prompt", r.prompt ?? "" }, { "code", r.code ?? "" } });
            o["running"] = runningList;

            if (!string.IsNullOrEmpty(failure))
                o["repair"] = new Dictionary<string, object> { { "code", failedCode ?? "" }, { "error", failure } };
            return o;
        }
    }

    public sealed class RunningExperience
    {
        public string id, title, prompt, code;
    }

    /// <summary>What a builder returns: something to say, and usually a program to run.</summary>
    public sealed class BuildResult
    {
        /// <summary>One short sentence for the user.</summary>
        public string reply = "";
        /// <summary>Two or three words naming what was built, for the "Built ..." chip. Empty when nothing was.</summary>
        public string title = "";
        /// <summary>Id for the experience. Re-using a running id replaces that experience and keeps its state.</summary>
        public string id = "";
        /// <summary>The program, or empty when there is nothing to run.</summary>
        public string code = "";
        /// <summary>Running experiences to stop.</summary>
        public List<string> remove = new List<string>();
        /// <summary>Stop everything.</summary>
        public bool clear;
        /// <summary>Which builder answered ("demo", a model id, ...).</summary>
        public string builder;
        public double latencyMs;
        /// <summary>Set when building failed; <see cref="reply"/> then says so in words the user can read.</summary>
        public string error;

        public bool HasProgram => !string.IsNullOrEmpty(code);

        public static BuildResult FromJson(Dictionary<string, object> o)
        {
            var r = new BuildResult
            {
                reply = o.Str("reply", ""),
                title = o.Str("title", ""),
                id = o.Str("id", ""),
                code = o.Str("code", ""),
                clear = o.Bool("clear"),
                builder = o.Str("builder"),
                latencyMs = o.Num("latencyMs"),
                error = o.Str("error"),
                remove = o.StrList("remove"),
            };
            return r;
        }

        public Dictionary<string, object> ToJson()
        {
            var o = new Dictionary<string, object> { { "reply", reply ?? "" }, { "title", title ?? "" }, { "id", id ?? "" }, { "code", code ?? "" } };
            if (remove.Count > 0) o["remove"] = new List<object>(remove);
            if (clear) o["clear"] = true;
            if (!string.IsNullOrEmpty(builder)) o["builder"] = builder;
            if (latencyMs > 0) o["latencyMs"] = Math.Round(latencyMs);
            if (!string.IsNullOrEmpty(error)) o["error"] = error;
            return o;
        }
    }

    /// <summary>Turns a request into a program.</summary>
    public interface IBuilder
    {
        string Name { get; }
        Task<BuildResult> BuildAsync(BuildRequest request);
    }

    public sealed class HttpResult
    {
        public bool ok;
        public int status;
        public string body;
        public string error;
    }

    /// <summary>Minimal HTTP seam so the core stays engine-independent. Unity supplies a UnityWebRequest implementation.</summary>
    public interface IHttpTransport
    {
        Task<HttpResult> PostJsonAsync(string url, string json, Dictionary<string, string> headers, int timeoutSeconds);
    }
}
