using System.Collections.Generic;
using Spatail.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Spatail
{
    public enum TwinMode
    {
        /// <summary>Opaque stand-in hologram. Use when the real equipment is not in the room.</summary>
        Solid,
        /// <summary>Faint overlay, for checking alignment against the real equipment.</summary>
        Ghost,
        /// <summary>Invisible but still hides content behind it, so overlays sit correctly on real equipment.</summary>
        Occluder,
        Hidden,
    }

    /// <summary>
    /// Draws the stand-in holograms for equipment parts and fixtures, and gives every nameable entity a collider so
    /// pointing at something tells Spatail what the user means by "this".
    /// </summary>
    public sealed class TwinRenderer : MonoBehaviour
    {
        static readonly Dictionary<List<ShapeDef>, Mesh> MeshCache = new Dictionary<List<ShapeDef>, Mesh>();

        readonly List<MeshRenderer> _renderers = new List<MeshRenderer>();
        Material _material;
        TwinMode _mode = TwinMode.Solid;

        public TwinMode Mode
        {
            get => _mode;
            set { _mode = value; ApplyMode(); }
        }

        void Awake()
        {
            _material = Assets.Solid(Color.white, 1f, 0.5f);
            _material.name = "Spatail Twin";
            _material.SetColor("_RimColor", Theme.TwinRim);
            _material.SetFloat("_RimPower", 2.2f);
            ApplyMode();
        }

        public void Rebuild(RoomModel room)
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
            _renderers.Clear();
            if (room == null) return;

            foreach (RoomEntity entity in room.entities)
            {
                if (EntityKind.IsSurface(entity.kind)) continue;
                var go = new GameObject(entity.id);
                go.transform.SetParent(transform, false);
                go.transform.position = entity.position.ToUnity();
                go.transform.rotation = entity.rotation.ToUnity();

                if (entity.size.SqrLength > 1e-6f && entity.kind != EntityKind.Equipment)
                {
                    var box = go.AddComponent<BoxCollider>();
                    box.size = entity.size.ToUnity();
                    go.AddComponent<EntityHandle>().EntityId = entity.id;
                }

                if (entity.shapes == null || entity.shapes.Count == 0) continue;
                GameObject mesh = MeshFactory.Spawn("Hologram", go.transform, MeshFor(entity.shapes), _material);
                // Shapes are authored in the shape frame; the entity object sits at the centre of the bounds.
                mesh.transform.localPosition = -entity.localCenter.ToUnity();
                _renderers.Add(mesh.GetComponent<MeshRenderer>());
            }
            ApplyMode();
        }

        void ApplyMode()
        {
            if (_material == null) return;
            switch (_mode)
            {
                case TwinMode.Solid:
                    _material.SetColor("_Color", Color.white);
                    _material.SetFloat("_ColorMask", 15f);
                    _material.SetFloat("_ZWrite", 1f);
                    _material.renderQueue = (int)RenderQueue.Geometry;
                    break;
                case TwinMode.Ghost:
                    _material.SetColor("_Color", new Color(1f, 1f, 1f, 0.2f));
                    _material.SetFloat("_ColorMask", 15f);
                    _material.SetFloat("_ZWrite", 1f);
                    _material.renderQueue = (int)RenderQueue.AlphaTest + 20;
                    break;
                case TwinMode.Occluder:
                    _material.SetFloat("_ColorMask", 0f);
                    _material.SetFloat("_ZWrite", 1f);
                    _material.renderQueue = (int)RenderQueue.Geometry - 10;
                    break;
            }
            foreach (MeshRenderer r in _renderers)
                if (r != null) r.enabled = _mode != TwinMode.Hidden;
        }

        static Mesh MeshFor(List<ShapeDef> shapes)
        {
            Mesh mesh;
            if (MeshCache.TryGetValue(shapes, out mesh) && mesh != null) return mesh;
            var builder = new MeshBuilder();
            foreach (ShapeDef shape in shapes)
            {
                Matrix4x4 m = Matrix4x4.TRS(shape.p.ToUnity(), Quaternion.Euler(shape.r.x, shape.r.y, shape.r.z), Vector3.one);
                Color color = ToneColor(shape.tone);
                switch (shape.type)
                {
                    case "cyl":
                        builder.AddCylinder(m, shape.s.x, shape.s.z > 0f ? shape.s.z : shape.s.x, shape.s.y, 28, color);
                        break;
                    case "torus":
                        builder.AddTorus(m, shape.s.x, shape.s.y, 32, 10, color);
                        break;
                    case "sphere":
                        builder.AddSphere(m, shape.s.x, 20, color);
                        break;
                    default:
                        builder.AddBox(m, shape.s.ToUnity(), color);
                        break;
                }
            }
            mesh = builder.ToMesh("Spatail Twin Part");
            MeshCache[shapes] = mesh;
            return mesh;
        }

        static Color ToneColor(string tone)
        {
            switch (tone)
            {
                case "shade": return Theme.TwinShade;
                case "dark": return Theme.TwinDark;
                case "glass": return Theme.AccentSoft;
                default: return Theme.TwinBody;
            }
        }

        void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
