using UnityEngine;

namespace Ashline.City
{
    /// <summary>Pier Ferris wheel: static A-frame supports, a slowly turning rim and gondolas that stay upright.</summary>
    public class FerrisWheel : MonoBehaviour
    {
        [SerializeField] float degreesPerSecond = 3.5f;

        Transform wheel;
        Transform[] cabins;
        float radius, hubHeight, angle;

        public void Build(CityArt art, float r)
        {
            radius = r;
            hubHeight = r + 3.2f;
            var white = art.paintWhite;
            var steel = new MeshBuilder();
            // Two A-frames either side of the wheel plane (local z is the axle).
            for (int side = -1; side <= 1; side += 2)
            for (int leg = -1; leg <= 1; leg += 2)
            {
                var foot = new Vector3(leg * r * 0.55f, 0, side * 3.2f);
                var top = new Vector3(0, hubHeight, side * 1.9f);
                Beam(steel, white, foot, top, 0.28f);
            }
            Beam(steel, white, new Vector3(0, hubHeight, -2.2f), new Vector3(0, hubHeight, 2.2f), 0.45f);
            steel.Box(art.metalDark, Vector2.one, new Vector3(-r * 0.6f, 0, -3.8f), new Vector3(r * 0.6f, 0.35f, 3.8f));
            steel.Build("Supports", transform);

            var rim = new MeshBuilder();
            const int segs = 48, spokes = 16;
            for (int ring = -1; ring <= 1; ring += 2)
            {
                float z = ring * 1.25f;
                for (int i = 0; i < segs; i++)
                {
                    float a0 = i * Mathf.PI * 2 / segs, a1 = (i + 1) * Mathf.PI * 2 / segs;
                    Beam(rim, white, new Vector3(Mathf.Cos(a0) * r, Mathf.Sin(a0) * r, z), new Vector3(Mathf.Cos(a1) * r, Mathf.Sin(a1) * r, z), 0.18f);
                    if (i % 2 == 0)
                    {
                        var bulb = new Vector3(Mathf.Cos(a0) * (r + 0.2f), Mathf.Sin(a0) * (r + 0.2f), z);
                        rim.Box(art.glowWhite, Vector2.one, bulb - Vector3.one * 0.09f, bulb + Vector3.one * 0.09f);
                    }
                }
                for (int i = 0; i < spokes; i++)
                {
                    float a = i * Mathf.PI * 2 / spokes;
                    Beam(rim, white, new Vector3(0, 0, z * 0.6f), new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z), 0.07f);
                }
            }
            for (int i = 0; i < spokes; i++)
            {
                float a = i * Mathf.PI * 2 / spokes;
                var p = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0);
                Beam(rim, white, p + Vector3.back * 1.25f, p + Vector3.forward * 1.25f, 0.1f);
            }
            var w = rim.Build("Wheel", transform);
            w.isStatic = false;
            wheel = w.transform;
            wheel.localPosition = Vector3.up * hubHeight;

            cabins = new Transform[spokes];
            for (int i = 0; i < spokes; i++)
            {
                var cab = new MeshBuilder();
                var paint = art.awnings != null && art.awnings.Length > 0 ? art.awnings[i % art.awnings.Length] : white;
                cab.Box(paint, Vector2.one, new Vector3(-1.0f, -2.3f, -0.8f), new Vector3(1.0f, -1.3f, 0.8f), true);
                cab.Box(art.glass, Vector2.one, new Vector3(-0.95f, -1.3f, -0.75f), new Vector3(0.95f, -0.55f, 0.75f));
                cab.Box(paint, Vector2.one, new Vector3(-1.1f, -0.55f, -0.9f), new Vector3(1.1f, -0.4f, 0.9f));
                cab.Box(white, Vector2.one, new Vector3(-0.05f, -0.4f, -0.05f), new Vector3(0.05f, 0f, 0.05f));
                var go = cab.Build("Gondola", transform);
                go.isStatic = false;
                cabins[i] = go.transform;
            }
            Place();
        }

        static void Beam(MeshBuilder mb, Material m, Vector3 a, Vector3 b, float thickness)
        {
            var dir = b - a;
            float len = dir.magnitude;
            if (len < 1e-3f) return;
            dir /= len;
            var side = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
            var up = Vector3.Cross(side, dir);
            mb.OrientedBox(m, Vector2.one, (a + b) * 0.5f, side, up, dir, new Vector3(thickness / 2, thickness / 2, len / 2));
        }

        void Update()
        {
            if (wheel == null) return;
            angle += degreesPerSecond * Time.deltaTime;
            Place();
        }

        void Place()
        {
            wheel.localRotation = Quaternion.Euler(0, 0, angle);
            for (int i = 0; i < cabins.Length; i++)
            {
                float a = (angle * Mathf.Deg2Rad) + i * Mathf.PI * 2 / cabins.Length;
                cabins[i].localPosition = new Vector3(Mathf.Cos(a) * radius, hubHeight + Mathf.Sin(a) * radius, 0);
                cabins[i].localRotation = Quaternion.Euler(0, 0, Mathf.Sin(Time.time * 0.7f + i) * 2f);
            }
        }
    }
}
