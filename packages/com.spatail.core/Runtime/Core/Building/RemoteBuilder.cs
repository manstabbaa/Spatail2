using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Spatail.Core
{
    /// <summary>
    /// Asks the Spatail planner service to write the program. The device sends what it senses right now (camera
    /// frame, depth samples, surfaces) and what the user said; the service has a model look at it and write code
    /// against the Spatail API. The device never holds a model API key.
    /// </summary>
    public sealed class RemoteBuilder : IBuilder
    {
        readonly IHttpTransport _http;
        readonly Func<string> _baseUrl;
        readonly Func<string> _token;

        public int TimeoutSeconds = 90;

        public RemoteBuilder(IHttpTransport http, Func<string> baseUrl, Func<string> token = null)
        {
            _http = http;
            _baseUrl = baseUrl;
            _token = token;
        }

        public string Name => "ai";
        public bool IsConfigured => !string.IsNullOrEmpty(_baseUrl());

        Dictionary<string, string> Headers()
        {
            var headers = new Dictionary<string, string>();
            string token = _token != null ? _token() : null;
            if (!string.IsNullOrEmpty(token)) headers["Authorization"] = "Bearer " + token;
            return headers;
        }

        string Url(string path) => (_baseUrl() ?? "").TrimEnd('/') + path;

        public async Task<BuildResult> BuildAsync(BuildRequest request)
        {
            var started = DateTime.UtcNow;
            HttpResult response;
            try
            {
                response = await _http.PostJsonAsync(Url("/v1/build"), Json.Write(request.ToJson()), Headers(), TimeoutSeconds);
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
            BuildResult result;
            try
            {
                result = BuildResult.FromJson(Json.ParseObject(response.body));
            }
            catch (Exception ex)
            {
                return Failure("Planner service returned something unreadable: " + ex.Message);
            }
            if (string.IsNullOrEmpty(result.builder)) result.builder = Name;
            result.latencyMs = (DateTime.UtcNow - started).TotalMilliseconds;
            return result;
        }

        /// <summary>
        /// Asks the service where something is in a camera frame. Returns image positions (u, v from 0 to 1) with
        /// labels, best first; the caller casts them into the world.
        /// </summary>
        public async Task<List<LocatedPoint>> LocateAsync(string description, byte[] imageJpeg)
        {
            var points = new List<LocatedPoint>();
            if (imageJpeg == null || imageJpeg.Length == 0) return points;
            var body = new Dictionary<string, object> { { "query", description }, { "image", Convert.ToBase64String(imageJpeg) } };
            HttpResult response;
            try
            {
                response = await _http.PostJsonAsync(Url("/v1/locate"), Json.Write(body), Headers(), 30);
            }
            catch (Exception)
            {
                return points;
            }
            object parsed;
            if (response == null || !response.ok || !Json.TryParse(response.body, out parsed)) return points;
            var o = parsed as Dictionary<string, object>;
            var list = o != null ? o.Arr("points") : null;
            if (list == null) return points;
            foreach (object item in list)
            {
                var d = item as Dictionary<string, object>;
                if (d == null) continue;
                points.Add(new LocatedPoint { u = d.Float("u"), v = d.Float("v"), label = d.Str("label", description) });
            }
            return points;
        }

        /// <summary>
        /// Sends a request and its result to the service to keep as an episode: a real moment in a real place that
        /// can be replayed against the planner later. Returns the saved name, or null if it could not be saved.
        /// </summary>
        public async Task<string> SaveEpisodeAsync(BuildRequest request, BuildResult result)
        {
            if (request == null) return null;
            var body = new Dictionary<string, object> { { "request", request.ToJson() } };
            if (result != null) body["result"] = result.ToJson();
            try
            {
                HttpResult response = await _http.PostJsonAsync(Url("/v1/episodes"), Json.Write(body), Headers(), 30);
                object parsed;
                if (response == null || !response.ok || !Json.TryParse(response.body, out parsed)) return null;
                var o = parsed as Dictionary<string, object>;
                return o != null ? o.Str("saved") : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static BuildResult Failure(string message) => new BuildResult { reply = message, error = message, builder = "ai" };
    }

    public struct LocatedPoint
    {
        public float u, v;
        public string label;
    }
}
