using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Spatail.Core;
using UnityEngine.Networking;

namespace Spatail
{
    /// <summary>HTTP for the planner client, on UnityWebRequest so it works on every Unity platform.</summary>
    public sealed class UnityHttpTransport : IHttpTransport
    {
        public Task<HttpResult> PostJsonAsync(string url, string json, Dictionary<string, string> headers, int timeoutSeconds)
        {
            var completion = new TaskCompletionSource<HttpResult>();
            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = timeoutSeconds,
            };
            request.SetRequestHeader("Content-Type", "application/json");
            if (headers != null)
                foreach (var header in headers) request.SetRequestHeader(header.Key, header.Value);

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            operation.completed += _ =>
            {
                bool ok = request.result == UnityWebRequest.Result.Success;
                string body = request.downloadHandler != null ? request.downloadHandler.text : null;
                completion.TrySetResult(new HttpResult
                {
                    ok = ok,
                    status = (int)request.responseCode,
                    body = body,
                    // Prefer the service's own message when it sent one.
                    error = ok ? null : (string.IsNullOrEmpty(body) ? request.error : request.error + " - " + Truncate(body, 300)),
                });
                request.Dispose();
            };
            return completion.Task;
        }

        static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "...";
    }
}
