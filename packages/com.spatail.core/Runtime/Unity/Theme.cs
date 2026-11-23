using Spatail.Core;
using UnityEngine;

namespace Spatail
{
    /// <summary>Spatail's visual language in one place: colours and type sizes (metres) tuned for arm's length to 3 m.</summary>
    public static class Theme
    {
        public static readonly Color Accent = Hex(0x6C5CE7);
        public static readonly Color AccentDeep = Hex(0x4B3FC4);
        public static readonly Color AccentSoft = Hex(0xE9E6FB);
        public static readonly Color Warning = Hex(0xE8722C);
        public static readonly Color WarningSoft = Hex(0xFDEBDD);
        public static readonly Color Success = Hex(0x2BA471);
        public static readonly Color Ink = Hex(0x17161D);
        public static readonly Color Muted = Hex(0x7B7A8C);
        public static readonly Color Surface = Hex(0xFFFFFF);
        public static readonly Color SurfaceDim = Hex(0xF4F3FA);

        public static readonly Color TwinBody = Hex(0xF7F7FB);
        public static readonly Color TwinShade = Hex(0xDCDCEA);
        public static readonly Color TwinDark = Hex(0x3A384D);
        public static readonly Color TwinRim = Hex(0x8B88AC);

        public const float TextBody = 0.022f;
        public const float TextLabel = 0.028f;
        public const float TextTitle = 0.03f;
        public const float TextSmall = 0.018f;
        public const float Border = 0.0026f;
        /// <summary>Gap between a panel and the text drawn on it, toward the viewer.</summary>
        public const float Layer = 0.0025f;

        public static Color Hex(int rgb, float a = 1f) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);

        public static Color WithAlpha(this Color c, float a) => new Color(c.r, c.g, c.b, a);

        /// <summary>Main colour for an element style.</summary>
        public static Color StyleColor(string style)
        {
            switch (style)
            {
                case ElementStyle.Warning: return Warning;
                case ElementStyle.Success: return Success;
                default: return Accent;
            }
        }
    }
}
