using System;
using System.Collections.Generic;
using Spatail.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Spatail
{
    /// <summary>
    /// Draws what programs build. Programs speak in a handful of primitive node kinds (group, box, sphere,
    /// cylinder, torus, disc, tube, mesh, text, panel); this turns each into a GameObject. There is nothing here
    /// about charts, labels or walkthroughs: those exist only as code the planner writes.
    /// </summary>
    public sealed class SceneHost : MonoBehaviour
    {
        sealed class NodeView
        {
            public int id;
            public string kind;
            public GameObject go;
            public Dictionary<string, object> props;
            public Material material;       // owned by this node
            public Mesh mesh;               // owned by this node
            public SpatialText text;
            public Panel panel;
            public bool interactive;
        }

        readonly Dictionary<int, NodeView> _nodes = new Dictionary<int, NodeView>();
        readonly List<NodeView> _interactive = new List<NodeView>();
        float _refit;

        /// <summary>Raised for pointer events on interactive nodes: (node id, "click" | "enter" | "exit").</summary>
        public event Action<int, string> NodeEvent;

        public int NodeCount => _nodes.Count;

        // ---- node operations (called by the script runtime) --------------------------------------

        public void Create(int id, string kind, Dictionary<string, object> props)
        {
            var view = new NodeView { id = id, kind = kind, props = new Dictionary<string, object>(props), go = new GameObject(kind + " " + id) };
            _nodes[id] = view;
            view.go.transform.SetParent(ParentOf(props), false);
            Build(view);
            ApplyCommon(view, view.props);
        }

        public void UpdateProps(int id, Dictionary<string, object> changed)
        {
            NodeView view;
            if (!_nodes.TryGetValue(id, out view)) return;
            bool rebuild = false;
            foreach (var kv in changed)
            {
                view.props[kv.Key] = kv.Value;
                if (IsShapeProp(view.kind, kv.Key)) rebuild = true;
            }
            if (changed.ContainsKey("parent")) view.go.transform.SetParent(ParentOf(changed), false);
            if (rebuild) Build(view);
            ApplyCommon(view, rebuild ? view.props : changed);
        }

        public void SetTransform(int id, Vec3 position, Quat rotation, Vec3 scale)
        {
            NodeView view;
            if (!_nodes.TryGetValue(id, out view)) return;
            Transform t = view.go.transform;
            t.localPosition = position.ToUnity();
            t.localRotation = rotation.ToUnity();
            t.localScale = scale.ToUnity();
        }

        public void DestroyNode(int id)
        {
            NodeView view;
            if (!_nodes.TryGetValue(id, out view)) return;
            _nodes.Remove(id);
            _interactive.Remove(view);
            if (view.material != null) Destroy(view.material);
            if (view.mesh != null) Destroy(view.mesh);
            if (view.go != null) Destroy(view.go);
        }

        Transform ParentOf(Dictionary<string, object> props)
        {
            NodeView parent;
            int parentId = props.Int("parent");
            return parentId != 0 && _nodes.TryGetValue(parentId, out parent) && parent.go != null ? parent.go.transform : transform;
        }

        static bool IsShapeProp(string kind, string key)
        {
            switch (key)
            {
                case "size": case "radius": case "height": case "topRadius": case "tube": case "arc":
                case "points": case "flow": case "positions": case "indices": case "colors":
                case "glow": case "shade":
                    return kind != "text" && kind != "panel" && kind != "group";
                default:
                    return false;
            }
        }

        // ---- building visuals ------------------------------------------------------------------

        void Build(NodeView view)
        {
            Dictionary<string, object> p = view.props;
            switch (view.kind)
            {
                case "group":
                    return;

                case "text":
                    if (view.text == null) view.text = view.go.AddComponent<SpatialText>();
                    return;   // text properties are applied in ApplyCommon

                case "panel":
                    if (view.panel == null)
                    {
                        view.panel = Panel.Create(view.go.transform, "Panel", new Vector2(0.1f, 0.1f), 0.01f, Theme.Surface, Theme.Accent);
                    }
                    return;
            }

            // Solids: generate the mesh at its real size so children are not distorted by scale.
            var builder = new MeshBuilder();
            Color white = Color.white;
            bool flow = false;
            switch (view.kind)
            {
                case "box":
                    builder.AddBox(Matrix4x4.identity, p.Vec("size", new Vec3(0.1f, 0.1f, 0.1f)).ToUnity(), white);
                    break;
                case "sphere":
                    builder.AddSphere(Matrix4x4.identity, p.Float("radius", 0.05f), 24, white);
                    break;
                case "cylinder":
                {
                    float radius = p.Float("radius", 0.05f);
                    builder.AddCylinder(Matrix4x4.identity, radius, p.Has("topRadius") ? p.Float("topRadius") : radius, p.Float("height", 0.1f), 28, white);
                    break;
                }
                case "torus":
                    builder.AddTorus(Matrix4x4.identity, p.Float("radius", 0.1f), p.Float("tube", 0.01f), 40, 10, white, p.Float("arc", 360f));
                    break;
                case "disc":
                    builder.AddDisc(Matrix4x4.identity, p.Float("radius", 0.1f), 40, white, true);
                    builder.AddDisc(Matrix4x4.identity, p.Float("radius", 0.1f), 40, white, false);
                    break;
                case "tube":
                {
                    List<Vector3> points = Points(p.Arr("points"));
                    float radius = p.Float("radius", 0.01f), distance = 0f;
                    for (int i = 0; i + 1 < points.Count; i++)
                    {
                        builder.AddTubeSegment(points[i], points[i + 1], radius, 20, white, distance);
                        distance += Vector3.Distance(points[i], points[i + 1]);
                    }
                    // Round the joints so bends do not show gaps.
                    for (int i = 1; i + 1 < points.Count; i++) builder.AddSphere(Matrix4x4.Translate(points[i]), radius, 12, white);
                    flow = p.Has("flow");
                    break;
                }
                case "mesh":
                    AddCustomMesh(builder, p);
                    break;
                default:
                    Debug.LogWarning("[Spatail] Unknown node kind '" + view.kind + "'.");
                    return;
            }

            if (view.mesh != null) Destroy(view.mesh);
            view.mesh = builder.ToMesh("Spatail " + view.kind);
            var filter = view.go.GetComponent<MeshFilter>();
            if (filter == null) filter = view.go.AddComponent<MeshFilter>();
            filter.sharedMesh = view.mesh;
            var meshRenderer = view.go.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                meshRenderer = view.go.AddComponent<MeshRenderer>();
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
            }

            if (view.material != null) Destroy(view.material);
            Color color = Color32(p.Str("color"), Theme.Accent);
            if (flow)
            {
                view.material = Assets.Flow(color);
                var f = p.Obj("flow");
                if (f != null)
                {
                    view.material.SetFloat("_Speed", f.Float("speed", 0.35f));
                    view.material.SetFloat("_Density", f.Float("density", 7f));
                }
            }
            else if (p.Bool("glow"))
            {
                view.material = Assets.Glow(color);
            }
            else
            {
                view.material = Assets.Solid(color, p.Bool("shade", true) ? 1f : 0f, p.Bool("shade", true) ? 0.15f : 0f);
            }
            meshRenderer.sharedMaterial = view.material;
        }

        static void AddCustomMesh(MeshBuilder builder, Dictionary<string, object> p)
        {
            var positions = p.Arr("positions");
            var indices = p.Arr("indices");
            var colors = p.Arr("colors");
            if (positions == null || indices == null) return;
            var points = new List<Vector3>();
            for (int i = 0; i + 2 < positions.Count; i += 3)
                points.Add(new Vector3((float)Json.ToDouble(positions[i]), (float)Json.ToDouble(positions[i + 1]), (float)Json.ToDouble(positions[i + 2])));
            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                int a = (int)Json.ToDouble(indices[i]), b = (int)Json.ToDouble(indices[i + 1]), c = (int)Json.ToDouble(indices[i + 2]);
                if (a < 0 || b < 0 || c < 0 || a >= points.Count || b >= points.Count || c >= points.Count) continue;
                Color ca = VertexColor(colors, a), cb = VertexColor(colors, b), cc = VertexColor(colors, c);
                builder.AddTriangle(points[a], points[b], points[c], ca, cb, cc);
            }
        }

        static Color VertexColor(List<object> colors, int index) =>
            colors != null && index < colors.Count ? Color32(colors[index] as string, Color.white) : Color.white;

        static List<Vector3> Points(List<object> list)
        {
            var points = new List<Vector3>();
            if (list == null) return points;
            foreach (object item in list) points.Add(Json.ToVec3(item).ToUnity());
            return points;
        }

        // ---- properties every kind shares ----------------------------------------------------------

        void ApplyCommon(NodeView view, Dictionary<string, object> p)
        {
            Dictionary<string, object> all = view.props;
            float opacity = all.Float("opacity", 1f);

            if (view.kind == "text")
            {
                SpatialText t = view.text;
                t.Text = all.Str("text", "");
                t.FontSize = all.Float("size", Theme.TextBody);
                t.Weight = all.Str("weight", "medium") == "semibold" ? TextWeight.SemiBold : TextWeight.Medium;
                string align = all.Str("align", "left"), anchor = all.Str("anchor", "middle");
                t.Align = align == "center" ? TextAlign.Center : (align == "right" ? TextAlign.Right : TextAlign.Left);
                t.Anchor = anchor == "top" ? TextAnchor3D.Top : (anchor == "bottom" ? TextAnchor3D.Bottom : TextAnchor3D.Middle);
                t.MaxWidth = all.Float("maxWidth");
                t.Color = Color32(all.Str("color"), Theme.Ink);
                t.Opacity = opacity;
            }
            else if (view.kind == "panel")
            {
                float w = all.Float("width", 0.1f), h = all.Float("height", 0.1f);
                view.panel.SetSize(new Vector2(w, h));
                view.panel.SetRadius(all.Float("radius", 0f));
                Color fill = Color32(all.Str("fill"), Theme.Surface);
                Color border = Color32(all.Str("border"), fill);
                view.panel.SetColors(fill, border);
                view.panel.SetBorder(all.Has("border") ? all.Float("borderWidth", Theme.Border) : 0f);
                view.panel.SetOpacity(opacity);
            }
            else if (view.material != null && (p.ContainsKey("color") || p.ContainsKey("opacity") || p == all))
            {
                Color color = Color32(all.Str("color"), Theme.Accent);
                bool glow = all.Bool("glow"), flow = all.Has("flow");
                if (flow)
                {
                    view.material.SetColor("_Color", color);
                    view.material.SetFloat("_Opacity", opacity);
                }
                else
                {
                    // Glow volumes default to see-through; solids to opaque.
                    float alpha = all.Has("opacity") ? opacity : (glow ? 0.3f : 1f);
                    view.material.SetColor("_Color", new Color(color.r, color.g, color.b, alpha));
                    if (!glow)
                    {
                        bool opaque = alpha >= 0.999f;
                        view.material.SetFloat("_ZWrite", opaque ? 1f : 0f);
                        view.material.renderQueue = opaque ? (int)RenderQueue.Geometry + 10 : (int)RenderQueue.Transparent + 2;
                    }
                }
            }

            if (p.ContainsKey("visible") || p == all) view.go.SetActive(all.Bool("visible", true));

            if (p.ContainsKey("billboard") || p == all)
            {
                string mode = all.Str("billboard", "none");
                var billboard = view.go.GetComponent<Billboard>();
                if (mode == "none")
                {
                    if (billboard != null) Destroy(billboard);
                }
                else
                {
                    if (billboard == null) billboard = view.go.AddComponent<Billboard>();
                    billboard.UprightOnly = mode == "upright";
                }
            }

            if (p.ContainsKey("interactive") || p == all) SetInteractive(view, all.Bool("interactive"));
        }

        // ---- interaction -----------------------------------------------------------------------

        void SetInteractive(NodeView view, bool interactive)
        {
            if (view.interactive == interactive) return;
            view.interactive = interactive;
            var existing = view.go.GetComponent<Interactable>();
            if (!interactive)
            {
                _interactive.Remove(view);
                if (existing != null) Destroy(existing);
                var box = view.go.GetComponent<BoxCollider>();
                if (box != null) Destroy(box);
                return;
            }
            int id = view.id;
            Interactable target = Interactable.Add(view.go, Vector3.one * 0.05f, () => Raise(id, "click"));
            target.OnHover = inside => Raise(id, inside ? "enter" : "exit");
            _interactive.Add(view);
            Fit(view);
        }

        void Raise(int id, string type)
        {
            if (NodeEvent != null) NodeEvent(id, type);
        }

        /// <summary>Sizes an interactive node's collider to whatever it and its children currently draw.</summary>
        void Fit(NodeView view)
        {
            var box = view.go.GetComponent<BoxCollider>();
            if (box == null) return;
            Transform root = view.go.transform;
            bool any = false;
            var bounds = new Bounds();
            foreach (MeshFilter filter in view.go.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                Bounds local = filter.sharedMesh.bounds;
                Matrix4x4 toRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 point = toRoot.MultiplyPoint3x4(corner);
                    if (!any) { bounds = new Bounds(point, Vector3.zero); any = true; }
                    else bounds.Encapsulate(point);
                }
            }
            if (!any) return;
            // Flat content needs some depth to be hit reliably.
            Vector3 size = bounds.size;
            size.x = Mathf.Max(size.x, 0.02f);
            size.y = Mathf.Max(size.y, 0.02f);
            size.z = Mathf.Max(size.z, 0.02f);
            box.center = bounds.center;
            box.size = size;
        }

        void LateUpdate()
        {
            // Programs keep adding children to interactive groups (a button's label, a card's rows), so refit now and then.
            _refit -= Time.unscaledDeltaTime;
            if (_refit > 0f) return;
            _refit = 0.25f;
            for (int i = 0; i < _interactive.Count; i++)
                if (_interactive[i].go != null && _interactive[i].go.activeInHierarchy) Fit(_interactive[i]);
        }

        // ---- helpers ---------------------------------------------------------------------------

        static Color Color32(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex) || hex[0] != '#' || (hex.Length != 7 && hex.Length != 9)) return fallback;
            try
            {
                int r = Convert.ToInt32(hex.Substring(1, 2), 16), g = Convert.ToInt32(hex.Substring(3, 2), 16), b = Convert.ToInt32(hex.Substring(5, 2), 16);
                int a = hex.Length == 9 ? Convert.ToInt32(hex.Substring(7, 2), 16) : 255;
                return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
            }
            catch (FormatException)
            {
                return fallback;
            }
        }
    }
}
