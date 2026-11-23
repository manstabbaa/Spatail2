using System.Collections.Generic;

namespace Spatail.Core
{
    /// <summary>The vocabulary of spatial content. A planner composes builds out of these.</summary>
    public static class ElementType
    {
        /// <summary>A pill of text tethered to an entity.</summary>
        public const string Label = "label";
        /// <summary>A numbered marker above an entity.</summary>
        public const string Pin = "pin";
        /// <summary>A numbered instruction card.</summary>
        public const string Card = "card";
        /// <summary>A motion cue on a part, such as a turn arrow around a handwheel.</summary>
        public const string Gizmo = "gizmo";
        /// <summary>Animated arrows along a path with lit pipe sections.</summary>
        public const string Flow = "flow";
        /// <summary>A tinted volume over an entity.</summary>
        public const string Highlight = "highlight";
        /// <summary>A 3D bar chart standing on a surface.</summary>
        public const string Chart = "chart";
        /// <summary>A floating checklist panel.</summary>
        public const string Checklist = "checklist";
        /// <summary>A remote collaborator's presence marker.</summary>
        public const string Avatar = "avatar";
        /// <summary>A free-form text panel.</summary>
        public const string Panel = "panel";

        public static readonly string[] All = { Label, Pin, Card, Gizmo, Flow, Highlight, Chart, Checklist, Avatar, Panel };

        public static bool IsKnown(string type)
        {
            for (int i = 0; i < All.Length; i++) if (All[i] == type) return true;
            return false;
        }
    }

    /// <summary>How an element's position is derived from its anchor entity. Resolved by the layout solver.</summary>
    public static class AnchorMode
    {
        /// <summary>The centre of the entity's bounds.</summary>
        public const string Center = "center";
        /// <summary>The top of the entity's bounds.</summary>
        public const string Top = "top";
        /// <summary>Floating clear above the entity.</summary>
        public const string Above = "above";
        /// <summary>Beside the entity on the user's side, at a readable height.</summary>
        public const string Callout = "callout";
        /// <summary>On the floor next to the entity, in free space.</summary>
        public const string FloorNear = "floor-near";
        /// <summary>On top of the entity's upper surface.</summary>
        public const string Surface = "surface";
        /// <summary>On the nearest wall to the entity, at eye height.</summary>
        public const string Wall = "wall";
        /// <summary>In front of the user; used when content is not about any particular thing.</summary>
        public const string User = "user";

        public static readonly string[] All = { Center, Top, Above, Callout, FloorNear, Surface, Wall, User };
    }

    public static class ElementStyle
    {
        public const string Accent = "accent";
        public const string Neutral = "neutral";
        public const string Warning = "warning";
        public const string Success = "success";
    }

    public sealed class Anchor
    {
        /// <summary>Id of the entity in the <see cref="RoomModel"/>.</summary>
        public string entity;
        public string at = AnchorMode.Center;
        /// <summary>Extra offset in metres, in the anchor entity's local axes.</summary>
        public Vec3 offset;

        public Anchor Clone() => new Anchor { entity = entity, at = at, offset = offset };
    }

    public sealed class ChecklistItem
    {
        public string text;
        public bool done;
    }

    /// <summary>
    /// One piece of spatial content. A single flat shape with optional fields keeps the wire format simple for
    /// both hand-written planners and language models; which fields matter depends on <see cref="type"/>.
    /// </summary>
    public sealed class Element
    {
        public string id;
        public string type = ElementType.Label;
        public Anchor anchor = new Anchor();
        /// <summary>0 = always visible. Otherwise only visible while the build is on this step.</summary>
        public int step;
        public string style = ElementStyle.Accent;
        public float scale = 1f;

        /// <summary>label, pin (optional caption).</summary>
        public string text;
        /// <summary>card, chart, checklist, panel.</summary>
        public string title;
        /// <summary>card, panel.</summary>
        public string body;
        /// <summary>pin, card.</summary>
        public int number;
        /// <summary>gizmo: turn-cw | turn-ccw | press | inspect.</summary>
        public string action;
        /// <summary>flow: centre-line in the anchor entity's local frame.</summary>
        public List<Vec3> points = new List<Vec3>();
        /// <summary>flow: pipe radius in metres.</summary>
        public float radius = 0.07f;
        /// <summary>flow: entity ids to light up.</summary>
        public List<string> entities = new List<string>();
        /// <summary>chart.</summary>
        public List<float> values = new List<float>();
        public List<string> labels = new List<string>();
        public string unit;
        /// <summary>checklist.</summary>
        public List<ChecklistItem> items = new List<ChecklistItem>();
        /// <summary>checklist: initials of people on the task.</summary>
        public List<string> people = new List<string>();
        public int more;
        /// <summary>avatar.</summary>
        public string name;

        public Element Clone()
        {
            var e = (Element)MemberwiseClone();
            e.anchor = anchor.Clone();
            e.points = new List<Vec3>(points);
            e.entities = new List<string>(entities);
            e.values = new List<float>(values);
            e.labels = new List<string>(labels);
            e.people = new List<string>(people);
            e.items = new List<ChecklistItem>();
            foreach (ChecklistItem i in items) e.items.Add(new ChecklistItem { text = i.text, done = i.done });
            return e;
        }
    }

    public static class BuildKind
    {
        public const string Labels = "labels";
        public const string Walkthrough = "walkthrough";
        public const string Flow = "flow";
        public const string Faults = "faults";
        public const string Dashboard = "dashboard";
        public const string Checklist = "checklist";
        public const string Session = "session";
        public const string Note = "note";
        public const string Custom = "custom";
    }

    /// <summary>A named group of elements produced by one request, such as "Walkthrough" or "Dashboard".</summary>
    public sealed class Build
    {
        public string id;
        public string kind = BuildKind.Custom;
        /// <summary>Short name shown in the "Built ..." chip.</summary>
        public string title;
        public List<Element> elements = new List<Element>();
        /// <summary>Number of steps, or 0 when the build is not sequenced.</summary>
        public int stepCount;
        /// <summary>1-based current step for sequenced builds.</summary>
        public int currentStep;

        public Element Find(string elementId)
        {
            foreach (Element e in elements) if (e.id == elementId) return e;
            return null;
        }

        public bool IsVisible(Element e) => e.step == 0 || e.step == currentStep;

        public Build Clone()
        {
            var b = new Build { id = id, kind = kind, title = title, stepCount = stepCount, currentStep = currentStep };
            foreach (Element e in elements) b.elements.Add(e.Clone());
            return b;
        }
    }

    public static class OpType
    {
        /// <summary>Add a build, replacing any existing build with the same id.</summary>
        public const string Add = "add";
        public const string Remove = "remove";
        /// <summary>Move a sequenced build to an absolute step.</summary>
        public const string Step = "step";
        /// <summary>Remove every build.</summary>
        public const string Clear = "clear";
    }

    public sealed class PlanOp
    {
        public string op = OpType.Add;
        public Build build;
        public string buildId;
        public int step;

        public static PlanOp Add(Build b) => new PlanOp { op = OpType.Add, build = b, buildId = b.id };
        public static PlanOp Remove(string id) => new PlanOp { op = OpType.Remove, buildId = id };
        public static PlanOp Step(string id, int step) => new PlanOp { op = OpType.Step, buildId = id, step = step };
        public static PlanOp Clear() => new PlanOp { op = OpType.Clear };
    }

    /// <summary>What a planner returns for one request.</summary>
    public sealed class PlanResult
    {
        /// <summary>One short sentence for the user. Spoken or shown under the prompt bar.</summary>
        public string reply;
        /// <summary>What was built, for the "Built ..." chip. Empty when nothing was built.</summary>
        public string chip;
        public List<PlanOp> ops = new List<PlanOp>();
        /// <summary>Which planner produced this ("offline", "claude-opus-5-5", ...). Filled by the planner.</summary>
        public string planner;
        public double latencyMs;
        /// <summary>Set when planning failed; <see cref="reply"/> then carries a user-facing explanation.</summary>
        public string error;

        public bool HasChanges => ops.Count > 0;
    }
}
