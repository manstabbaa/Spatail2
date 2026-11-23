using System.Collections.Generic;
using UnityEngine;

namespace Spatail
{
    /// <summary>
    /// Accumulates procedural primitives into one mesh. Triangles are oriented automatically from the vertex normals,
    /// so shapes face outward whatever order their vertices were generated in.
    /// </summary>
    public sealed class MeshBuilder
    {
        readonly List<Vector3> _vertices = new List<Vector3>();
        readonly List<Vector3> _normals = new List<Vector3>();
        readonly List<Color> _colors = new List<Color>();
        readonly List<Vector2> _uvs = new List<Vector2>();
        readonly List<int> _triangles = new List<int>();

        public int VertexCount => _vertices.Count;

        int Vertex(Vector3 position, Vector3 normal, Color color, Vector2 uv)
        {
            _vertices.Add(position);
            _normals.Add(normal);
            _colors.Add(color);
            _uvs.Add(uv);
            return _vertices.Count - 1;
        }

        void Triangle(int a, int b, int c)
        {
            // Unity front faces wind clockwise: the geometric normal is Cross(b - a, c - a).
            Vector3 geometric = Vector3.Cross(_vertices[b] - _vertices[a], _vertices[c] - _vertices[a]);
            Vector3 intended = _normals[a] + _normals[b] + _normals[c];
            if (Vector3.Dot(geometric, intended) < 0f) { int t = b; b = c; c = t; }
            _triangles.Add(a);
            _triangles.Add(b);
            _triangles.Add(c);
        }

        void Quad(int a, int b, int c, int d)
        {
            Triangle(a, b, c);
            Triangle(a, c, d);
        }

        public MeshBuilder AddBox(Matrix4x4 m, Vector3 size, Color color)
        {
            Vector3 h = size * 0.5f;
            Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
            for (int axis = 0; axis < 3; axis++)
            {
                Vector3 n = axes[axis];
                Vector3 u = axes[(axis + 1) % 3];
                Vector3 v = axes[(axis + 2) % 3];
                float hn = h[axis], hu = h[(axis + 1) % 3], hv = h[(axis + 2) % 3];
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector3 centre = n * (hn * sign);
                    Vector3 normal = m.MultiplyVector(n * sign).normalized;
                    int a = Vertex(m.MultiplyPoint3x4(centre - u * hu - v * hv), normal, color, new Vector2(0, 0));
                    int b = Vertex(m.MultiplyPoint3x4(centre + u * hu - v * hv), normal, color, new Vector2(1, 0));
                    int c = Vertex(m.MultiplyPoint3x4(centre + u * hu + v * hv), normal, color, new Vector2(1, 1));
                    int d = Vertex(m.MultiplyPoint3x4(centre - u * hu + v * hv), normal, color, new Vector2(0, 1));
                    Quad(a, b, c, d);
                }
            }
            return this;
        }

        /// <summary>Cylinder or cone along local Y, centred on the origin. Pass a different top radius for a taper.</summary>
        public MeshBuilder AddCylinder(Matrix4x4 m, float bottomRadius, float topRadius, float length, int segments, Color color, bool caps = true)
        {
            float half = length * 0.5f;
            float slope = (bottomRadius - topRadius) / Mathf.Max(length, 1e-5f);
            int start = _vertices.Count;
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float angle = t * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Vector3 normal = m.MultiplyVector(new Vector3(dir.x, slope, dir.z)).normalized;
                Vertex(m.MultiplyPoint3x4(dir * bottomRadius + Vector3.down * half), normal, color, new Vector2(t, 0));
                Vertex(m.MultiplyPoint3x4(dir * topRadius + Vector3.up * half), normal, color, new Vector2(t, 1));
            }
            for (int i = 0; i < segments; i++)
            {
                int a = start + i * 2;
                Quad(a, a + 1, a + 3, a + 2);
            }
            if (caps)
            {
                if (bottomRadius > 1e-5f) AddDisc(m * Matrix4x4.Translate(Vector3.down * half), bottomRadius, segments, color, false);
                if (topRadius > 1e-5f) AddDisc(m * Matrix4x4.Translate(Vector3.up * half), topRadius, segments, color, true);
            }
            return this;
        }

        /// <summary>Flat disc in the local XZ plane, facing +Y or -Y.</summary>
        public MeshBuilder AddDisc(Matrix4x4 m, float radius, int segments, Color color, bool faceUp = true)
        {
            Vector3 normal = m.MultiplyVector(faceUp ? Vector3.up : Vector3.down).normalized;
            int centre = Vertex(m.MultiplyPoint3x4(Vector3.zero), normal, color, new Vector2(0.5f, 0.5f));
            int first = _vertices.Count;
            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Vertex(m.MultiplyPoint3x4(dir * radius), normal, color, new Vector2(dir.x * 0.5f + 0.5f, dir.z * 0.5f + 0.5f));
            }
            for (int i = 0; i < segments; i++) Triangle(centre, first + i, first + i + 1);
            return this;
        }

        /// <summary>Torus (or an arc of one) lying in the local XZ plane. The arc starts on +X and sweeps toward +Z.</summary>
        public MeshBuilder AddTorus(Matrix4x4 m, float ringRadius, float tubeRadius, int segments, int sides, Color color, float arcDegrees = 360f)
        {
            int start = _vertices.Count;
            float arc = arcDegrees * Mathf.Deg2Rad;
            for (int i = 0; i <= segments; i++)
            {
                float u = (float)i / segments;
                float a = u * arc;
                var radial = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                for (int j = 0; j <= sides; j++)
                {
                    float b = (float)j / sides * Mathf.PI * 2f;
                    Vector3 normal = radial * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    Vector3 position = radial * ringRadius + normal * tubeRadius;
                    Vertex(m.MultiplyPoint3x4(position), m.MultiplyVector(normal).normalized, color, new Vector2(u * ringRadius * arc, (float)j / sides));
                }
            }
            int ring = sides + 1;
            for (int i = 0; i < segments; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a = start + i * ring + j;
                    Quad(a, a + 1, a + ring + 1, a + ring);
                }
            if (arcDegrees < 359.9f)
            {
                // Close the two open ends.
                var startFrame = m * Matrix4x4.TRS(new Vector3(ringRadius, 0, 0), Quaternion.Euler(90, 0, 0), Vector3.one);
                AddDisc(startFrame, tubeRadius, sides, color, false);
                var endRadial = new Vector3(Mathf.Cos(arc), 0, Mathf.Sin(arc));
                var endTangent = new Vector3(-Mathf.Sin(arc), 0, Mathf.Cos(arc));
                var endFrame = m * Matrix4x4.TRS(endRadial * ringRadius, Quaternion.FromToRotation(Vector3.up, endTangent), Vector3.one);
                AddDisc(endFrame, tubeRadius, sides, color, true);
            }
            return this;
        }

        public MeshBuilder AddSphere(Matrix4x4 m, float radius, int segments, Color color)
        {
            int rings = Mathf.Max(4, segments / 2);
            int start = _vertices.Count;
            for (int i = 0; i <= rings; i++)
            {
                float v = (float)i / rings;
                float polar = v * Mathf.PI;
                for (int j = 0; j <= segments; j++)
                {
                    float u = (float)j / segments;
                    float az = u * Mathf.PI * 2f;
                    var normal = new Vector3(Mathf.Sin(polar) * Mathf.Cos(az), Mathf.Cos(polar), Mathf.Sin(polar) * Mathf.Sin(az));
                    Vertex(m.MultiplyPoint3x4(normal * radius), m.MultiplyVector(normal).normalized, color, new Vector2(u, v));
                }
            }
            int row = segments + 1;
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < segments; j++)
                {
                    int a = start + i * row + j;
                    // Skip the degenerate triangles at the poles.
                    if (i > 0) Triangle(a, a + 1, a + row + 1);
                    if (i < rings - 1) Triangle(a, a + row + 1, a + row);
                }
            return this;
        }

        /// <summary>
        /// Straight tube from <paramref name="from"/> to <paramref name="to"/>. UV.x carries distance in metres starting at
        /// <paramref name="uStart"/>, so textures flow continuously across consecutive segments.
        /// </summary>
        public MeshBuilder AddTubeSegment(Vector3 from, Vector3 to, float radius, int sides, Color color, float uStart)
        {
            Vector3 axis = to - from;
            float length = axis.magnitude;
            if (length < 1e-5f) return this;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, axis / length);
            int start = _vertices.Count;
            for (int i = 0; i <= sides; i++)
            {
                float t = (float)i / sides;
                float angle = t * Mathf.PI * 2f;
                Vector3 normal = rotation * new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Vertex(from + normal * radius, normal, color, new Vector2(uStart, t));
                Vertex(to + normal * radius, normal, color, new Vector2(uStart + length, t));
            }
            for (int i = 0; i < sides; i++)
            {
                int a = start + i * 2;
                Quad(a, a + 1, a + 3, a + 2);
            }
            return this;
        }

        /// <summary>One free-standing triangle with a flat normal, visible from both sides.</summary>
        public MeshBuilder AddTriangle(Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            if (normal.sqrMagnitude < 1e-10f) return this;
            for (int side = 0; side < 2; side++)
            {
                Vector3 n = side == 0 ? normal : -normal;
                int i0 = Vertex(a, n, ca, Vector2.zero), i1 = Vertex(b, n, cb, Vector2.zero), i2 = Vertex(c, n, cc, Vector2.zero);
                Triangle(i0, i1, i2);
            }
            return this;
        }

        public Mesh ToMesh(string name = "Spatail Mesh")
        {
            var mesh = new Mesh { name = name };
            if (_vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetColors(_colors);
            mesh.SetUVs(0, _uvs);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>Shared primitive meshes and small helpers for putting them in the scene.</summary>
    public static class MeshFactory
    {
        static Mesh _quad, _unitCylinder, _unitSphere, _unitBox, _disc;

        /// <summary>1 x 1 quad in the XY plane, centred, facing -Z (toward a viewer looking along +Z).</summary>
        public static Mesh Quad
        {
            get
            {
                if (_quad != null) return _quad;
                _quad = new Mesh { name = "Spatail Quad" };
                _quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(0.5f, -0.5f, 0) };
                _quad.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
                _quad.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
                _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                _quad.RecalculateBounds();
                return _quad;
            }
        }

        /// <summary>Radius 0.5, height 1, along Y. Scale it to size.</summary>
        public static Mesh UnitCylinder =>
            _unitCylinder != null ? _unitCylinder : (_unitCylinder = new MeshBuilder().AddCylinder(Matrix4x4.identity, 0.5f, 0.5f, 1f, 28, Color.white).ToMesh("Spatail Cylinder"));

        public static Mesh UnitSphere =>
            _unitSphere != null ? _unitSphere : (_unitSphere = new MeshBuilder().AddSphere(Matrix4x4.identity, 0.5f, 24, Color.white).ToMesh("Spatail Sphere"));

        public static Mesh UnitBox =>
            _unitBox != null ? _unitBox : (_unitBox = new MeshBuilder().AddBox(Matrix4x4.identity, Vector3.one, Color.white).ToMesh("Spatail Box"));

        /// <summary>Radius 0.5 disc in the XZ plane facing +Y.</summary>
        public static Mesh Disc =>
            _disc != null ? _disc : (_disc = new MeshBuilder().AddDisc(Matrix4x4.identity, 0.5f, 32, Color.white).ToMesh("Spatail Disc"));

        public static GameObject Spawn(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return go;
        }

        /// <summary>A thin rod between two points in the parent's local space.</summary>
        public static GameObject Rod(string name, Transform parent, Vector3 from, Vector3 to, float thickness, Material material)
        {
            GameObject go = Spawn(name, parent, UnitCylinder, material);
            Vector3 axis = to - from;
            float length = axis.magnitude;
            go.transform.localPosition = (from + to) * 0.5f;
            go.transform.localRotation = length > 1e-5f ? Quaternion.FromToRotation(Vector3.up, axis / length) : Quaternion.identity;
            go.transform.localScale = new Vector3(thickness, length, thickness);
            return go;
        }
    }
}
