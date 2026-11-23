using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Spatail
{
    /// <summary>
    /// Loads Spatail's shaders and fonts from Resources and hands out materials. Everything is created in code so the
    /// package needs no prefabs, material assets or import settings.
    /// </summary>
    public static class Assets
    {
        public const string SurfaceShader = "Spatail/Shaders/SpatailSurface";
        public const string PanelShader = "Spatail/Shaders/SpatailPanel";
        public const string TextShader = "Spatail/Shaders/SpatailText";
        public const string FlowShader = "Spatail/Shaders/SpatailFlow";

        static readonly Dictionary<string, Shader> Shaders = new Dictionary<string, Shader>();
        static readonly Dictionary<string, Material> Shared = new Dictionary<string, Material>();

        public static Shader LoadShader(string path)
        {
            Shader shader;
            if (Shaders.TryGetValue(path, out shader) && shader != null) return shader;
            shader = Resources.Load<Shader>(path);
            if (shader == null)
            {
                Debug.LogError("[Spatail] Missing shader at Resources/" + path + ". Falling back to an unlit built-in shader.");
                shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            }
            Shaders[path] = shader;
            return shader;
        }

        /// <summary>A new solid, shaded, opaque material.</summary>
        public static Material Solid(Color color, float shade = 1f, float rim = 0f)
        {
            var m = new Material(LoadShader(SurfaceShader)) { name = "Spatail Solid" };
            m.SetColor("_Color", color);
            m.SetFloat("_Shade", shade);
            m.SetFloat("_Rim", rim);
            m.SetFloat("_ZWrite", color.a >= 0.999f ? 1f : 0f);
            m.renderQueue = color.a >= 0.999f ? (int)RenderQueue.Geometry + 10 : (int)RenderQueue.Transparent + 2;
            return m;
        }

        /// <summary>A new see-through material for highlight volumes and glows. Does not write depth.</summary>
        public static Material Glow(Color color, float rim = 0.6f)
        {
            var m = new Material(LoadShader(SurfaceShader)) { name = "Spatail Glow" };
            m.SetColor("_Color", color);
            m.SetColor("_RimColor", new Color(color.r, color.g, color.b, 1f));
            m.SetFloat("_Shade", 0f);
            m.SetFloat("_Rim", rim);
            m.SetFloat("_RimPower", 2f);
            m.SetFloat("_ZWrite", 0f);
            m.renderQueue = (int)RenderQueue.Transparent + 1;
            return m;
        }

        /// <summary>Shared material by key, created on first use. Do not modify the result.</summary>
        public static Material SharedSolid(string key, Color color, float shade = 1f, float rim = 0f)
        {
            Material m;
            if (Shared.TryGetValue(key, out m) && m != null) return m;
            m = Solid(color, shade, rim);
            m.name = "Spatail " + key;
            Shared[key] = m;
            return m;
        }

        public static Material Panel(Vector2 size, float radius, Color fill, Color border, float borderWidth)
        {
            var m = new Material(LoadShader(PanelShader)) { name = "Spatail Panel" };
            m.SetColor("_Color", fill);
            m.SetColor("_BorderColor", border);
            m.SetVector("_Size", new Vector4(size.x, size.y, 0, 0));
            m.SetFloat("_Radius", radius);
            m.SetFloat("_Border", borderWidth);
            return m;
        }

        public static Material Flow(Color color)
        {
            var m = new Material(LoadShader(FlowShader)) { name = "Spatail Flow" };
            m.SetColor("_Color", color);
            return m;
        }
    }
}
