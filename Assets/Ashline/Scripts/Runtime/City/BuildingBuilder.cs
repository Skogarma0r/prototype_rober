using UnityEngine;

namespace Ashline.City
{
    /// <summary>
    /// Real geometry facades: every window is a recess with jambs, head, sill ledge and a glass pane
    /// set back into the wall, so sun shadows and sky reflections read correctly at street level.
    /// </summary>
    public class BuildingBuilder
    {
        readonly CityArt art;
        readonly MeshBuilder mb;
        readonly float baseY;

        public BuildingBuilder(CityArt art, MeshBuilder mb, float baseY)
        {
            this.art = art;
            this.mb = mb;
            this.baseY = baseY;
        }

        struct Face
        {
            public Vector3 p0;
            public Vector3 t;   // along the facade
            public Vector3 n;   // outward
            public float length;

            public Vector3 At(float u, float y, float depth) => p0 + t * u + Vector3.up * y + n * depth;
        }

        struct Window
        {
            public float width, height, sill, inset, bay;
            public bool mullion;
        }

        static Face[] Faces(Rect r)
        {
            Face F(Vector3 a, Vector3 b)
            {
                var t = (b - a).normalized;
                return new Face { p0 = a, t = t, n = Vector3.Cross(Vector3.up, t), length = Vector3.Distance(a, b) };
            }
            var p00 = new Vector3(r.xMin, 0, r.yMin);
            var p10 = new Vector3(r.xMax, 0, r.yMin);
            var p11 = new Vector3(r.xMax, 0, r.yMax);
            var p01 = new Vector3(r.xMin, 0, r.yMax);
            return new[] { F(p00, p10), F(p10, p11), F(p11, p01), F(p01, p00) };
        }

        public void Build(Building b)
        {
            var rng = new System.Random(b.seed);
            room = Mathf.Abs(b.seed) % 8191;
            var wall = art.Get(b.wall);
            var trim = art.Get(b.trim);
            float top = baseY + b.Height;

            Rect lower = b.footprint;
            Rect upper = b.setback > 0 ? Shrink(lower, b.setback) : lower;
            float splitY = b.setback > 0 ? baseY + b.groundHeight + b.setbackFloor * b.floorHeight : top;

            var win = WindowFor(b, rng);
            BuildMass(b, lower, baseY, splitY, true, wall, trim, win, rng);
            if (b.setback > 0)
            {
                Roof(lower, splitY, trim, false);
                BuildMass(b, upper, splitY, top, false, wall, trim, win, rng);
            }
            if (b.roof == RoofKind.Hip)
            {
                HipRoof(upper, top, trim, rng);
                return;
            }
            Roof(upper, top, trim, true);
            RoofDetails(b, upper, top, wall, trim, rng);
        }

        static Rect Shrink(Rect r, float d) => new Rect(r.x + d, r.y + d, r.width - 2 * d, r.height - 2 * d);

        Window WindowFor(Building b, System.Random rng)
        {
            switch (b.style)
            {
                case FacadeStyle.Curtain:
                    return new Window { width = 1.5f, height = b.floorHeight - 0.9f, sill = 0.8f, inset = 0.06f, bay = 1.5f };
                case FacadeStyle.Office:
                    return new Window { width = 2.2f, height = 1.9f, sill = 0.9f, inset = 0.18f, bay = 3.0f, mullion = true };
                case FacadeStyle.Warehouse:
                    return new Window { width = 3.6f, height = 1.2f, sill = b.groundHeight - 2.0f, inset = 0.12f, bay = 7.5f, mullion = true };
                case FacadeStyle.Brick:
                    return new Window { width = rng.NextDouble() < 0.5 ? 1.3f : 1.5f, height = 1.8f, sill = 0.85f, inset = 0.24f, bay = 3.1f, mullion = true };
                default:
                    if (b.balconies) return new Window { width = 1.8f, height = b.floorHeight - 0.75f, sill = 0.2f, inset = 0.16f, bay = 3.6f, mullion = true };
                    return new Window { width = 1.4f, height = 1.6f, sill = 0.9f, inset = 0.2f, bay = 3.2f };
            }
        }

        void BuildMass(Building b, Rect r, float y0, float y1, bool withGround, CityArt.Surface wall, CityArt.Surface trim, Window win, System.Random rng)
        {
            foreach (var f in Faces(r))
            {
                float y = y0;
                if (withGround)
                {
                    float gTop = baseY + b.groundHeight;
                    GroundStorey(b, f, y, gTop, wall, trim, rng);
                    y = gTop;
                    // Cornice band between the storefronts and the upper floors.
                    Ledge(f, gTop - 0.35f, gTop, 0.18f, trim);
                }
                if (b.style == FacadeStyle.Warehouse) continue;
                while (y < y1 - 0.01f)
                {
                    float fy1 = Mathf.Min(y1, y + b.floorHeight);
                    if (b.style == FacadeStyle.Curtain) CurtainFloor(f, y, fy1, wall, win);
                    else PunchedFloor(f, y, fy1, wall, trim, win);
                    if (b.balconies && f.length > 7f) Balcony(f, y, wall, trim);
                    y = fy1;
                }
            }
        }

        // ---------------------------------------------------------------- floors

        void PunchedFloor(Face f, float y0, float y1, CityArt.Surface wall, CityArt.Surface trim, Window w)
        {
            float margin = 0.9f;
            int count = Mathf.Max(1, Mathf.FloorToInt((f.length - 2 * margin) / w.bay));
            float bay = (f.length - 2 * margin) / count;
            float wy0 = y0 + w.sill, wy1 = Mathf.Min(y1 - 0.3f, wy0 + w.height);

            WallStrip(f, 0, f.length, y0, wy0, wall);
            WallStrip(f, 0, f.length, wy1, y1, wall);
            float u = 0;
            for (int i = 0; i < count; i++)
            {
                float c = margin + bay * (i + 0.5f);
                float a = c - w.width / 2, bEnd = c + w.width / 2;
                WallStrip(f, u, a, wy0, wy1, wall);
                Opening(f, a, bEnd, wy0, wy1, w.inset, wall, art.glass);
                if (w.mullion) Mullion(f, c, wy0, wy1, w.inset, w.width / 2);
                // Precast sill ledge under each window.
                Ledge(f, wy0 - 0.08f, wy0, 0.07f, trim, a - 0.08f, bEnd + 0.08f);
                u = bEnd;
            }
            WallStrip(f, u, f.length, wy0, wy1, wall);
        }

        void CurtainFloor(Face f, float y0, float y1, CityArt.Surface panel, Window w)
        {
            float spandrel = 0.85f;
            // Opaque spandrel band hides the slab edge, then a continuous glass ribbon.
            WallStrip(f, 0, f.length, y0, y0 + spandrel, panel);
            float gy0 = y0 + spandrel, gy1 = y1;
            var glass = art.glass;
            var frame = art.windowFrame;
            int count = Mathf.Max(1, Mathf.RoundToInt(f.length / w.bay));
            // One fake room per glazing bay: the integer part of u steps once per bay.
            float id = NextRoom();
            room += count;
            mb.Stamp = new Vector2(f.length / count, gy1 - gy0);
            mb.Quad(glass,
                f.At(0, gy0, -w.inset), f.At(0, gy1, -w.inset), f.At(f.length, gy1, -w.inset), f.At(f.length, gy0, -w.inset),
                new Vector2(id, 0), new Vector2(id, 0.999f), new Vector2(id + count - 0.001f, 0.999f), new Vector2(id + count - 0.001f, 0));
            mb.Stamp = Vector2.zero;
            float step = f.length / count;
            for (int i = 0; i <= count; i++)
            {
                var c = f.At(i * step, (gy0 + gy1) / 2, -w.inset / 2 + 0.02f);
                mb.OrientedBox(frame, Vector2.one, c, f.t, Vector3.up, f.n, new Vector3(0.05f, (gy1 - gy0) / 2, w.inset / 2 + 0.03f),
                    MeshBuilder.Faces.Front | MeshBuilder.Faces.Sides);
            }
            var tc = f.At(f.length / 2, gy0 + 0.04f, 0.0f);
            mb.OrientedBox(frame, Vector2.one, tc, f.t, Vector3.up, f.n, new Vector3(f.length / 2, 0.04f, 0.06f),
                MeshBuilder.Faces.Front | MeshBuilder.Faces.Top | MeshBuilder.Faces.Bottom);
        }

        void GroundStorey(Building b, Face f, float y0, float y1, CityArt.Surface wall, CityArt.Surface trim, System.Random rng)
        {
            switch (b.ground)
            {
                case GroundFloor.Shops:
                case GroundFloor.Cafe:
                case GroundFloor.Lobby:
                {
                    // Large storefront bays with a stone base, a fascia for signage and piers between.
                    float pier = b.ground == GroundFloor.Lobby ? 1.0f : 0.7f;
                    float bayW = b.ground == GroundFloor.Lobby ? 4.5f : 5.5f;
                    int count = Mathf.Max(1, Mathf.FloorToInt(f.length / bayW));
                    float bay = f.length / count;
                    float baseH = 0.45f, glassTop = y1 - 1.1f;
                    WallStrip(f, 0, f.length, y0, y0 + baseH, trim);
                    WallStrip(f, 0, f.length, glassTop, y1, b.ground == GroundFloor.Lobby ? wall : trim);
                    float u = 0;
                    for (int i = 0; i < count; i++)
                    {
                        float a = i * bay + pier / 2, e = (i + 1) * bay - pier / 2;
                        WallStrip(f, u, a, y0 + baseH, glassTop, b.ground == GroundFloor.Lobby ? wall : trim);
                        Opening(f, a, e, y0 + baseH, glassTop, 0.15f, trim, art.shopGlass);
                        Mullion(f, (a + e) / 2, y0 + baseH, glassTop, 0.15f, (e - a) / 2);
                        u = e;
                    }
                    WallStrip(f, u, f.length, y0 + baseH, glassTop, b.ground == GroundFloor.Lobby ? wall : trim);
                    if (b.awning >= 0 && art.awnings != null && art.awnings.Length > 0 && b.ground != GroundFloor.Lobby)
                    {
                        var cloth = art.awnings[b.awning % art.awnings.Length];
                        for (int i = 0; i < count; i++)
                        {
                            float a = i * bay + pier / 2 + 0.1f, e = (i + 1) * bay - pier / 2 - 0.1f;
                            Awning(f, a, e, glassTop + 0.45f, b.ground == GroundFloor.Cafe ? 1.7f : 1.15f, cloth);
                        }
                    }
                    break;
                }
                case GroundFloor.Loading:
                {
                    // Roll-up doors and a band of clerestory windows high on the wall.
                    float doorW = 4.2f, doorH = 4.4f;
                    int doors = Mathf.Max(1, Mathf.FloorToInt(f.length / 12f));
                    float bay = f.length / doors;
                    float u = 0;
                    for (int i = 0; i < doors; i++)
                    {
                        float c = (i + 0.5f) * bay;
                        WallStrip(f, u, c - doorW / 2, y0, y0 + doorH, wall);
                        Opening(f, c - doorW / 2, c + doorW / 2, y0, y0 + doorH, 0.25f, trim, art.metalDark);
                        u = c + doorW / 2;
                    }
                    WallStrip(f, u, f.length, y0, y0 + doorH, wall);
                    float wy0 = y1 - 2.0f, wy1 = y1 - 0.8f;
                    WallStrip(f, 0, f.length, y0 + doorH, wy0, wall);
                    WallStrip(f, 0, f.length, wy1, y1, wall);
                    int wins = Mathf.Max(1, Mathf.FloorToInt(f.length / 6f));
                    float wb = f.length / wins;
                    u = 0;
                    for (int i = 0; i < wins; i++)
                    {
                        float a = i * wb + 1.2f, e = (i + 1) * wb - 1.2f;
                        WallStrip(f, u, a, wy0, wy1, wall);
                        Opening(f, a, e, wy0, wy1, 0.1f, trim, art.glass);
                        u = e;
                    }
                    WallStrip(f, u, f.length, wy0, wy1, wall);
                    break;
                }
                default:
                    PunchedFloor(f, y0, y1, wall, trim, new Window { width = 1.3f, height = 1.6f, sill = 0.9f, inset = 0.22f, bay = 3.2f, mullion = true });
                    break;
            }
        }

        // ---------------------------------------------------------------- pieces

        void WallStrip(Face f, float u0, float u1, float y0, float y1, CityArt.Surface s)
        {
            if (u1 - u0 < 0.005f || y1 - y0 < 0.005f) return;
            var a = f.At(u0, y0, 0);
            var b = f.At(u0, y1, 0);
            var c = f.At(u1, y1, 0);
            var d = f.At(u1, y0, 0);
            // Continuous UVs across strips so brick courses line up around openings.
            float k0 = UCoord(f, u0), k1 = UCoord(f, u1);
            mb.Quad(s.material, a, b, c, d,
                new Vector2(k0 / s.size.x, y0 / s.size.y), new Vector2(k0 / s.size.x, y1 / s.size.y),
                new Vector2(k1 / s.size.x, y1 / s.size.y), new Vector2(k1 / s.size.x, y0 / s.size.y));
        }

        static float UCoord(Face f, float u) => Vector3.Dot(f.p0 + f.t * u, f.t);

        /// <summary>Window or door hole: four reveals and the pane set back by depth.</summary>
        void Opening(Face f, float u0, float u1, float y0, float y1, float depth, CityArt.Surface reveal, Material pane)
        {
            var s = reveal.size;
            // Jambs face inward toward the opening center.
            mb.Quad(reveal.material, f.At(u0, y0, 0), f.At(u0, y1, 0), f.At(u0, y1, -depth), f.At(u0, y0, -depth),
                new Vector2(0, y0 / s.y), new Vector2(0, y1 / s.y), new Vector2(depth / s.x, y1 / s.y), new Vector2(depth / s.x, y0 / s.y));
            mb.Quad(reveal.material, f.At(u1, y0, -depth), f.At(u1, y1, -depth), f.At(u1, y1, 0), f.At(u1, y0, 0),
                new Vector2(0, y0 / s.y), new Vector2(0, y1 / s.y), new Vector2(depth / s.x, y1 / s.y), new Vector2(depth / s.x, y0 / s.y));
            // Head faces down, sill faces up.
            mb.Quad(reveal.material, f.At(u0, y1, -depth), f.At(u0, y1, 0), f.At(u1, y1, 0), f.At(u1, y1, -depth),
                new Vector2(u0 / s.x, 0), new Vector2(u0 / s.x, depth / s.y), new Vector2(u1 / s.x, depth / s.y), new Vector2(u1 / s.x, 0));
            mb.Quad(reveal.material, f.At(u0, y0, 0), f.At(u0, y0, -depth), f.At(u1, y0, -depth), f.At(u1, y0, 0),
                new Vector2(u0 / s.x, 0), new Vector2(u0 / s.x, depth / s.y), new Vector2(u1 / s.x, depth / s.y), new Vector2(u1 / s.x, 0));
            float id = NextRoom();
            mb.Stamp = new Vector2(u1 - u0, y1 - y0);
            mb.Quad(pane, f.At(u0, y0, -depth), f.At(u0, y1, -depth), f.At(u1, y1, -depth), f.At(u1, y0, -depth),
                new Vector2(id, 0), new Vector2(id, 0.999f), new Vector2(id + 0.999f, 0.999f), new Vector2(id + 0.999f, 0));
            mb.Stamp = Vector2.zero;
        }

        int room;

        /// <summary>Integer room id carried in the pane UVs; the window shader hashes it per room.</summary>
        float NextRoom() => room = (room + 1) % 8191;

        void Mullion(Face f, float u, float y0, float y1, float depth, float halfWidth)
        {
            var c = f.At(u, (y0 + y1) / 2, -depth + 0.04f);
            mb.OrientedBox(art.windowFrame, Vector2.one, c, f.t, Vector3.up, f.n, new Vector3(0.035f, (y1 - y0) / 2, 0.04f),
                MeshBuilder.Faces.Front | MeshBuilder.Faces.Sides);
            // Transom bar a third of the way down, like real casement windows.
            var t = f.At(u, y1 - (y1 - y0) * 0.3f, -depth + 0.04f);
            mb.OrientedBox(art.windowFrame, Vector2.one, t, f.t, Vector3.up, f.n, new Vector3(halfWidth, 0.03f, 0.04f),
                MeshBuilder.Faces.Front | MeshBuilder.Faces.Top | MeshBuilder.Faces.Bottom);
        }

        void Ledge(Face f, float y0, float y1, float proud, CityArt.Surface s, float u0 = 0, float u1 = -1)
        {
            if (u1 < 0) u1 = f.length;
            var c = f.At((u0 + u1) / 2, (y0 + y1) / 2, proud / 2);
            mb.OrientedBox(s.material, s.size, c, f.t, Vector3.up, f.n, new Vector3((u1 - u0) / 2, (y1 - y0) / 2, proud / 2));
        }

        void Roof(Rect r, float y, CityArt.Surface trim, bool parapet)
        {
            var roof = art.Get("tarred_gravel");
            mb.Floor(roof.material, roof.size, r.xMin, r.yMin, r.xMax, r.yMax, y);
            if (!parapet) return;
            float h = 0.9f, t = 0.25f;
            mb.Box(trim.material, trim.size, new Vector3(r.xMin, y, r.yMin), new Vector3(r.xMax, y + h, r.yMin + t));
            mb.Box(trim.material, trim.size, new Vector3(r.xMin, y, r.yMax - t), new Vector3(r.xMax, y + h, r.yMax));
            mb.Box(trim.material, trim.size, new Vector3(r.xMin, y, r.yMin + t), new Vector3(r.xMin + t, y + h, r.yMax - t));
            mb.Box(trim.material, trim.size, new Vector3(r.xMax - t, y, r.yMin + t), new Vector3(r.xMax, y + h, r.yMax - t));
        }

        void RoofDetails(Building b, Rect r, float y, CityArt.Surface wall, CityArt.Surface trim, System.Random rng)
        {
            if (r.width < 9 || r.height < 9) return;
            // Stair/elevator penthouse clad like the facade, with its own gravel roof, coping and a door.
            float w = Mathf.Min(7f, r.width * 0.32f), d = Mathf.Min(5.5f, r.height * 0.32f), h = 3.2f;
            float cx = r.center.x + (float)(rng.NextDouble() - 0.5) * (r.width - w - 3) * 0.5f;
            float cz = r.center.y + (float)(rng.NextDouble() - 0.5) * (r.height - d - 3) * 0.5f;
            var pr = new Rect(cx - w / 2, cz - d / 2, w, d);
            var cladding = b.style == FacadeStyle.Curtain ? trim : wall;
            foreach (var f in Faces(pr)) WallStrip(f, 0, f.length, y, y + h, cladding);
            var door = Faces(pr)[0];
            Opening(door, door.length / 2 - 0.5f, door.length / 2 + 0.5f, y, y + 2.1f, 0.08f, trim, art.metalDark);
            Roof(pr, y + h, trim, false);
            Ledge(Faces(pr)[0], y + h - 0.25f, y + h, 0.12f, trim);
            Ledge(Faces(pr)[2], y + h - 0.25f, y + h, 0.12f, trim);

            // Condenser units and a water tank on bigger roofs.
            int units = Mathf.Clamp(Mathf.FloorToInt(r.width * r.height / 120f), 1, 6);
            for (int i = 0; i < units; i++)
            {
                float ux = Mathf.Lerp(r.xMin + 2f, r.xMax - 2f, (float)rng.NextDouble());
                float uz = Mathf.Lerp(r.yMin + 2f, r.yMax - 2f, (float)rng.NextDouble());
                if (pr.Contains(new Vector2(ux, uz))) continue;
                mb.Box(art.metalDark, Vector2.one, new Vector3(ux - 0.7f, y, uz - 0.5f), new Vector3(ux + 0.7f, y + 1.1f, uz + 0.5f));
                mb.Box(trim.material, trim.size, new Vector3(ux - 0.75f, y + 1.1f, uz - 0.55f), new Vector3(ux + 0.75f, y + 1.18f, uz + 0.55f));
            }
            if (b.floors >= 6 && rng.NextDouble() < 0.6)
            {
                var tank = new Vector3(pr.xMax + 2.2f < r.xMax - 1 ? pr.xMax + 2.2f : pr.xMin - 2.2f, y, cz);
                for (int k = 0; k < 4; k++)
                    mb.Box(art.metalDark, Vector2.one, tank + new Vector3((k & 1) * 2 - 1, 0, (k >> 1) * 2 - 1) * 0.9f + new Vector3(-0.08f, 0, -0.08f),
                        tank + new Vector3((k & 1) * 2 - 1, 0, (k >> 1) * 2 - 1) * 0.9f + new Vector3(0.08f, 2f, 0.08f));
                var wood = art.Get("wood_floor_deck") ?? trim;
                mb.Cylinder(wood.material, wood.size, tank + Vector3.up * 2f, 1.3f, 2.6f, 14);
                mb.Floor(trim.material, trim.size, tank.x - 1.3f, tank.z - 1.3f, tank.x + 1.3f, tank.z + 1.3f, y + 4.6f);
            }
        }

        void HipRoof(Rect r, float y, CityArt.Surface trim, System.Random rng)
        {
            var tiles = art.Get("clay_roof_tiles_02");
            float o = 0.55f;
            float x0 = r.xMin - o, x1 = r.xMax + o, z0 = r.yMin - o, z1 = r.yMax + o;
            bool alongX = x1 - x0 >= z1 - z0;
            float half = (alongX ? z1 - z0 : x1 - x0) * 0.5f, h = half * 0.55f;
            var center = new Vector3((x0 + x1) / 2, y, (z0 + z1) / 2);
            Vector3 r0, r1;
            if (alongX) { r0 = new Vector3(x0 + half, y + h, center.z); r1 = new Vector3(x1 - half, y + h, center.z); }
            else { r0 = new Vector3(center.x, y + h, z0 + half); r1 = new Vector3(center.x, y + h, z1 - half); }
            Vector3 c00 = new Vector3(x0, y, z0), c10 = new Vector3(x1, y, z0), c11 = new Vector3(x1, y, z1), c01 = new Vector3(x0, y, z1);
            if (alongX)
            {
                Slope(tiles, c00, r0, r1, c10, center);
                Slope(tiles, c11, r1, r0, c01, center);
                Slope(tiles, c01, r0, r0, c00, center);
                Slope(tiles, c10, r1, r1, c11, center);
            }
            else
            {
                Slope(tiles, c10, r0, r1, c11, center);
                Slope(tiles, c01, r1, r0, c00, center);
                Slope(tiles, c00, r0, r0, c10, center);
                Slope(tiles, c11, r1, r1, c01, center);
            }
            // Soffit under the eaves and a ridge cap.
            mb.Floor(trim.material, trim.size, x0, z0, x1, z1, y - 0.02f, true);
            var ridge = r1 - r0;
            if (ridge.magnitude > 0.1f)
                mb.OrientedBox(trim.material, trim.size, (r0 + r1) / 2 + Vector3.up * 0.06f, Vector3.Cross(Vector3.up, ridge.normalized), Vector3.up, ridge.normalized, new Vector3(0.14f, 0.08f, ridge.magnitude / 2 + 0.1f));
            // Chimney.
            float cx = Mathf.Lerp(r.xMin + 1.5f, r.xMax - 1.5f, (float)rng.NextDouble());
            float cz = Mathf.Lerp(r.yMin + 1.5f, r.yMax - 1.5f, (float)rng.NextDouble());
            var brick = art.Get("white_sandstone_bricks") ?? trim;
            mb.Box(brick.material, brick.size, new Vector3(cx - 0.45f, y, cz - 0.45f), new Vector3(cx + 0.45f, y + h + 1.1f, cz + 0.45f));
        }

        /// <summary>Roof plane from eave a→d up to ridge points b, c (b == c for hip ends); faces away from the roof center.</summary>
        void Slope(CityArt.Surface s, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 center)
        {
            var eave = (d - a).normalized;
            var mid = (a + d) * 0.5f;
            var up = b + c - a - d;
            var slopeDir = (up - eave * Vector3.Dot(up, eave)).normalized;
            Vector2 UV(Vector3 p) => new Vector2(Vector3.Dot(p - a, eave) / s.size.x, Vector3.Dot(p - a, slopeDir) / s.size.y);
            var n = Vector3.Cross(b - a, d - a);
            if (Vector3.Dot(n, mid - center) < 0) { var t = a; a = d; d = t; t = b; b = c; c = t; }
            mb.Quad(s.material, a, b, c, d, UV(a), UV(b), UV(c), UV(d));
        }

        void Balcony(Face f, float y, CityArt.Surface wall, CityArt.Surface trim)
        {
            float depth = 1.35f, margin = 0.7f;
            float len = f.length - margin * 2;
            var slab = f.At(f.length / 2, y + 0.09f, depth / 2);
            mb.OrientedBox(trim.material, trim.size, slab, f.t, Vector3.up, f.n, new Vector3(len / 2, 0.09f, depth / 2));
            var front = f.At(f.length / 2, y + 0.18f + 0.5f, depth - 0.06f);
            mb.OrientedBox(wall.material, wall.size, front, f.t, Vector3.up, f.n, new Vector3(len / 2, 0.5f, 0.06f));
            mb.OrientedBox(trim.material, trim.size, front + Vector3.up * 0.53f, f.t, Vector3.up, f.n, new Vector3(len / 2 + 0.02f, 0.035f, 0.09f));
            // Side walls and party walls every few bays so each unit reads separately.
            int units = Mathf.Max(1, Mathf.RoundToInt(len / 7.2f));
            for (int i = 0; i <= units; i++)
            {
                var c = f.At(margin + len * i / units, y + 0.18f + 0.5f, depth / 2);
                mb.OrientedBox(wall.material, wall.size, c, f.t, Vector3.up, f.n, new Vector3(0.06f, 0.5f, depth / 2));
            }
        }

        void Awning(Face f, float u0, float u1, float y, float reach, Material cloth)
        {
            if (u1 - u0 < 0.6f) return;
            float drop = reach * 0.38f;
            Vector3 a = f.At(u0, y, 0.02f), b = f.At(u0, y - drop, reach), c = f.At(u1, y - drop, reach), d = f.At(u1, y, 0.02f);
            var size = new Vector2(1.2f, 1.2f);
            Vector2 U(float u, float v) => new Vector2(u / size.x, v / size.y);
            // Top and underside, then the scalloped valance.
            mb.Quad(cloth, a, d, c, b, U(u0, 0), U(u1, 0), U(u1, reach), U(u0, reach));
            mb.Quad(cloth, a, b, c, d, U(u0, 0), U(u0, reach), U(u1, reach), U(u1, 0));
            Vector3 b2 = b + Vector3.down * 0.28f, c2 = c + Vector3.down * 0.28f;
            mb.Quad(cloth, b2, b, c, c2, U(u0, 0), U(u0, 0.28f), U(u1, 0.28f), U(u1, 0));
            mb.Quad(cloth, c2, c, b, b2, U(u1, 0), U(u1, 0.28f), U(u0, 0.28f), U(u0, 0));
        }
    }
}
