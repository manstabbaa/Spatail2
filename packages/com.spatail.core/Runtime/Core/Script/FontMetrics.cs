using System;
using System.Collections.Generic;
using System.Text;

namespace Spatail.Core
{
    /// <summary>
    /// Glyph metrics for one baked font, in em units. Lives in the engine-independent core so that programs can
    /// measure text (to size a panel around it) with exactly the numbers the renderer will use.
    /// </summary>
    public sealed class FontMetrics
    {
        public struct Glyph
        {
            public float advance;
            public float left, right, top, bottom;
            public float u0, u1, v0, v1;
            public bool visible;
        }

        public float Ascender = 0.97f, Descender = 0.24f, LineHeight = 1.3f, CapHeight = 0.72f;
        readonly Dictionary<char, Glyph> _glyphs = new Dictionary<char, Glyph>();

        public static FontMetrics Parse(string json)
        {
            var font = new FontMetrics();
            var o = Json.ParseObject(json);
            font.Ascender = o.Float("ascender", font.Ascender);
            font.Descender = o.Float("descender", font.Descender);
            font.LineHeight = o.Float("lineHeight", font.LineHeight);
            font.CapHeight = o.Float("capHeight", font.CapHeight);
            var glyphs = o.Obj("glyphs");
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
            return font;
        }

        public bool TryGetGlyph(char c, out Glyph glyph)
        {
            if (_glyphs.TryGetValue(c, out glyph)) return true;
            return _glyphs.TryGetValue('?', out glyph);
        }

        /// <summary>Width of part of a string in em units.</summary>
        public float Width(string s, int start, int length)
        {
            float w = 0;
            for (int i = start; i < start + length; i++)
            {
                Glyph g;
                if (TryGetGlyph(s[i], out g)) w += g.advance;
            }
            return w;
        }

        public float Width(string s) => Width(s, 0, s.Length);

        /// <summary>Breaks text into lines no wider than <paramref name="maxWidthEm"/> (0 = only at newlines).</summary>
        public List<string> Wrap(string text, float maxWidthEm)
        {
            var lines = new List<string>();
            foreach (string paragraph in (text ?? "").Split('\n'))
            {
                if (maxWidthEm <= 0f) { lines.Add(paragraph); continue; }
                var line = new StringBuilder();
                float width = 0f;
                float space = Width(" ");
                foreach (string word in paragraph.Split(' '))
                {
                    if (word.Length == 0) continue;
                    float w = Width(word);
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

        /// <summary>
        /// Size of a laid-out block in metres. Height runs from the top of the first line's capitals to the last
        /// line's baseline, which is what centring text in a pill needs.
        /// </summary>
        public void Measure(string text, float fontSize, float maxWidth, out float width, out float height)
        {
            List<string> lines = Wrap(text, maxWidth > 0f && fontSize > 0f ? maxWidth / fontSize : 0f);
            width = 0f;
            foreach (string line in lines) width = Math.Max(width, Width(line) * fontSize);
            height = Math.Max(0, lines.Count - 1) * LineHeight * fontSize + CapHeight * fontSize;
        }
    }

    /// <summary>The fonts a host has loaded, by weight name ("medium", "semibold").</summary>
    public sealed class FontSet
    {
        readonly Dictionary<string, FontMetrics> _fonts = new Dictionary<string, FontMetrics>();
        FontMetrics _fallback = new FontMetrics();

        public void Add(string weight, FontMetrics metrics)
        {
            _fonts[weight] = metrics;
            if (weight == "medium" || _fonts.Count == 1) _fallback = metrics;
        }

        public FontMetrics Get(string weight)
        {
            FontMetrics m;
            return weight != null && _fonts.TryGetValue(weight, out m) ? m : _fallback;
        }
    }
}
