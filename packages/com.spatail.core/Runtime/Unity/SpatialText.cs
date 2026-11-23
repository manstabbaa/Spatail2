using System.Collections.Generic;
using Spatail.Core;
using UnityEngine;

namespace Spatail
{
    public enum TextWeight { Medium, SemiBold }
    public enum TextAlign { Left, Center, Right }
    public enum TextAnchor3D { Top, Middle, Bottom }

    /// <summary>A baked signed-distance-field font: the atlas texture plus the shared glyph metrics.</summary>
    public sealed class SdfFont
    {
        public Texture2D Atlas { get; private set; }
        public Material Material { get; private set; }
        /// <summary>The same metrics programs measure text with, so layout and rendering always agree.</summary>
        public FontMetrics Metrics { get; private set; }

        static readonly Dictionary<TextWeight, SdfFont> Loaded = new Dictionary<TextWeight, SdfFont>();
        static FontSet _set;

        public static SdfFont Get(TextWeight weight)
        {
            SdfFont font;
            if (Loaded.TryGetValue(weight, out font) && font.Atlas != null) return font;
            font = Load(weight == TextWeight.SemiBold ? "inter_semibold" : "inter_medium");
            Loaded[weight] = font;
            return font;
        }

        /// <summary>Both weights' metrics, for the script runtime.</summary>
        public static FontSet MetricSet
        {
            get
            {
                if (_set != null) return _set;
                _set = new FontSet();
                _set.Add("medium", Get(TextWeight.Medium).Metrics);
                _set.Add("semibold", Get(TextWeight.SemiBold).Metrics);
                return _set;
            }
        }

        static SdfFont Load(string name)
        {
            var font = new SdfFont { Metrics = new FontMetrics() };
            var atlas = Resources.Load<TextAsset>("Spatail/Fonts/" + name + "_atlas");
            var metrics = Resources.Load<TextAsset>("Spatail/Fonts/" + name + "_metrics");
            if (atlas == null || metrics == null)
            {
                Debug.LogError("[Spatail] Font '" + name + "' is missing from Resources/Spatail/Fonts.");
                font.Atlas = Texture2D.whiteTexture;
            }
            else
            {
                // Linear (not sRGB) with mipmaps: the texture stores distances, not colour.
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, true) { name = name };
                texture.LoadImage(atlas.bytes, true);
                texture.filterMode = FilterMode.Trilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.anisoLevel = 4;
                font.Atlas = texture;
                font.Metrics = FontMetrics.Parse(metrics.text);
            }
            font.Material = new Material(Assets.LoadShader(Assets.TextShader)) { name = "Spatail Text " + name };
            font.Material.mainTexture = font.Atlas;
            return font;
        }
    }

    /// <summary>
    /// World-space text. Lays out in the local XY plane facing -Z, wraps to <see cref="MaxWidth"/>, and reports its
    /// measured <see cref="Size"/> so panels can be sized around it. Vertical metrics are cap-height based: the block
    /// runs from the top of the first line's capitals to the last line's baseline, which is what you want when
    /// centring a label in a pill.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class SpatialText : MonoBehaviour
    {
        string _text = "";
        float _fontSize = Theme.TextBody;
        Color _color = Color.black;
        TextWeight _weight = TextWeight.Medium;
        TextAlign _align = TextAlign.Left;
        TextAnchor3D _anchor = TextAnchor3D.Middle;
        float _maxWidth;
        bool _dirty = true;
        float _opacity = 1f;
        Material _material;
        Mesh _mesh;
        Vector2 _size;

        public string Text { get => _text; set { value = value ?? ""; if (_text != value) { _text = value; _dirty = true; } } }
        /// <summary>Em size in metres.</summary>
        public float FontSize { get => _fontSize; set { if (_fontSize != value) { _fontSize = value; _dirty = true; } } }
        public Color Color { get => _color; set { if (_color != value) { _color = value; _dirty = true; } } }
        public TextWeight Weight { get => _weight; set { if (_weight != value) { _weight = value; _dirty = true; } } }
        public TextAlign Align { get => _align; set { if (_align != value) { _align = value; _dirty = true; } } }
        public TextAnchor3D Anchor { get => _anchor; set { if (_anchor != value) { _anchor = value; _dirty = true; } } }
        /// <summary>Wrap width in metres; 0 disables wrapping.</summary>
        public float MaxWidth { get => _maxWidth; set { if (_maxWidth != value) { _maxWidth = value; _dirty = true; } } }

        /// <summary>Fades the text. Uses a private material only once a value other than 1 is set.</summary>
        public float Opacity
        {
            get => _opacity;
            set
            {
                if (Mathf.Approximately(_opacity, value)) return;
                _opacity = value;
                if (_material == null)
                {
                    _material = new Material(SdfFont.Get(_weight).Material);
                    GetComponent<MeshRenderer>().sharedMaterial = _material;
                }
                _material.SetFloat("_Opacity", value);
            }
        }

        /// <summary>Measured width and height of the laid-out block in metres.</summary>
        public Vector2 Size
        {
            get
            {
                if (_dirty) Rebuild();
                return _size;
            }
        }

        public static SpatialText Create(Transform parent, string text, float fontSize, Color color,
            TextWeight weight = TextWeight.Medium, TextAlign align = TextAlign.Left, TextAnchor3D anchor = TextAnchor3D.Middle, float maxWidth = 0f)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<SpatialText>();
            t._text = text ?? "";
            t._fontSize = fontSize;
            t._color = color;
            t._weight = weight;
            t._align = align;
            t._anchor = anchor;
            t._maxWidth = maxWidth;
            t.Rebuild();
            return t;
        }

        void LateUpdate()
        {
            if (_dirty) Rebuild();
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_material != null) Destroy(_material);
        }

        void Rebuild()
        {
            _dirty = false;
            SdfFont sdf = SdfFont.Get(_weight);
            FontMetrics font = sdf.Metrics;
            var filter = GetComponent<MeshFilter>();
            var meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = _material != null ? _material : sdf.Material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            List<string> lines = font.Wrap(_text, _maxWidth > 0f ? _maxWidth / _fontSize : 0f);
            float lineStep = font.LineHeight * _fontSize;
            float cap = font.CapHeight * _fontSize;
            float blockHeight = Mathf.Max(0, lines.Count - 1) * lineStep + cap;
            float blockWidth = 0f;
            var widths = new float[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                widths[i] = font.Width(lines[i]) * _fontSize;
                if (widths[i] > blockWidth) blockWidth = widths[i];
            }
            _size = new Vector2(blockWidth, blockHeight);

            float top;
            switch (_anchor)
            {
                case TextAnchor3D.Top: top = 0f; break;
                case TextAnchor3D.Bottom: top = blockHeight; break;
                default: top = blockHeight * 0.5f; break;
            }

            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color>();
            var triangles = new List<int>();

            for (int i = 0; i < lines.Count; i++)
            {
                float baseline = top - cap - i * lineStep;
                float x;
                switch (_align)
                {
                    case TextAlign.Center: x = -widths[i] * 0.5f; break;
                    case TextAlign.Right: x = -widths[i]; break;
                    default: x = 0f; break;
                }
                foreach (char c in lines[i])
                {
                    FontMetrics.Glyph g;
                    if (!font.TryGetGlyph(c, out g)) continue;
                    if (g.visible)
                    {
                        int v = vertices.Count;
                        float l = x + g.left * _fontSize, r = x + g.right * _fontSize;
                        float b = baseline + g.bottom * _fontSize, t = baseline + g.top * _fontSize;
                        vertices.Add(new Vector3(l, b, 0));
                        vertices.Add(new Vector3(l, t, 0));
                        vertices.Add(new Vector3(r, t, 0));
                        vertices.Add(new Vector3(r, b, 0));
                        uvs.Add(new Vector2(g.u0, g.v0));
                        uvs.Add(new Vector2(g.u0, g.v1));
                        uvs.Add(new Vector2(g.u1, g.v1));
                        uvs.Add(new Vector2(g.u1, g.v0));
                        colors.Add(_color); colors.Add(_color); colors.Add(_color); colors.Add(_color);
                        triangles.Add(v); triangles.Add(v + 1); triangles.Add(v + 2);
                        triangles.Add(v); triangles.Add(v + 2); triangles.Add(v + 3);
                    }
                    x += g.advance * _fontSize;
                }
            }

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "Spatail Text" };
                _mesh.MarkDynamic();
                filter.sharedMesh = _mesh;
            }
            _mesh.Clear();
            _mesh.SetVertices(vertices);
            _mesh.SetUVs(0, uvs);
            _mesh.SetColors(colors);
            _mesh.SetTriangles(triangles, 0);
            _mesh.RecalculateBounds();
        }
    }
}
