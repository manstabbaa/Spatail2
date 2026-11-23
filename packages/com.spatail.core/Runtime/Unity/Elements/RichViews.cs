using System.Collections.Generic;
using Spatail.Core;
using UnityEngine;

namespace Spatail
{
    /// <summary>Chevrons streaming along the pipework, with the wetted sections lit.</summary>
    public sealed class FlowView : ElementView
    {
        readonly List<Material> _owned = new List<Material>();

        protected override bool AnimateScale => false;

        protected override void BuildView()
        {
            // Flows are drawn in world space; the path comes from the layout solver.
            transform.position = Vector3.zero;
            transform.rotation = Quaternion.identity;
            List<Vec3> path = LayoutSolver.FlowPath(Element, App.Room);
            Color color = Theme.StyleColor(Element.style);

            if (path.Count >= 2)
            {
                var builder = new MeshBuilder();
                float distance = 0f;
                float radius = Element.radius * 1.2f;
                for (int i = 0; i < path.Count - 1; i++)
                {
                    Vector3 from = path[i].ToUnity(), to = path[i + 1].ToUnity();
                    builder.AddTubeSegment(from, to, radius, 24, Color.white, distance);
                    distance += Vector3.Distance(from, to);
                }
                Material flow = Assets.Flow(color.WithAlpha(0.85f));
                _owned.Add(flow);
                MeshFactory.Spawn("Stream", Content, builder.ToMesh("Spatail Flow"), flow);
            }

            Material glow = Assets.Glow(color.WithAlpha(0.2f), 0.5f);
            _owned.Add(glow);
            foreach (string id in Element.entities)
            {
                RoomEntity entity = App.Room.Get(id);
                if (entity == null) continue;
                var holder = new GameObject("Lit " + id);
                holder.transform.SetParent(Content, false);
                holder.transform.position = entity.position.ToUnity();
                holder.transform.rotation = entity.rotation.ToUnity();
                Volume(holder.transform, entity, glow, 1.02f);
            }

            if (!string.IsNullOrEmpty(Element.text) && path.Count >= 2)
            {
                // The flow rate rides above the middle of the first run.
                Vector3 mid = (path[0].ToUnity() + path[1].ToUnity()) * 0.5f + Vector3.up * (Element.radius + 0.2f);
                var tag = new GameObject("Rate");
                tag.transform.SetParent(Content, false);
                tag.transform.position = mid;
                SpatialText text = SpatialText.Create(tag.transform, Element.text, Theme.TextSmall, color, TextWeight.SemiBold, TextAlign.Center);
                text.transform.localPosition = new Vector3(0, 0, -Theme.Layer);
                Panel.Create(tag.transform, "Background", new Vector2(text.Size.x + 0.028f, 0.036f), 0.018f, Theme.Surface, color);
                tag.AddComponent<Billboard>();
            }
        }

        void OnDestroy()
        {
            foreach (Material m in _owned) if (m != null) Destroy(m);
        }
    }

    /// <summary>A bar chart with a trend line, standing on a base slab.</summary>
    public sealed class ChartView : ElementView
    {
        const float Pitch = 0.085f, BarWidth = 0.055f, MaxHeight = 0.42f, Depth = 0.24f;

        readonly List<Transform> _bars = new List<Transform>();
        readonly List<float> _heights = new List<float>();
        Transform _trend;
        float _age;

        protected override void BuildView()
        {
            int n = Element.values.Count;
            float width = LayoutSolver.ChartWidth(n);
            Color main = Theme.StyleColor(Element.style);

            // Base slab: an accent rim under a white top.
            GameObject rim = MeshFactory.Spawn("Rim", Content, MeshFactory.UnitBox, Assets.SharedSolid("chart-rim-" + Element.style, main, 0.6f));
            rim.transform.localPosition = new Vector3(0, 0.005f, 0);
            rim.transform.localScale = new Vector3(width + 0.012f, 0.01f, Depth + 0.012f);
            GameObject slab = MeshFactory.Spawn("Slab", Content, MeshFactory.UnitBox, Assets.SharedSolid("chart-slab", Theme.Surface, 0.5f));
            slab.transform.localPosition = new Vector3(0, 0.009f, 0);
            slab.transform.localScale = new Vector3(width, 0.014f, Depth);

            float max = 0.0001f;
            foreach (float v in Element.values) if (v > max) max = v;

            Material barMaterial = Assets.SharedSolid("chart-bar-" + Element.style, Color.Lerp(main, Color.white, 0.22f), 1f, 0.15f);
            Material trendMaterial = Assets.SharedSolid("chart-trend-" + Element.style, Color.Lerp(main, Color.black, 0.25f), 0f);
            var trend = new GameObject("Trend");
            trend.transform.SetParent(Content, false);
            _trend = trend.transform;

            Vector3 previous = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                float h = Mathf.Max(0.02f, Element.values[i] / max * MaxHeight);
                float x = (i - (n - 1) * 0.5f) * Pitch;
                // Bars pivot at their base so they can grow upward.
                var pivot = new GameObject("Bar " + i);
                pivot.transform.SetParent(Content, false);
                pivot.transform.localPosition = new Vector3(x, 0.016f, 0.02f);
                GameObject bar = MeshFactory.Spawn("Mesh", pivot.transform, MeshFactory.UnitBox, barMaterial);
                bar.transform.localPosition = new Vector3(0, 0.5f, 0);
                pivot.transform.localScale = new Vector3(BarWidth, 0.0001f, BarWidth);
                _bars.Add(pivot.transform);
                _heights.Add(h);

                var point = new Vector3(x, 0.016f + h + 0.04f, 0.02f);
                GameObject dot = MeshFactory.Spawn("Dot", trend.transform, MeshFactory.UnitSphere, trendMaterial);
                dot.transform.localPosition = point;
                dot.transform.localScale = Vector3.one * 0.016f;
                if (i > 0) MeshFactory.Rod("Line", trend.transform, previous, point, 0.005f, trendMaterial);
                previous = point;
            }
            trend.SetActive(false);

            // Title plate along the front edge, tipped back toward the viewer.
            var plate = new GameObject("Title");
            plate.transform.SetParent(Content, false);
            plate.transform.localPosition = new Vector3(0, 0.05f, -Depth * 0.5f - 0.035f);
            plate.transform.localRotation = Quaternion.Euler(35f, 0f, 0f);
            string caption = string.IsNullOrEmpty(Element.unit) ? (Element.title ?? "") : (Element.title ?? "") + "  ·  " + Element.unit;
            SpatialText text = SpatialText.Create(plate.transform, caption, Theme.TextLabel * 0.9f, main, TextWeight.SemiBold, TextAlign.Center);
            text.transform.localPosition = new Vector3(0, 0, -Theme.Layer);
            Panel.Create(plate.transform, "Background", new Vector2(text.Size.x + 0.04f, 0.05f), 0.014f, Theme.Surface, main);
        }

        protected override void Update()
        {
            base.Update();
            if (_bars.Count == 0 || !Content.gameObject.activeInHierarchy) return;
            _age += Time.deltaTime;
            bool done = true;
            for (int i = 0; i < _bars.Count; i++)
            {
                // Bars rise one after another, left to right.
                float t = Mathf.Clamp01((_age - 0.15f - i * 0.06f) / 0.45f);
                if (t < 1f) done = false;
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                Vector3 s = _bars[i].localScale;
                s.y = Mathf.Max(0.0001f, _heights[i] * eased);
                _bars[i].localScale = s;
            }
            if (done && _trend != null && !_trend.gameObject.activeSelf) _trend.gameObject.SetActive(true);
        }
    }

    /// <summary>A tickable list with the people on the job along the bottom.</summary>
    public sealed class ChecklistView : ElementView
    {
        readonly List<Panel> _boxes = new List<Panel>();
        readonly List<SpatialText> _ticks = new List<SpatialText>();
        readonly List<SpatialText> _labels = new List<SpatialText>();
        Color _main;

        protected override void BuildView()
        {
            const float width = 0.3f, pad = 0.024f, row = 0.042f, box = 0.027f, avatar = 0.036f;
            _main = Theme.StyleColor(Element.style);
            bool people = Element.people.Count > 0 || Element.more > 0;

            SpatialText title = SpatialText.Create(Content, Element.title ?? "", Theme.TextTitle, Theme.Ink, TextWeight.SemiBold, TextAlign.Left, TextAnchor3D.Top, width - pad * 2f);
            float height = pad + title.Size.y + 0.02f + Element.items.Count * row + (people ? 0.012f + avatar : 0f) + pad;
            float centreY = Placement.pivotBottom ? height * 0.5f : 0f;
            float top = centreY + height * 0.5f;
            float left = -width * 0.5f + pad;

            Panel background = Panel.Create(Content, "Background", new Vector2(width, height), 0.024f, Theme.Surface, _main, 0.003f);
            background.transform.localPosition = new Vector3(0, centreY, 0);
            title.transform.localPosition = new Vector3(left, top - pad, -Theme.Layer);

            float y = top - pad - title.Size.y - 0.02f;
            for (int i = 0; i < Element.items.Count; i++)
            {
                int index = i;
                var item = new GameObject("Item " + i);
                item.transform.SetParent(Content, false);
                item.transform.localPosition = new Vector3(0, y - row * 0.5f, -Theme.Layer);

                var holder = new GameObject("Box");
                holder.transform.SetParent(item.transform, false);
                holder.transform.localPosition = new Vector3(left + box * 0.5f, 0, 0);
                _boxes.Add(Panel.Create(holder.transform, "Square", new Vector2(box, box), 0.007f, Theme.Surface, _main, 0.0028f));
                SpatialText tick = SpatialText.Create(holder.transform, "✓", 0.024f, Color.white, TextWeight.SemiBold, TextAlign.Center);
                tick.transform.localPosition = new Vector3(0, 0, -Theme.Layer);
                _ticks.Add(tick);

                SpatialText label = SpatialText.Create(item.transform, Element.items[i].text, Theme.TextBody * 1.08f, Theme.Ink, TextWeight.Medium);
                label.transform.localPosition = new Vector3(left + box + 0.014f, 0, 0);
                _labels.Add(label);

                Interactable.Add(item, new Vector3(width - pad, row, 0.02f), () => App.ToggleChecklistItem(Build.id, Element.id, index));
                y -= row;
            }

            if (people)
            {
                float x = left + avatar * 0.5f;
                float py = y - 0.012f - avatar * 0.5f;
                var entries = new List<string>(Element.people);
                if (Element.more > 0) entries.Add("+" + Element.more);
                for (int i = 0; i < entries.Count; i++)
                {
                    bool overflow = Element.more > 0 && i == entries.Count - 1;
                    var chip = new GameObject("Person " + entries[i]);
                    chip.transform.SetParent(Content, false);
                    // Later avatars overlap earlier ones slightly and sit a hair closer to the viewer.
                    chip.transform.localPosition = new Vector3(x, py, -Theme.Layer * (1.5f + i * 0.5f));
                    Panel.Create(chip.transform, "Disc", new Vector2(avatar, avatar), avatar * 0.5f,
                        overflow ? Theme.AccentSoft : Color.Lerp(_main, Color.white, i * 0.22f), Theme.Surface, 0.0025f);
                    SpatialText initial = SpatialText.Create(chip.transform, entries[i], overflow ? 0.014f : 0.017f, overflow ? _main : Color.white, TextWeight.SemiBold, TextAlign.Center);
                    initial.transform.localPosition = new Vector3(0, 0, -Theme.Layer * 0.4f);
                    x += avatar * 0.78f;
                }
            }
            OnBuildChanged();
        }

        public override void OnBuildChanged()
        {
            for (int i = 0; i < _boxes.Count && i < Element.items.Count; i++)
            {
                bool done = Element.items[i].done;
                _boxes[i].SetColors(done ? _main : Theme.Surface, _main);
                _ticks[i].gameObject.SetActive(done);
                _labels[i].Color = done ? Theme.Muted : Theme.Ink;
            }
        }
    }

    /// <summary>
    /// A collaborator's presence marker. When a session peer with the same name is connected the marker follows
    /// their real position; otherwise it stands where the plan put it.
    /// </summary>
    public sealed class AvatarView : ElementView
    {
        Vector3 _home;
        Transform _ring;

        protected override void BuildView()
        {
            _home = transform.position;
            Color main = Theme.StyleColor(Element.style);

            var builder = new MeshBuilder();
            builder.AddCylinder(Matrix4x4.Translate(new Vector3(0, 0.23f, 0)), 0.12f, 0.035f, 0.44f, 28, Color.white);
            MeshFactory.Spawn("Body", Content, builder.ToMesh("Spatail Avatar"), Assets.SharedSolid("avatar-" + Element.style, main, 1f, 0.2f));

            GameObject ring = MeshFactory.Spawn("Ring", Content, MeshFactory.Disc, Assets.SharedSolid("avatar-ring-" + Element.style, main.WithAlpha(0.22f), 0f));
            ring.transform.localPosition = new Vector3(0, 0.004f, 0);
            ring.transform.localScale = new Vector3(0.44f, 1f, 0.44f);
            _ring = ring.transform;

            // Head disc and name tag turn to face the viewer.
            var face = new GameObject("Face");
            face.transform.SetParent(Content, false);
            face.transform.localPosition = new Vector3(0, 0.55f, 0);
            Panel.Create(face.transform, "Disc", new Vector2(0.14f, 0.14f), 0.07f, main, Color.white, 0.004f);
            string initial = !string.IsNullOrEmpty(Element.text) ? Element.text : (!string.IsNullOrEmpty(Element.name) ? Element.name.Substring(0, 1) : "?");
            SpatialText letter = SpatialText.Create(face.transform, initial, 0.07f, Color.white, TextWeight.SemiBold, TextAlign.Center);
            letter.transform.localPosition = new Vector3(0, 0, -Theme.Layer);
            face.AddComponent<Billboard>();

            if (!string.IsNullOrEmpty(Element.name))
            {
                var tag = new GameObject("Name");
                tag.transform.SetParent(Content, false);
                tag.transform.localPosition = new Vector3(0, 0.72f, 0);
                SpatialText name = SpatialText.Create(tag.transform, Element.name, Theme.TextLabel, main, TextWeight.SemiBold, TextAlign.Center);
                name.transform.localPosition = new Vector3(0, 0, -Theme.Layer);
                Panel.Create(tag.transform, "Background", new Vector2(name.Size.x + 0.04f, 0.05f), 0.025f, Theme.Surface, main);
                tag.AddComponent<Billboard>();
            }
        }

        protected override void Update()
        {
            base.Update();
            if (_ring != null)
            {
                float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 2.4f);
                _ring.localScale = new Vector3(0.44f * pulse, 1f, 0.44f * pulse);
            }
            if (App == null || App.Session == null) return;
            Vector3 goal = _home;
            foreach (Peer peer in App.Session.Peers)
            {
                if (!peer.hasPose || !string.Equals(peer.name, Element.name, System.StringComparison.OrdinalIgnoreCase)) continue;
                Vector3 world = App.Station.TransformPoint(peer.position).ToUnity();
                goal = new Vector3(world.x, App.Room.floorY, world.z);
                break;
            }
            transform.position = Vector3.Lerp(transform.position, goal, 1f - Mathf.Exp(-8f * Time.deltaTime));
        }
    }
}
