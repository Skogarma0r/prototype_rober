using System.Collections.Generic;
using UnityEngine;

namespace Ashline.City
{
    /// <summary>Stretch of road between two junctions (or a dead end / closed loop).</summary>
    public class RoadEdge
    {
        public RoadDef def;
        public List<Vector2> pts;
        public float[] s;               // arc length at each point
        public int a = -1, b = -1;      // junction ids at start and end (-1 = free end)
        public float trimA, trimB;      // distance kept clear for the junction surface
        public bool loop;

        public float Length => s[s.Length - 1];
        public float HalfWidth => def.HalfWidth;
        public float Sidewalk => def.Sidewalk;
        public float Outer => def.HalfWidth + def.Sidewalk;

        public void Sample(float d, out Vector2 p, out Vector2 tangent)
        {
            d = Mathf.Clamp(d, 0, Length);
            int i = System.Array.BinarySearch(s, d);
            if (i < 0) i = ~i;
            i = Mathf.Clamp(i, 1, pts.Count - 1);
            float seg = s[i] - s[i - 1];
            float t = seg > 1e-5f ? (d - s[i - 1]) / seg : 0f;
            p = Vector2.Lerp(pts[i - 1], pts[i], t);
            // Blend neighbouring segment directions so offsets stay smooth through bends.
            var t0 = (pts[i] - pts[i - 1]).normalized;
            var t1 = i + 1 < pts.Count ? (pts[i + 1] - pts[i]).normalized : t0;
            var tm = i - 2 >= 0 ? (pts[i - 1] - pts[i - 2]).normalized : t0;
            tangent = (t < 0.5f ? Vector2.Lerp(tm, t0, t + 0.5f) : Vector2.Lerp(t0, t1, t - 0.5f)).normalized;
        }
    }

    public class RoadNode
    {
        public Vector2 pos;
        public readonly List<NodeEnd> ends = new List<NodeEnd>();
        public int Degree => ends.Count;
    }

    public struct NodeEnd
    {
        public int edge;
        public bool atStart;
        public Vector2 dir;             // leaving the node along the edge
        public float trim;
    }

    /// <summary>
    /// Turns authored road splines into a graph: crossings and T-joins become junctions, roads are
    /// split into edges and every edge end is pulled back so the junction surface can be built.
    /// </summary>
    public class RoadNetwork
    {
        public readonly List<RoadEdge> edges = new List<RoadEdge>();
        public readonly List<RoadNode> nodes = new List<RoadNode>();

        const float SampleStep = 3f;
        const float SnapDistance = 34f;
        const float MergeDistance = 9f;

        struct Hit
        {
            public int roadA, roadB;
            public float sA, sB;
            public Vector2 pos;
        }

        class Line
        {
            public RoadDef def;
            public List<Vector2> pts;
            public float[] s;
            public Rect bounds;
        }

        public RoadNetwork(IList<RoadDef> defs)
        {
            var lines = new List<Line>();
            foreach (var d in defs)
            {
                var curve = Geo2D.CatmullRom(d.ctrl, 2f, d.closed);
                if (d.closed) curve.Add(curve[0]);
                lines.Add(MakeLine(d, Geo2D.Resample(curve, SampleStep)));
            }

            var hits = new List<Hit>();
            SnapEnds(lines, hits);
            for (int a = 0; a < lines.Count; a++)
            for (int b = a + 1; b < lines.Count; b++)
                Crossings(lines, a, b, hits);

            // Cluster hits into junctions.
            var nodeOf = new List<int>();
            foreach (var h in hits)
            {
                int found = -1;
                for (int n = 0; n < nodes.Count; n++)
                    if (Vector2.Distance(nodes[n].pos, h.pos) < MergeDistance) { found = n; break; }
                if (found < 0)
                {
                    nodes.Add(new RoadNode { pos = h.pos });
                    found = nodes.Count - 1;
                }
                nodeOf.Add(found);
            }

            // Per road: the junction stations along it.
            var stations = new List<List<(float s, int node)>>();
            for (int i = 0; i < lines.Count; i++) stations.Add(new List<(float, int)>());
            for (int k = 0; k < hits.Count; k++)
            {
                AddStation(stations[hits[k].roadA], hits[k].sA, nodeOf[k]);
                AddStation(stations[hits[k].roadB], hits[k].sB, nodeOf[k]);
            }
            for (int i = 0; i < lines.Count; i++) Split(lines[i], stations[i]);

            foreach (var e in edges)
            {
                if (e.a >= 0) e.pts[0] = nodes[e.a].pos;
                if (e.b >= 0) e.pts[e.pts.Count - 1] = nodes[e.b].pos;
                e.s = ArcLengths(e.pts);
            }
            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                if (e.a >= 0) nodes[e.a].ends.Add(new NodeEnd { edge = i, atStart = true, dir = EndDir(e, true) });
                if (e.b >= 0) nodes[e.b].ends.Add(new NodeEnd { edge = i, atStart = false, dir = EndDir(e, false) });
            }
            foreach (var n in nodes)
            {
                n.ends.Sort((x, y) => Angle(x.dir).CompareTo(Angle(y.dir)));
                ComputeTrims(n);
            }
            for (int ni = 0; ni < nodes.Count; ni++)
                foreach (var end in nodes[ni].ends)
                {
                    if (end.atStart) edges[end.edge].trimA = end.trim;
                    else edges[end.edge].trimB = end.trim;
                }
        }

        static float Angle(Vector2 d) => Mathf.Atan2(d.y, d.x);

        static Line MakeLine(RoadDef d, List<Vector2> pts)
        {
            var l = new Line { def = d, pts = pts, s = ArcLengths(pts) };
            l.bounds = BoundsOf(pts);
            return l;
        }

        static Rect BoundsOf(List<Vector2> pts)
        {
            Vector2 min = pts[0], max = pts[0];
            foreach (var p in pts) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        static float[] ArcLengths(List<Vector2> pts)
        {
            var s = new float[pts.Count];
            for (int i = 1; i < pts.Count; i++) s[i] = s[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
            return s;
        }

        static void AddStation(List<(float s, int node)> list, float s, int node)
        {
            foreach (var st in list) if (st.node == node && Mathf.Abs(st.s - s) < 20f) return;
            list.Add((s, node));
        }

        /// <summary>Open road ends that stop near another road are extended or pulled onto it.</summary>
        void SnapEnds(List<Line> lines, List<Hit> hits)
        {
            for (int a = 0; a < lines.Count; a++)
            {
                var la = lines[a];
                if (la.def.closed) continue;
                for (int end = 0; end < 2; end++)
                {
                    var p = end == 0 ? la.pts[0] : la.pts[la.pts.Count - 1];
                    float best = SnapDistance;
                    int bestRoad = -1;
                    Vector2 bestPos = default;
                    float bestS = 0;
                    for (int b = 0; b < lines.Count; b++)
                    {
                        if (b == a) continue;
                        var lb = lines[b];
                        for (int i = 1; i < lb.pts.Count; i++)
                        {
                            var q0 = lb.pts[i - 1];
                            var q1 = lb.pts[i];
                            var ab = q1 - q0;
                            float t = Mathf.Clamp01(Vector2.Dot(p - q0, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                            var q = q0 + ab * t;
                            float d = Vector2.Distance(p, q);
                            if (d < best)
                            {
                                best = d;
                                bestRoad = b;
                                bestPos = q;
                                bestS = lb.s[i - 1] + ab.magnitude * t;
                            }
                        }
                    }
                    if (bestRoad < 0) continue;
                    if (end == 0) { la.pts[0] = bestPos; }
                    else { la.pts[la.pts.Count - 1] = bestPos; }
                    la.s = ArcLengths(la.pts);
                    hits.Add(new Hit { roadA = a, roadB = bestRoad, sA = end == 0 ? 0f : la.s[la.s.Length - 1], sB = bestS, pos = bestPos });
                }
            }
            foreach (var l in lines) l.bounds = BoundsOf(l.pts);
        }

        void Crossings(List<Line> lines, int a, int b, List<Hit> hits)
        {
            var la = lines[a];
            var lb = lines[b];
            var ra = la.bounds;
            var rb = lb.bounds;
            if (ra.xMax < rb.xMin || rb.xMax < ra.xMin || ra.yMax < rb.yMin || rb.yMax < ra.yMin) return;
            for (int i = 1; i < la.pts.Count; i++)
            for (int j = 1; j < lb.pts.Count; j++)
            {
                if (!Geo2D.SegmentIntersect(la.pts[i - 1], la.pts[i], lb.pts[j - 1], lb.pts[j], out float t, out float u)) continue;
                var pos = Vector2.Lerp(la.pts[i - 1], la.pts[i], t);
                bool dup = false;
                foreach (var h in hits) if (Vector2.Distance(h.pos, pos) < MergeDistance * 1.5f) { dup = true; break; }
                if (dup) continue;
                hits.Add(new Hit
                {
                    roadA = a, roadB = b, pos = pos,
                    sA = la.s[i - 1] + Vector2.Distance(la.pts[i - 1], la.pts[i]) * t,
                    sB = lb.s[j - 1] + Vector2.Distance(lb.pts[j - 1], lb.pts[j]) * u,
                });
            }
        }

        void Split(Line line, List<(float s, int node)> st)
        {
            st.Sort((x, y) => x.s.CompareTo(y.s));
            float L = line.s[line.s.Length - 1];
            if (line.def.closed)
            {
                if (st.Count == 0)
                {
                    edges.Add(new RoadEdge { def = line.def, pts = new List<Vector2>(line.pts), loop = true });
                    return;
                }
                for (int k = 0; k < st.Count; k++)
                {
                    var from = st[k];
                    var to = st[(k + 1) % st.Count];
                    float s1 = k + 1 < st.Count ? to.s : to.s + L;
                    AddEdge(line, from.s, s1, from.node, to.node, L);
                }
                return;
            }
            float prev = 0f;
            int prevNode = -1;
            foreach (var x in st)
            {
                if (x.s - prev > 1.5f) AddEdge(line, prev, x.s, prevNode, x.node, L);
                prev = x.s;
                prevNode = x.node;
            }
            if (L - prev > 1.5f) AddEdge(line, prev, L, prevNode, -1, L);
        }

        void AddEdge(Line line, float s0, float s1, int a, int b, float loopLength)
        {
            var pts = new List<Vector2>();
            pts.Add(At(line, s0, loopLength));
            for (int i = 0; i < line.pts.Count * 2; i++)
            {
                int k = i % line.pts.Count;
                float s = line.s[k] + (i >= line.pts.Count ? loopLength : 0f);
                if (s > s0 + 0.5f && s < s1 - 0.5f) pts.Add(line.pts[k]);
            }
            pts.Add(At(line, s1, loopLength));
            if (pts.Count < 2 || Geo2D.Length(pts) < 1f) return;
            edges.Add(new RoadEdge { def = line.def, pts = pts, s = ArcLengths(pts), a = a, b = b });
        }

        static Vector2 At(Line line, float s, float loopLength)
        {
            if (line.def.closed) s = Mathf.Repeat(s, loopLength);
            s = Mathf.Clamp(s, 0, line.s[line.s.Length - 1]);
            int i = System.Array.BinarySearch(line.s, s);
            if (i < 0) i = ~i;
            i = Mathf.Clamp(i, 1, line.pts.Count - 1);
            float seg = line.s[i] - line.s[i - 1];
            return Vector2.Lerp(line.pts[i - 1], line.pts[i], seg > 1e-5f ? (s - line.s[i - 1]) / seg : 0f);
        }

        static Vector2 EndDir(RoadEdge e, bool start)
        {
            float d = Mathf.Min(8f, e.Length * 0.4f);
            e.Sample(start ? d : e.Length - d, out var p, out _);
            var origin = start ? e.pts[0] : e.pts[e.pts.Count - 1];
            return (p - origin).normalized;
        }

        void ComputeTrims(RoadNode n)
        {
            int c = n.ends.Count;
            for (int i = 0; i < c; i++)
            {
                var ei = n.ends[i];
                var wi = edges[ei.edge].Outer;
                float trim = edges[ei.edge].HalfWidth * 0.25f;
                foreach (int k in new[] { (i + 1) % c, (i - 1 + c) % c })
                {
                    if (k == i) continue;
                    var ek = n.ends[k];
                    float wk = edges[ek.edge].Outer;
                    float cos = Vector2.Dot(ei.dir, ek.dir);
                    float ang = Mathf.Acos(Mathf.Clamp(cos, -1f, 1f));
                    if (ang > Mathf.Deg2Rad * 165f) continue;
                    float sin = Mathf.Max(Mathf.Sin(ang), 0.25f);
                    float need = (wk + wi * cos) / sin + 2.5f;
                    trim = Mathf.Max(trim, need);
                }
                ei.trim = Mathf.Min(trim, Mathf.Min(42f, edges[ei.edge].Length * 0.45f));
                n.ends[i] = ei;
            }
        }

        // ------------------------------------------------------------------ queries

        /// <summary>Nearest edge and distance from its centerline.</summary>
        public float DistanceToRoad(Vector2 p, out RoadEdge nearest)
        {
            nearest = null;
            float best = float.MaxValue;
            foreach (var e in edges)
            {
                float d = Geo2D.DistanceToPolyline(p, e.pts);
                if (d < best) { best = d; nearest = e; }
            }
            return best;
        }
    }
}
