using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Spatail.Core
{
    /// <summary>
    /// Takes a request from "said" to "running in the room". It offers short commands to the programs already
    /// running, otherwise captures what the device senses, asks a builder for a program, runs it, and if the
    /// program fails gives the builder one chance to repair it.
    /// </summary>
    public sealed class Director
    {
        readonly ExperienceRuntime _runtime;
        readonly IScriptHost _host;
        readonly DemoBuilder _demo;
        readonly RemoteBuilder _remote;

        /// <summary>When false the planner service is never called, even if configured.</summary>
        public bool UseRemote = true;
        /// <summary>How many times a failing program is sent back for repair.</summary>
        public int MaxRepairs = 1;

        public Director(ExperienceRuntime runtime, IScriptHost host, DemoBuilder demo, RemoteBuilder remote)
        {
            _runtime = runtime;
            _host = host;
            _demo = demo;
            _remote = remote;
        }

        public bool RemoteActive => UseRemote && _remote != null && _remote.IsConfigured;

        /// <param name="capture">Fills in the camera frame for the request. May leave it empty.</param>
        public async Task<BuildResult> SubmitAsync(string prompt, Action<BuildRequest> capture)
        {
            // Short commands ("next", "back") belong to whatever is running and never leave the device.
            if (_runtime.HandleVoice(prompt))
                return new BuildResult { reply = "", title = "", builder = "experience", latencyMs = 1 };

            BuildRequest request = NewRequest(prompt, capture);
            bool remote = RemoteActive;
            BuildResult result = remote ? await _remote.BuildAsync(request) : _demo.Build(request);

            if (remote && !string.IsNullOrEmpty(result.error))
            {
                // The service failed. Fall back to the recorded demo so the user is not left with nothing.
                string why = result.error;
                result = _demo.Build(request);
                result.error = why;
                result.builder = "demo (planner unavailable)";
                remote = false;
            }

            Experience started = Apply(result, request);

            for (int attempt = 0; remote && attempt < MaxRepairs && started != null && started.Error != null; attempt++)
            {
                BuildRequest repair = NewRequest(prompt, capture);
                repair.failedCode = result.code;
                repair.failure = started.Error;
                BuildResult fixedResult = await _remote.BuildAsync(repair);
                if (!string.IsNullOrEmpty(fixedResult.error) || !fixedResult.HasProgram) break;
                fixedResult.latencyMs += result.latencyMs;
                result = fixedResult;
                started = Apply(result, repair);
            }

            if (started != null && started.Error != null)
            {
                result.error = started.Error;
                result.reply = "I built something for that but it did not run. " + FirstLine(started.Error);
                result.title = "";
            }
            return result;
        }

        BuildRequest NewRequest(string prompt, Action<BuildRequest> capture)
        {
            var request = new BuildRequest { prompt = prompt, floorY = _host.FloorY };
            Quat rotation;
            _host.GetUser(out request.userPosition, out request.userForward, out rotation);
            request.surfaces.AddRange(_host.Surfaces);
            request.props.AddRange(_host.Props);
            foreach (Experience e in _runtime.Experiences)
                request.running.Add(new RunningExperience { id = e.Id, title = e.Title, prompt = e.Prompt, code = e.Code });
            if (capture != null) capture(request);
            if (request.view != null && request.depth.Count == 0) request.SampleDepth(_host);
            return request;
        }

        Experience Apply(BuildResult result, BuildRequest request)
        {
            if (result.clear) _runtime.StopAll();
            foreach (string id in result.remove) _runtime.Stop(id);
            if (!result.HasProgram) return null;
            return _runtime.Start(result.id, result.title, request.prompt, result.code, request.view);
        }

        static string FirstLine(string s)
        {
            int nl = s.IndexOf('\n');
            return nl < 0 ? s : s.Substring(0, nl);
        }
    }
}
