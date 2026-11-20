using System.Collections.Generic;
using UnityEngine;

namespace Ashline.City
{
    /// <summary>
    /// Meshes for the road graph: curved asphalt ribbons, rounded junction plates with curbs,
    /// sidewalks, right-hand-traffic markings, bridges over water and street lamps.
    /// </summary>
    public class RoadBuilder
    {
        public const float RoadY = 0.05f;
        public const float CurbTop = RoadY + IslandPlan.CurbHeight;
        const float PaintY = RoadY + 0.012f;
        const float DeckBottom = -1.5f;
        const float Step = 3f;

        readonly CityArt art;
        readonly IslandPlan plan;
        readonly RoadNetwork net;

        public readonly List<Vector3> lampFeet = new List<Vector3>();
        public readonly List<Vector3> pillarFeet = new List<Vector3>();

        public RoadBuilder(CityArt art, IslandPlan plan, RoadNetwork net)
        {
            this.art = art;
            this.plan = plan;
            this.net = net;
        }

        struct S
        {
            public Vector2 p, t, n;
            public float d;
            public bool deck;
        }

        List<S> Samples(RoadEdge e, float d0, float d1)
        {
            var list = new List<S>();
            int count = Mathf.Max(1, Mathf.CeilToInt((d1 - d0) / Step));
            for (int i = 0; i <= count; i++)
            {
                float d = Mathf.Lerp(d0, d1, i / (float)count);
                e.Sample(d, out var p, out var t);
                list.Add(new S { p = p, t = t, n = Geo2D.Perp(t), d = d, deck = plan.TerrainHeight(p) < -0.3f });
            }
            return list;
        }

        string PavingFor(Vector2 p)
        {
            switch (plan.DistrictAt(p))
            {
                case District.Beach: return "red_sandstone_pavement";
                case District.Palmside: return "herringbone_pavement_02";
                case District.Park: return "herringbone_pavement_02";
                default: return "concrete_pavement_02";
            }
        }

        // ------------------------------------------------------------------ primitives

        static Vector2 Planar(Vector3 p, Vector2 size) => new Vector2(p.x / size.x, p.z / size.y);

        /// <summary>Horizontal quad; winding fixed so it faces up (or down).</summary>
        static void Flat(MeshBuilder mb, CityArt.Surface s, Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool down = false)
        {
            Flat(mb, s.material, s.size, a, b, c, d, down);
        }

        static void Flat(MeshBuilder mb, Material m, Vector2 size, Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool down = false)
        {
            float ny = Vector3.Cross(b - a, d - a).y + Vector3.Cross(d - c, b - c).y;
            if ((ny < 0) != down) { var t = b; b = d; d = t; }
            mb.Quad(m, a, b, c, d, Planar(a, size), Planar(b, size), Planar(c, size), Planar(d, size));
        }

        static void FlatTri(MeshBuilder mb, CityArt.Surface s, Vector3 a, Vector3 b, Vector3 c)
        {
            if (Vector3.Cross(b - a, c - a).y < 0) { var t = b; b = c; c = t; }
            mb.Tri(s.material, a, b, c, Planar(a, s.size), Planar(b, s.size), Planar(c, s.size));
        }

        /// <summary>Vertical face between two plan points; facing picks the visible side.</summary>
        static void Side(MeshBuilder mb, CityArt.Surface s, Vector2 p0, Vector2 p1, float y0, float y1, Vector2 facing, float u0 = 0)
        {
            var dir = p1 - p0;
            // Wall() faces to the right of p0→p1.
            var right = new Vector2(dir.y, -dir.x);
            if (Vector2.Dot(right, facing) < 0) { var t = p0; p0 = p1; p1 = t; }
            mb.Wall(s.material, s.size, p0.X0Z(), p1.X0Z(), y0, y1, u0);
        }

        // ------------------------------------------------------------------ roads

        public void Build(MeshBuilder road, MeshBuilder walk, MeshBuilder deck)
        {
            foreach (var e in net.edges) Edge(road, walk, deck, e);
            for (int i = 0; i < net.nodes.Count; i++) Junction(road, walk, net.nodes[i]);
        }

        void Edge(MeshBuilder road, MeshBuilder walk, MeshBuilder deck, RoadEdge e)
        {
            float d0 = e.trimA, d1 = e.Length - e.trimB;
            if (d1 - d0 < 0.3f) return;
            var ss = Samples(e, d0, d1);
            float hw = e.HalfWidth, sw = e.Sidewalk, outer = e.Outer;
            bool pier = e.def.kind == RoadKind.Pier;
            var asphalt = art.Get(pier ? "wood_floor_deck" : e.def.kind == RoadKind.Highway || e.def.kind == RoadKind.Lane ? "asphalt_04" : "asphalt_02");
            var curb = art.Get("brushed_concrete");
            var concrete = art.Get("concrete_panels");
            float surfaceY = RoadY;

            for (int i = 1; i < ss.Count; i++)
            {
                var A = ss[i - 1];
                var B = ss[i];
                Flat(road, asphalt, (A.p - A.n * hw).X0Z(surfaceY), (B.p - B.n * hw).X0Z(surfaceY), (B.p + B.n * hw).X0Z(surfaceY), (A.p + A.n * hw).X0Z(surfaceY));
                bool isDeck = A.deck || B.deck;
                if (sw > 0)
                {
                    var paving = art.Get(isDeck ? "brushed_concrete" : PavingFor(A.p));
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector2 a0 = A.p + A.n * (hw * side), b0 = B.p + B.n * (hw * side);
                        Vector2 a1 = A.p + A.n * ((hw + 0.3f) * side), b1 = B.p + B.n * ((hw + 0.3f) * side);
                        Vector2 a2 = A.p + A.n * (outer * side), b2 = B.p + B.n * (outer * side);
                        Side(walk, curb, a0, b0, RoadY, CurbTop, -A.n * side, A.d);
                        Flat(walk, curb, a0.X0Z(CurbTop), b0.X0Z(CurbTop), b1.X0Z(CurbTop), a1.X0Z(CurbTop));
                        Flat(walk, paving, a1.X0Z(CurbTop), b1.X0Z(CurbTop), b2.X0Z(CurbTop), a2.X0Z(CurbTop));
                        Side(walk, curb, a2, b2, isDeck ? DeckBottom : -0.3f, CurbTop, A.n * side, A.d);
                    }
                }
                else if (!pier)
                {
                    // Lanes: thin asphalt edge dropping into the verge.
                    for (int side = -1; side <= 1; side += 2)
                        Side(walk, curb, A.p + A.n * (hw * side), B.p + B.n * (hw * side), isDeck ? DeckBottom : -0.2f, RoadY, A.n * side, A.d);
                }
                else
                {
                    for (int side = -1; side <= 1; side += 2)
                        Side(deck, asphalt, A.p + A.n * (hw * side), B.p + B.n * (hw * side), surfaceY - 0.35f, surfaceY, A.n * side, A.d);
                }

                if (isDeck)
                {
                    float w = sw > 0 ? outer : hw;
                    float bottom = pier ? surfaceY - 0.35f : DeckBottom;
                    Flat(deck, pier ? asphalt : concrete, (A.p - A.n * w).X0Z(bottom), (B.p - B.n * w).X0Z(bottom), (B.p + B.n * w).X0Z(bottom), (A.p + A.n * w).X0Z(bottom), true);
                    float top = sw > 0 ? CurbTop : surfaceY;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var a = A.p + A.n * ((w - 0.15f) * side);
                        var b = B.p + B.n * ((w - 0.15f) * side);
                        if (pier) Rail(deck, a, b, top);
                        else Parapet(deck, concrete, a, b, top, A.n * side);
                    }
                }
            }

            if (sw > 0 || pier) Markings(road, e, ss, surfaceY);
            Pillars(deck, e, ss, pier ? hw : outer, pier ? surfaceY - 0.35f : DeckBottom, pier);
            Lamps(road, e, ss);
            if (e.a < 0) EndCap(walk, e, ss[0], -1, surfaceY);
            if (e.b < 0) EndCap(walk, e, ss[ss.Count - 1], 1, surfaceY);
        }

        void Parapet(MeshBuilder mb, CityArt.Surface s, Vector2 a, Vector2 b, float y, Vector2 outward)
        {
            var c = (a + b) * 0.5f;
            var t = (b - a);
            float len = t.magnitude;
            if (len < 0.01f) return;
            t /= len;
            var fwd = new Vector3(t.x, 0, t.y);
            var right = Vector3.Cross(Vector3.up, fwd);
            mb.OrientedBox(s.material, s.size, c.X0Z(y + 0.45f), right, Vector3.up, fwd, new Vector3(0.15f, 0.45f, len * 0.5f + 0.02f));
        }

        void Rail(MeshBuilder mb, Vector2 a, Vector2 b, float y)
        {
            var wood = art.Get("wood_floor_deck");
            var t = b - a;
            float len = t.magnitude;
            if (len < 0.01f) return;
            t /= len;
            var fwd = new Vector3(t.x, 0, t.y);
            var right = Vector3.Cross(Vector3.up, fwd);
            var c = (a + b) * 0.5f;
            mb.OrientedBox(wood.material, wood.size, c.X0Z(y + 1.0f), right, Vector3.up, fwd, new Vector3(0.06f, 0.05f, len * 0.5f + 0.03f));
            mb.OrientedBox(wood.material, wood.size, c.X0Z(y + 0.55f), right, Vector3.up, fwd, new Vector3(0.04f, 0.04f, len * 0.5f + 0.03f));
            mb.OrientedBox(wood.material, wood.size, a.X0Z(y + 0.5f), right, Vector3.up, fwd, new Vector3(0.07f, 0.5f, 0.07f));
        }

        void Pillars(MeshBuilder mb, RoadEdge e, List<S> ss, float w, float bottom, bool pier)
        {
            var concrete = art.Get(pier ? "wood_floor_deck" : "concrete_panels");
            float spacing = pier ? 6f : 34f, next = ss[0].d + spacing * 0.5f;
            foreach (var x in ss)
            {
                if (x.d < next) continue;
                next = x.d + spacing;
                if (!x.deck) continue;
                var fwd = new Vector3(x.t.x, 0, x.t.y);
                var right = Vector3.Cross(Vector3.up, fwd);
                float depth = bottom - (-7f);
                if (pier)
                {
                    for (int side = -1; side <= 1; side += 2)
                        mb.Cylinder(concrete.material, concrete.size, (x.p + x.n * (w - 0.4f) * side).X0Z(-6f), 0.18f, bottom + 6f, 8);
                }
                else
                {
                    mb.OrientedBox(concrete.material, concrete.size, x.p.X0Z(bottom - depth * 0.5f), right, Vector3.up, fwd, new Vector3(w * 0.55f, depth * 0.5f, 1.1f));
                    mb.OrientedBox(concrete.material, concrete.size, x.p.X0Z(bottom - 0.4f), right, Vector3.up, fwd, new Vector3(w * 0.9f, 0.4f, 1.4f));
                    pillarFeet.Add(x.p.X0Z(0));
                }
            }
        }

        void EndCap(MeshBuilder mb, RoadEdge e, S x, int dir, float y)
        {
            var t = x.t * dir;
            if (e.def.kind == RoadKind.Pier)
            {
                // Wider platform at the far end of a pier.
                var deck = art.Get("wood_floor_deck");
                float hw = e.HalfWidth + 4f;
                Vector2 a = x.p - x.n * hw, b = x.p + x.n * hw, c = b + t * 14f, d = a + t * 14f;
                Flat(mb, deck, a.X0Z(y), b.X0Z(y), c.X0Z(y), d.X0Z(y));
                Rail(mb, d, c, y);
                Rail(mb, a + t * 0.1f, d, y);
                Rail(mb, b + t * 0.1f, c, y);
                Side(mb, deck, d, c, y - 0.35f, y, t);
                Side(mb, deck, a, d, y - 0.35f, y, -x.n);
                Side(mb, deck, b, c, y - 0.35f, y, x.n);
                for (int k = 0; k <= 2; k++)
                for (int j = -1; j <= 1; j += 2)
                    mb.Cylinder(deck.material, deck.size, (x.p + t * (k * 6.5f + 0.5f) + x.n * (hw - 0.6f) * j).X0Z(-6f), 0.2f, y + 5.6f, 8);
                return;
            }
            if (e.Sidewalk <= 0) return;
            var paving = art.Get(PavingFor(x.p));
            var curb = art.Get("brushed_concrete");
            float o = e.Outer;
            Vector2 p0 = x.p - x.n * o, p1 = x.p + x.n * o, p2 = p1 + t * 4f, p3 = p0 + t * 4f;
            Side(mb, curb, x.p - x.n * e.HalfWidth, x.p + x.n * e.HalfWidth, RoadY, CurbTop, -t);
            Flat(mb, paving, p0.X0Z(CurbTop), p1.X0Z(CurbTop), p2.X0Z(CurbTop), p3.X0Z(CurbTop));
            Side(mb, curb, p3, p2, -0.3f, CurbTop, t);
        }

        // ------------------------------------------------------------------ markings

        void Markings(MeshBuilder mb, RoadEdge e, List<S> ss, float y)
        {
            if (e.def.kind == RoadKind.Pier) return;
            float d0 = ss[0].d, d1 = ss[ss.Count - 1].d;
            bool startJ = e.a >= 0 && net.nodes[e.a].Degree >= 3;
            bool endJ = e.b >= 0 && net.nodes[e.b].Degree >= 3;
            float m0 = d0 + (startJ ? 5.2f : 0f), m1 = d1 - (endJ ? 5.2f : 0f);
            float hw = e.HalfWidth;
            var white = art.roadPaint;
            var yellow = art.roadPaintYellow;
            float py = y + 0.012f;

            switch (e.def.kind)
            {
                case RoadKind.Highway:
                case RoadKind.Avenue:
                    Line(mb, yellow, e, m0, m1, 0.1f, 0.22f, py);
                    Line(mb, yellow, e, m0, m1, -0.22f, -0.1f, py);
                    Dashes(mb, white, e, m0, m1, LaneOffset(1) - 0.07f, LaneOffset(1) + 0.07f, py);
                    Dashes(mb, white, e, m0, m1, -LaneOffset(1) - 0.07f, -LaneOffset(1) + 0.07f, py);
                    Line(mb, white, e, m0, m1, hw - 0.55f, hw - 0.4f, py);
                    Line(mb, white, e, m0, m1, -hw + 0.4f, -hw + 0.55f, py);
                    break;
                case RoadKind.Street:
                    Dashes(mb, yellow, e, m0, m1, -0.07f, 0.07f, py);
                    break;
            }
            if (e.def.kind == RoadKind.Highway) return;
            // Crosswalk and stop line on the side arriving at the junction (right-hand traffic).
            if (startJ && !ss[0].deck)
            {
                Zebra(mb, e, d0 + 0.4f, d0 + 3.4f, py);
                Line(mb, white, e, d0 + 3.9f, d0 + 4.35f, 0.25f, hw - 0.4f, py);
            }
            if (endJ && !ss[ss.Count - 1].deck)
            {
                Zebra(mb, e, d1 - 3.4f, d1 - 0.4f, py);
                Line(mb, white, e, d1 - 4.35f, d1 - 3.9f, -hw + 0.4f, -0.25f, py);
            }
        }

        static float LaneOffset(int k) => k * IslandPlan.LaneWidth;

        void Line(MeshBuilder mb, Material m, RoadEdge e, float d0, float d1, float off0, float off1, float y)
        {
            if (d1 - d0 < 0.2f) return;
            int count = Mathf.Max(1, Mathf.CeilToInt((d1 - d0) / Step));
            e.Sample(d0, out var pa, out var ta);
            for (int i = 1; i <= count; i++)
            {
                e.Sample(Mathf.Lerp(d0, d1, i / (float)count), out var pb, out var tb);
                Vector2 na = Geo2D.Perp(ta), nb = Geo2D.Perp(tb);
                Flat(mb, m, new Vector2(4, 4), (pa + na * off0).X0Z(y), (pb + nb * off0).X0Z(y), (pb + nb * off1).X0Z(y), (pa + na * off1).X0Z(y));
                pa = pb;
                ta = tb;
            }
        }

        void Dashes(MeshBuilder mb, Material m, RoadEdge e, float d0, float d1, float off0, float off1, float y)
        {
            for (float d = d0 + 1f; d + 3f < d1; d += 9f) Line(mb, m, e, d, d + 3f, off0, off1, y);
        }

        void Zebra(MeshBuilder mb, RoadEdge e, float d0, float d1, float y)
        {
            float hw = e.HalfWidth;
            for (float w = -hw + 0.6f; w + 0.5f < hw - 0.4f; w += 1.0f) Line(mb, art.roadPaint, e, d0, d1, w, w + 0.5f, y);
        }

        // ------------------------------------------------------------------ junctions

        void Junction(MeshBuilder road, MeshBuilder walk, RoadNode n)
        {
            if (n.Degree < 2) return;
            bool anyPier = false;
            foreach (var end in n.ends) anyPier |= net.edges[end.edge].def.kind == RoadKind.Pier;
            var asphalt = art.Get(anyPier ? "wood_floor_deck" : "asphalt_02");
            var curb = art.Get("brushed_concrete");
            var paving = art.Get(PavingFor(n.pos));
            float y = RoadY;

            var poly = new List<Vector2>();
            int c = n.ends.Count;
            for (int i = 0; i < c; i++)
            {
                var ei = n.ends[i];
                var ej = n.ends[(i + 1) % c];
                var Ei = net.edges[ei.edge];
                var Ej = net.edges[ej.edge];
                Vector2 di = ei.dir, dj = ej.dir;
                Vector2 ni = Geo2D.Perp(di), nj = Geo2D.Perp(dj);
                Vector2 ci = n.pos + di * ei.trim, cj = n.pos + dj * ej.trim;
                // Sample the trimmed edge itself so the plate meets the ribbon exactly on curves.
                ci = EdgePoint(Ei, ei, out var tdi);
                cj = EdgePoint(Ej, ej, out var tdj);
                ni = Geo2D.Perp(tdi);
                nj = Geo2D.Perp(tdj);
                Vector2 Ri = ci - ni * Ei.HalfWidth, Li = ci + ni * Ei.HalfWidth;
                Vector2 Rj = cj - nj * Ej.HalfWidth;
                poly.Add(Ri);
                poly.Add(Li);

                var curve = Corner(Li, tdi, Rj, tdj, n.pos, out bool straight);
                poly.AddRange(curve);

                float swi = Ei.Sidewalk, swj = Ej.Sidewalk;
                if (swi <= 0 && swj <= 0) continue;
                Vector2 Lo = ci + ni * Ei.Outer, Ro = cj - nj * Ej.Outer;
                var outerCurve = Corner(Lo, tdi, Ro, tdj, n.pos, out _);
                var inner = new List<Vector2> { Li };
                inner.AddRange(curve);
                inner.Add(Rj);
                var outerL = new List<Vector2> { Lo };
                outerL.AddRange(outerCurve);
                outerL.Add(Ro);
                CornerWalk(walk, inner, outerL, curb, paving, y);
            }

            var center = n.pos.X0Z(y);
            float py = y;
            for (int i = 0; i < poly.Count; i++)
                FlatTri(road, asphalt, center, poly[i].X0Z(py), poly[(i + 1) % poly.Count].X0Z(py));
        }

        static Vector2 EdgePoint(RoadEdge e, NodeEnd end, out Vector2 outward)
        {
            float d = end.atStart ? end.trim : e.Length - end.trim;
            e.Sample(d, out var p, out var t);
            outward = end.atStart ? t : -t;
            return p;
        }

        /// <summary>Points strictly between a and b along a rounded corner (quadratic Bezier through the edge-line intersection).</summary>
        static List<Vector2> Corner(Vector2 a, Vector2 da, Vector2 b, Vector2 db, Vector2 center, out bool straight)
        {
            var pts = new List<Vector2>();
            straight = true;
            // Edge lines run outward from the junction; their crossing is the sharp curb corner.
            float den = Geo2D.Cross(da, db);
            if (Mathf.Abs(den) > 0.08f)
            {
                float k = Geo2D.Cross(b - a, db) / den;
                var x = a + da * k;
                bool between = Vector2.Distance(x, center) < Vector2.Distance(a, center) + Vector2.Distance(b, center);
                if (k < 0.5f && between && Vector2.Dot(x - center, (a + b) * 0.5f - center) > 0)
                {
                    straight = false;
                    for (int i = 1; i < 8; i++)
                    {
                        float t = i / 8f;
                        pts.Add((1 - t) * (1 - t) * a + 2 * (1 - t) * t * x + t * t * b);
                    }
                }
            }
            return pts;
        }

        void CornerWalk(MeshBuilder mb, List<Vector2> inner, List<Vector2> outer, CityArt.Surface curb, CityArt.Surface paving, float y)
        {
            // Resample both chains to the same count so the strip pairs up.
            int count = Mathf.Max(inner.Count, outer.Count, 2);
            var a = Geo2D.Resample(inner, Mathf.Max(0.05f, Geo2D.Length(inner) / (count - 1)));
            var b = Geo2D.Resample(outer, Mathf.Max(0.05f, Geo2D.Length(outer) / (count - 1)));
            int m = Mathf.Min(a.Count, b.Count);
            if (m < 2) return;
            a[m - 1] = inner[inner.Count - 1];
            b[m - 1] = outer[outer.Count - 1];
            for (int i = 1; i < m; i++)
            {
                var face = a[i - 1] - b[i - 1];
                Side(mb, curb, a[i - 1], a[i], y, CurbTop, face);
                Vector2 ia = Vector2.Lerp(a[i - 1], b[i - 1], 0.3f / Mathf.Max(0.3f, face.magnitude));
                Vector2 ib = Vector2.Lerp(a[i], b[i], 0.3f / Mathf.Max(0.3f, (a[i] - b[i]).magnitude));
                Flat(mb, curb, a[i - 1].X0Z(CurbTop), a[i].X0Z(CurbTop), ib.X0Z(CurbTop), ia.X0Z(CurbTop));
                Flat(mb, paving, ia.X0Z(CurbTop), ib.X0Z(CurbTop), b[i].X0Z(CurbTop), b[i - 1].X0Z(CurbTop));
                Side(mb, curb, b[i - 1], b[i], -0.3f, CurbTop, -face);
            }
        }

        // ------------------------------------------------------------------ lamps

        void Lamps(MeshBuilder mb, RoadEdge e, List<S> ss)
        {
            if (e.def.kind == RoadKind.Lane || e.def.kind == RoadKind.Pier) return;
            float spacing = e.def.kind == RoadKind.Highway ? 40f : 30f;
            float first = ss[0].d + 6f, last = ss[ss.Count - 1].d - 6f;
            int k = 0;
            for (float d = first; d < last; d += spacing, k++)
            {
                e.Sample(d, out var p, out var t);
                var n = Geo2D.Perp(t);
                int side = (k & 1) == 0 ? 1 : -1;
                bool deck = plan.TerrainHeight(p) < -0.3f;
                float off = deck ? e.Outer - 0.45f : e.HalfWidth + 0.7f;
                var foot = (p + n * off * side).X0Z(0);
                Lamp(mb, foot, -n * side, deck ? CurbTop + 0.9f : CurbTop);
            }
        }

        void Lamp(MeshBuilder mb, Vector3 foot, Vector2 toRoad2, float baseY)
        {
            var metal = art.metalDark;
            var toRoad = new Vector3(toRoad2.x, 0, toRoad2.y);
            var basePos = new Vector3(foot.x, baseY, foot.z);
            mb.Cylinder(metal, Vector2.one, basePos, 0.13f, 0.6f, 10);
            mb.Cylinder(metal, Vector2.one, basePos + Vector3.up * 0.6f, 0.085f, 7.4f, 10);
            var top = basePos + Vector3.up * 7.9f;
            var side = Vector3.Cross(Vector3.up, toRoad);
            mb.OrientedBox(metal, Vector2.one, top + toRoad * 0.9f, side, Vector3.up, toRoad, new Vector3(0.05f, 0.05f, 0.95f));
            var head = top + toRoad * 1.9f + Vector3.down * 0.05f;
            mb.OrientedBox(metal, Vector2.one, head, side, Vector3.up, toRoad, new Vector3(0.22f, 0.07f, 0.38f));
            mb.OrientedBox(art.lampEmissive, Vector2.one, head + Vector3.down * 0.08f, side, Vector3.up, toRoad, new Vector3(0.17f, 0.012f, 0.32f));
            lampFeet.Add(new Vector3(foot.x, baseY, foot.z));
        }
    }
}
