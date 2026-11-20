using System.Collections.Generic;
using UnityEngine;

namespace Ashline.City
{
    /// <summary>
    /// Builds the whole island map at startup: terrain and sea, the road graph, lots and buildings,
    /// landmarks, vegetation and colliders. Meshes are merged per 160 m chunk.
    /// </summary>
    public class CityGenerator : MonoBehaviour
    {
        [SerializeField] int seed = 1337;

        public static CityGenerator Instance { get; private set; }
        public IslandPlan Plan { get; private set; }
        public RoadNetwork Network { get; private set; }
        public LotPlanner Lots { get; private set; }

        const float ChunkSize = 160f;
        CityArt art;
        Transform colliders;
        readonly Dictionary<Vector2Int, MeshBuilder> chunks = new Dictionary<Vector2Int, MeshBuilder>();
        System.Random rng;

        void Awake()
        {
            Instance = this;
            Generate();
        }

        public void Generate()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
            }
            art = CityArt.Load();
            if (art == null)
            {
                Debug.LogError("CityArt asset missing: run Ashline > Build Art in the editor.");
                return;
            }
            rng = new System.Random(seed);
            chunks.Clear();
            colliders = new GameObject("Colliders").transform;
            colliders.SetParent(transform, false);

            Plan = new IslandPlan();
            Network = new RoadNetwork(Plan.roads);

            Terrain();

            var roads = new RoadBuilder(art, Plan, Network);
            var road = new MeshBuilder();
            var walk = new MeshBuilder();
            var deck = new MeshBuilder();
            roads.Build(road, walk, deck);
            road.Build("Roads", transform, false, true);
            walk.Build("Sidewalks", transform, false, true);
            deck.Build("Bridges", transform, true, true);
            foreach (var foot in roads.lampFeet) Capsule(foot, 0.15f, 8f);

            Lots = new LotPlanner(Plan, Network, seed);
            Lots.reserved.Add((IslandPlan.P(338, 455), 26f));
            Lots.PlaceLandmark("police", IslandPlan.P(505, 470), 44, 30, 9);
            Lots.PlaceLandmark("hospital", IslandPlan.P(615, 465), 46, 26, 4);
            Lots.PlaceLandmark("gallery", IslandPlan.P(585, 640), 40, 28, 4);
            Lots.PlaceLandmark("bank", IslandPlan.P(818, 282), 36, 32, 1);
            Lots.PlanFrontage();
            Lots.DesignBuildings();
            foreach (var lot in Lots.lots) BuildLot(lot);

            Plaza(IslandPlan.P(338, 455), 33f - 8.5f);
            Ponds();
            Vegetation(roads.lampFeet);
            BeachProps();
            HighwayEnds();
            FerrisWheel();

            foreach (var kv in chunks) kv.Value.Build($"Chunk_{kv.Key.x}_{kv.Key.y}", transform);
            Debug.Log($"Island built: {Network.edges.Count} road edges, {Network.nodes.Count} junctions, {Lots.lots.Count} lots.");
        }

        MeshBuilder Chunk(Vector2 p)
        {
            var k = new Vector2Int(Mathf.FloorToInt(p.x / ChunkSize), Mathf.FloorToInt(p.y / ChunkSize));
            if (!chunks.TryGetValue(k, out var mb)) chunks[k] = mb = new MeshBuilder();
            return mb;
        }

        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        // ------------------------------------------------------------------ helpers

        static Vector2 Planar(Vector3 p, Vector2 size) => new Vector2(p.x / size.x, p.z / size.y);

        static void Tri(MeshBuilder mb, CityArt.Surface s, Vector3 a, Vector3 b, Vector3 c, bool down = false)
        {
            Tri(mb, s.material, s.size, a, b, c, down);
        }

        static void Tri(MeshBuilder mb, Material m, Vector2 size, Vector3 a, Vector3 b, Vector3 c, bool down = false)
        {
            if ((Vector3.Cross(b - a, c - a).y < 0) != down) { var t = b; b = c; c = t; }
            mb.Tri(m, a, b, c, Planar(a, size), Planar(b, size), Planar(c, size));
        }

        static void Polygon(MeshBuilder mb, Material m, Vector2 size, List<Vector2> poly, float y)
        {
            var tris = Geo2D.Triangulate(poly);
            for (int i = 0; i + 2 < tris.Count; i += 3)
                FineTri(mb, m, size, poly[tris[i]].X0Z(y), poly[tris[i + 1]].X0Z(y), poly[tris[i + 2]].X0Z(y), 0);
        }

        /// <summary>Splits long flat triangles so physics queries and fog interpolation stay well behaved.</summary>
        static void FineTri(MeshBuilder mb, Material m, Vector2 size, Vector3 a, Vector3 b, Vector3 c, int depth)
        {
            float longest = Mathf.Max((a - b).sqrMagnitude, Mathf.Max((b - c).sqrMagnitude, (c - a).sqrMagnitude));
            if (longest < 70f * 70f || depth > 6)
            {
                Tri(mb, m, size, a, b, c);
                return;
            }
            Vector3 ab = (a + b) * 0.5f, bc = (b + c) * 0.5f, ca = (c + a) * 0.5f;
            FineTri(mb, m, size, a, ab, ca, depth + 1);
            FineTri(mb, m, size, ab, b, bc, depth + 1);
            FineTri(mb, m, size, ca, bc, c, depth + 1);
            FineTri(mb, m, size, ab, bc, ca, depth + 1);
        }

        /// <summary>Strip between two loops of equal length, with per-loop heights.</summary>
        static void Band(MeshBuilder mb, CityArt.Surface s, List<Vector2> a, float ya, List<Vector2> b, float yb)
        {
            int n = Mathf.Min(a.Count, b.Count);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Tri(mb, s, a[i].X0Z(ya), a[j].X0Z(ya), b[j].X0Z(yb));
                Tri(mb, s, a[i].X0Z(ya), b[j].X0Z(yb), b[i].X0Z(yb));
            }
        }

        static void Disc(MeshBuilder mb, Material m, Vector2 size, Vector2 c, float r, float y, int segs = 40)
        {
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2 / segs, a1 = (i + 1) * Mathf.PI * 2 / segs;
                Tri(mb, m, size, c.X0Z(y), (c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r).X0Z(y), (c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r).X0Z(y));
            }
        }

        static void Ring(MeshBuilder mb, CityArt.Surface s, Vector2 c, float r0, float r1, float y, int segs = 40)
        {
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2 / segs, a1 = (i + 1) * Mathf.PI * 2 / segs;
                Vector2 d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)), d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                Tri(mb, s, (c + d0 * r0).X0Z(y), (c + d1 * r0).X0Z(y), (c + d1 * r1).X0Z(y));
                Tri(mb, s, (c + d0 * r0).X0Z(y), (c + d1 * r1).X0Z(y), (c + d0 * r1).X0Z(y));
            }
        }

        /// <summary>Inward-facing cylinder wall (pool and fountain basins).</summary>
        static void InnerWall(MeshBuilder mb, CityArt.Surface s, Vector2 c, float r, float y0, float y1, int segs = 40)
        {
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2 / segs, a1 = (i + 1) * Mathf.PI * 2 / segs;
                var p0 = c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r;
                var p1 = c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r;
                mb.Wall(s.material, s.size, p0.X0Z(), p1.X0Z(), y0, y1, a0 * r);
            }
        }

        BoxCollider Box(Vector3 center, Quaternion rot, Vector3 size)
        {
            var go = new GameObject("Box");
            go.transform.SetParent(colliders, false);
            go.transform.SetPositionAndRotation(center, rot);
            go.isStatic = true;
            var col = go.AddComponent<BoxCollider>();
            col.size = size;
            return col;
        }

        void Capsule(Vector3 foot, float radius, float height)
        {
            var go = new GameObject("Pole");
            go.transform.SetParent(colliders, false);
            go.transform.position = foot;
            go.isStatic = true;
            var cap = go.AddComponent<CapsuleCollider>();
            cap.center = Vector3.up * height / 2;
            cap.radius = radius;
            cap.height = height;
        }

        void Octagon(Vector2 c, float r, float height)
        {
            for (int i = 0; i < 4; i++)
                Box(c.X0Z(height / 2), Quaternion.Euler(0, i * 45f, 0), new Vector3(r * 2 * 0.83f, height, r * 2 * 0.83f));
        }

        // ------------------------------------------------------------------ terrain

        void Terrain()
        {
            var land = new MeshBuilder();
            var shore = new MeshBuilder();
            var sand = art.Get("sand_03");
            var damp = art.Get("damp_beach_sand");
            var rock = art.Get("seaside_rock");
            foreach (var isl in Plan.islands)
            {
                var ground = art.Get(isl.ground);
                Polygon(land, ground.material, ground.size, isl.inner, 0f);
                if (isl.kind == Coast.Beach)
                {
                    var mid = new List<Vector2>();
                    for (int i = 0; i < isl.coast.Count; i++) mid.Add(Vector2.Lerp(isl.inner[i], isl.coast[i], 0.45f));
                    Band(land, sand, isl.inner, 0f, mid, -1.9f * 0.45f);
                    Band(land, damp, mid, -1.9f * 0.45f, isl.coast, -1.9f);
                    Band(shore, damp, isl.coast, -1.9f, Geo2D.OffsetLoop(isl.coast, -22f), -5f);
                }
                else
                {
                    // Rock revetment along the Downtown seawall.
                    var toe = Geo2D.OffsetLoop(isl.coast, -5f);
                    Band(land, rock, isl.coast, 0f, toe, -3.6f);
                    Band(shore, damp, toe, -3.6f, Geo2D.OffsetLoop(isl.coast, -20f), -6f);
                }
            }
            land.Build("Land", transform, false, true);

            float far = 4000f;
            shore.Floor(damp.material, new Vector2(6, 6), -far, -far, far, far, -7f);
            shore.Build("SeaFloor", transform, false);

            if (art.water != null)
            {
                var sea = new MeshBuilder();
                // A grid instead of one quad keeps fog and depth interpolation clean on the horizon.
                float cell = 500f;
                for (float x = -far; x < far; x += cell)
                for (float z = -far; z < far; z += cell)
                    sea.Floor(art.water, new Vector2(1, 1), x, z, x + cell, z + cell, IslandPlan.WaterLevel);
                var go = sea.Build("Sea", transform, false);
                if (go != null) go.isStatic = false;
            }
        }

        void Ponds()
        {
            var rock = art.Get("seaside_rock");
            var path = art.Get("herringbone_pavement_02");
            foreach (var pond in Plan.ponds)
            {
                var mb = Chunk(pond[0]);
                Polygon(mb, art.pond != null ? art.pond : art.glass, new Vector2(4, 4), pond, 0.06f);
                var rim = Geo2D.OffsetLoop(pond, -1.4f);
                Band(mb, rock, pond, 0.22f, rim, 0.12f);
                for (int i = 0; i < pond.Count; i++)
                {
                    var a = pond[i];
                    var b = pond[(i + 1) % pond.Count];
                    mb.Wall(rock.material, rock.size, a.X0Z(), b.X0Z(), 0.0f, 0.22f, i * 2f);
                }
            }
            foreach (var line in Plan.parkPaths)
            {
                bool closed = Vector2.Distance(line[0], line[line.Count - 1]) > 1f && line.Count > 8;
                var pts = Geo2D.Resample(Geo2D.CatmullRom(line, 2f, closed), 2.5f);
                if (closed) pts.Add(pts[0]);
                var mb = Chunk(pts[0]);
                for (int i = 1; i < pts.Count; i++)
                {
                    var t0 = (pts[i] - pts[i - 1]).normalized;
                    var n0 = Geo2D.Perp(t0) * 1.6f;
                    var a = pts[i - 1];
                    var b = pts[i];
                    var t1 = i + 1 < pts.Count ? (pts[i + 1] - pts[i]).normalized : t0;
                    var n1 = Geo2D.Perp((t0 + t1).normalized) * 1.6f;
                    Tri(mb, path, (a - n0).X0Z(0.03f), (b - n1).X0Z(0.03f), (b + n1).X0Z(0.03f));
                    Tri(mb, path, (a - n0).X0Z(0.03f), (b + n1).X0Z(0.03f), (a + n0).X0Z(0.03f));
                }
            }
        }

        // ------------------------------------------------------------------ lots

        void BuildLot(Lot lot)
        {
            var mb = Chunk(lot.center);
            if (lot.kind == LotKind.Landmark) lot.building = LandmarkBuilding(lot);
            if (lot.building == null) return;
            mb.Matrix = lot.Matrix(0f);
            float baseY = RoadBuilder.CurbTop;
            if (lot.kind == LotKind.Mansion)
            {
                Garden(mb, lot);
                baseY = 0.3f;
            }
            else Pad(mb, lot);
            new BuildingBuilder(art, mb, baseY).Build(lot.building);
            if (lot.kind == LotKind.Landmark) LandmarkDressing(mb, lot, baseY);
            mb.Matrix = Matrix4x4.identity;

            var h = lot.house;
            float height = lot.building.Height + 1.5f;
            var rot = Quaternion.LookRotation(new Vector3(lot.away.x, 0, lot.away.y));
            var c = lot.ToWorld(h.center);
            Box(new Vector3(c.x, baseY + height / 2, c.y), rot, new Vector3(h.width, height, h.height));
        }

        string PavingFor(District d) => d switch
        {
            District.Beach => "red_sandstone_pavement",
            District.Palmside => "herringbone_pavement_02",
            District.Docks => "brushed_concrete",
            _ => "concrete_pavement_02",
        };

        void Pad(MeshBuilder mb, Lot lot)
        {
            var pave = art.Get(lot.kind == LotKind.Landmark && lot.landmark == "police" ? "asphalt_02" : PavingFor(lot.district));
            var curb = art.Get("brushed_concrete");
            float y = RoadBuilder.CurbTop - 0.01f;
            float front = -lot.depth / 2 - lot.setback - 0.3f, back = lot.depth / 2, half = lot.width / 2;
            mb.Floor(pave.material, pave.size, -half, front, half, back, y);
            var p00 = new Vector3(-half, 0, front);
            var p10 = new Vector3(half, 0, front);
            var p11 = new Vector3(half, 0, back);
            var p01 = new Vector3(-half, 0, back);
            mb.Wall(curb.material, curb.size, p10, p11, -0.3f, y, front);
            mb.Wall(curb.material, curb.size, p11, p01, -0.3f, y, -half);
            mb.Wall(curb.material, curb.size, p01, p00, -0.3f, y, -back);
        }

        void Garden(MeshBuilder mb, Lot lot)
        {
            var stone = art.Get("white_sandstone_bricks");
            var trim = art.Get("brushed_concrete");
            var drive = art.Get("herringbone_pavement_02");
            float hw = lot.width / 2, hd = lot.depth / 2, gate = 3.2f, wallH = 1.25f, t = 0.3f;
            var rot = Quaternion.LookRotation(new Vector3(lot.away.x, 0, lot.away.y));

            void WallSeg(float x0, float z0, float x1, float z1)
            {
                mb.Box(stone.material, stone.size, new Vector3(x0, 0, z0), new Vector3(x1, wallH, z1));
                mb.Box(trim.material, trim.size, new Vector3(x0 - 0.05f, wallH, z0 - 0.05f), new Vector3(x1 + 0.05f, wallH + 0.08f, z1 + 0.05f));
                var c = lot.ToWorld(new Vector2((x0 + x1) / 2, (z0 + z1) / 2));
                Box(new Vector3(c.x, wallH / 2, c.y), rot, new Vector3(x1 - x0, wallH, z1 - z0));
            }
            WallSeg(-hw, -hd, -gate, -hd + t);
            WallSeg(gate, -hd, hw, -hd + t);
            WallSeg(-hw, hd - t, hw, hd);
            WallSeg(-hw, -hd + t, -hw + t, hd - t);
            WallSeg(hw - t, -hd + t, hw, hd - t);
            // Gate piers with lamps.
            foreach (float x in new[] { -gate - 0.35f, gate + 0.35f })
            {
                mb.Box(stone.material, stone.size, new Vector3(x - 0.35f, 0, -hd - 0.05f), new Vector3(x + 0.35f, 1.9f, -hd + 0.65f));
                mb.Box(art.lampEmissive, Vector2.one, new Vector3(x - 0.15f, 1.9f, -hd + 0.15f), new Vector3(x + 0.15f, 2.2f, -hd + 0.45f));
            }

            var h = lot.house;
            // Driveway from the road edge to the front door.
            float dx = Mathf.Clamp(h.center.x, -hw + 4f, hw - 4f);
            mb.Floor(drive.material, drive.size, -gate + 0.2f, -hd - lot.setback - 0.4f, gate - 0.2f, -hd + 6f, 0.07f);
            mb.Floor(drive.material, drive.size, Mathf.Min(dx, 0) - 2.6f, -hd + 6f, Mathf.Max(dx, 0) + 2.6f, h.yMin, 0.07f);
            // Plinth and front steps.
            mb.Box(stone.material, stone.size, new Vector3(h.xMin - 0.4f, 0, h.yMin - 0.4f), new Vector3(h.xMax + 0.4f, 0.3f, h.yMax + 0.4f));

            // Pool on the free side of the front garden.
            float poolW = 10f, poolD = 4.6f;
            float side = h.center.x > 0 ? -1f : 1f;
            float px = side * (hw - poolW / 2 - 2.5f), pz = h.yMin - poolD / 2 - 3f;
            if (pz - poolD / 2 > -hd + 7f && Mathf.Abs(px) + poolW / 2 < hw - 1f && (side < 0 ? px + poolW / 2 < dx - 3.5f : px - poolW / 2 > dx + 3.5f))
            {
                var coping = art.Get("white_sandstone_bricks");
                float x0 = px - poolW / 2, x1 = px + poolW / 2, z0 = pz - poolD / 2, z1 = pz + poolD / 2, c = 0.6f;
                mb.Box(coping.material, coping.size, new Vector3(x0 - c, 0, z0 - c), new Vector3(x1 + c, 0.14f, z0));
                mb.Box(coping.material, coping.size, new Vector3(x0 - c, 0, z1), new Vector3(x1 + c, 0.14f, z1 + c));
                mb.Box(coping.material, coping.size, new Vector3(x0 - c, 0, z0), new Vector3(x0, 0.14f, z1));
                mb.Box(coping.material, coping.size, new Vector3(x1, 0, z0), new Vector3(x1 + c, 0.14f, z1));
                mb.Floor(art.pool, new Vector2(2, 2), x0, z0, x1, z1, 0.1f);
                // Sun loungers.
                for (int i = 0; i < 3; i++)
                {
                    float lx = x0 + 1.5f + i * 2.4f;
                    mb.Box(art.paintWhite, Vector2.one, new Vector3(lx - 0.35f, 0.25f, z0 - c - 2.3f), new Vector3(lx + 0.35f, 0.38f, z0 - c - 0.4f));
                    mb.Box(art.paintWhite, Vector2.one, new Vector3(lx - 0.35f, 0.38f, z0 - c - 0.7f), new Vector3(lx + 0.35f, 0.85f, z0 - c - 0.4f));
                }
            }
            mb.Matrix = Matrix4x4.identity;
            // Palms inside the garden corners.
            var corners = new[] { new Vector2(-hw + 2.5f, -hd + 2.5f), new Vector2(hw - 2.5f, -hd + 2.5f), new Vector2(-hw + 2.5f, hd - 2.5f), new Vector2(hw - 2.5f, hd - 2.5f) };
            foreach (var cl in corners)
            {
                if (rng.NextDouble() < 0.3) continue;
                if (h.Contains(cl)) continue;
                Palm(lot.ToWorld(cl).X0Z(0f), true);
            }
            mb.Matrix = lot.Matrix(0f);
        }

        Building LandmarkBuilding(Lot lot)
        {
            var b = new Building { seed = rng.Next(), trim = "brushed_concrete" };
            float inset = 1f;
            switch (lot.landmark)
            {
                case "police":
                    b.style = FacadeStyle.Office; b.wall = "concrete_tile_facade";
                    b.floors = 3; b.floorHeight = 3.8f; b.groundHeight = 5f; b.ground = GroundFloor.Lobby;
                    inset = 1.5f;
                    break;
                case "hospital":
                    b.style = FacadeStyle.Plaster; b.wall = "plastered_wall_04";
                    b.floors = 6; b.floorHeight = 3.6f; b.groundHeight = 5f; b.ground = GroundFloor.Lobby;
                    break;
                case "gallery":
                    b.style = FacadeStyle.Office; b.wall = "white_sandstone_bricks"; b.trim = "white_sandstone_bricks";
                    b.floors = 2; b.floorHeight = 5f; b.groundHeight = 6f; b.ground = GroundFloor.Lobby;
                    break;
                case "bank":
                    b.style = FacadeStyle.Curtain; b.wall = "concrete_panels";
                    b.floors = 22; b.floorHeight = 3.6f; b.groundHeight = 7.5f; b.ground = GroundFloor.Lobby;
                    b.setback = 3f; b.setbackFloor = 4;
                    break;
                default:
                    return null;
            }
            lot.house = new Rect(-lot.width / 2 + inset, -lot.depth / 2 + inset, lot.width - inset * 2, lot.depth - inset * 2);
            b.footprint = lot.house;
            return b;
        }

        void LandmarkDressing(MeshBuilder mb, Lot lot, float baseY)
        {
            var h = lot.house;
            var b = lot.building;
            float signY = baseY + b.groundHeight - 0.6f;
            string text = null;
            Material plate = art.metalDark;
            switch (lot.landmark)
            {
                case "police":
                    text = "POLICE";
                    plate = art.paintBlue;
                    // Blue band over the entrance floor and parking bays in front.
                    mb.Box(art.paintBlue, Vector2.one, new Vector3(h.xMin - 0.05f, baseY + b.groundHeight - 1.1f, h.yMin - 0.25f), new Vector3(h.xMax + 0.05f, baseY + b.groundHeight - 0.3f, h.yMin));
                    for (float x = h.xMin + 1f; x < h.xMax - 1f; x += 3f)
                        mb.Floor(art.roadPaint, Vector2.one, x, -lot.depth / 2 - lot.setback + 0.6f, x + 0.12f, -lot.depth / 2 - lot.setback + 5.6f, RoadBuilder.CurbTop + 0.005f);
                    break;
                case "hospital":
                    text = "HOSPITAL";
                    var cross = new Vector3(h.center.x, baseY + b.Height - 2.6f, h.yMin - 0.18f);
                    mb.Box(art.paintRed, Vector2.one, cross + new Vector3(-0.5f, -1.6f, 0), cross + new Vector3(0.5f, 1.6f, 0.2f));
                    mb.Box(art.paintRed, Vector2.one, cross + new Vector3(-1.6f, -0.5f, 0), cross + new Vector3(1.6f, 0.5f, 0.2f));
                    break;
                case "gallery": text = "ART GALLERY"; break;
                case "bank": text = "ASHLINE BANK"; break;
            }
            if (text == null) return;
            float plateW = text.Length * 1.15f + 1.5f;
            mb.Box(plate, Vector2.one, new Vector3(h.center.x - plateW / 2, signY - 0.75f, h.yMin - 0.22f), new Vector3(h.center.x + plateW / 2, signY + 0.75f, h.yMin - 0.02f));
            var local = new Vector3(h.center.x, signY, h.yMin - 0.26f);
            var world = lot.Matrix(0f).MultiplyPoint3x4(local);
            Sign(text, world, Quaternion.LookRotation(new Vector3(lot.away.x, 0, lot.away.y)), 1.1f, Color.white);
        }

        void Sign(string text, Vector3 pos, Quaternion rot, float letterHeight, Color color)
        {
            var go = new GameObject("Sign_" + text);
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(pos, rot);
            var tm = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            tm.text = text;
            tm.fontSize = 96;
            tm.characterSize = letterHeight / 9.6f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold;
            tm.color = color;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }

        // ------------------------------------------------------------------ plaza and landmarks

        void Plaza(Vector2 c, float radius)
        {
            var mb = Chunk(c);
            var pave = art.Get("herringbone_pavement_02");
            var stone = art.Get("white_sandstone_bricks");
            Disc(mb, pave.material, pave.size, c, radius + 0.4f, RoadBuilder.CurbTop - 0.01f, 48);
            // Fountain: stone basin, water, two tiers.
            Ring(mb, stone, c, 7.4f, 8f, 0.75f);
            mb.Cylinder(stone.material, stone.size, c.X0Z(0f), 8f, 0.75f, 40);
            InnerWall(mb, stone, c, 7.4f, 0.3f, 0.75f);
            Disc(mb, art.pool, new Vector2(2, 2), c, 7.4f, 0.55f);
            mb.Cylinder(stone.material, stone.size, c.X0Z(0.5f), 1.1f, 1.9f, 16);
            Disc(mb, stone.material, stone.size, c, 3f, 2.4f, 24);
            mb.Cylinder(stone.material, stone.size, c.X0Z(2.2f), 3f, 0.2f, 24);
            mb.Cylinder(stone.material, stone.size, c.X0Z(2.4f), 0.5f, 1.4f, 12);
            Disc(mb, stone.material, stone.size, c, 1.2f, 3.8f, 16);
            Octagon(c, 8f, 1f);
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * 2 / 10 + 0.2f;
                Palm((c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (radius - 4.5f)).X0Z(RoadBuilder.CurbTop), true);
            }
        }

        void FerrisWheel()
        {
            RoadEdge pier = null;
            foreach (var e in Network.edges) if (e.def.name == "South Pier" && e.b < 0) pier = e;
            if (pier == null) return;
            pier.Sample(pier.Length, out var end, out var t);
            var center = end + t * 7f;
            var go = new GameObject("FerrisWheel");
            go.transform.SetParent(transform, false);
            go.transform.position = center.X0Z(RoadBuilder.RoadY);
            go.transform.rotation = Quaternion.LookRotation(new Vector3(-t.y, 0, t.x));
            go.AddComponent<FerrisWheel>().Build(art, 15f);
        }

        void HighwayEnds()
        {
            var concrete = art.Get("brushed_concrete");
            foreach (var e in Network.edges)
            {
                if (e.def.kind != RoadKind.Highway) continue;
                for (int end = 0; end < 2; end++)
                {
                    if ((end == 0 ? e.a : e.b) >= 0) continue;
                    e.Sample(end == 0 ? 2f : e.Length - 2f, out var p, out var t);
                    var mb = Chunk(p);
                    var n = Geo2D.Perp(t);
                    var fwd = new Vector3(t.x, 0, t.y);
                    var right = Vector3.Cross(Vector3.up, fwd);
                    // Jersey barriers with hazard paint across the carriageway.
                    for (float x = -e.HalfWidth + 1f; x < e.HalfWidth; x += 2.1f)
                    {
                        var c = (p + n * x).X0Z(RoadBuilder.RoadY + 0.45f);
                        mb.OrientedBox(concrete.material, concrete.size, c, right, Vector3.up, fwd, new Vector3(1f, 0.45f, 0.3f));
                        mb.OrientedBox(art.paintRed, Vector2.one, c + Vector3.up * 0.3f, right, Vector3.up, fwd, new Vector3(1.01f, 0.1f, 0.31f));
                    }
                    Box(p.X0Z(RoadBuilder.RoadY + 1f), Quaternion.LookRotation(fwd), new Vector3(e.Outer * 2, 2f, 0.6f));
                }
            }
        }

        // ------------------------------------------------------------------ vegetation and beach

        void Palm(Vector3 basePos, bool collider)
        {
            var mb = Chunk(basePos.XZ());
            PalmBuilder.Build(mb, art.palmTrunk, art.palmFrond, basePos, rng.Next());
            if (collider) Capsule(basePos, 0.3f, 4f);
        }

        void Vegetation(List<Vector3> lamps)
        {
            if (art.palmTrunk == null || art.palmFrond == null) return;
            // Street palms on the sidewalks of the resort districts.
            foreach (var e in Network.edges)
            {
                if (e.Sidewalk <= 0 || e.def.kind == RoadKind.Highway) continue;
                float first = e.trimA + 7f, last = e.Length - e.trimB - 7f;
                for (float d = first; d < last; d += 17f)
                {
                    e.Sample(d, out var p, out var t);
                    var dist = Plan.DistrictAt(p);
                    if (dist == District.Downtown || dist == District.Docks) continue;
                    var n = Geo2D.Perp(t);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var q = p + n * side * (e.Outer - 0.9f);
                        if (!Plan.OnSolidLand(q)) continue;
                        bool nearLamp = false;
                        foreach (var l in lamps) if ((l.XZ() - q).sqrMagnitude < 9f) { nearLamp = true; break; }
                        if (nearLamp) continue;
                        Palm(q.X0Z(RoadBuilder.CurbTop), true);
                    }
                }
            }
            // Downtown gets planted medians of smaller trees? Keep its streets hard and urban.

            // Palms along the beaches.
            foreach (var isl in Plan.islands)
            {
                if (isl.kind != Coast.Beach) continue;
                float acc = 0;
                for (int i = 1; i < isl.coast.Count; i++)
                {
                    acc += Vector2.Distance(isl.inner[i], isl.inner[i - 1]);
                    if (acc < 16f) continue;
                    acc = 0;
                    if (rng.NextDouble() < 0.35) continue;
                    var q = Vector2.Lerp(isl.inner[i], isl.coast[i], R(0.05f, 0.35f));
                    if (Lots.RoadClearance(q) < 2f) continue;
                    Palm(q.X0Z(Plan.TerrainHeight(q) - 0.05f), true);
                }
            }
            // Park.
            var west = Plan.islands[0];
            Vector2 min = west.coast[0], max = west.coast[0];
            foreach (var p in west.coast) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            for (int i = 0; i < 900; i++)
            {
                var q = new Vector2(R(min.x, max.x), R(min.y, max.y));
                var d = Plan.DistrictAt(q);
                if (d != District.Park && !(d == District.Hills && rng.NextDouble() < 0.25)) continue;
                if (!Lots.Free(q, 3f)) continue;
                bool onPath = false;
                foreach (var path in Plan.parkPaths) if (Geo2D.DistanceToPolyline(q, path) < 3f) { onPath = true; break; }
                if (onPath) continue;
                Palm(q.X0Z(-0.02f), true);
            }
        }

        void BeachProps()
        {
            var wood = art.Get("wood_floor_deck");
            foreach (var isl in Plan.islands)
            {
                if (isl.kind != Coast.Beach) continue;
                float acc = 60f;
                for (int i = 1; i < isl.coast.Count; i++)
                {
                    acc += Vector2.Distance(isl.coast[i], isl.coast[i - 1]);
                    if (acc < 130f) continue;
                    var q = Vector2.Lerp(isl.inner[i], isl.coast[i], 0.5f);
                    if (Lots.RoadClearance(q) < 6f) continue;
                    acc = 0;
                    var toSea = (isl.coast[i] - isl.inner[i]).normalized;
                    LifeguardTower(wood, q, toSea);
                    var along = Geo2D.Perp(toSea);
                    for (int k = 0; k < 7; k++)
                    {
                        var u = q + along * R(-35f, 35f) + toSea * R(-6f, 3f);
                        if ((u - q).sqrMagnitude < 25f || Lots.RoadClearance(u) < 3f) continue;
                        Umbrella(u, rng.Next(8));
                    }
                }
            }
        }

        void LifeguardTower(CityArt.Surface wood, Vector2 p, Vector2 toSea)
        {
            float y = Plan.TerrainHeight(p);
            var mb = Chunk(p);
            var fwd = new Vector3(toSea.x, 0, toSea.y);
            var right = Vector3.Cross(Vector3.up, fwd);
            var basePos = p.X0Z(y);
            for (int i = 0; i < 4; i++)
            {
                var o = right * ((i & 1) * 2 - 1) * 1.1f + fwd * ((i >> 1) * 2 - 1) * 1.1f;
                mb.OrientedBox(wood.material, wood.size, basePos + o + Vector3.up * 1.2f, right, Vector3.up, fwd, new Vector3(0.1f, 1.25f, 0.1f));
            }
            var deck = basePos + Vector3.up * 2.45f;
            mb.OrientedBox(wood.material, wood.size, deck, right, Vector3.up, fwd, new Vector3(1.5f, 0.07f, 1.7f));
            mb.OrientedBox(art.paintWhite, Vector2.one, deck + Vector3.up * 1.05f - fwd * 0.15f, right, Vector3.up, fwd, new Vector3(1.15f, 1f, 1.05f));
            mb.OrientedBox(art.paintBlue, Vector2.one, deck + Vector3.up * 0.45f - fwd * 0.15f, right, Vector3.up, fwd, new Vector3(1.17f, 0.12f, 1.07f));
            mb.OrientedBox(art.metalDark, Vector2.one, deck + Vector3.up * 1.4f + fwd * 0.92f, right, Vector3.up, fwd, new Vector3(0.85f, 0.32f, 0.02f));
            mb.OrientedBox(art.paintRed, Vector2.one, deck + Vector3.up * 2.15f, right, Vector3.up, fwd, new Vector3(1.45f, 0.08f, 1.5f));
            // Ramp down to the sand at the back.
            var ramp = basePos + Vector3.up * 1.2f - fwd * 3.2f;
            var rampDir = (fwd * 2.9f + Vector3.up * 2.4f).normalized;
            mb.OrientedBox(wood.material, wood.size, ramp, right, Vector3.Cross(rampDir, right), rampDir, new Vector3(0.6f, 0.05f, 1.9f));
            Box(basePos + Vector3.up * 2.2f, Quaternion.LookRotation(fwd), new Vector3(2.4f, 4.4f, 2.4f));
        }

        void Umbrella(Vector2 p, int color)
        {
            if (art.awnings == null || art.awnings.Length == 0) return;
            var mb = Chunk(p);
            float y = Plan.TerrainHeight(p);
            var cloth = art.awnings[color % art.awnings.Length];
            var foot = p.X0Z(y - 0.1f);
            mb.Cylinder(art.paintWhite, Vector2.one, foot, 0.025f, 2.5f, 6);
            var apex = foot + Vector3.up * 2.55f;
            const int segs = 10;
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2 / segs, a1 = (i + 1) * Mathf.PI * 2 / segs;
                var r0 = foot + new Vector3(Mathf.Cos(a0) * 1.4f, 2.1f, Mathf.Sin(a0) * 1.4f);
                var r1 = foot + new Vector3(Mathf.Cos(a1) * 1.4f, 2.1f, Mathf.Sin(a1) * 1.4f);
                Tri(mb, cloth, Vector2.one, apex, r0, r1);
                Tri(mb, cloth, Vector2.one, apex, r0, r1, true);
            }
            // Towel on the sand.
            var dir = new Vector2(Mathf.Cos(color), Mathf.Sin(color));
            var n = Geo2D.Perp(dir);
            var tc = p + dir * 1.6f;
            Tri(mb, art.awnings[(color + 3) % art.awnings.Length], Vector2.one, (tc - dir * 0.9f - n * 0.4f).X0Z(y + 0.02f), (tc + dir * 0.9f - n * 0.4f).X0Z(y + 0.02f), (tc + dir * 0.9f + n * 0.4f).X0Z(y + 0.02f));
            Tri(mb, art.awnings[(color + 3) % art.awnings.Length], Vector2.one, (tc - dir * 0.9f - n * 0.4f).X0Z(y + 0.02f), (tc + dir * 0.9f + n * 0.4f).X0Z(y + 0.02f), (tc - dir * 0.9f + n * 0.4f).X0Z(y + 0.02f));
        }

        // ------------------------------------------------------------------ queries

        /// <summary>Pose on the named road: t01 along it, lateral offset from the centerline (negative = right lane).</summary>
        public bool RoadPose(string road, float t01, float lateral, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero;
            rot = Quaternion.identity;
            RoadEdge best = null;
            foreach (var e in Network.edges)
                if (e.def.name == road && (best == null || e.Length > best.Length)) best = e;
            if (best == null) return false;
            best.Sample(best.Length * t01, out var p, out var t);
            var q = p + Geo2D.Perp(t) * lateral;
            pos = q.X0Z(lateral > best.HalfWidth ? RoadBuilder.CurbTop : RoadBuilder.RoadY);
            rot = Quaternion.LookRotation(new Vector3(t.x, 0, t.y));
            return true;
        }
    }
}
