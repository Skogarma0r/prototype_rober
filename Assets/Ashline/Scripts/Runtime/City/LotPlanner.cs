using System.Collections.Generic;
using UnityEngine;

namespace Ashline.City
{
    public enum LotKind { Urban, Mansion, Landmark }

    public class Lot
    {
        public Vector2 center, along, away;
        public float width, depth, setback;
        public District district;
        public LotKind kind;
        public string landmark;
        public Vector2[] corners;
        public RoadEdge road;
        public Building building;
        public Rect house;              // local footprint of the main building

        public Matrix4x4 Matrix(float y)
        {
            var m = Matrix4x4.identity;
            m.SetColumn(0, new Vector4(along.x, 0, along.y, 0));
            m.SetColumn(1, new Vector4(0, 1, 0, 0));
            m.SetColumn(2, new Vector4(away.x, 0, away.y, 0));
            m.SetColumn(3, new Vector4(center.x, y, center.y, 1));
            return m;
        }

        public Vector2 ToWorld(Vector2 local) => center + along * local.x + away * local.y;
        public float Yaw => Mathf.Atan2(away.x, away.y) * Mathf.Rad2Deg;
    }

    /// <summary>
    /// Places building lots along both sides of every road, oriented to the road tangent, and checks
    /// each one against the water, other roads, junctions and previous lots.
    /// </summary>
    public class LotPlanner
    {
        readonly IslandPlan plan;
        readonly RoadNetwork net;
        readonly System.Random rng;
        public readonly List<Lot> lots = new List<Lot>();
        public readonly List<(Vector2 c, float r)> reserved = new List<(Vector2, float)>();

        const float Cell = 24f;
        readonly Dictionary<Vector2Int, List<(int edge, int seg)>> segGrid = new Dictionary<Vector2Int, List<(int, int)>>();
        readonly Dictionary<Vector2Int, List<int>> lotGrid = new Dictionary<Vector2Int, List<int>>();
        readonly float[] nodeRadius;

        public LotPlanner(IslandPlan plan, RoadNetwork net, int seed)
        {
            this.plan = plan;
            this.net = net;
            rng = new System.Random(seed);
            for (int e = 0; e < net.edges.Count; e++)
            {
                var edge = net.edges[e];
                for (int i = 1; i < edge.pts.Count; i++)
                {
                    var a = edge.pts[i - 1];
                    var b = edge.pts[i];
                    float r = edge.Outer + 1f;
                    var min = Vector2.Min(a, b) - Vector2.one * r;
                    var max = Vector2.Max(a, b) + Vector2.one * r;
                    for (int x = Mathf.FloorToInt(min.x / Cell); x <= Mathf.FloorToInt(max.x / Cell); x++)
                    for (int y = Mathf.FloorToInt(min.y / Cell); y <= Mathf.FloorToInt(max.y / Cell); y++)
                    {
                        var k = new Vector2Int(x, y);
                        if (!segGrid.TryGetValue(k, out var list)) segGrid[k] = list = new List<(int, int)>();
                        list.Add((e, i));
                    }
                }
            }
            nodeRadius = new float[net.nodes.Count];
            for (int n = 0; n < net.nodes.Count; n++)
            {
                float r = 0;
                foreach (var end in net.nodes[n].ends)
                {
                    var e = net.edges[end.edge];
                    r = Mathf.Max(r, Mathf.Sqrt(end.trim * end.trim + e.Outer * e.Outer));
                }
                nodeRadius[n] = r;
            }
        }

        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        /// <summary>Signed clearance from road surfaces (incl. sidewalks) at a point.</summary>
        public float RoadClearance(Vector2 p)
        {
            float best = 999f;
            var k = new Vector2Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell));
            if (segGrid.TryGetValue(k, out var list))
                foreach (var (e, i) in list)
                {
                    var edge = net.edges[e];
                    best = Mathf.Min(best, Geo2D.DistanceToSegment(p, edge.pts[i - 1], edge.pts[i]) - edge.Outer);
                }
            for (int n = 0; n < net.nodes.Count; n++)
            {
                float d = Vector2.Distance(p, net.nodes[n].pos) - nodeRadius[n];
                if (d < best) best = d;
            }
            return best;
        }

        bool Reserved(Vector2 p, float margin = 0)
        {
            foreach (var (c, r) in reserved) if (Vector2.Distance(p, c) < r + margin) return true;
            return false;
        }

        public bool Free(Vector2 p, float margin)
        {
            if (!plan.OnSolidLand(p) || RoadClearance(p) < margin || Reserved(p)) return false;
            foreach (var pond in plan.ponds) if (Geo2D.PointInPolygon(p, pond) || Geo2D.DistanceToLoop(p, pond) < margin + 1f) return false;
            return !InsideLot(p, margin);
        }

        bool InsideLot(Vector2 p, float margin)
        {
            var k = new Vector2Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell));
            if (!lotGrid.TryGetValue(k, out var list)) return false;
            foreach (int i in list)
            {
                var l = lots[i];
                var d = p - l.center;
                if (Mathf.Abs(Vector2.Dot(d, l.along)) < l.width / 2 + margin && Mathf.Abs(Vector2.Dot(d, l.away)) < l.depth / 2 + margin) return true;
            }
            return false;
        }

        Lot MakeLot(RoadEdge e, float d, int side, float w, float depth, float setback)
        {
            e.Sample(Mathf.Clamp(d + w / 2, 0, e.Length), out var p, out var t);
            var away = Geo2D.Perp(t) * side;
            var front = p + away * (e.Outer + setback);
            var lot = new Lot
            {
                center = front + away * (depth / 2),
                away = away,
                width = w,
                depth = depth,
                setback = setback,
                road = e,
            };
            // Right-handed frame: along = up × away.
            var a3 = Vector3.Cross(Vector3.up, new Vector3(away.x, 0, away.y));
            lot.along = new Vector2(a3.x, a3.z);
            lot.corners = new[]
            {
                lot.ToWorld(new Vector2(-w / 2, -depth / 2)), lot.ToWorld(new Vector2(w / 2, -depth / 2)),
                lot.ToWorld(new Vector2(w / 2, depth / 2)), lot.ToWorld(new Vector2(-w / 2, depth / 2)),
            };
            lot.district = plan.DistrictAt(lot.center);
            return lot;
        }

        bool Valid(Lot lot, District want, float margin)
        {
            if (lot.district != want) return false;
            int nx = Mathf.Max(2, Mathf.CeilToInt(lot.width / 4f)), nz = Mathf.Max(2, Mathf.CeilToInt(lot.depth / 4f));
            for (int i = 0; i <= nx; i++)
            for (int j = 0; j <= nz; j++)
            {
                var p = lot.ToWorld(new Vector2((i / (float)nx - 0.5f) * lot.width, (j / (float)nz - 0.5f) * lot.depth));
                if (!plan.OnSolidLand(p) || RoadClearance(p) < margin || Reserved(p)) return false;
                foreach (var pond in plan.ponds) if (Geo2D.PointInPolygon(p, pond)) return false;
            }
            var min = lot.corners[0];
            var max = lot.corners[0];
            foreach (var c in lot.corners) { min = Vector2.Min(min, c); max = Vector2.Max(max, c); }
            var seen = new HashSet<int>();
            for (int x = Mathf.FloorToInt(min.x / Cell); x <= Mathf.FloorToInt(max.x / Cell); x++)
            for (int y = Mathf.FloorToInt(min.y / Cell); y <= Mathf.FloorToInt(max.y / Cell); y++)
            {
                if (!lotGrid.TryGetValue(new Vector2Int(x, y), out var list)) continue;
                foreach (int k in list)
                    if (seen.Add(k) && Geo2D.QuadsOverlap(Shrink(lot.corners, 0.05f), Shrink(lots[k].corners, 0.05f))) return false;
            }
            return true;
        }

        static Vector2[] Shrink(Vector2[] q, float d)
        {
            var c = (q[0] + q[2]) * 0.5f;
            var r = new Vector2[4];
            for (int i = 0; i < 4; i++) r[i] = q[i] + (c - q[i]).normalized * d;
            return r;
        }

        void Add(Lot lot)
        {
            lots.Add(lot);
            var min = lot.corners[0];
            var max = lot.corners[0];
            foreach (var c in lot.corners) { min = Vector2.Min(min, c); max = Vector2.Max(max, c); }
            for (int x = Mathf.FloorToInt(min.x / Cell); x <= Mathf.FloorToInt(max.x / Cell); x++)
            for (int y = Mathf.FloorToInt(min.y / Cell); y <= Mathf.FloorToInt(max.y / Cell); y++)
            {
                var k = new Vector2Int(x, y);
                if (!lotGrid.TryGetValue(k, out var list)) lotGrid[k] = list = new List<int>();
                list.Add(lots.Count - 1);
            }
        }

        struct Style
        {
            public float wMin, wMax, dMin, dMax, setback, gapMin, gapMax;
            public LotKind kind;
        }

        static Style StyleFor(District d) => d switch
        {
            District.Downtown => new Style { wMin = 22, wMax = 34, dMin = 22, dMax = 34, setback = 0.2f, gapMin = 0, gapMax = 0.4f },
            District.Docks => new Style { wMin = 30, wMax = 42, dMin = 22, dMax = 30, setback = 4, gapMin = 5, gapMax = 9 },
            District.Beach => new Style { wMin = 14, wMax = 26, dMin = 16, dMax = 24, setback = 0.2f, gapMin = 0, gapMax = 2 },
            District.Palmside => new Style { wMin = 16, wMax = 26, dMin = 16, dMax = 24, setback = 2, gapMin = 2, gapMax = 5 },
            District.Hills => new Style { wMin = 34, wMax = 46, dMin = 30, dMax = 40, setback = 4, gapMin = 7, gapMax = 14, kind = LotKind.Mansion },
            _ => new Style { wMin = 0 },
        };

        // ------------------------------------------------------------------ planning

        public void PlaceLandmark(string id, Vector2 pos, float w, float depth, float setback)
        {
            net.DistanceToRoad(pos, out var e);
            if (e == null) return;
            // Station along the edge closest to the landmark and the side it lies on.
            float bestD = 0, best = float.MaxValue;
            for (float d = 0; d < e.Length; d += 1f)
            {
                e.Sample(d, out var p, out _);
                float dist = Vector2.Distance(p, pos);
                if (dist < best) { best = dist; bestD = d; }
            }
            e.Sample(bestD, out var q, out var t);
            int side = Vector2.Dot(pos - q, Geo2D.Perp(t)) > 0 ? 1 : -1;
            for (float scale = 1f; scale > 0.6f; scale -= 0.1f)
            {
                var lot = MakeLot(e, bestD - w * scale / 2, side, w * scale, depth * scale, setback);
                if (!Valid(lot, lot.district, 0.3f)) continue;
                lot.kind = LotKind.Landmark;
                lot.landmark = id;
                Add(lot);
                return;
            }
            Debug.LogWarning($"Landmark {id} did not fit.");
        }

        public void PlanFrontage()
        {
            var order = new List<RoadEdge>(net.edges);
            int Rank(RoadKind k) => k switch { RoadKind.Avenue => 0, RoadKind.Highway => 1, RoadKind.Street => 2, RoadKind.Lane => 3, _ => 9 };
            order.Sort((a, b) => Rank(a.def.kind).CompareTo(Rank(b.def.kind)));
            foreach (var e in order)
            {
                if (e.def.kind == RoadKind.Pier) continue;
                for (int side = -1; side <= 1; side += 2) Frontage(e, side);
            }
        }

        void Frontage(RoadEdge e, int side)
        {
            float d = e.trimA + 1f, end = e.Length - e.trimB - 1f;
            while (d < end - 8f)
            {
                e.Sample(d, out var p, out var t);
                var probe = p + Geo2D.Perp(t) * side * (e.Outer + 8f);
                var district = plan.DistrictAt(probe);
                var st = StyleFor(district);
                if (st.wMin <= 0 || !plan.OnSolidLand(probe)) { d += 4f; continue; }
                float w = Mathf.Min(R(st.wMin, st.wMax), end - d);
                if (w < st.wMin * 0.7f) break;
                bool placed = false;
                float depth = R(st.dMin, st.dMax);
                for (int attempt = 0; attempt < 3 && !placed; attempt++)
                {
                    var lot = MakeLot(e, d, side, w, depth, st.setback);
                    if (Valid(lot, district, st.kind == LotKind.Mansion ? 1.5f : 0.15f))
                    {
                        lot.kind = st.kind;
                        Add(lot);
                        placed = true;
                    }
                    else depth *= 0.72f;
                    if (depth < st.dMin * 0.55f) break;
                }
                d += placed ? w + R(st.gapMin, st.gapMax) : 4f;
            }
        }

        // ------------------------------------------------------------------ buildings

        static readonly string[] BrickWalls = { "brick_wall_10", "brown_brick_02" };
        static readonly string[] PlasterWalls = { "plastered_wall_04", "beige_wall_001" };
        static readonly string[] OfficeWalls = { "concrete_tile_facade", "rectangular_facade_tiles_02", "concrete_panels" };

        public void DesignBuildings()
        {
            foreach (var lot in lots)
            {
                if (lot.kind == LotKind.Landmark) continue;
                var b = new Building { seed = rng.Next(), trim = "brushed_concrete" };
                float margin = 0f;
                switch (lot.district)
                {
                    case District.Downtown:
                    {
                        bool glass = rng.NextDouble() < 0.55;
                        b.style = glass ? FacadeStyle.Curtain : FacadeStyle.Office;
                        b.wall = OfficeWalls[rng.Next(OfficeWalls.Length)];
                        // Taller toward the middle of the island.
                        float core = Mathf.Clamp01(1f - Vector2.Distance(lot.center, IslandPlan.P(830, 450)) / 220f);
                        b.floors = Mathf.RoundToInt(R(6, 12) + core * R(8, 20));
                        b.floorHeight = 3.6f;
                        b.groundHeight = 5.2f;
                        b.ground = rng.NextDouble() < 0.55 ? GroundFloor.Lobby : GroundFloor.Shops;
                        if (b.ground == GroundFloor.Shops && rng.NextDouble() < 0.5) b.awning = rng.Next(8);
                        if (b.floors > 15 && rng.NextDouble() < 0.6)
                        {
                            b.setback = R(2.5f, 4f);
                            b.setbackFloor = rng.Next(4, b.floors - 6);
                        }
                        margin = 0.4f;
                        break;
                    }
                    case District.Docks:
                        b.style = FacadeStyle.Warehouse;
                        b.wall = "corrugated_iron_02";
                        b.floors = rng.Next(1, 3);
                        b.floorHeight = 4.5f;
                        b.groundHeight = 6.5f;
                        b.ground = GroundFloor.Loading;
                        margin = 1.5f;
                        break;
                    case District.Beach:
                    {
                        bool hotel = lot.width >= 20f && rng.NextDouble() < 0.65;
                        if (hotel)
                        {
                            b.style = FacadeStyle.Plaster;
                            b.wall = PlasterWalls[rng.Next(PlasterWalls.Length)];
                            b.trim = rng.NextDouble() < 0.5 ? "white_sandstone_bricks" : "brushed_concrete";
                            b.floors = rng.Next(5, 15);
                            b.floorHeight = 3.2f;
                            b.groundHeight = 4.6f;
                            b.balconies = true;
                            b.ground = rng.NextDouble() < 0.5 ? GroundFloor.Lobby : GroundFloor.Cafe;
                            b.awning = rng.Next(8);
                        }
                        else
                        {
                            b.style = rng.NextDouble() < 0.4 ? FacadeStyle.Brick : FacadeStyle.Plaster;
                            b.wall = b.style == FacadeStyle.Brick ? BrickWalls[rng.Next(BrickWalls.Length)] : PlasterWalls[rng.Next(PlasterWalls.Length)];
                            b.floors = rng.Next(2, 5);
                            b.floorHeight = 3.2f;
                            b.groundHeight = 4.4f;
                            b.ground = rng.NextDouble() < 0.5 ? GroundFloor.Cafe : GroundFloor.Shops;
                            b.awning = rng.Next(8);
                        }
                        margin = 0.3f;
                        break;
                    }
                    case District.Palmside:
                        b.style = rng.NextDouble() < 0.4 ? FacadeStyle.Plaster : rng.NextDouble() < 0.5 ? FacadeStyle.Brick : FacadeStyle.Office;
                        b.wall = b.style switch
                        {
                            FacadeStyle.Brick => BrickWalls[rng.Next(BrickWalls.Length)],
                            FacadeStyle.Plaster => PlasterWalls[rng.Next(PlasterWalls.Length)],
                            _ => OfficeWalls[rng.Next(OfficeWalls.Length)],
                        };
                        b.floors = rng.Next(3, 7);
                        b.floorHeight = 3.3f;
                        b.groundHeight = 4.4f;
                        b.ground = rng.NextDouble() < 0.6 ? GroundFloor.Shops : GroundFloor.None;
                        if (b.ground == GroundFloor.Shops && rng.NextDouble() < 0.6) b.awning = rng.Next(8);
                        margin = 0.5f;
                        break;
                    case District.Hills:
                        b.style = FacadeStyle.Plaster;
                        b.wall = rng.NextDouble() < 0.45 ? "white_sandstone_bricks" : PlasterWalls[rng.Next(PlasterWalls.Length)];
                        b.trim = "white_sandstone_bricks";
                        b.floors = rng.Next(2, 4);
                        b.floorHeight = 3.4f;
                        b.groundHeight = 3.8f;
                        b.ground = GroundFloor.None;
                        b.roof = RoofKind.Hip;
                        break;
                }
                if (lot.kind == LotKind.Mansion)
                {
                    float hw = lot.width * R(0.48f, 0.6f), hd = lot.depth * R(0.38f, 0.48f);
                    float hx = R(-1f, 1f) * (lot.width - hw - 6f) * 0.5f;
                    float hz = lot.depth / 2 - hd / 2 - R(3f, 5f);
                    lot.house = new Rect(hx - hw / 2, hz - hd / 2, hw, hd);
                }
                else
                {
                    lot.house = new Rect(-lot.width / 2 + margin, -lot.depth / 2 + margin, lot.width - 2 * margin, lot.depth - 2 * margin);
                }
                b.footprint = lot.house;
                lot.building = b;
            }
        }

        public float Next() => (float)rng.NextDouble();
        public int NextInt(int max) => rng.Next(max);
    }
}
