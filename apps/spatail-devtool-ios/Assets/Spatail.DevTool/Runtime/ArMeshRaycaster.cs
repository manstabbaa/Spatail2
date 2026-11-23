using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Spatail.DevTool
{
    /// <summary>
    /// Casts rays into the mesh ARKit reconstructs on devices with a LiDAR scanner.
    ///
    /// The mesh is the only thing ARKit offers that answers an arbitrary world ray against real geometry, so it is
    /// the first thing <see cref="ArSensor"/> asks. Triangles are tested here rather than through physics colliders
    /// so that the real world never gets in the way of the pointer that clicks on virtual content.
    /// On devices without LiDAR no mesh ever arrives and this class stays empty.
    /// </summary>
    public sealed class ArMeshRaycaster
    {
        sealed class Chunk
        {
            public MeshFilter filter;
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<int> triangles = new List<int>();
            public Bounds bounds;
            public bool stale = true;
        }

        readonly Dictionary<MeshFilter, Chunk> _chunks = new Dictionary<MeshFilter, Chunk>();
        readonly List<MeshFilter> _gone = new List<MeshFilter>();
        bool _failed;

        /// <summary>Pieces of mesh received so far.</summary>
        public int ChunkCount => _chunks.Count;
        /// <summary>Set if the mesh data could not be read; the mesh is then ignored.</summary>
        public string Failure { get; private set; }

        public ArMeshRaycaster(ARMeshManager manager)
        {
            manager.meshesChanged += OnMeshesChanged;
        }

        void OnMeshesChanged(ARMeshesChangedEventArgs change)
        {
            if (change.added != null) foreach (MeshFilter filter in change.added) Touch(filter);
            if (change.updated != null) foreach (MeshFilter filter in change.updated) Touch(filter);
            if (change.removed != null) foreach (MeshFilter filter in change.removed) if (filter != null) _chunks.Remove(filter);
        }

        void Touch(MeshFilter filter)
        {
            if (filter == null) return;
            Chunk chunk;
            if (!_chunks.TryGetValue(filter, out chunk))
            {
                chunk = new Chunk { filter = filter };
                _chunks[filter] = chunk;
            }
            chunk.stale = true;
        }

        /// <summary>The nearest triangle along a world ray, within <paramref name="maxDistance"/>.</summary>
        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out Vector3 point, out Vector3 normal, out float distance)
        {
            point = normal = Vector3.zero;
            distance = maxDistance;
            if (_failed || _chunks.Count == 0) return false;

            bool found = false;
            foreach (KeyValuePair<MeshFilter, Chunk> pair in _chunks)
            {
                Chunk chunk = pair.Value;
                if (chunk.filter == null) { _gone.Add(pair.Key); continue; }
                if (!chunk.filter.gameObject.activeInHierarchy) continue;
                if (chunk.stale && !Refresh(chunk)) return false;
                if (chunk.triangles.Count == 0) continue;

                // Work in the chunk's own space. The direction is transformed as a vector and left unnormalised, so
                // a distance along it is the same number in both spaces whatever the chunk's transform is.
                Transform t = chunk.filter.transform;
                Vector3 o = t.InverseTransformPoint(origin);
                Vector3 d = t.InverseTransformVector(direction);
                if (!HitsBounds(chunk.bounds, o, d, distance)) continue;

                List<Vector3> vertices = chunk.vertices;
                List<int> triangles = chunk.triangles;
                for (int i = 0; i + 2 < triangles.Count; i += 3)
                {
                    Vector3 a = vertices[triangles[i]];
                    Vector3 e1 = vertices[triangles[i + 1]] - a;
                    Vector3 e2 = vertices[triangles[i + 2]] - a;
                    // Möller–Trumbore, both faces.
                    Vector3 p = Vector3.Cross(d, e2);
                    float det = Vector3.Dot(e1, p);
                    if (det > -1e-9f && det < 1e-9f) continue;
                    float inv = 1f / det;
                    Vector3 s = o - a;
                    float u = Vector3.Dot(s, p) * inv;
                    if (u < 0f || u > 1f) continue;
                    Vector3 q = Vector3.Cross(s, e1);
                    float v = Vector3.Dot(d, q) * inv;
                    if (v < 0f || u + v > 1f) continue;
                    float along = Vector3.Dot(e2, q) * inv;
                    if (along < 0.02f || along >= distance) continue;

                    distance = along;
                    normal = t.TransformDirection(Vector3.Cross(e1, e2));
                    found = true;
                }
            }
            if (_gone.Count > 0)
            {
                foreach (MeshFilter filter in _gone) _chunks.Remove(filter);
                _gone.Clear();
            }
            if (!found) return false;

            point = origin + direction * distance;
            normal = normal.sqrMagnitude > 1e-12f ? normal.normalized : -direction;
            if (Vector3.Dot(normal, direction) > 0f) normal = -normal;
            return true;
        }

        bool Refresh(Chunk chunk)
        {
            chunk.stale = false;
            chunk.vertices.Clear();
            chunk.triangles.Clear();
            Mesh mesh = chunk.filter.sharedMesh;
            if (mesh == null || mesh.vertexCount == 0) return true;
            try
            {
                mesh.GetVertices(chunk.vertices);
                mesh.GetTriangles(chunk.triangles, 0);
                chunk.bounds = mesh.bounds;
                return true;
            }
            catch (Exception ex)
            {
                // The mesh is not readable on this device or Unity version: carry on without it.
                _failed = true;
                Failure = ex.Message;
                return false;
            }
        }

        static bool HitsBounds(Bounds bounds, Vector3 origin, Vector3 direction, float maxDistance)
        {
            float near = 0f, far = maxDistance;
            Vector3 min = bounds.min, max = bounds.max;
            for (int axis = 0; axis < 3; axis++)
            {
                float o = origin[axis], d = direction[axis];
                if (Mathf.Abs(d) < 1e-9f)
                {
                    if (o < min[axis] || o > max[axis]) return false;
                    continue;
                }
                float t1 = (min[axis] - o) / d, t2 = (max[axis] - o) / d;
                if (t1 > t2) { float swap = t1; t1 = t2; t2 = swap; }
                if (t1 > near) near = t1;
                if (t2 < far) far = t2;
                if (near > far) return false;
            }
            return true;
        }
    }
}
