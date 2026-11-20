using System.Collections.Generic;
using UnityEngine;

namespace Ashline.City
{
    /// <summary>Procedural coconut palm: curved tapering trunk and drooping alpha-cut fronds.</summary>
    public static class PalmBuilder
    {
        public static void Build(MeshBuilder mb, Material trunk, Material frond, Vector3 basePos, int seed)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            float height = 7.5f + R() * 5f;
            float leanAngle = R() * Mathf.PI * 2;
            var lean = new Vector3(Mathf.Cos(leanAngle), 0, Mathf.Sin(leanAngle)) * (0.6f + R() * 1.8f);

            Vector3 Spine(float t) => basePos + Vector3.up * (height * t) + lean * (t * t);

            // Trunk.
            const int rings = 12, sides = 8;
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var tris = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float t = r / (float)rings;
                var c = Spine(t);
                var up = (Spine(Mathf.Min(1, t + 0.02f)) - Spine(Mathf.Max(0, t - 0.02f))).normalized;
                var side = Vector3.Cross(up, Vector3.forward).normalized;
                var side2 = Vector3.Cross(up, side);
                float radius = Mathf.Lerp(0.23f, 0.15f, t) + 0.12f * Mathf.Exp(-t * 18f);
                for (int s = 0; s <= sides; s++)
                {
                    float a = s / (float)sides * Mathf.PI * 2;
                    var dir = side * Mathf.Cos(a) + side2 * Mathf.Sin(a);
                    v.Add(c + dir * radius);
                    n.Add(dir);
                    uv.Add(new Vector2(s / (float)sides, height * t / 0.9f));
                }
            }
            for (int r = 0; r < rings; r++)
            for (int s = 0; s < sides; s++)
            {
                int i0 = r * (sides + 1) + s, i1 = i0 + 1, i2 = i0 + sides + 1, i3 = i2 + 1;
                tris.Add(i0); tris.Add(i1); tris.Add(i2);
                tris.Add(i1); tris.Add(i3); tris.Add(i2);
            }
            mb.Raw(trunk, v, n, uv, tris);

            // Crown of fronds.
            var top = Spine(1f);
            int fronds = 10 + rng.Next(4);
            v.Clear(); n.Clear(); uv.Clear(); tris.Clear();
            const int segs = 9;
            for (int f = 0; f < fronds; f++)
            {
                float yaw = f / (float)fronds * Mathf.PI * 2 + R() * 0.4f;
                var dir = new Vector3(Mathf.Cos(yaw), 0, Mathf.Sin(yaw));
                var across = Vector3.Cross(Vector3.up, dir);
                float length = 3.6f + R() * 1.6f;
                float lift = 0.5f + R() * 0.6f;
                float droop = 0.22f + R() * 0.12f;
                int start = v.Count;
                for (int k = 0; k <= segs; k++)
                {
                    float t = k / (float)segs;
                    float d = length * t;
                    var p = top + dir * d + Vector3.up * (lift * d - droop * d * d);
                    float w = 0.62f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Clamp(t * 1.05f, 0, 1))), 0.6f) + 0.05f;
                    var tangent = (dir + Vector3.up * (lift - 2 * droop * d)).normalized;
                    var normal = Vector3.Cross(tangent, across).normalized;
                    if (normal.y < 0) normal = -normal;
                    // Leaflets fold down from the rib in a shallow V.
                    var fold = -normal * (w * 0.28f);
                    v.Add(p - across * w + fold); n.Add((normal - across * 0.4f).normalized); uv.Add(new Vector2(t, 0));
                    v.Add(p); n.Add(normal); uv.Add(new Vector2(t, 0.5f));
                    v.Add(p + across * w + fold); n.Add((normal + across * 0.4f).normalized); uv.Add(new Vector2(t, 1));
                }
                for (int k = 0; k < segs; k++)
                {
                    int a = start + k * 3, b = a + 3;
                    tris.Add(a); tris.Add(a + 1); tris.Add(b);
                    tris.Add(b); tris.Add(a + 1); tris.Add(b + 1);
                    tris.Add(a + 1); tris.Add(a + 2); tris.Add(b + 1);
                    tris.Add(b + 1); tris.Add(a + 2); tris.Add(b + 2);
                }
            }
            mb.Raw(frond, v, n, uv, tris);
        }
    }
}
