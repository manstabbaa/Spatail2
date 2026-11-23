using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Spatail.Core
{
    /// <summary>Everything a planner needs to answer one request.</summary>
    public sealed class PlanRequest
    {
        public string prompt;
        public RoomModel room;
        public SiteDefinition site;
        /// <summary>What is already in the room, so follow-ups ("make it bigger", "next") can refer to it.</summary>
        public IReadOnlyList<Build> builds = new List<Build>();
    }

    /// <summary>Turns a spoken or typed request into changes to the room's content.</summary>
    public interface IPlanner
    {
        string Name { get; }
        Task<PlanResult> PlanAsync(PlanRequest request);
    }

    /// <summary>
    /// Deterministic, on-device planner. It understands the core requests (walkthroughs, flow, fault history,
    /// dashboards, checklists, bringing someone in) and follow-up edits, with no network and no latency.
    /// It is the fallback when the AI planner is unreachable and the reference for what a plan looks like.
    /// </summary>
    public sealed class OfflinePlanner : IPlanner
    {
        public string Name => "offline";

        public Task<PlanResult> PlanAsync(PlanRequest request) => Task.FromResult(Plan(request));

        public PlanResult Plan(PlanRequest request)
        {
            var started = DateTime.UtcNow;
            PlanResult result = PlanInternal(request);
            result.planner = Name;
            result.latencyMs = Math.Max(1, (DateTime.UtcNow - started).TotalMilliseconds);
            SpecJson.Sanitize(result, request.room);
            return result;
        }

        PlanResult PlanInternal(PlanRequest request)
        {
            string p = TextMatch.Normalize(request.prompt);
            RoomModel room = request.room;
            SiteDefinition site = request.site;
            Knowledge k = site.knowledge;
            if (p.Length == 0) return Say("Tell me what you need.");

            // ---- follow-ups on what is already in the room ---------------------------------
            if (request.builds.Count > 0)
            {
                if (IsClear(p))
                {
                    var r = Say("Cleared.");
                    r.ops.Add(PlanOp.Clear());
                    return r;
                }

                PlanResult stepResult = TryStep(p, request);
                if (stepResult != null) return stepResult;

                Build target = FindBuild(p, request.builds);
                bool refersToIt = TextMatch.ContainsAny(p, "it", "that", "this one", "them");

                if (TextMatch.ContainsAny(p, "remove", "hide", "delete", "dismiss", "get rid of", "take away", "turn off"))
                {
                    Build victim = target ?? (refersToIt ? Latest(request.builds) : null);
                    if (victim != null)
                    {
                        var r = Say("Removed the " + victim.title.ToLowerInvariant() + ".");
                        r.ops.Add(PlanOp.Remove(victim.id));
                        return r;
                    }
                }

                if (TextMatch.ContainsAny(p, "bigger", "larger", "enlarge", "scale up", "smaller", "shrink", "scale down"))
                {
                    bool grow = TextMatch.ContainsAny(p, "bigger", "larger", "enlarge", "scale up");
                    Build subject = target ?? Latest(request.builds);
                    Build copy = subject.Clone();
                    foreach (Element e in copy.elements) e.scale *= grow ? 1.35f : 0.75f;
                    var r = Say((grow ? "Made the " : "Shrunk the ") + subject.title.ToLowerInvariant() + (grow ? " bigger." : "."));
                    r.chip = subject.title;
                    r.ops.Add(PlanOp.Add(copy));
                    return r;
                }

                if (TextMatch.ContainsAny(p, "move", "put", "place", "shift") && (target != null || refersToIt))
                {
                    string at;
                    RoomEntity where = FindPlacement(p, room, out at);
                    Build subject = target ?? Latest(request.builds);
                    if (where != null && HasMovable(subject))
                    {
                        Build copy = subject.Clone();
                        foreach (Element e in copy.elements)
                            if (IsMovable(e)) e.anchor = new Anchor { entity = where.id, at = AdaptMode(e, at) };
                        var r = Say("Moved the " + subject.title.ToLowerInvariant() + " to the " + where.label.ToLowerInvariant() + ".");
                        r.chip = subject.title;
                        r.ops.Add(PlanOp.Add(copy));
                        return r;
                    }
                }
            }

            // ---- new content ----------------------------------------------------------------
            Person person = k.FindPerson(p);
            if (TextMatch.ContainsAny(p, "bring", "invite", "call", "join", "loop in", "share", "session") &&
                (person != null || TextMatch.ContainsAny(p, "session", "someone", "expert", "colleague")))
            {
                if (person == null && k.people.Count > 0) person = k.people[0];
                if (person != null)
                    return Built(BuildFactory.Session(person, site.equipmentId), person.name + " is here with you.");
            }

            if (TextMatch.ContainsAny(p, "flow", "flowing", "water", "fluid", "liquid", "direction") && k.flows.Count > 0)
            {
                FlowDef flow = Best(p, k.flows, f => f.aliases, f => f.title) ?? k.flows[0];
                string detail = string.IsNullOrEmpty(flow.rate) ? "" : " Running at " + flow.rate + ".";
                return Built(BuildFactory.Flow(flow, site, room), "In at the inlet, up and out at the outlet." + detail);
            }

            if (TextMatch.ContainsAny(p, "fail", "failed", "failure", "fault", "broke", "broken", "alarm", "issue", "problem", "wrong", "trip", "tripped") && k.faults.Count > 0)
            {
                return Built(BuildFactory.Faults(k, room), FaultSummary(k, room));
            }

            if (TextMatch.ContainsAny(p, "part", "spare", "checklist", "tool", "inventory", "stock", "kit") && k.spares.Count > 0)
            {
                string at;
                RoomEntity where = FindPlacement(p, room, out at);
                if (where == null) { where = DefaultSurface(room, site); at = AnchorMode.Above; }
                else if (at == AnchorMode.FloorNear) at = AnchorMode.Above;
                int missing = 0;
                foreach (SpareItem s in k.spares) if (!s.done) missing++;
                string reply = missing == 0 ? "Everything is in the kit." : (missing == 1 ? "One item still to pick up." : missing + " items still to pick up.");
                return Built(BuildFactory.Checklist(k, where.id, at), reply);
            }

            MetricDef namedMetric = Best(p, k.metrics, m => m.aliases, m => m.title);
            if ((namedMetric != null || TextMatch.ContainsAny(p, "chart", "dashboard", "graph", "metric", "kpi", "number", "stat", "performance", "trend")) && k.metrics.Count > 0)
            {
                MetricDef metric = namedMetric ?? k.metrics[0];
                string at;
                RoomEntity where = FindPlacement(p, room, out at);
                if (where == null) { where = DefaultSurface(room, site); at = AnchorMode.FloorNear; }
                return Built(BuildFactory.Dashboard(metric, where.id, at), metric.title + ", by the " + where.label.ToLowerInvariant() + ".");
            }

            if (TextMatch.ContainsAny(p, "walkthrough", "walk me", "guide", "procedure", "step", "how do i", "how to", "service", "isolate", "start up", "startup", "instruction", "train", "teach", "show me how to") && k.procedures.Count > 0)
            {
                Procedure proc = Best(p, k.procedures, x => x.aliases, x => x.title) ?? k.procedures[0];
                Build b = BuildFactory.Walkthrough(proc, room);
                return Built(b, proc.title + " - " + b.stepCount + " steps. Say \"next\" when you are ready.");
            }

            if (TextMatch.ContainsAny(p, "label", "identify", "scan", "what am i looking at", "name everything", "what is everything"))
            {
                return Built(BuildFactory.Labels(room), "Here is what I can see.");
            }

            if (TextMatch.ContainsAny(p, "what is", "what s", "whats", "what does", "tell me about", "explain", "describe", "about"))
            {
                RoomEntity subject = FindMentioned(p, room);
                if (subject != null) return Built(BuildFactory.Describe(subject), subject.label + ".");
            }

            return Say("I can build a walkthrough, show the flow, fault history, this month's output, a parts checklist, or bring someone in. Connect the AI planner for anything else.");
        }

        // ---- step navigation ---------------------------------------------------------------

        static PlanResult TryStep(string p, PlanRequest request)
        {
            Build stepped = null;
            for (int i = request.builds.Count - 1; i >= 0; i--)
                if (request.builds[i].stepCount > 0) { stepped = request.builds[i]; break; }
            if (stepped == null) return null;

            // Navigation words are common in ordinary sentences ("has it failed before?"), so only treat them as
            // navigation in short commands or when the user actually says "step".
            bool shortCommand = p.Split(' ').Length <= 4;
            bool saysStep = TextMatch.ContainsWord(p, "step");
            if (!shortCommand && !saysStep) return null;

            int target = -1;
            int explicitStep = ParseStepNumber(p);
            if (explicitStep > 0) target = explicitStep;
            else if (TextMatch.ContainsAny(p, "next", "continue", "done", "go on", "keep going", "then what", "what next")) target = stepped.currentStep + 1;
            else if (TextMatch.ContainsAny(p, "back", "previous", "go back")) target = stepped.currentStep - 1;
            else if (TextMatch.ContainsAny(p, "first step", "start again", "restart", "from the top")) target = 1;
            else if (TextMatch.ContainsAny(p, "last step", "final step")) target = stepped.stepCount;
            if (target < 0) return null;

            if (target > stepped.stepCount) return Say("That was the last step.");
            if (target < 1) return Say("You are on the first step.");

            Element card = null;
            foreach (Element e in stepped.elements)
                if (e.type == ElementType.Card && e.step == target) card = e;
            var r = Say("Step " + target + " of " + stepped.stepCount + (card != null ? ": " + card.title + "." : "."));
            r.chip = "Step " + target;
            r.ops.Add(PlanOp.Step(stepped.id, target));
            return r;
        }

        static int ParseStepNumber(string p)
        {
            string[] words = p.Split(' ');
            string[] names = { "", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten" };
            for (int i = 0; i < words.Length - 1; i++)
            {
                if (words[i] != "step") continue;
                int n;
                if (int.TryParse(words[i + 1], out n) && n > 0) return n;
                int idx = Array.IndexOf(names, words[i + 1]);
                if (idx > 0) return idx;
            }
            return 0;
        }

        // ---- helpers -----------------------------------------------------------------------

        static bool IsClear(string p) =>
            p == "clear" || p == "reset" ||
            TextMatch.ContainsAny(p, "clear everything", "clear all", "clear it all", "remove everything", "remove all", "hide everything", "start over", "clear the room", "reset everything");

        static readonly KeyValuePair<string, string[]>[] BuildWords =
        {
            new KeyValuePair<string, string[]>(BuildKind.Walkthrough, new[] { "walkthrough", "guide", "steps", "procedure", "instructions" }),
            new KeyValuePair<string, string[]>(BuildKind.Flow, new[] { "flow", "arrows", "water" }),
            new KeyValuePair<string, string[]>(BuildKind.Faults, new[] { "faults", "fault history", "failures", "alarms", "warnings" }),
            new KeyValuePair<string, string[]>(BuildKind.Dashboard, new[] { "dashboard", "chart", "graph", "output", "bars" }),
            new KeyValuePair<string, string[]>(BuildKind.Checklist, new[] { "checklist", "parts", "list", "parts check" }),
            new KeyValuePair<string, string[]>(BuildKind.Session, new[] { "session", "avatar", "sara" }),
            new KeyValuePair<string, string[]>(BuildKind.Labels, new[] { "labels", "names", "tags" }),
            new KeyValuePair<string, string[]>(BuildKind.Note, new[] { "note", "notes", "panel", "explanation" }),
        };

        /// <summary>Which existing build a phrase is talking about, if any.</summary>
        static Build FindBuild(string p, IReadOnlyList<Build> builds)
        {
            for (int i = builds.Count - 1; i >= 0; i--)
            {
                Build b = builds[i];
                if (TextMatch.ContainsWord(p, b.title)) return b;
                foreach (var entry in BuildWords)
                    if (entry.Key == b.kind && TextMatch.ContainsAny(p, entry.Value)) return b;
                foreach (Element e in b.elements)
                    if (e.type == ElementType.Avatar && !string.IsNullOrEmpty(e.name) && TextMatch.ContainsWord(p, e.name)) return b;
            }
            return null;
        }

        static Build Latest(IReadOnlyList<Build> builds) => builds[builds.Count - 1];

        static bool IsMovable(Element e) =>
            e.type == ElementType.Chart || e.type == ElementType.Checklist || e.type == ElementType.Panel ||
            e.type == ElementType.Avatar || e.type == ElementType.Card;

        static bool HasMovable(Build b)
        {
            foreach (Element e in b.elements) if (IsMovable(e)) return true;
            return false;
        }

        /// <summary>Charts and avatars stand on things; panels hang above or beside them.</summary>
        static string AdaptMode(Element e, string requested)
        {
            if (requested == AnchorMode.Wall) return AnchorMode.Wall;
            bool stands = e.type == ElementType.Chart || e.type == ElementType.Avatar;
            if (requested == AnchorMode.Surface) return stands ? AnchorMode.Surface : AnchorMode.Above;
            if (requested == AnchorMode.Above) return AnchorMode.Above;
            return stands ? AnchorMode.FloorNear : AnchorMode.Above;
        }

        static readonly KeyValuePair<string, string>[] Prepositions =
        {
            new KeyValuePair<string, string>("on top of", AnchorMode.Surface),
            new KeyValuePair<string, string>("next to", AnchorMode.FloorNear),
            new KeyValuePair<string, string>("in front of", AnchorMode.FloorNear),
            new KeyValuePair<string, string>("close to", AnchorMode.FloorNear),
            new KeyValuePair<string, string>("beside", AnchorMode.FloorNear),
            new KeyValuePair<string, string>("above", AnchorMode.Above),
            new KeyValuePair<string, string>("over", AnchorMode.Above),
            new KeyValuePair<string, string>("onto", AnchorMode.Surface),
            new KeyValuePair<string, string>("near", AnchorMode.FloorNear),
            new KeyValuePair<string, string>("by", AnchorMode.FloorNear),
            new KeyValuePair<string, string>("on", AnchorMode.Surface),
            new KeyValuePair<string, string>("at", AnchorMode.FloorNear),
            new KeyValuePair<string, string>("to", AnchorMode.FloorNear),
        };

        /// <summary>Reads "... by the bench" / "... on the wall" and returns the entity plus how to anchor to it.</summary>
        static RoomEntity FindPlacement(string p, RoomModel room, out string at)
        {
            at = AnchorMode.FloorNear;
            string padded = " " + p + " ";
            foreach (var prep in Prepositions)
            {
                int idx = padded.LastIndexOf(" " + prep.Key + " ", StringComparison.Ordinal);
                if (idx < 0) continue;
                string rest = padded.Substring(idx + prep.Key.Length + 2).Trim();
                rest = StripArticles(rest);
                if (rest.Length == 0) continue;
                if (TextMatch.ContainsWord(rest, "wall"))
                {
                    RoomEntity anchor = room.FirstOfKind(EntityKind.Equipment) ?? room.FirstOfKind(EntityKind.Wall);
                    if (anchor != null) { at = AnchorMode.Wall; return anchor; }
                }
                RoomEntity e = room.Resolve(rest);
                if (e != null && !EntityKind.IsSurface(e.kind))
                {
                    at = prep.Value;
                    return e;
                }
            }
            return null;
        }

        static RoomEntity FindMentioned(string p, RoomModel room)
        {
            RoomEntity best = null;
            int bestScore = 0;
            foreach (RoomEntity e in room.Nameable())
            {
                int score = TextMatch.ContainsWord(p, e.label) ? 100 + e.label.Length : 0;
                foreach (string alias in e.aliases)
                    if (TextMatch.ContainsWord(p, alias)) score = Math.Max(score, 50 + alias.Length);
                if (score > bestScore) { bestScore = score; best = e; }
            }
            return best;
        }

        static string StripArticles(string s)
        {
            foreach (string article in new[] { "the ", "a ", "an ", "my ", "this ", "that " })
                if (s.StartsWith(article, StringComparison.Ordinal)) return s.Substring(article.Length);
            return s;
        }

        /// <summary>Where free-standing content goes by default: the workbench if the room has one, else the equipment.</summary>
        static RoomEntity DefaultSurface(RoomModel room, SiteDefinition site)
        {
            return room.FirstOfKind(EntityKind.Table) ?? room.Get(site.equipmentId) ?? room.entities[0];
        }

        static T Best<T>(string p, List<T> items, Func<T, List<string>> aliases, Func<T, string> title) where T : class
        {
            T best = null;
            int bestScore = 0;
            foreach (T item in items)
            {
                int score = 0;
                foreach (string alias in aliases(item))
                    if (TextMatch.ContainsWord(p, alias)) score = Math.Max(score, 10 + alias.Length);
                if (TextMatch.ContainsWord(p, title(item))) score = Math.Max(score, 100);
                if (score > bestScore) { bestScore = score; best = item; }
            }
            return best;
        }

        static string FaultSummary(Knowledge k, RoomModel room)
        {
            var parts = new List<string>();
            foreach (FaultRecord f in k.faults)
            {
                RoomEntity e = room.Get(f.entity);
                if (e == null) continue;
                parts.Add(f.title.ToLowerInvariant() + " on the " + e.label.ToLowerInvariant() + (string.IsNullOrEmpty(f.when) ? "" : " (" + f.when + ")"));
            }
            if (parts.Count == 0) return "No faults on record.";
            if (parts.Count == 1) return "One fault on record: " + parts[0] + ".";
            return parts.Count + " faults on record: " + string.Join("; ", parts.ToArray()) + ".";
        }

        static PlanResult Say(string reply) => new PlanResult { reply = reply, chip = "" };

        static PlanResult Built(Build build, string reply)
        {
            var r = new PlanResult { reply = reply, chip = build.title };
            r.ops.Add(PlanOp.Add(build));
            return r;
        }
    }
}
