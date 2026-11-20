using System.Collections.Generic;
using UnityEngine;

namespace Ashline.City
{
    public enum District { Hills, Park, Beach, Palmside, Downtown, Docks }

    public enum RoadKind { Highway, Avenue, Street, Lane, Pier }

    public enum Coast { Beach, Seawall }

    public class RoadDef
    {
        public string name;
        public RoadKind kind;
        public List<Vector2> ctrl;
        public bool closed;

        public float HalfWidth => kind switch
        {
            RoadKind.Highway => 8.5f,
            RoadKind.Avenue => 7f,
            RoadKind.Street => 5f,
            RoadKind.Lane => 3.6f,
            _ => 5f,
        };

        public float Sidewalk => kind switch
        {
            RoadKind.Highway => 2.2f,
            RoadKind.Avenue => 4f,
            RoadKind.Street => 3.5f,
            _ => 0f,
        };

        public int LanesPerSide => kind == RoadKind.Highway || kind == RoadKind.Avenue ? 2 : 1;
    }

    public class IslandDef
    {
        public string name;
        public List<Vector2> coast;     // smoothed, counter-clockwise
        public List<Vector2> inner;     // coast pulled inland by the beach/seawall band
        public Coast kind;
        public float band;
        public string ground;
    }

    public class Landmark
    {
        public string id;
        public Vector2 pos;
        public float facing;            // yaw in degrees the front faces
    }

    /// <summary>
    /// Hand-authored island map inspired by ROB RUN's Looty Bay: a western island with hillside
    /// mansions, a park and the Sunny Beach strip; a small Palmside island with the police station
    /// and hospital; a dense Downtown on the eastern island; a highway crossing all three on bridges.
    /// Control points are authored in reference-map pixels and converted to meters.
    /// </summary>
    public class IslandPlan
    {
        public const float Scale = 1.1f;
        public const float CurbHeight = 0.15f;
        public const float LaneWidth = 3.5f;
        public const float WaterLevel = -1.1f;

        public readonly List<IslandDef> islands = new List<IslandDef>();
        public readonly List<RoadDef> roads = new List<RoadDef>();
        public readonly List<Landmark> landmarks = new List<Landmark>();
        public readonly List<List<Vector2>> ponds = new List<List<Vector2>>();
        public readonly List<List<Vector2>> parkPaths = new List<List<Vector2>>();

        public static Vector2 P(float x, float y) => new Vector2((x - 512f) * Scale, (392f - y) * Scale);

        static List<Vector2> L(params float[] xy)
        {
            var l = new List<Vector2>(xy.Length / 2);
            for (int i = 0; i + 1 < xy.Length; i += 2) l.Add(P(xy[i], xy[i + 1]));
            return l;
        }

        public IslandPlan()
        {
            Island("West", Coast.Beach, 24f, "leafy_grass", L(
                150, 230, 180, 205, 200, 125, 300, 78, 400, 92, 470, 80, 540, 105, 555, 160, 530, 200,
                515, 250, 500, 300, 480, 360, 466, 450, 466, 520, 470, 580, 478, 620, 450, 650, 405, 668,
                375, 700, 330, 700, 300, 668, 250, 655, 195, 645, 158, 605, 160, 560, 176, 470, 186, 400,
                160, 320));
            Island("Palmside", Coast.Beach, 18f, "leafy_grass", L(
                478, 440, 505, 395, 560, 385, 610, 400, 660, 420, 675, 470, 668, 560, 662, 620, 640, 670,
                590, 702, 540, 702, 495, 668, 478, 600, 470, 520));
            Island("Downtown", Coast.Seawall, 0f, "concrete_pavement_02", L(
                685, 290, 695, 250, 735, 205, 800, 190, 870, 186, 935, 200, 965, 250, 975, 300, 978, 400,
                975, 560, 985, 610, 972, 682, 940, 702, 760, 706, 700, 692, 692, 600, 688, 480, 692, 330));

            // Main highway crossing every island and leaving the map at both ends.
            Road("Highway", RoadKind.Highway, false, -90, 600, 140, 604, 300, 599, 470, 605, 600, 597, 760, 600, 1000, 593, 1120, 596);

            // West island.
            Road("Ring West", RoadKind.Avenue, false,
                436, 599, 432, 550, 428, 500, 431, 440, 442, 392, 460, 345, 472, 300, 462, 262, 432, 238, 380, 234,
                300, 236, 258, 240, 232, 262, 222, 300, 232, 345, 262, 385, 270, 420, 258, 455, 236, 495, 218, 540, 208, 600);
            Road("Loraine Loop", RoadKind.Street, false, 276, 236, 274, 190, 276, 152, 292, 138, 360, 133, 420, 138, 434, 152, 434, 190, 432, 237);
            Road("Loraine Cross", RoadKind.Street, false, 352, 235, 350, 186, 355, 134);
            Road("Hills Lane", RoadKind.Lane, false, 275, 188, 312, 192, 350, 186, 392, 182, 434, 188);
            Road("Cliff Drive", RoadKind.Lane, false, 434, 168, 462, 172, 480, 196, 488, 226, 478, 258, 466, 285);
            Road("Sunset Drive", RoadKind.Lane, false, 276, 168, 248, 162, 226, 186, 220, 222, 236, 252);
            Road("Plaza Circle", RoadKind.Street, true, Circle(338, 455, 30, 14));
            Road("Park Road", RoadKind.Street, false, 338, 425, 334, 380, 320, 322, 308, 278, 310, 237);
            Road("Gallery Spur", RoadKind.Street, false, 308, 456, 285, 450, 266, 438);
            Road("Stadium Way", RoadKind.Street, false, 368, 455, 400, 452, 432, 446);
            Road("Ocean Drive", RoadKind.Avenue, false, 338, 485, 341, 540, 344, 601);
            Road("Palm Diagonal", RoadKind.Street, false, 317, 477, 292, 503, 256, 534, 222, 541);
            Road("Beach Boulevard", RoadKind.Street, false, 228, 518, 300, 523, 342, 520, 429, 520);
            Road("Sunny Street", RoadKind.Street, false, 213, 568, 280, 566, 342, 565, 433, 566);
            Road("Pier Loop", RoadKind.Street, false, 296, 600, 294, 628, 318, 650, 372, 652, 396, 632, 398, 601);
            Road("South Pier", RoadKind.Pier, false, 345, 652, 346, 700, 347, 745);

            // North bridge to Downtown and the north-east exit.
            Road("North Bridge", RoadKind.Avenue, false, 470, 306, 520, 312, 600, 318, 690, 323, 716, 326);
            Road("North-East Exit", RoadKind.Highway, false, 944, 300, 1000, 282, 1060, 268, 1120, 262);

            // Palmside.
            Road("Palmside Avenue", RoadKind.Street, false, 548, 598, 546, 540, 540, 470, 530, 425);
            Road("Harbor Street", RoadKind.Street, false, 488, 520, 546, 521, 600, 516, 640, 508);
            Road("Gallery Lane", RoadKind.Lane, false, 600, 516, 626, 556, 616, 598);
            Road("Palmside Pier", RoadKind.Pier, false, 560, 660, 561, 700, 562, 745);

            // Downtown.
            Road("Downtown Ring", RoadKind.Avenue, true,
                722, 262, 745, 240, 800, 232, 870, 232, 920, 250, 942, 290, 946, 360, 944, 450, 946, 540,
                944, 640, 930, 668, 860, 672, 780, 672, 735, 666, 720, 640, 716, 560, 714, 460, 716, 360, 714, 300);
            Road("Bank Street", RoadKind.Street, false, 775, 232, 772, 330, 778, 420, 771, 510, 776, 598, 774, 672);
            Road("Main Street", RoadKind.Street, false, 860, 232, 862, 330, 857, 420, 862, 510, 858, 598, 860, 672);
            Road("North Avenue", RoadKind.Street, false, 716, 328, 800, 332, 860, 330, 945, 320);
            Road("Central Avenue", RoadKind.Avenue, false, 715, 420, 800, 422, 860, 419, 945, 420);
            Road("Market Street", RoadKind.Street, false, 715, 510, 800, 508, 860, 512, 945, 510);
            Road("Dock Pier", RoadKind.Pier, false, 800, 232, 792, 205, 770, 170, 748, 150);

            Landmark("police", 505, 470, 90);
            Landmark("hospital", 615, 465, 270);
            Landmark("gallery", 585, 640, 0);
            Landmark("bank", 818, 282, 180);
            Landmark("ferris", 347, 742, 90);
            Landmark("stadium", 338, 455, 0);

            ponds.Add(Blob(360, 320, 26, 18, 7));
            parkPaths.Add(Blob(360, 320, 40, 30, 9));
            parkPaths.Add(new List<Vector2> { P(270, 330), P(300, 335), P(328, 322) });
            parkPaths.Add(new List<Vector2> { P(392, 330), P(420, 340), P(450, 352) });
        }

        void Island(string name, Coast kind, float band, string ground, List<Vector2> rough)
        {
            var coast = Geo2D.Chaikin(rough, 4, true);
            if (Geo2D.SignedArea(coast) < 0) coast.Reverse();
            var inner = kind == Coast.Beach ? Geo2D.OffsetLoop(coast, band) : new List<Vector2>(coast);
            islands.Add(new IslandDef { name = name, coast = coast, inner = inner, kind = kind, band = band, ground = ground });
        }

        void Road(string name, RoadKind kind, bool closed, params float[] xy)
        {
            roads.Add(new RoadDef { name = name, kind = kind, closed = closed, ctrl = L(xy) });
        }

        void Landmark(string id, float x, float y, float facing)
        {
            landmarks.Add(new Landmark { id = id, pos = P(x, y), facing = facing });
        }

        static float[] Circle(float cx, float cy, float r, int n)
        {
            var xy = new float[n * 2];
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2 / n;
                xy[i * 2] = cx + Mathf.Cos(a) * r;
                xy[i * 2 + 1] = cy + Mathf.Sin(a) * r;
            }
            return xy;
        }

        static List<Vector2> Blob(float cx, float cy, float rx, float ry, int seed)
        {
            var rng = new System.Random(seed);
            var pts = new List<Vector2>();
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2 / 12;
                float k = 0.85f + (float)rng.NextDouble() * 0.3f;
                pts.Add(P(cx + Mathf.Cos(a) * rx * k, cy - Mathf.Sin(a) * ry * k));
            }
            return Geo2D.Chaikin(pts, 3, true);
        }

        // ------------------------------------------------------------------ queries

        public IslandDef IslandAt(Vector2 p)
        {
            foreach (var i in islands) if (Geo2D.PointInPolygon(p, i.coast)) return i;
            return null;
        }

        /// <summary>Ground height: 0 on land, sloping beach down into the water, sea floor offshore.</summary>
        public float TerrainHeight(Vector2 p)
        {
            foreach (var i in islands)
            {
                if (!Geo2D.PointInPolygon(p, i.coast)) continue;
                if (i.kind == Coast.Seawall || Geo2D.PointInPolygon(p, i.inner)) return 0f;
                float d = Geo2D.DistanceToLoop(p, i.coast);
                return Mathf.Lerp(-1.9f, 0f, Mathf.Clamp01(d / i.band));
            }
            return -6f;
        }

        public bool OnSolidLand(Vector2 p)
        {
            foreach (var i in islands) if (Geo2D.PointInPolygon(p, i.inner)) return true;
            return false;
        }

        public District DistrictAt(Vector2 p)
        {
            var isl = IslandAt(p);
            float px = p.x / Scale + 512f, py = 392f - p.y / Scale;
            if (isl == null) return District.Docks;
            switch (isl.name)
            {
                case "West":
                    if (py < 242) return District.Hills;
                    if (py < 425) return District.Park;
                    return District.Beach;
                case "Palmside":
                    return District.Palmside;
                default:
                    return py < 236 || px > 950 ? District.Docks : District.Downtown;
            }
        }
    }
}
