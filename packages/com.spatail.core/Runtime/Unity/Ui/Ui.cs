using System;
using UnityEngine;

namespace Spatail
{
    /// <summary>A rounded rectangle drawn by the panel shader. Resize and recolour it freely at runtime.</summary>
    public sealed class Panel : MonoBehaviour
    {
        Material _material;
        Vector2 _size;

        public Vector2 Size => _size;

        public static Panel Create(Transform parent, string name, Vector2 size, float radius, Color fill, Color border, float borderWidth = Theme.Border)
        {
            Material material = Assets.Panel(size, radius, fill, border, borderWidth);
            GameObject go = MeshFactory.Spawn(name, parent, MeshFactory.Quad, material);
            var panel = go.AddComponent<Panel>();
            panel._material = material;
            panel._size = size;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            return panel;
        }

        /// <summary>A filled circle.</summary>
        public static Panel Disc(Transform parent, string name, float diameter, Color fill) =>
            Create(parent, name, new Vector2(diameter, diameter), diameter * 0.5f, fill, fill, 0f);

        public void SetSize(Vector2 size)
        {
            _size = size;
            transform.localScale = new Vector3(size.x, size.y, 1f);
            _material.SetVector("_Size", new Vector4(size.x, size.y, 0, 0));
        }

        public void SetColors(Color fill, Color border)
        {
            _material.SetColor("_Color", fill);
            _material.SetColor("_BorderColor", border);
        }

        public void SetRadius(float radius) => _material.SetFloat("_Radius", radius);

        void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }

    /// <summary>A pill-shaped button: panel, centred label, collider, hover feedback.</summary>
    public sealed class PillButton : MonoBehaviour
    {
        public Panel Background { get; private set; }
        public SpatialText Label { get; private set; }
        public Vector2 Size { get; private set; }

        Color _fill, _border, _hoverFill;
        bool _hovered;

        public static PillButton Create(Transform parent, string text, float fontSize, Color fill, Color border, Color textColor, Action onClick,
            float paddingX = 0.016f, float height = 0f, float minWidth = 0f)
        {
            var go = new GameObject("Button " + text);
            go.transform.SetParent(parent, false);
            var button = go.AddComponent<PillButton>();
            button.Label = SpatialText.Create(go.transform, text, fontSize, textColor, TextWeight.SemiBold, TextAlign.Center);
            button.Label.transform.localPosition = new Vector3(0, 0, -Theme.Layer);
            if (height <= 0f) height = fontSize * 2.1f;
            float width = Mathf.Max(minWidth, button.Label.Size.x + paddingX * 2f);
            button.Size = new Vector2(width, height);
            button._fill = fill;
            button._border = border;
            button._hoverFill = Color.Lerp(fill, border.a > 0.01f ? border : Theme.Accent, 0.18f);
            button.Background = Panel.Create(go.transform, "Background", button.Size, height * 0.5f, fill, border);
            Interactable interactable = Interactable.Add(go, new Vector3(width, height, 0.02f), onClick);
            interactable.OnHover = button.SetHover;
            return button;
        }

        public void SetText(string text, float paddingX = 0.016f)
        {
            Label.Text = text;
            float width = Label.Size.x + paddingX * 2f;
            Size = new Vector2(width, Size.y);
            Background.SetSize(Size);
            var box = GetComponent<BoxCollider>();
            if (box != null) box.size = new Vector3(width, Size.y, 0.02f);
        }

        public void SetColors(Color fill, Color border, Color text)
        {
            _fill = fill;
            _border = border;
            _hoverFill = Color.Lerp(fill, border.a > 0.01f ? border : Theme.Accent, 0.18f);
            Label.Color = text;
            Background.SetColors(_hovered ? _hoverFill : _fill, _border);
        }

        void SetHover(bool hovered)
        {
            _hovered = hovered;
            if (Background != null) Background.SetColors(hovered ? _hoverFill : _fill, _border);
            transform.localScale = Vector3.one * (hovered ? 1.04f : 1f);
        }
    }
}
