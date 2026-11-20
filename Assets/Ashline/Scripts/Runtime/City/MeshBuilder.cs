using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ashline.City
{
    /// <summary>
    /// Accumulates quads per material and bakes them into one multi-submesh Mesh.
    /// UVs are world-planar in meters divided by the texture's real size, so every surface keeps
    /// its photographed scale no matter how big the face is.
    /// </summary>
    public class MeshBuilder
    {
        class Part
        {
            public readonly List<int> tris = new List<int>();
        }

        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<Vector2> uvs2 = new List<Vector2>();
        readonly Dictionary<Material, Part> parts = new Dictionary<Material, Part>();
        readonly List<Material> order = new List<Material>();

        public int VertexCount => verts.Count;

        /// <summary>Written to UV1 of every vertex added while set (window shaders read the pane size from it).</summary>
        public Vector2 Stamp;

        /// <summary>Applied to every vertex added while set, so buildings can be authored axis-aligned and placed rotated.</summary>
        public Matrix4x4 Matrix
        {
            get => matrix;
            set { matrix = value; transformed = value != Matrix4x4.identity; }
        }

        Matrix4x4 matrix = Matrix4x4.identity;
        bool transformed;

        Part PartFor(Material m)
        {
            if (!parts.TryGetValue(m, out var p))
            {
                p = new Part();
                parts[m] = p;
                order.Add(m);
            }
            return p;
        }

        /// <summary>Quad from four corners in counter-clockwise order seen from the front.</summary>
        public void Quad(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            if (transformed)
            {
                a = matrix.MultiplyPoint3x4(a);
                b = matrix.MultiplyPoint3x4(b);
                c = matrix.MultiplyPoint3x4(c);
                d = matrix.MultiplyPoint3x4(d);
            }
            var n = Vector3.Cross(b - a, d - a);
            if (n.sqrMagnitude < 1e-12f) n = Vector3.Cross(c - b, a - b);
            n.Normalize();
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            normals.Add(n); normals.Add(n); normals.Add(n); normals.Add(n);
            uvs.Add(ua); uvs.Add(ub); uvs.Add(uc); uvs.Add(ud);
            uvs2.Add(Stamp); uvs2.Add(Stamp); uvs2.Add(Stamp); uvs2.Add(Stamp);
            var t = PartFor(m).tris;
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        /// <summary>Single triangle, counter-clockwise seen from the front.</summary>
        public void Tri(Material m, Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            if (transformed)
            {
                a = matrix.MultiplyPoint3x4(a);
                b = matrix.MultiplyPoint3x4(b);
                c = matrix.MultiplyPoint3x4(c);
            }
            var n = Vector3.Cross(b - a, c - a).normalized;
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            normals.Add(n); normals.Add(n); normals.Add(n);
            uvs.Add(ua); uvs.Add(ub); uvs.Add(uc);
            uvs2.Add(Stamp); uvs2.Add(Stamp); uvs2.Add(Stamp);
            var t = PartFor(m).tris;
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        /// <summary>Raw vertex/triangle append with explicit normals (smooth surfaces such as palm fronds).</summary>
        public void Raw(Material m, IList<Vector3> v, IList<Vector3> n, IList<Vector2> uv, IList<int> tris)
        {
            int i0 = verts.Count;
            for (int i = 0; i < v.Count; i++)
            {
                verts.Add(transformed ? matrix.MultiplyPoint3x4(v[i]) : v[i]);
                normals.Add(transformed ? matrix.MultiplyVector(n[i]).normalized : n[i]);
                uvs.Add(uv[i]);
                uvs2.Add(Stamp);
            }
            var t = PartFor(m).tris;
            foreach (var k in tris) t.Add(i0 + k);
        }

        /// <summary>
        /// Vertical wall face from p0 to p1 (bottom edge) between heights y0..y1, facing to the
        /// right of the p0→p1 direction. UV u runs along the wall from uOffset.
        /// </summary>
        public void Wall(Material m, Vector2 size, Vector3 p0, Vector3 p1, float y0, float y1, float uOffset = 0f)
        {
            float len = Vector3.Distance(new Vector3(p0.x, 0, p0.z), new Vector3(p1.x, 0, p1.z));
            float u0 = uOffset / size.x, u1 = (uOffset + len) / size.x;
            float v0 = y0 / size.y, v1 = y1 / size.y;
            Quad(m,
                new Vector3(p0.x, y0, p0.z), new Vector3(p0.x, y1, p0.z), new Vector3(p1.x, y1, p1.z), new Vector3(p1.x, y0, p1.z),
                new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0));
        }

        /// <summary>Horizontal face facing up (or down when flip) with world-planar UVs.</summary>
        public void Floor(Material m, Vector2 size, float x0, float z0, float x1, float z1, float y, bool flip = false)
        {
            var a = new Vector3(x0, y, z0);
            var b = new Vector3(x0, y, z1);
            var c = new Vector3(x1, y, z1);
            var d = new Vector3(x1, y, z0);
            Vector2 U(Vector3 p) => new Vector2(p.x / size.x, p.z / size.y);
            if (flip) Quad(m, a, d, c, b, U(a), U(d), U(c), U(b));
            else Quad(m, a, b, c, d, U(a), U(b), U(c), U(d));
        }

        /// <summary>Axis-aligned box; all six faces unless the caller skips the bottom.</summary>
        public void Box(Material m, Vector2 size, Vector3 min, Vector3 max, bool bottom = false)
        {
            Floor(m, size, min.x, min.z, max.x, max.z, max.y);
            if (bottom) Floor(m, size, min.x, min.z, max.x, max.z, min.y, true);
            var p00 = new Vector3(min.x, 0, min.z);
            var p10 = new Vector3(max.x, 0, min.z);
            var p11 = new Vector3(max.x, 0, max.z);
            var p01 = new Vector3(min.x, 0, max.z);
            Wall(m, size, p00, p10, min.y, max.y, min.x);
            Wall(m, size, p10, p11, min.y, max.y, min.z);
            Wall(m, size, p11, p01, min.y, max.y, -max.x);
            Wall(m, size, p01, p00, min.y, max.y, -max.z);
        }

        /// <summary>Oriented box: center, half extents along right/up/forward axes.</summary>
        [System.Flags]
        public enum Faces { Front = 1, Back = 2, Right = 4, Left = 8, Top = 16, Bottom = 32, Sides = Right | Left, All = 63 }

        public void OrientedBox(Material m, Vector2 size, Vector3 c, Vector3 right, Vector3 up, Vector3 fwd, Vector3 half, Faces faces = Faces.All)
        {
            // Face winding below assumes Cross(right, up) == fwd; the box is symmetric, so only the sign of right can change.
            right = Vector3.Cross(up, fwd).normalized;
            Vector3 r = right * half.x, u = up * half.y, f = fwd * half.z;
            Vector3 P(int sx, int sy, int sz) => c + r * sx + u * sy + f * sz;
            Vector2 uv(float a, float b) => new Vector2(a / size.x, b / size.y);
            float w = half.x * 2, h = half.y * 2, d = half.z * 2;
            if ((faces & Faces.Front) != 0) Quad(m, P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), P(-1, -1, 1), uv(0, 0), uv(0, h), uv(w, h), uv(w, 0));
            if ((faces & Faces.Back) != 0) Quad(m, P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1), uv(0, 0), uv(0, h), uv(w, h), uv(w, 0));
            if ((faces & Faces.Right) != 0) Quad(m, P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), uv(0, 0), uv(0, h), uv(d, h), uv(d, 0));
            if ((faces & Faces.Left) != 0) Quad(m, P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), P(-1, -1, -1), uv(0, 0), uv(0, h), uv(d, h), uv(d, 0));
            if ((faces & Faces.Top) != 0) Quad(m, P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), uv(0, 0), uv(0, d), uv(w, d), uv(w, 0));
            if ((faces & Faces.Bottom) != 0) Quad(m, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), uv(0, 0), uv(w, 0), uv(w, d), uv(0, d));
        }

        /// <summary>Upright n-sided cylinder (no caps) between two heights.</summary>
        public void Cylinder(Material m, Vector2 size, Vector3 baseCenter, float radius, float height, int sides = 8)
        {
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2 / sides, a1 = (i + 1) * Mathf.PI * 2 / sides;
                var p0 = baseCenter + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius;
                var p1 = baseCenter + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
                float u0 = a0 * radius, u1 = a1 * radius;
                Quad(m,
                    p0, p0 + Vector3.up * height, p1 + Vector3.up * height, p1,
                    new Vector2(u0 / size.x, 0), new Vector2(u0 / size.x, height / size.y), new Vector2(u1 / size.x, height / size.y), new Vector2(u1 / size.x, 0));
            }
        }

        /// <summary>Bakes into a GameObject child of parent. Returns null when empty.</summary>
        public GameObject Build(string name, Transform parent, bool castShadows = true, bool collider = false)
        {
            if (verts.Count == 0) return null;
            var mesh = new Mesh { name = name, indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, uvs2);
            mesh.subMeshCount = order.Count;
            for (int i = 0; i < order.Count; i++) mesh.SetTriangles(parts[order[i]].tris, i, false);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            mesh.UploadMeshData(!collider);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.isStatic = true;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = order.ToArray();
            mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }
    }
}
