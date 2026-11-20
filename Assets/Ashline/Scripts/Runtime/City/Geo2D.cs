using System.Collections.Generic;
using UnityEngine;

namespace Ashline.City
{
    /// <summary>Plan-view geometry helpers. Vector2 is (x, z) in meters.</summary>
    public static class Geo2D
    {
        public static Vector3 X0Z(this Vector2 p, float y = 0f) => new Vector3(p.x, y, p.y);
        public static Vector2 XZ(this Vector3 p) => new Vector2(p.x, p.z);
        public static Vector2 Perp(Vector2 d) => new Vector2(-d.y, d.x);
        public static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        /// <summary>Chaikin corner cutting; closed loops wrap around.</summary>
        public static List<Vector2> Chaikin(IList<Vector2> pts, int iterations, bool closed)
        {
            var cur = new List<Vector2>(pts);
            for (int it = 0; it < iterations; it++)
            {
                var next = new List<Vector2>(cur.Count * 2);
                int n = cur.Count;
                if (!closed) next.Add(cur[0]);
                for (int i = 0; i < (closed ? n : n - 1); i++)
                {
                    var a = cur[i];
                    var b = cur[(i + 1) % n];
                    next.Add(Vector2.Lerp(a, b, 0.25f));
                    next.Add(Vector2.Lerp(a, b, 0.75f));
                }
                if (!closed) next.Add(cur[n - 1]);
                cur = next;
            }
            return cur;
        }

        /// <summary>Centripetal-ish Catmull-Rom through the control points, sampled about every `step` meters.</summary>
        public static List<Vector2> CatmullRom(IList<Vector2> pts, float step, bool closed = false)
        {
            var result = new List<Vector2>();
            int n = pts.Count;
            if (n < 2) return new List<Vector2>(pts);
            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                Vector2 p0 = pts[closed ? (i - 1 + n) % n : Mathf.Max(i - 1, 0)];
                Vector2 p1 = pts[i];
                Vector2 p2 = pts[(i + 1) % n];
                Vector2 p3 = pts[closed ? (i + 2) % n : Mathf.Min(i + 2, n - 1)];
                int count = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(p1, p2) / step));
                for (int k = 0; k < count; k++)
                {
                    float t = k / (float)count;
                    float t2 = t * t, t3 = t2 * t;
                    result.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            if (!closed) result.Add(pts[n - 1]);
            return result;
        }

        public static float Length(IList<Vector2> pts)
        {
            float l = 0;
            for (int i = 1; i < pts.Count; i++) l += Vector2.Distance(pts[i - 1], pts[i]);
            return l;
        }

        /// <summary>Re-samples a polyline at a uniform spacing.</summary>
        public static List<Vector2> Resample(IList<Vector2> pts, float step)
        {
            var outPts = new List<Vector2> { pts[0] };
            float carry = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1];
                var b = pts[i];
                float seg = Vector2.Distance(a, b);
                float d = step - carry;
                while (d <= seg)
                {
                    outPts.Add(Vector2.Lerp(a, b, d / seg));
                    d += step;
                }
                carry = seg - (d - step);
            }
            if (Vector2.Distance(outPts[outPts.Count - 1], pts[pts.Count - 1]) > step * 0.3f) outPts.Add(pts[pts.Count - 1]);
            else outPts[outPts.Count - 1] = pts[pts.Count - 1];
            return outPts;
        }

        public static bool PointInPolygon(Vector2 p, IList<Vector2> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i];
                var b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        public static float SignedArea(IList<Vector2> poly)
        {
            float a = 0;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++) a += Cross(poly[j], poly[i]);
            return a * 0.5f;
        }

        public static bool SegmentIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out float t, out float u)
        {
            var r = b - a;
            var s = d - c;
            float den = Cross(r, s);
            t = u = 0;
            if (Mathf.Abs(den) < 1e-6f) return false;
            t = Cross(c - a, s) / den;
            u = Cross(c - a, r) / den;
            return t >= 0 && t <= 1 && u >= 0 && u <= 1;
        }

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return Vector2.Distance(p, a + ab * t);
        }

        public static float DistanceToPolyline(Vector2 p, IList<Vector2> line)
        {
            float best = float.MaxValue;
            for (int i = 1; i < line.Count; i++) best = Mathf.Min(best, DistanceToSegment(p, line[i - 1], line[i]));
            return best;
        }

        public static float DistanceToLoop(Vector2 p, IList<Vector2> loop)
        {
            float best = DistanceToSegment(p, loop[loop.Count - 1], loop[0]);
            for (int i = 1; i < loop.Count; i++) best = Mathf.Min(best, DistanceToSegment(p, loop[i - 1], loop[i]));
            return best;
        }

        /// <summary>Offsets a closed loop; positive distance moves toward the inside for counter-clockwise loops.</summary>
        public static List<Vector2> OffsetLoop(IList<Vector2> loop, float distance)
        {
            int n = loop.Count;
            float sign = SignedArea(loop) > 0 ? 1f : -1f;
            var result = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                var prev = loop[(i - 1 + n) % n];
                var cur = loop[i];
                var next = loop[(i + 1) % n];
                var n0 = Perp((cur - prev).normalized);
                var n1 = Perp((next - cur).normalized);
                var nrm = (n0 + n1).normalized;
                float k = Mathf.Max(0.35f, Vector2.Dot(nrm, n0));
                result.Add(cur + nrm * (sign * distance / k));
            }
            return result;
        }

        /// <summary>Ear-clipping triangulation of a simple polygon; returns index triples.</summary>
        public static List<int> Triangulate(IList<Vector2> poly)
        {
            var tris = new List<int>();
            var idx = new List<int>();
            for (int i = 0; i < poly.Count; i++) idx.Add(i);
            bool ccw = SignedArea(poly) > 0;
            int guard = 0;
            while (idx.Count > 3 && guard++ < 100000)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int i0 = idx[(i - 1 + idx.Count) % idx.Count], i1 = idx[i], i2 = idx[(i + 1) % idx.Count];
                    var a = poly[i0];
                    var b = poly[i1];
                    var c = poly[i2];
                    float cr = Cross(b - a, c - b);
                    if (ccw ? cr <= 0 : cr >= 0) continue;
                    bool contains = false;
                    for (int k = 0; k < idx.Count && !contains; k++)
                    {
                        int j = idx[k];
                        if (j == i0 || j == i1 || j == i2) continue;
                        contains = InTriangle(poly[j], a, b, c);
                    }
                    if (contains) continue;
                    tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            if (idx.Count == 3) { tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]); }
            return tris;
        }

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(b - a, p - a), d2 = Cross(c - b, p - b), d3 = Cross(a - c, p - c);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        /// <summary>Separating-axis test for two oriented rectangles given by their four corners.</summary>
        public static bool QuadsOverlap(Vector2[] a, Vector2[] b)
        {
            return !Separated(a, b) && !Separated(b, a);
        }

        static bool Separated(Vector2[] a, Vector2[] b)
        {
            for (int i = 0; i < 4; i++)
            {
                var axis = Perp(a[(i + 1) % 4] - a[i]);
                float aMin = float.MaxValue, aMax = float.MinValue, bMin = float.MaxValue, bMax = float.MinValue;
                foreach (var p in a) { float d = Vector2.Dot(p, axis); aMin = Mathf.Min(aMin, d); aMax = Mathf.Max(aMax, d); }
                foreach (var p in b) { float d = Vector2.Dot(p, axis); bMin = Mathf.Min(bMin, d); bMax = Mathf.Max(bMax, d); }
                if (aMax < bMin || bMax < aMin) return true;
            }
            return false;
        }
    }
}
