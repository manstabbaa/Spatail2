using System.Collections.Generic;
using UnityEngine;

namespace Spatail.DevTool
{
    /// <summary>
    /// Look and feel of the dev tool, built entirely in code: rounded backgrounds are generated textures, sizes are
    /// in points and converted with the display's pixel density, so nothing here needs an asset.
    /// </summary>
    public sealed partial class DevToolUi
    {
        float _scale = 1f;
        float _styledScale = -1f;

        GUIStyle _text, _textMuted, _textSmall, _textBold, _textAccent, _textMono, _textWhite;
        GUIStyle _panel, _card, _strip;
        GUIStyle _buttonPrimary, _buttonSecondary, _buttonQuiet, _buttonDanger, _buttonRound, _buttonRoundQuiet, _buttonRoundHot;
        GUIStyle _chip, _chipBuilt, _tabOn, _tabOff, _field, _fieldEmpty, _segmentOn, _segmentOff, _badge;

        static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();

        static readonly Color Ink = Theme.Ink;
        static readonly Color Muted = Theme.Muted;
        static readonly Color Accent = Theme.Accent;
        static readonly Color AccentDeep = Theme.AccentDeep;
        static readonly Color AccentSoft = Theme.AccentSoft;
        static readonly Color Surface = Theme.Surface;
        static readonly Color SurfaceDim = Theme.SurfaceDim;
        static readonly Color Warning = Theme.Warning;
        static readonly Color Clear = new Color(1f, 1f, 1f, 0f);

        /// <summary>Points to pixels.</summary>
        float P(float points) => Mathf.Round(points * _scale);
        int Pi(float points) => Mathf.RoundToInt(points * _scale);

        void UpdateScale()
        {
            float scale;
            if (Application.isEditor || Screen.dpi <= 0f)
            {
                // The Game view reports the monitor's density; size against its height instead.
                scale = Mathf.Clamp(Mathf.Min(Screen.height, Screen.width * 1.6f) / 760f, 1f, 2.5f);
            }
            else
            {
                scale = Mathf.Clamp(Screen.dpi / 155f, 1f, 4f);
            }
            // Keep the layout usable on small windows: it is designed for at least 320 points across.
            scale = Mathf.Min(scale, Screen.width / 320f);
            _scale = Mathf.Max(0.75f, scale);
            if (Mathf.Abs(_scale - _styledScale) > 0.01f) BuildStyles();
        }

        void BuildStyles()
        {
            _styledScale = _scale;

            _text = Text(Ink, 14, FontStyle.Normal);
            _textMuted = Text(Muted, 13, FontStyle.Normal);
            _textSmall = Text(Muted, 11.5f, FontStyle.Normal);
            _textBold = Text(Ink, 15, FontStyle.Bold);
            _textAccent = Text(Accent, 13, FontStyle.Bold);
            _textWhite = Text(Color.white, 13, FontStyle.Bold);
            _textMono = Text(Ink, 11.5f, FontStyle.Normal);
            _textMono.wordWrap = true;
            _textMono.alignment = TextAnchor.UpperLeft;
            _textMono.clipping = TextClipping.Clip;

            _panel = Background(Surface.WithAlpha(0.96f), AccentSoft, 18, 1);
            _card = Background(SurfaceDim, Clear, 12, 0);
            _strip = Background(Surface.WithAlpha(0.9f), AccentSoft, 14, 1);

            _buttonPrimary = Button(Accent, AccentDeep, Clear, Color.white, 22, 14, FontStyle.Bold, 16);
            _buttonSecondary = Button(Surface, AccentSoft, Accent, Accent, 22, 14, FontStyle.Bold, 16);
            _buttonQuiet = Button(SurfaceDim, AccentSoft, Clear, Ink, 22, 14, FontStyle.Normal, 14);
            _buttonDanger = Button(Surface, Theme.WarningSoft, Warning, Warning, 22, 14, FontStyle.Bold, 14);
            _buttonRound = Button(Accent, AccentDeep, Clear, Color.white, 22, 13, FontStyle.Bold, 0);
            _buttonRoundQuiet = Button(SurfaceDim, AccentSoft, Clear, Accent, 22, 13, FontStyle.Bold, 0);
            _buttonRoundHot = Button(Warning, Warning, Clear, Color.white, 22, 13, FontStyle.Bold, 0);

            _chip = Button(Surface, AccentSoft, AccentSoft, Accent, 17, 13, FontStyle.Normal, 14);
            _chipBuilt = Button(Accent, Accent, Clear, Color.white, 13, 11.5f, FontStyle.Bold, 10);
            _badge = Button(AccentSoft, AccentSoft, Clear, Accent, 10, 10.5f, FontStyle.Bold, 8);

            _tabOn = Button(Accent, Accent, Clear, Color.white, 17, 12.5f, FontStyle.Bold, 2);
            _tabOff = Button(Clear, AccentSoft, Clear, Muted, 17, 12.5f, FontStyle.Normal, 2);
            _segmentOn = Button(Accent, Accent, Clear, Color.white, 14, 12.5f, FontStyle.Bold, 2);
            _segmentOff = Button(SurfaceDim, AccentSoft, Clear, Ink, 14, 12.5f, FontStyle.Normal, 2);

            _field = Button(SurfaceDim, SurfaceDim, Clear, Ink, 14, 14, FontStyle.Normal, 14);
            _field.alignment = TextAnchor.MiddleLeft;
            _field.focused.background = _field.normal.background;
            _field.focused.textColor = Ink;
            _fieldEmpty = new GUIStyle(_field);
            _fieldEmpty.normal.textColor = _fieldEmpty.active.textColor = _fieldEmpty.hover.textColor = Muted;
        }

        GUIStyle Text(Color color, float points, FontStyle fontStyle)
        {
            var s = new GUIStyle
            {
                fontSize = Mathf.Max(8, Pi(points)),
                fontStyle = fontStyle,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                clipping = TextClipping.Clip,
                richText = false,
            };
            s.normal.textColor = color;
            return s;
        }

        GUIStyle Background(Color fill, Color border, float radiusPoints, int borderPixels)
        {
            int radius = Mathf.Max(2, Pi(radiusPoints));
            var s = new GUIStyle();
            s.normal.background = Rounded(fill, border, radius, border.a > 0f ? Mathf.Max(1, Mathf.RoundToInt(borderPixels * _scale)) : 0);
            s.border = new RectOffset(radius, radius, radius, radius);
            return s;
        }

        GUIStyle Button(Color fill, Color pressedFill, Color border, Color text, float radiusPoints, float fontPoints, FontStyle fontStyle, float paddingPoints)
        {
            int radius = Mathf.Max(2, Pi(radiusPoints));
            int borderPixels = border.a > 0f ? Mathf.Max(1, Mathf.RoundToInt(1.2f * _scale)) : 0;
            var s = new GUIStyle
            {
                fontSize = Mathf.Max(8, Pi(fontPoints)),
                fontStyle = fontStyle,
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                wordWrap = false,
                richText = false,
                border = new RectOffset(radius, radius, radius, radius),
                padding = new RectOffset(Pi(paddingPoints), Pi(paddingPoints), 0, 0),
            };
            Texture2D normal = Rounded(fill, border, radius, borderPixels);
            Texture2D pressed = Rounded(pressedFill, border, radius, borderPixels);
            s.normal.background = normal;
            s.hover.background = normal;
            s.active.background = pressed;
            s.normal.textColor = s.hover.textColor = s.active.textColor = text;
            return s;
        }

        /// <summary>
        /// A square texture holding a rounded rectangle, meant to be drawn nine-sliced with a border equal to its
        /// corner radius. Cached per appearance.
        /// </summary>
        static Texture2D Rounded(Color fill, Color border, int radius, int borderPixels)
        {
            string key = ColorUtility.ToHtmlStringRGBA(fill) + ColorUtility.ToHtmlStringRGBA(border) + "_" + radius + "_" + borderPixels;
            Texture2D texture;
            if (Textures.TryGetValue(key, out texture) && texture != null) return texture;

            int size = radius * 2 + 2;
            texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "DevTool " + key,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Signed distance to a rounded square that fills the texture.
                    float px = Mathf.Abs(x + 0.5f - half) - (half - radius);
                    float py = Mathf.Abs(y + 0.5f - half) - (half - radius);
                    float outside = new Vector2(Mathf.Max(px, 0f), Mathf.Max(py, 0f)).magnitude;
                    float distance = outside + Mathf.Min(Mathf.Max(px, py), 0f) - radius;

                    float coverage = Mathf.Clamp01(0.5f - distance);
                    Color c = fill;
                    if (borderPixels > 0)
                    {
                        float inBorder = Mathf.Clamp01(distance + borderPixels + 0.5f);
                        c = Color.Lerp(fill, border, inBorder);
                        // A transparent fill with a visible border must not fade the border's own alpha.
                        c.a = Mathf.Lerp(fill.a, border.a, inBorder);
                    }
                    c.a *= coverage;
                    pixels[y * size + x] = c;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            Textures[key] = texture;
            return texture;
        }
    }
}
