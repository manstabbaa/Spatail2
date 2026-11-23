using System;
using System.Collections.Generic;
using UnityEngine;

namespace Spatail
{
    /// <summary>
    /// Something the user can point with: a controller, a pinching hand, a mouse, a finger on a touchscreen.
    /// Each platform adapter supplies its own; the rest of Spatail only sees rays and presses.
    /// </summary>
    public abstract class PointerSource : MonoBehaviour
    {
        /// <summary>False when this pointer is not currently aiming at anything (hand out of view, no touch).</summary>
        public abstract bool TryGetRay(out Ray ray);
        /// <summary>True while the trigger, pinch, mouse button or finger is held.</summary>
        public abstract bool IsPressed { get; }
        /// <summary>Headset pointers draw a visible ray; mouse and touch do not.</summary>
        public virtual bool ShowRay => false;

        // Maintained by the InteractionSystem.
        [NonSerialized] public bool WasPressed;
        [NonSerialized] public Interactable Hovered;
        [NonSerialized] public Interactable Captured;
        [NonSerialized] public bool HasHit;
        [NonSerialized] public Vector3 HitPoint;
        [NonSerialized] public float HitDistance;
    }

    /// <summary>Marks an object as clickable. Needs a collider on the same object.</summary>
    public sealed class Interactable : MonoBehaviour
    {
        public Action OnClick;
        public Action<bool> OnHover;
        /// <summary>Called every frame while pressed on this object, with the pointer's current ray.</summary>
        public Action<Ray> OnDrag;
        public Action OnRelease;

        public static Interactable Add(GameObject go, Vector3 size, Action onClick, Vector3 center = default(Vector3))
        {
            var box = go.GetComponent<BoxCollider>();
            if (box == null) box = go.AddComponent<BoxCollider>();
            box.size = size;
            box.center = center;
            var i = go.GetComponent<Interactable>();
            if (i == null) i = go.AddComponent<Interactable>();
            i.OnClick = onClick;
            return i;
        }
    }

    /// <summary>Tags a collider as belonging to a room entity, so pointing at it tells Spatail what "this" means.</summary>
    public sealed class EntityHandle : MonoBehaviour
    {
        public string EntityId;
    }

    /// <summary>
    /// Casts every pointer into the scene each frame, tracks hover and press state, and dispatches clicks. Also
    /// records which room entity the primary pointer is on, which the planner receives as the focus of a request.
    /// </summary>
    public sealed class InteractionSystem : MonoBehaviour
    {
        public const float MaxDistance = 12f;

        readonly List<PointerSource> _pointers = new List<PointerSource>();
        readonly RaycastHit[] _hits = new RaycastHit[32];

        /// <summary>Entity under the most recently active pointer, or null.</summary>
        public string FocusEntityId { get; private set; }
        /// <summary>Set by modal tools (such as moving the station) to stop clicks reaching content.</summary>
        public bool Suspended;
        /// <summary>Raised when a press lands on nothing interactive. Argument is the pointer.</summary>
        public event Action<PointerSource> PressedEmpty;

        public IReadOnlyList<PointerSource> Pointers => _pointers;

        public void Register(PointerSource pointer)
        {
            if (pointer != null && !_pointers.Contains(pointer)) _pointers.Add(pointer);
        }

        /// <summary>The first pointer that currently has a ray, preferring one that is pressed.</summary>
        public PointerSource Primary
        {
            get
            {
                PointerSource candidate = null;
                foreach (PointerSource p in _pointers)
                {
                    Ray ray;
                    if (p == null || !p.isActiveAndEnabled || !p.TryGetRay(out ray)) continue;
                    if (p.IsPressed) return p;
                    if (candidate == null) candidate = p;
                }
                return candidate;
            }
        }

        void Update()
        {
            Physics.SyncTransforms();
            string focus = null;

            foreach (PointerSource pointer in _pointers)
            {
                if (pointer == null || !pointer.isActiveAndEnabled) continue;
                Ray ray;
                bool hasRay = pointer.TryGetRay(out ray);
                bool pressed = hasRay && pointer.IsPressed;
                bool down = pressed && !pointer.WasPressed;
                bool up = !pressed && pointer.WasPressed;
                pointer.WasPressed = pressed;

                Interactable target = null;
                string entity = null;
                pointer.HasHit = false;

                if (hasRay)
                {
                    int count = Physics.RaycastNonAlloc(ray, _hits, MaxDistance, ~0, QueryTriggerInteraction.Collide);
                    float nearestInteractable = float.MaxValue, nearestEntity = float.MaxValue, nearest = float.MaxValue;
                    for (int i = 0; i < count; i++)
                    {
                        RaycastHit hit = _hits[i];
                        if (hit.distance < nearest)
                        {
                            nearest = hit.distance;
                            pointer.HasHit = true;
                            pointer.HitPoint = hit.point;
                            pointer.HitDistance = hit.distance;
                        }
                        var interactable = hit.collider.GetComponentInParent<Interactable>();
                        if (interactable != null && interactable.isActiveAndEnabled && hit.distance < nearestInteractable)
                        {
                            nearestInteractable = hit.distance;
                            target = interactable;
                        }
                        var handle = hit.collider.GetComponent<EntityHandle>();
                        if (handle != null && hit.distance < nearestEntity)
                        {
                            nearestEntity = hit.distance;
                            entity = handle.EntityId;
                        }
                    }
                    // UI in front of an entity wins the pointer, but the entity behind it is still the focus.
                    if (target != null)
                    {
                        pointer.HitDistance = nearestInteractable;
                        pointer.HitPoint = ray.GetPoint(nearestInteractable);
                    }
                }

                if (Suspended) target = null;
                if (entity != null && focus == null) focus = entity;

                if (pointer.Hovered != target)
                {
                    if (pointer.Hovered != null && pointer.Hovered.OnHover != null) pointer.Hovered.OnHover(false);
                    pointer.Hovered = target;
                    if (target != null && target.OnHover != null) target.OnHover(true);
                }

                if (down)
                {
                    pointer.Captured = target;
                    if (target == null && PressedEmpty != null) PressedEmpty(pointer);
                }
                if (pressed && pointer.Captured != null && pointer.Captured.OnDrag != null) pointer.Captured.OnDrag(ray);
                if (up)
                {
                    Interactable captured = pointer.Captured;
                    pointer.Captured = null;
                    if (captured != null)
                    {
                        if (captured.OnRelease != null) captured.OnRelease();
                        // A click is a press and release on the same object.
                        if (captured == target && captured.OnClick != null) captured.OnClick();
                    }
                }
            }
            FocusEntityId = focus;
        }
    }

    /// <summary>Draws a pointer's ray and a dot where it lands. Added automatically for pointers that ask for it.</summary>
    public sealed class PointerVisual : MonoBehaviour
    {
        public PointerSource Pointer;
        GameObject _ray, _dot;

        void Start()
        {
            Material material = Assets.SharedSolid("pointer", Theme.Accent.WithAlpha(0.75f), 0f);
            _ray = MeshFactory.Spawn("Ray", transform, MeshFactory.UnitCylinder, material);
            _dot = MeshFactory.Spawn("Dot", transform, MeshFactory.UnitSphere, Assets.SharedSolid("pointer-dot", Theme.Accent, 0f));
        }

        void LateUpdate()
        {
            Ray ray;
            bool visible = Pointer != null && Pointer.isActiveAndEnabled && Pointer.ShowRay && Pointer.TryGetRay(out ray);
            if (!visible)
            {
                _ray.SetActive(false);
                _dot.SetActive(false);
                return;
            }
            Pointer.TryGetRay(out ray);
            float length = Pointer.HasHit ? Pointer.HitDistance : 1.2f;
            Vector3 end = ray.GetPoint(length);
            _ray.SetActive(true);
            _ray.transform.position = (ray.origin + end) * 0.5f;
            _ray.transform.rotation = Quaternion.FromToRotation(Vector3.up, ray.direction);
            _ray.transform.localScale = new Vector3(0.0025f, length, 0.0025f);
            _dot.SetActive(Pointer.HasHit);
            _dot.transform.position = end;
            float size = (Pointer.IsPressed ? 0.012f : 0.018f) * Mathf.Max(0.6f, length);
            _dot.transform.localScale = Vector3.one * size;
        }
    }

    /// <summary>Keeps an object turned toward the viewer.</summary>
    public sealed class Billboard : MonoBehaviour
    {
        public Transform Target;
        /// <summary>When true the object only yaws, staying upright.</summary>
        public bool UprightOnly;

        void LateUpdate()
        {
            if (Target == null)
            {
                Camera cam = SpatailApp.Instance != null ? SpatailApp.Instance.Head : Camera.main;
                if (cam == null) return;
                Target = cam.transform;
            }
            // Content faces -Z, so point +Z away from the viewer.
            Vector3 away = transform.position - Target.position;
            if (UprightOnly) away.y = 0f;
            if (away.sqrMagnitude < 1e-6f) return;
            transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }
    }
}
