using System.Collections.Generic;
using Spatail.Core;
using UnityEngine;

namespace Spatail
{
    /// <summary>
    /// Keeps the scene in step with the <see cref="BuildStore"/>: creates a view for each element when a build is
    /// added, animates views in and out as steps change, and re-lays everything out when the room model changes.
    /// </summary>
    public sealed class ContentRenderer : MonoBehaviour
    {
        SpatailApp _app;
        readonly Dictionary<Build, List<ElementView>> _views = new Dictionary<Build, List<ElementView>>();

        public void Init(SpatailApp app)
        {
            _app = app;
            app.Store.Added += b => Create(b, true);
            app.Store.Removed += Remove;
            app.Store.Changed += Refresh;
        }

        void Create(Build build, bool animate)
        {
            var list = new List<ElementView>();
            _views[build] = list;
            foreach (Element element in build.elements)
            {
                Placement placement = LayoutSolver.Place(element, _app.Room);
                if (!placement.valid) continue;
                var go = new GameObject(build.id + "/" + element.id);
                go.transform.SetParent(transform, false);
                ElementView view = AddView(go, element.type);
                if (view == null) { Destroy(go); continue; }
                view.Init(_app, build, element, placement, build.IsVisible(element), animate);
                list.Add(view);
            }
        }

        static ElementView AddView(GameObject go, string type)
        {
            switch (type)
            {
                case ElementType.Label: return go.AddComponent<LabelView>();
                case ElementType.Pin: return go.AddComponent<PinView>();
                case ElementType.Card: return go.AddComponent<CardView>();
                case ElementType.Gizmo: return go.AddComponent<GizmoView>();
                case ElementType.Flow: return go.AddComponent<FlowView>();
                case ElementType.Highlight: return go.AddComponent<HighlightView>();
                case ElementType.Chart: return go.AddComponent<ChartView>();
                case ElementType.Checklist: return go.AddComponent<ChecklistView>();
                case ElementType.Avatar: return go.AddComponent<AvatarView>();
                case ElementType.Panel: return go.AddComponent<PanelView>();
                default: return null;
            }
        }

        void Remove(Build build)
        {
            List<ElementView> list;
            if (!_views.TryGetValue(build, out list)) return;
            _views.Remove(build);
            foreach (ElementView view in list)
                if (view != null) view.Dismiss();
        }

        void Refresh(Build build)
        {
            List<ElementView> list;
            if (!_views.TryGetValue(build, out list)) return;
            foreach (ElementView view in list)
            {
                if (view == null) continue;
                view.SetShown(build.IsVisible(view.Element));
                view.OnBuildChanged();
            }
        }

        /// <summary>Rebuilds every view against the current room model, without animation.</summary>
        public void Relayout()
        {
            foreach (var pair in _views)
                foreach (ElementView view in pair.Value)
                    if (view != null) Destroy(view.gameObject);
            _views.Clear();
            foreach (Build build in _app.Store.Builds) Create(build, false);
        }

        public int ViewCount
        {
            get
            {
                int n = 0;
                foreach (var pair in _views) n += pair.Value.Count;
                return n;
            }
        }
    }
}
