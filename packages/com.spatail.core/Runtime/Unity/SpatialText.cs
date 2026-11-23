using System.Collections.Generic;
using System.Text;
using Spatail.Core;
using UnityEngine;

namespace Spatail
{
    public enum TextWeight { Medium, SemiBold }
    public enum TextAlign { Left, Center, Right }
    public enum TextAnchor3D { Top, Middle, Bottom }

    /// <summary>A baked signed-distance-field font: one atlas texture plus glyph metrics in em units.</summary>
    public sealed class SdfFont
    {
        public struct Glyph
        {
            public float advance;
            public float left, right, top, bottom;
            public float u0, u1, v0, v1;
            public bool visible;
        }

        public Texture2D Atlas { get; private set; }
        public Material Material { get; private set; }
        public float Ascender { get; private set; }
        public float Descender { get; private set; }
        public float LineHeight { get; private set; }
        public float CapHeight { get; private set; }

        readonly Dictionary<char, Glyph> _glyphs = new Dictionary<char, Glyph>();

        static readonly Dictionary<TextWeight, SdfFont> Loaded = new Dictionary<TextWeight, SdfFont>();

        public static SdfFont Get(TextWeight weight)
        {
            SdfFont font;
            if (Loaded.TryGetValue(weight, out font) && font.Atlas != null) return font;
            font = Load(weight == TextWeight.SemiBold ? "inter_semibold" : "inter_medium");
            Loaded[weight] = font;
            return font;
        }

        static SdfFont Load(string name)
        {
            var font = new SdfFont();
            var atlas = Resources.Load<TextAsset>("Spatail/Fonts/" + name + "_atlas");
            var metrics = Resources.Load<TextAsset>("Spatail/Fonts/" + name + "_metrics");
            if (atlas == null || metrics == null)
            {
                Debug.LogError("[Spatail] Font '" + name + "' is missing from Resources/Spatail/Fonts.");
                font.Atlas = Texture2D.whiteTexture;
                font.LineHeight = 1.3f;
                font.CapHeight = 0.72f;
                font.Ascender = 0.97f;
                font.Descender = 0.24f;
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

                var json = Json.ParseObject(metrics.text);
                font.Ascender = json.Float("ascender", 0.97f);
                font.Descender = json.Float("descender", 0.24f);
                font.LineHeight = json.Float("lineHeight", 1.3f);
                font.CapHeight = json.Float("capHeight", 0.72f);
                var glyphs = json.Obj("glyphs");
                if (glyphs != null)
                    foreach (var kv in glyphs)
                    {
                        var g = kv.Value as Dictionary<string, object>;
                        int code;
                        if (g == null || !int.TryParse(kv.Key, out code)) continue;
                        font._glyphs[(char)code] = new Glyph
                        {
                            advance = g.Float("a"),
                            left = g.Float("l"), right = g.Float("r"), top = g.Float("t"), bottom = g.Float("b"),
                            u0 = g.Float("u0"), u1 = g.Float("u1"), v0 = g.Float("v0"), v1 = g.Float("v1"),
                            visible = g.Has("u0"),
                        };
                    }
            }
            font.Material = new Material(Assets.LoadShader(Assets.TextShader)) { name = "Spatail Text " + name };
            font.Material.mainTexture = font.Atlas;
            return font;
        }

        public bool TryGetGlyph(char c, out Glyph glyph)
        {
            if (_glyphs.TryGetValue(c, out glyph)) return true;
            // Curly quotes and friends that the atlas lacks fall back to their plain forms.
            return _glyphs.TryGetValue('?', out glyph);
        }

        /// <summary>Width of a string in em units.</summary>
        public float Measure(string s, int start, int length)
        {
            float w = 0;
            for (int i = start; i < start + length; i++)
            {
                Glyph g;
                if (TryGetGlyph(s[i], out g)) w += g.advance;
            }
            return w;
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
        }

        static List<string> Wrap(SdfFont font, string text, float maxWidthEm)
        {
            var lines = new List<string>();
            foreach (string paragraph in text.Split('\n'))
            {
                if (maxWidthEm <= 0f) { lines.Add(paragraph); continue; }
                var line = new StringBuilder();
                float width = 0f;
                float space = font.Measure(" ", 0, 1);
                foreach (string word in paragraph.Split(' '))
                {
                    if (word.Length == 0) continue;
                    float w = font.Measure(word, 0, word.Length);
                    if (line.Length > 0 && width + space + w > maxWidthEm)
                    {
                        lines.Add(line.ToString());
                        line.Length = 0;
                        width = 0f;
                    }
                    if (line.Length > 0) { line.Append(' '); width += space; }
                    line.Append(word);
                    width += w;
                }
                lines.Add(line.ToString());
            }
            return lines;
        }

        void Rebuild()
        {
            _dirty = false;
            SdfFont font = SdfFont.Get(_weight);
            var filter = GetComponent<MeshFilter>();
            var meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = font.Material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            List<string> lines = Wrap(font, _text, _maxWidth > 0f ? _maxWidth / _fontSize : 0f);
            float lineStep = font.LineHeight * _fontSize;
            float cap = font.CapHeight * _fontSize;
            float blockHeight = Mathf.Max(0, lines.Count - 1) * lineStep + cap;
            float blockWidth = 0f;
            var widths = new float[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                widths[i] = font.Measure(lines[i], 0, lines[i].Length) * _fontSize;
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
                    SdfFont.Glyph g;
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
