using Spatail.Core;
using UnityEngine;

namespace Spatail
{
    /// <summary>
    /// Base class for the visual of one spec element. Subclasses build their geometry once in <see cref="BuildView"/>;
    /// this class owns placement, the pop-in/pop-out animation and step visibility.
    /// </summary>
    public abstract class ElementView : MonoBehaviour
    {
        public Element Element { get; private set; }
        public Build Build { get; private set; }
        protected SpatailApp App { get; private set; }
        protected Placement Placement { get; private set; }
        /// <summary>Parent for everything a view builds. Hidden as a whole when the element is not on the current step.</summary>
        protected Transform Content { get; private set; }

        float _shown;        // 0..1 animation state
        bool _target = true;
        bool _dismissing;
        const float Duration = 0.24f;

        /// <summary>Views that live in world space rather than around one point skip the scale animation.</summary>
        protected virtual bool AnimateScale => true;

        public void Init(SpatailApp app, Build build, Element element, Placement placement, bool visible, bool animate)
        {
            App = app;
            Build = build;
            Element = element;
            Placement = placement;
            transform.position = placement.position.ToUnity();
            transform.rotation = placement.rotation.ToUnity();
            var content = new GameObject("Content");
            content.transform.SetParent(transform, false);
            Content = content.transform;
            BuildView();
            _target = visible;
            _shown = animate ? 0f : (visible ? 1f : 0f);
            ApplyAnimation();
        }

        protected abstract void BuildView();

        /// <summary>Called when something inside the build changed (step moved, item ticked).</summary>
        public virtual void OnBuildChanged() { }

        public void SetShown(bool shown) => _target = shown;

        /// <summary>Animate out, then destroy.</summary>
        public void Dismiss()
        {
            _dismissing = true;
            _target = false;
            foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
        }

        protected virtual void Update()
        {
            float goal = _target ? 1f : 0f;
            if (!Mathf.Approximately(_shown, goal))
            {
                _shown = Mathf.MoveTowards(_shown, goal, Time.unscaledDeltaTime / Duration);
                ApplyAnimation();
            }
            else if (_dismissing)
            {
                Destroy(gameObject);
            }
        }

        void ApplyAnimation()
        {
            bool visible = _shown > 0.001f;
            if (AnimateScale)
            {
                // Ease out with a slight overshoot, like the pop in the reference animation.
                float t = _shown;
                float eased = 1f + 2.2f * Mathf.Pow(t - 1f, 3f) + 1.2f * Mathf.Pow(t - 1f, 2f);
                transform.localScale = Vector3.one * Mathf.Max(0.0001f, eased * Element.scale);
            }
            else
            {
                transform.localScale = Vector3.one;
            }
            if (Content != null && Content.gameObject.activeSelf != visible) Content.gameObject.SetActive(visible);
        }

        // ---- shared building blocks --------------------------------------------------------

        /// <summary>A translucent volume hugging an entity: a cylinder for round parts, a box otherwise.</summary>
        protected static GameObject Volume(Transform parent, RoomEntity entity, Material material, float inflate = 1.08f)
        {
            Vector3 size = entity.size.ToUnity();
            GameObject go;
            int axis = RoundAxis(size);
            if (axis >= 0)
            {
                float length = size[axis] * 1.02f;
                float diameter = Mathf.Max(size[(axis + 1) % 3], size[(axis + 2) % 3]) * inflate;
                go = MeshFactory.Spawn("Volume", parent, MeshFactory.UnitCylinder, material);
                go.transform.localScale = new Vector3(diameter, length, diameter);
                go.transform.localRotation = axis == 0 ? Quaternion.Euler(0, 0, 90) : (axis == 2 ? Quaternion.Euler(90, 0, 0) : Quaternion.identity);
            }
            else
            {
                go = MeshFactory.Spawn("Volume", parent, MeshFactory.UnitBox, material);
                go.transform.localScale = size * inflate;
            }
            return go;
        }

        /// <summary>The axis a part is round about (its other two extents are similar), or -1 for boxy parts.</summary>
        static int RoundAxis(Vector3 size)
        {
            int best = -1;
            float bestLength = 0f;
            for (int axis = 0; axis < 3; axis++)
            {
                float a = size[(axis + 1) % 3], b = size[(axis + 2) % 3];
                if (a <= 0f || b <= 0f) continue;
                if (Mathf.Abs(a - b) / Mathf.Max(a, b) > 0.3f) continue;
                if (size[axis] > bestLength) { bestLength = size[axis]; best = axis; }
            }
            return best;
        }
    }
}
