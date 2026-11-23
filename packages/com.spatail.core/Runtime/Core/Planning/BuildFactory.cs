using System.Collections.Generic;

namespace Spatail.Core
{
    /// <summary>
    /// Recipes that turn knowledge records into builds. The offline planner uses these directly; they also document,
    /// by example, what well-formed builds look like for anyone writing a new planner.
    /// </summary>
    public static class BuildFactory
    {
        /// <summary>Announces what the scan recognised: one outlined label per notable entity.</summary>
        public static Build Labels(RoomModel room)
        {
            var b = new Build { id = BuildKind.Labels, kind = BuildKind.Labels, title = "Labels" };
            foreach (RoomEntity e in room.entities)
            {
                if (!e.scanLabel || string.IsNullOrEmpty(e.label)) continue;
                b.elements.Add(new Element
                {
                    id = "label-" + e.id,
                    type = ElementType.Label,
                    text = e.label,
                    style = ElementStyle.Neutral,
                    anchor = new Anchor { entity = e.id, at = AnchorMode.Top },
                });
            }
            return b;
        }

        /// <summary>A numbered pin on every step's part, plus a card and motion cue for the current step.</summary>
        public static Build Walkthrough(Procedure procedure, RoomModel room)
        {
            var b = new Build { id = BuildKind.Walkthrough, kind = BuildKind.Walkthrough, title = "Walkthrough" };
            int n = 0;
            foreach (ProcedureStep step in procedure.steps)
            {
                if (room.Get(step.entity) == null) continue;
                n++;
                b.elements.Add(new Element
                {
                    id = "pin-" + n,
                    type = ElementType.Pin,
                    number = n,
                    anchor = new Anchor { entity = step.entity, at = AnchorMode.Top },
                });
                b.elements.Add(new Element
                {
                    id = "card-" + n,
                    type = ElementType.Card,
                    step = n,
                    number = n,
                    title = step.title,
                    body = step.body,
                    anchor = new Anchor { entity = step.entity, at = AnchorMode.Callout },
                });
                if (step.action == "turn-cw" || step.action == "turn-ccw")
                {
                    b.elements.Add(new Element
                    {
                        id = "gizmo-" + n,
                        type = ElementType.Gizmo,
                        step = n,
                        action = step.action,
                        anchor = new Anchor { entity = step.entity, at = AnchorMode.Top },
                    });
                }
                else if (step.action == "press" || step.action == "inspect")
                {
                    b.elements.Add(new Element
                    {
                        id = "focus-" + n,
                        type = ElementType.Highlight,
                        step = n,
                        action = step.action,
                        anchor = new Anchor { entity = step.entity, at = AnchorMode.Center },
                    });
                }
            }
            b.stepCount = n;
            b.currentStep = n > 0 ? 1 : 0;
            return b;
        }

        /// <summary>Animated arrows through the pipework with the wetted sections lit and the ends named.</summary>
        public static Build Flow(FlowDef flow, SiteDefinition site, RoomModel room)
        {
            var b = new Build { id = BuildKind.Flow, kind = BuildKind.Flow, title = "Live flow" };
            var path = new Element
            {
                id = "flow-path",
                type = ElementType.Flow,
                radius = flow.radius,
                text = flow.rate,
                anchor = new Anchor { entity = site.equipmentId, at = AnchorMode.Center },
            };
            path.points.AddRange(flow.path);
            foreach (string id in flow.highlight)
                if (room.Get(id) != null) path.entities.Add(id);
            b.elements.Add(path);

            if (room.Get(flow.inletEntity) != null)
                b.elements.Add(new Element
                {
                    id = "label-inlet", type = ElementType.Label, text = "Inlet",
                    anchor = new Anchor { entity = flow.inletEntity, at = AnchorMode.Top },
                });
            if (room.Get(flow.outletEntity) != null)
                b.elements.Add(new Element
                {
                    id = "label-outlet", type = ElementType.Label, text = "Outlet",
                    anchor = new Anchor { entity = flow.outletEntity, at = AnchorMode.Top },
                });
            return b;
        }

        /// <summary>Warning-tinted volumes and markers on every part with a fault on record.</summary>
        public static Build Faults(Knowledge knowledge, RoomModel room)
        {
            var b = new Build { id = BuildKind.Faults, kind = BuildKind.Faults, title = "Fault history" };
            int n = 0;
            string first = null;
            foreach (FaultRecord fault in knowledge.faults)
            {
                if (room.Get(fault.entity) == null) continue;
                n++;
                if (first == null) first = fault.entity;
                b.elements.Add(new Element
                {
                    id = "fault-" + n,
                    type = ElementType.Highlight,
                    style = ElementStyle.Warning,
                    title = fault.title,
                    body = Join(fault.when, fault.detail),
                    anchor = new Anchor { entity = fault.entity, at = AnchorMode.Center },
                });
                b.elements.Add(new Element
                {
                    id = "fault-pin-" + n,
                    type = ElementType.Pin,
                    style = ElementStyle.Warning,
                    text = "!",
                    anchor = new Anchor { entity = fault.entity, at = AnchorMode.Top },
                });
            }
            if (first != null)
                b.elements.Add(new Element
                {
                    id = "fault-title",
                    type = ElementType.Label,
                    style = ElementStyle.Warning,
                    text = knowledge.faultsTitle,
                    anchor = new Anchor { entity = first, at = AnchorMode.Callout, offset = new Vec3(0, 0.12f, 0) },
                });
            return b;
        }

        /// <summary>A bar chart with a trend line, standing on the floor (or a surface) by the chosen entity.</summary>
        public static Build Dashboard(MetricDef metric, string entityId, string at)
        {
            var b = new Build { id = BuildKind.Dashboard, kind = BuildKind.Dashboard, title = "Dashboard" };
            var chart = new Element
            {
                id = "chart-" + metric.id,
                type = ElementType.Chart,
                title = metric.title,
                unit = metric.unit,
                anchor = new Anchor { entity = entityId, at = at },
            };
            chart.values.AddRange(metric.values);
            chart.labels.AddRange(metric.labels);
            b.elements.Add(chart);
            return b;
        }

        /// <summary>The spares list as a tickable panel, with who else is on the job.</summary>
        public static Build Checklist(Knowledge knowledge, string entityId, string at)
        {
            var b = new Build { id = BuildKind.Checklist, kind = BuildKind.Checklist, title = "Checklist" };
            var list = new Element
            {
                id = "checklist-spares",
                type = ElementType.Checklist,
                title = knowledge.sparesTitle,
                anchor = new Anchor { entity = entityId, at = at },
            };
            foreach (SpareItem item in knowledge.spares) list.items.Add(new ChecklistItem { text = item.text, done = item.done });
            list.people.AddRange(knowledge.sparesPeople);
            list.more = knowledge.sparesMore;
            b.elements.Add(list);
            return b;
        }

        /// <summary>A presence marker for a collaborator joining the room.</summary>
        public static Build Session(Person person, string entityId)
        {
            var b = new Build { id = BuildKind.Session, kind = BuildKind.Session, title = "Shared session" };
            b.elements.Add(new Element
            {
                id = "avatar-" + TextMatch.Normalize(person.name).Replace(' ', '-'),
                type = ElementType.Avatar,
                name = person.name,
                text = person.initial,
                body = person.role,
                anchor = new Anchor { entity = entityId, at = AnchorMode.FloorNear, offset = new Vec3(0.95f, 0, 0) },
            });
            return b;
        }

        /// <summary>A short explanation of one entity, beside it.</summary>
        public static Build Describe(RoomEntity entity)
        {
            var b = new Build { id = BuildKind.Note, kind = BuildKind.Note, title = "Note" };
            b.elements.Add(new Element
            {
                id = "note-" + entity.id,
                type = ElementType.Panel,
                title = entity.label,
                body = string.IsNullOrEmpty(entity.about) ? "No notes on record for this yet." : entity.about,
                anchor = new Anchor { entity = entity.id, at = AnchorMode.Callout },
            });
            b.elements.Add(new Element
            {
                id = "note-focus-" + entity.id,
                type = ElementType.Highlight,
                anchor = new Anchor { entity = entity.id, at = AnchorMode.Center },
            });
            return b;
        }

        static string Join(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b ?? "";
            if (string.IsNullOrEmpty(b)) return a;
            return a + " - " + b;
        }
    }
}
