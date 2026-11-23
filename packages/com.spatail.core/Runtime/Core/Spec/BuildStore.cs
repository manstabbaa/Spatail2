using System;
using System.Collections.Generic;

namespace Spatail.Core
{
    /// <summary>
    /// The live set of builds in the room. Planners produce <see cref="PlanResult"/>s; applying them here is the only
    /// way content changes, which keeps rendering, the session link and the dev tool all looking at one truth.
    /// </summary>
    public sealed class BuildStore
    {
        readonly List<Build> _builds = new List<Build>();

        public IReadOnlyList<Build> Builds => _builds;

        /// <summary>A build appeared, or was replaced by a new version with the same id.</summary>
        public event Action<Build> Added;
        public event Action<Build> Removed;
        /// <summary>Something inside a build changed without replacing it (step moved, checklist item ticked).</summary>
        public event Action<Build> Changed;

        public Build Get(string id)
        {
            foreach (Build b in _builds) if (b.id == id) return b;
            return null;
        }

        public Build FindByKind(string kind)
        {
            for (int i = _builds.Count - 1; i >= 0; i--) if (_builds[i].kind == kind) return _builds[i];
            return null;
        }

        /// <summary>The most recently added build, or null.</summary>
        public Build Latest => _builds.Count > 0 ? _builds[_builds.Count - 1] : null;

        /// <summary>The most recent build that has steps, or null.</summary>
        public Build LatestStepped
        {
            get
            {
                for (int i = _builds.Count - 1; i >= 0; i--) if (_builds[i].stepCount > 0) return _builds[i];
                return null;
            }
        }

        /// <summary>
        /// House rule shared by every host: the labels left by the room scan make way for the first real content,
        /// as they do in the reference animation. Adds the removal to the plan itself so it also reaches session
        /// peers and anything replaying the plan. Call before <see cref="Apply"/>.
        /// </summary>
        public void RetireScanLabels(PlanResult plan)
        {
            if (plan == null || Get(BuildKind.Labels) == null) return;
            bool addsContent = false, alreadyRemoved = false;
            foreach (PlanOp op in plan.ops)
            {
                if (op.op == OpType.Add && op.build != null && op.build.kind != BuildKind.Labels) addsContent = true;
                if ((op.op == OpType.Remove && op.buildId == BuildKind.Labels) || op.op == OpType.Clear) alreadyRemoved = true;
            }
            if (addsContent && !alreadyRemoved) plan.ops.Insert(0, PlanOp.Remove(BuildKind.Labels));
        }

        public void Apply(PlanResult plan)
        {
            if (plan == null) return;
            foreach (PlanOp op in plan.ops)
            {
                switch (op.op)
                {
                    case OpType.Add:
                        if (op.build != null) Add(op.build);
                        break;
                    case OpType.Remove:
                        Remove(op.buildId);
                        break;
                    case OpType.Step:
                        SetStep(op.buildId, op.step);
                        break;
                    case OpType.Clear:
                        Clear();
                        break;
                }
            }
        }

        public void Add(Build build)
        {
            Build existing = Get(build.id);
            if (existing != null)
            {
                _builds.Remove(existing);
                if (Removed != null) Removed(existing);
            }
            _builds.Add(build);
            if (Added != null) Added(build);
        }

        public bool Remove(string id)
        {
            Build b = Get(id);
            if (b == null) return false;
            _builds.Remove(b);
            if (Removed != null) Removed(b);
            return true;
        }

        public void Clear()
        {
            var copy = new List<Build>(_builds);
            _builds.Clear();
            if (Removed != null) foreach (Build b in copy) Removed(b);
        }

        public bool SetStep(string id, int step)
        {
            Build b = Get(id);
            if (b == null || b.stepCount == 0) return false;
            int clamped = Math.Max(1, Math.Min(b.stepCount, step));
            if (clamped == b.currentStep) return false;
            b.currentStep = clamped;
            if (Changed != null) Changed(b);
            return true;
        }

        public bool ToggleChecklistItem(string buildId, string elementId, int index)
        {
            Build b = Get(buildId);
            Element e = b != null ? b.Find(elementId) : null;
            if (e == null || index < 0 || index >= e.items.Count) return false;
            e.items[index].done = !e.items[index].done;
            if (Changed != null) Changed(b);
            return true;
        }

        /// <summary>The whole store as one plan that recreates it. Used to bring a newly joined peer up to date.</summary>
        public PlanResult Snapshot()
        {
            var plan = new PlanResult { reply = "", chip = "" };
            plan.ops.Add(PlanOp.Clear());
            foreach (Build b in _builds) plan.ops.Add(PlanOp.Add(b.Clone()));
            return plan;
        }
    }
}
