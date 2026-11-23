using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Spatail.Core
{
    /// <summary>
    /// Answers without a model, by playing back programs that were written ahead of time for the stand-in pump.
    /// It exists so the app can be shown with no network. It is a recording, not the product: these programs only
    /// make sense next to the stand-in machine, and anything beyond the handful of requests below needs the
    /// planner service.
    /// </summary>
    public sealed class DemoBuilder : IBuilder
    {
        public sealed class Entry
        {
            public string id, title, reply;
            public string[] words;
        }

        /// <summary>Checked in order; the first entry with a matching word wins.</summary>
        public static readonly Entry[] Entries =
        {
            new Entry { id = "session", title = "Shared session", reply = "Sara is here with you.", words = new[] { "sara", "bring", "invite", "join", "colleague" } },
            new Entry { id = "flow", title = "Live flow", reply = "In at the inlet, up and out at the outlet. Running at 42 m³/h.", words = new[] { "flow", "flows", "water", "fluid", "direction" } },
            new Entry { id = "faults", title = "Fault history", reply = "Two faults last week: the motor bearing ran hot, and the coupling tripped a vibration alarm.", words = new[] { "fail", "failed", "fault", "faults", "broke", "alarm", "wrong", "trip" } },
            new Entry { id = "checklist", title = "Checklist", reply = "One item still to pick up.", words = new[] { "part", "parts", "spare", "spares", "checklist", "kit" } },
            new Entry { id = "dashboard", title = "Dashboard", reply = "Output this month, by the bench.", words = new[] { "output", "production", "chart", "dashboard", "graph", "throughput" } },
            new Entry { id = "walkthrough", title = "Walkthrough", reply = "Three steps to isolate the pump. Say \"next\" when you are ready.", words = new[] { "walkthrough", "walk", "guide", "procedure", "steps", "service", "isolate", "how" } },
        };

        readonly Func<string, string> _loadProgram;

        /// <param name="loadProgram">Returns the source of an example program by id, or null if it is missing.</param>
        public DemoBuilder(Func<string, string> loadProgram)
        {
            _loadProgram = loadProgram;
        }

        public string Name => "demo";

        public Task<BuildResult> BuildAsync(BuildRequest request) => Task.FromResult(Build(request));

        public BuildResult Build(BuildRequest request)
        {
            string p = TextMatch.Normalize(request.prompt);
            var result = new BuildResult { builder = Name, latencyMs = 1 };

            if (request.running.Count > 0)
            {
                if (p == "clear" || p == "reset" || TextMatch.ContainsAny(p, "clear everything", "clear all", "remove everything", "start over"))
                {
                    result.clear = true;
                    result.reply = "Cleared.";
                    return result;
                }
                if (TextMatch.ContainsAny(p, "remove", "hide", "delete", "dismiss", "get rid of"))
                {
                    for (int i = request.running.Count - 1; i >= 0; i--)
                    {
                        RunningExperience r = request.running[i];
                        if (!TextMatch.ContainsWord(p, r.title) && !Mentions(p, r.id)) continue;
                        result.remove.Add(r.id);
                        result.reply = "Removed the " + r.title.ToLowerInvariant() + ".";
                        return result;
                    }
                }
            }

            foreach (Entry entry in Entries)
            {
                if (!TextMatch.ContainsAny(p, entry.words)) continue;
                string code = _loadProgram(entry.id);
                if (string.IsNullOrEmpty(code)) continue;
                if (request.props.Count == 0)
                {
                    result.reply = "That demo needs the stand-in pump. Turn it on, or connect the planner to build for this space.";
                    return result;
                }
                result.id = entry.id;
                result.title = entry.title;
                result.reply = entry.reply;
                result.code = code;
                return result;
            }

            result.reply = "Without the planner I can only replay the pump demo: a walkthrough, the flow, fault history, this month's output, a parts list, or bringing Sara in.";
            return result;
        }

        static bool Mentions(string phrase, string id)
        {
            foreach (Entry entry in Entries)
                if (entry.id == id) return TextMatch.ContainsAny(phrase, entry.words);
            return false;
        }
    }
}
