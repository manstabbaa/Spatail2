using UnityEngine;

namespace Spatail
{
    /// <summary>
    /// The sweep of light that plays while Spatail reads the room: a tall translucent sheet that crosses the space
    /// in front of the user, leaving labels behind it.
    /// </summary>
    public sealed class ScanEffect : MonoBehaviour
    {
        public const float Duration = 1.7f;

        float _age;
        Vector3 _from, _to;
        Material _sheet, _edge;

        public static ScanEffect Play(Vector3 centre, Vector3 right, float floorY)
        {
            var go = new GameObject("Spatail Scan");
            var effect = go.AddComponent<ScanEffect>();
            right.y = 0f;
            right = right.sqrMagnitude > 1e-6f ? right.normalized : Vector3.right;
            centre.y = floorY;
            effect._from = centre - right * 2.6f;
            effect._to = centre + right * 2.6f;
            // The sheet's face looks along the sweep direction.
            go.transform.rotation = Quaternion.LookRotation(right, Vector3.up);
            go.transform.position = effect._from;

            effect._sheet = Assets.Glow(Theme.Accent.WithAlpha(0.12f), 0f);
            effect._sheet.SetFloat("_Cull", 0f);
            GameObject sheet = MeshFactory.Spawn("Sheet", go.transform, MeshFactory.Quad, effect._sheet);
            sheet.transform.localPosition = new Vector3(0, 1.4f, 0);
            sheet.transform.localScale = new Vector3(5f, 2.8f, 1f);

            effect._edge = Assets.Glow(Theme.Accent.WithAlpha(0.55f), 0f);
            GameObject edge = MeshFactory.Spawn("Edge", go.transform, MeshFactory.UnitBox, effect._edge);
            edge.transform.localPosition = new Vector3(0, 0.004f, 0);
            edge.transform.localScale = new Vector3(5f, 0.006f, 0.012f);
            return effect;
        }

        void Update()
        {
            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / Duration);
            float eased = t * t * (3f - 2f * t);
            transform.position = Vector3.Lerp(_from, _to, eased);
            float fade = Mathf.Sin(t * Mathf.PI);
            _sheet.SetColor("_Color", Theme.Accent.WithAlpha(0.14f * fade));
            _edge.SetColor("_Color", Theme.Accent.WithAlpha(0.6f * fade));
            if (t >= 1f) Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_sheet != null) Destroy(_sheet);
            if (_edge != null) Destroy(_edge);
        }
    }
}
