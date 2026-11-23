using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Spatail.Core
{
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

    /// <summary>
    /// Asks the Spatail planner service to plan a request with a large language model. The device sends the prompt,
    /// its room model, the site's knowledge and the current builds; the service owns the model, the system prompt
    /// and the API key, and answers with a <see cref="PlanResult"/> in the same wire format the offline planner uses.
    /// </summary>
    public sealed class RemotePlanner : IPlanner
    {
        readonly IHttpTransport _http;
        readonly Func<string> _baseUrl;
        readonly Func<string> _token;

        public int TimeoutSeconds = 45;

        public RemotePlanner(IHttpTransport http, Func<string> baseUrl, Func<string> token = null)
        {
            _http = http;
            _baseUrl = baseUrl;
            _token = token;
        }

        public string Name => "ai";

        public bool IsConfigured => !string.IsNullOrEmpty(_baseUrl());

        public static string BuildRequestJson(PlanRequest request)
        {
            var builds = new List<object>();
            foreach (Build b in request.builds) builds.Add(SpecJson.ToJson(b));
            var body = new Dictionary<string, object>
            {
                { "prompt", request.prompt },
                { "site", new Dictionary<string, object> { { "id", request.site.id }, { "name", request.site.name }, { "equipment", request.site.equipmentId } } },
                { "room", request.room.ToJson() },
                { "knowledge", request.site.knowledge.raw },
                { "builds", builds },
            };
            return Json.Write(body);
        }

        public async Task<PlanResult> PlanAsync(PlanRequest request)
        {
            var started = DateTime.UtcNow;
            string url = (_baseUrl() ?? "").TrimEnd('/') + "/v1/plan";
            var headers = new Dictionary<string, string>();
            string token = _token != null ? _token() : null;
            if (!string.IsNullOrEmpty(token)) headers["Authorization"] = "Bearer " + token;

            HttpResult response;
            try
            {
                response = await _http.PostJsonAsync(url, BuildRequestJson(request), headers, TimeoutSeconds);
            }
            catch (Exception ex)
            {
                return Failure("Could not reach the planner service: " + ex.Message);
            }
            if (response == null || !response.ok)
            {
                string why = response == null ? "no response" : (string.IsNullOrEmpty(response.error) ? "HTTP " + response.status : response.error);
                return Failure("Planner service error: " + why);
            }

            PlanResult plan;
            try
            {
                plan = SpecJson.ParsePlan(response.body);
            }
            catch (Exception ex)
            {
                return Failure("Planner service returned something unreadable: " + ex.Message);
            }
            if (string.IsNullOrEmpty(plan.planner)) plan.planner = Name;
            plan.latencyMs = (DateTime.UtcNow - started).TotalMilliseconds;
            SpecJson.Sanitize(plan, request.room);
            return plan;
        }

        static PlanResult Failure(string message) => new PlanResult { reply = message, chip = "", error = message, planner = "ai" };
    }

    /// <summary>
    /// Uses the AI planner when it is configured and reachable, and the offline planner otherwise, so a request always
    /// gets an answer. Step navigation and other instant follow-ups are served on-device to keep them snappy.
    /// </summary>
    public sealed class PlannerRouter : IPlanner
    {
        readonly OfflinePlanner _offline;
        readonly RemotePlanner _remote;

        /// <summary>When false the AI planner is never called, even if configured.</summary>
        public bool UseRemote = true;

        public PlannerRouter(OfflinePlanner offline, RemotePlanner remote)
        {
            _offline = offline;
            _remote = remote;
        }

        public string Name => RemoteActive ? "ai+offline" : "offline";

        public bool RemoteActive => UseRemote && _remote != null && _remote.IsConfigured;

        public async Task<PlanResult> PlanAsync(PlanRequest request)
        {
            if (!RemoteActive) return _offline.Plan(request);

            // Instant on-device answers for navigation ("next", "back", "step 2") and "clear".
            PlanResult local = _offline.Plan(request);
            if (IsInstant(local)) return local;

            PlanResult remote = await _remote.PlanAsync(request);
            if (string.IsNullOrEmpty(remote.error)) return remote;

            // The service failed; answer on-device and say so in the dev log rather than leaving the user with nothing.
            local.error = remote.error;
            local.planner = "offline (AI unavailable)";
            return local;
        }

        static bool IsInstant(PlanResult plan)
        {
            if (plan.ops.Count != 1) return false;
            string op = plan.ops[0].op;
            return op == OpType.Step || op == OpType.Clear;
        }
    }
}
