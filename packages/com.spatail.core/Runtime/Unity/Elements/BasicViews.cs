using Spatail.Core;
using UnityEngine;

namespace Spatail
{
    /// <summary>A pill of text tethered to its entity by a short leader.</summary>
    public sealed class LabelView : ElementView
    {
        protected override void BuildView()
        {
            bool warning = Element.style == ElementStyle.Warning;
            bool neutral = Element.style == ElementStyle.Neutral;
            Color main = Theme.StyleColor(Element.style);
            Color fill = neutral || warning ? Theme.Surface : main;
            Color textColor = neutral || warning ? (warning ? Theme.Warning : Theme.Accent) : Color.white;

            const float padX = 0.02f, height = 0.05f, badge = 0.032f;
            float stem = Placement.pivotBottom ? 0.085f : 0f;

            var pill = new GameObject("Pill");
            pill.transform.SetParent(Content, false);
            pill.transform.localPosition = new Vector3(0, stem + (Placement.pivotBottom ? height * 0.5f : 0f), 0);

            SpatialText text = SpatialText.Create(pill.transform, Element.text, Theme.TextLabel, textColor, TextWeight.SemiBold, TextAlign.Center);
            float width = text.Size.x + padX * 2f + (warning ? badge + 0.01f : 0f);
            text.transform.localPosition = new Vector3(warning ? (badge + 0.01f) * 0.5f : 0f, 0, -Theme.Layer);
            Panel.Create(pill.transform, "Background", new Vector2(width, height), height * 0.5f, fill, main);

            if (warning)
            {
                var b = new GameObject("Badge");
                b.transform.SetParent(pill.transform, false);
                b.transform.localPosition = new Vector3(-width * 0.5f + padX * 0.6f + badge * 0.5f, 0, -Theme.Layer);
                Panel.Disc(b.transform, "Disc", badge, main);
                SpatialText mark = SpatialText.Create(b.transform, "!", Theme.TextLabel * 0.9f, Color.white, TextWeight.SemiBold, TextAlign.Center);
                mark.transform.localPosition = new Vector3(0, 0, -Theme.Layer);
            }

            if (stem > 0f)
            {
                Material line = Assets.SharedSolid("leader-" + Element.style, main, 0f);
                MeshFactory.Rod("Leader", Content, Vector3.zero, new Vector3(0, stem, 0), 0.003f, line);
                GameObject dot = MeshFactory.Spawn("Dot", Content, MeshFactory.UnitSphere, line);
                dot.transform.localScale = Vector3.one * 0.014f;
            }
            gameObject.AddComponent<Billboard>().UprightOnly = true;
        }
    }

    /// <summary>A numbered marker standing above an entity. Clicking it jumps a walkthrough to that step.</summary>
    public sealed class PinView : ElementView
    {
        Transform _head;

        protected override void BuildView()
        {
            Color main = Theme.StyleColor(Element.style);
            const float stem = 0.14f, diameter = 0.062f;
            Material line = Assets.SharedSolid("leader-" + Element.style, main, 0f);
            MeshFactory.Rod("Stem", Content, Vector3.zero, new Vector3(0, stem, 0), 0.0035f, line);
            GameObject dot = MeshFactory.Spawn("Dot", Content, MeshFactory.UnitSphere, line);
            dot.transform.localScale = Vector3.one * 0.016f;

            var head = new GameObject("Head");
            head.transform.SetParent(Content, false);
            head.transform.localPosition = new Vector3(0, stem + diameter * 0.5f, 0);
            _head = head.transform;
            Panel.Create(head.transform, "Disc", new Vector2(diameter, diameter), diameter * 0.5f, main, Color.white, 0.003f);
            string caption = Element.number > 0 ? Element.number.ToString() : (string.IsNullOrEmpty(Element.text) ? "" : Element.text);
            SpatialText text = SpatialText.Create(head.transform, caption, 0.034f, Color.white, TextWeight.SemiBold, TextAlign.Center);
            text.transform.localPosition = new Vector3(0, 0, -Theme.Layer);

            if (Build.stepCount > 0 && Element.number > 0)
            {
                int step = Element.number;
                Interactable.Add(head, new Vector3(diameter * 1.5f, diameter * 1.5f, 0.03f), () => App.SetStep(Build.id, step));
            }
            gameObject.AddComponent<Billboard>().UprightOnly = true;
            OnBuildChanged();
        }

        public override void OnBuildChanged()
        {
            if (_head == null) return;
            bool current = Build.stepCount > 0 && Element.number == Build.currentStep;
            _head.localScale = Vector3.one * (current ? 1.3f : 1f);
        }
    }

    /// <summary>A numbered instruction card with step controls.</summary>
    public sealed class CardView : ElementView
    {
        protected override void BuildView()
        {
            const float width = 0.36f, pad = 0.022f, circle = 0.046f, footerHeight = 0.036f;
            Color main = Theme.StyleColor(Element.style);
            float textLeft = -width * 0.5f + pad + circle + 0.014f;
            float titleWidth = width * 0.5f - pad - textLeft;

            SpatialText title = SpatialText.Create(Content, Element.title ?? "", Theme.TextTitle, main, TextWeight.SemiBold, TextAlign.Left, TextAnchor3D.Top, titleWidth);
            float rowHeight = Mathf.Max(circle, title.Size.y);

            SpatialText body = null;
            float bodyHeight = 0f;
            if (!string.IsNullOrEmpty(Element.body))
            {
                body = SpatialText.Create(Content, Element.body, Theme.TextBody, Theme.Ink, TextWeight.Medium, TextAlign.Left, TextAnchor3D.Top, width - pad * 2f);
                bodyHeight = body.Size.y + 0.016f;
            }

            bool stepped = Build.stepCount > 0 && Element.step > 0;
            float footer = stepped ? footerHeight + 0.018f : 0f;
            float height = pad + rowHeight + bodyHeight + footer + pad;
            float top = height * 0.5f;

            Panel.Create(Content, "Background", new Vector2(width, height), 0.022f, Theme.Surface, main);

            // Number in a ring, title beside it.
            var ring = new GameObject("Number");
            ring.transform.SetParent(Content, false);
            ring.transform.localPosition = new Vector3(-width * 0.5f + pad + circle * 0.5f, top - pad - rowHeight * 0.5f, -Theme.Layer);
            Panel.Create(ring.transform, "Ring", new Vector2(circle, circle), circle * 0.5f, Theme.Surface, main, 0.003f);
            SpatialText number = SpatialText.Create(ring.transform, Element.number > 0 ? Element.number.ToString() : "", 0.026f, main, TextWeight.SemiBold, TextAlign.Center);
            number.transform.localPosition = new Vector3(0, 0, -Theme.Layer);
            title.transform.localPosition = new Vector3(textLeft, top - pad - (rowHeight - title.Size.y) * 0.5f, -Theme.Layer);

            if (body != null)
                body.transform.localPosition = new Vector3(-width * 0.5f + pad, top - pad - rowHeight - 0.016f, -Theme.Layer);

            if (stepped)
            {
                float y = -top + pad + footerHeight * 0.5f;
                SpatialText progress = SpatialText.Create(Content, Element.step + " of " + Build.stepCount, Theme.TextSmall, Theme.Muted, TextWeight.Medium);
                progress.transform.localPosition = new Vector3(-width * 0.5f + pad, y, -Theme.Layer);

                int step = Element.step;
                bool last = step >= Build.stepCount;
                PillButton next = PillButton.Create(Content, last ? "Done" : "Next", Theme.TextSmall, main, main, Color.white,
                    () => { if (last) App.RemoveBuild(Build.id); else App.SetStep(Build.id, step + 1); }, 0.018f, footerHeight);
                next.transform.localPosition = new Vector3(width * 0.5f - pad - next.Size.x * 0.5f, y, -Theme.Layer);
                if (step > 1)
                {
                    PillButton back = PillButton.Create(Content, "Back", Theme.TextSmall, Theme.Surface, main, main,
                        () => App.SetStep(Build.id, step - 1), 0.018f, footerHeight);
                    back.transform.localPosition = new Vector3(next.transform.localPosition.x - next.Size.x * 0.5f - 0.01f - back.Size.x * 0.5f, y, -Theme.Layer);
                }
            }
        }
    }

    /// <summary>A free-form text panel: a title and a paragraph.</summary>
    public sealed class PanelView : ElementView
    {
        protected override void BuildView()
        {
            const float width = 0.38f, pad = 0.024f;
            Color main = Theme.StyleColor(Element.style);
            SpatialText title = SpatialText.Create(Content, Element.title ?? "", Theme.TextTitle, Theme.Ink, TextWeight.SemiBold, TextAlign.Left, TextAnchor3D.Top, width - pad * 2f);
            SpatialText body = SpatialText.Create(Content, Element.body ?? Element.text ?? "", Theme.TextBody, Theme.Ink, TextWeight.Medium, TextAlign.Left, TextAnchor3D.Top, width - pad * 2f);
            float gap = string.IsNullOrEmpty(title.Text) || string.IsNullOrEmpty(body.Text) ? 0f : 0.016f;
            float height = pad + title.Size.y + gap + body.Size.y + pad;
            float centreY = Placement.pivotBottom ? height * 0.5f : 0f;
            float top = centreY + height * 0.5f;

            Panel background = Panel.Create(Content, "Background", new Vector2(width, height), 0.022f, Theme.Surface, main);
            background.transform.localPosition = new Vector3(0, centreY, 0);
            title.transform.localPosition = new Vector3(-width * 0.5f + pad, top - pad, -Theme.Layer);
            body.transform.localPosition = new Vector3(-width * 0.5f + pad, top - pad - title.Size.y - gap, -Theme.Layer);
        }
    }

    /// <summary>A pulsing tinted volume over an entity.</summary>
    public sealed class HighlightView : ElementView
    {
        Material _material;
        Color _color;

        protected override bool AnimateScale => false;

        protected override void BuildView()
        {
            RoomEntity entity = Placement.entity;
            if (entity == null) return;
            transform.position = entity.position.ToUnity();
            transform.rotation = entity.rotation.ToUnity();
            _color = Theme.StyleColor(Element.style);
            _material = Assets.Glow(_color.WithAlpha(0.3f));
            Volume(Content, entity, _material);
        }

        protected override void Update()
        {
            base.Update();
            if (_material == null) return;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 3.2f);
            _material.SetColor("_Color", _color.WithAlpha(Mathf.Lerp(0.2f, 0.38f, pulse)));
        }

        void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }

    /// <summary>A motion cue: a circular arrow that turns the way the part should be turned.</summary>
    public sealed class GizmoView : ElementView
    {
        Transform _spinner;
        float _speed;

        protected override void BuildView()
        {
            RoomEntity entity = Placement.entity;
            float radius = 0.12f;
            if (entity != null) radius = Mathf.Clamp(Mathf.Max(entity.size.x, entity.size.z) * 0.5f + 0.045f, 0.08f, 0.35f);
            bool clockwise = Element.action != "turn-ccw";
            // Seen from above, positive rotation about +Y is clockwise in Unity.
            _speed = clockwise ? 80f : -80f;

            var spinner = new GameObject("Spinner");
            spinner.transform.SetParent(Content, false);
            spinner.transform.localPosition = new Vector3(0, 0.03f, 0);
            _spinner = spinner.transform;

            const float arc = 250f, tube = 0.011f;
            var builder = new MeshBuilder();
            Color color = Theme.StyleColor(Element.style);
            builder.AddTorus(Matrix4x4.identity, radius, tube, 40, 10, color, arc);
            // The arc is generated anticlockwise (seen from above) starting on +X. The arrowhead leads the motion:
            // at the start of the arc for clockwise turns, at the end for anticlockwise ones.
            float a = clockwise ? 0f : arc * Mathf.Deg2Rad;
            var radial = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var tangent = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)) * (clockwise ? -1f : 1f);
            const float headLength = 0.07f;
            Matrix4x4 head = Matrix4x4.TRS(radial * radius + tangent * (headLength * 0.5f), Quaternion.FromToRotation(Vector3.up, tangent), Vector3.one);
            builder.AddCylinder(head, 0.032f, 0f, headLength, 20, color);
            MeshFactory.Spawn("Arrow", spinner.transform, builder.ToMesh("Spatail Turn Arrow"), Assets.SharedSolid("gizmo-" + Element.style, Color.white, 1f, 0.25f));
        }

        protected override void Update()
        {
            base.Update();
            if (_spinner != null) _spinner.Rotate(0f, _speed * Time.deltaTime, 0f, Space.Self);
        }
    }
}
