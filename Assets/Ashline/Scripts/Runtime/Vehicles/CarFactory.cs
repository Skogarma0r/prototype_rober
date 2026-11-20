using System.Collections.Generic;
using UnityEngine;

namespace Ashline.Vehicles
{
    /// <summary>
    /// Assembles a drivable car from any model with four wheel nodes named "wheel…" (a mesh or a group).
    /// The model is sat on its wheels; optionally rescaled to a real-world length.
    /// </summary>
    public static class CarFactory
    {
        public class Options
        {
            /// <summary>Target length in meters; 0 keeps the model's own scale.</summary>
            public float length;
            /// <summary>Squash the body to this width when wider (toy-proportioned models); 0 disables.</summary>
            public float maxWidth;
            public Material paint;
            public float mass = 1350f;
        }

        public static CarController Create(GameObject model, Vector3 position, Quaternion rotation, Options o = null)
        {
            o ??= new Options();
            var root = new GameObject(model.name);
            // Place before adding the interpolated Rigidbody, which would otherwise overwrite the transform.
            root.transform.SetPositionAndRotation(position + rotation * Vector3.up * 0.25f, rotation);
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = o.mass;
            rb.linearDamping = 0.02f;
            rb.angularDamping = 0.35f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var view = Object.Instantiate(model, root.transform);
            view.name = "View";
            view.transform.localPosition = Vector3.zero;
            view.transform.localRotation = model.transform.localRotation;

            var wheels = new List<Transform>();
            foreach (var t in view.GetComponentsInChildren<Transform>())
                if (IsWheel(t) && !IsWheel(t.parent) && t.GetComponentInChildren<Renderer>() != null) wheels.Add(t);

            var all = LocalBounds(root.transform, view.GetComponentsInChildren<Renderer>());
            float scale = o.length > 0f ? o.length / Mathf.Max(all.size.z, 0.01f) : 1f;
            float squash = o.maxWidth > 0f ? Mathf.Min(1f, o.maxWidth / Mathf.Max(all.size.x * scale, 0.01f)) : 1f;
            view.transform.localScale = Vector3.Scale(view.transform.localScale, new Vector3(scale * squash, scale, scale));
            all = LocalBounds(root.transform, view.GetComponentsInChildren<Renderer>());
            view.transform.localPosition -= new Vector3(all.center.x, all.min.y, all.center.z);

            var wheelRenderers = new HashSet<Renderer>();
            foreach (var w in wheels)
                foreach (var r in w.GetComponentsInChildren<Renderer>()) wheelRenderers.Add(r);

            if (o.paint != null)
                foreach (var r in view.GetComponentsInChildren<Renderer>())
                    if (!wheelRenderers.Contains(r)) r.sharedMaterial = o.paint;

            var car = root.AddComponent<CarController>();
            var pivots = new Transform[4];
            var knuckles = new Transform[4];
            float radius = 0.34f;
            foreach (var w in wheels)
            {
                Straighten(w, root.transform);
                var wb = WorldBounds(w);
                var p = root.transform.InverseTransformPoint(wb.center);
                radius = wb.extents.y;
                int slot = (p.z > 0 ? 0 : 2) + (p.x < 0 ? 0 : 1);
                pivots[slot] = Repivot(w, root.transform, wb.center, out knuckles[slot]);
            }
            car.wheelRadius = radius;
            if (wheels.Count == 4 && pivots[0] && pivots[1] && pivots[2] && pivots[3])
                car.SetupWheels(pivots, knuckles);
            else Debug.LogError($"{model.name}: expected four wheel nodes, found {wheels.Count}");

            var bodyRenderers = new List<Renderer>();
            foreach (var r in view.GetComponentsInChildren<Renderer>())
                if (!wheelRenderers.Contains(r)) bodyRenderers.Add(r);
            var body = LocalBounds(root.transform, bodyRenderers.ToArray());
            var bodyMaterial = new PhysicsMaterial("CarBody")
            {
                dynamicFriction = 0.15f, staticFriction = 0.2f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounciness = 0.05f,
            };
            float width = Mathf.Min(body.size.x, 2.0f);
            float zFront = -1e9f, zRear = 1e9f, axleY = radius;
            foreach (var w in pivots)
            {
                if (!w) continue;
                var lp = root.transform.InverseTransformPoint(w.position);
                zFront = Mathf.Max(zFront, lp.z); zRear = Mathf.Min(zRear, lp.z); axleY = lp.y;
            }
            if (zFront < zRear) { zFront = body.max.z; zRear = body.min.z; }
            // The floor between the axles sits low; the bumpers ride higher so the car can climb a curb on its springs.
            void Box(float bottom, float z0, float z1)
            {
                var col = root.AddComponent<BoxCollider>();
                col.center = new Vector3(body.center.x, (bottom + body.max.y) / 2f, (z0 + z1) / 2f);
                col.size = new Vector3(width, body.max.y - bottom, z1 - z0);
                col.sharedMaterial = bodyMaterial;
            }
            Box(Mathf.Max(body.min.y, axleY - radius * 0.3f), zRear, zFront);
            Box(Mathf.Max(body.min.y, axleY + radius * 0.1f), body.min.z + body.size.z * 0.01f, body.max.z - body.size.z * 0.01f);

            rb.centerOfMass = new Vector3(0f, radius * 0.95f, 0.08f);
            SetupLods(root, wheels);
            return car;
        }

        /// <summary>
        /// Showroom models are 200k+ triangles; most of that is interior, wipers, rims and brakes that vanish
        /// a few car lengths away. LODs reuse the same renderers, dropping small and hidden parts with distance.
        /// </summary>
        static void SetupLods(GameObject root, List<Transform> wheels)
        {
            var wheelParts = new HashSet<Renderer>();
            var mid = new List<Renderer>();
            var far = new List<Renderer>();
            foreach (var w in wheels)
            {
                Renderer tire = null;
                foreach (var r in w.GetComponentsInChildren<Renderer>())
                {
                    wheelParts.Add(r);
                    string n = r.name.ToLowerInvariant();
                    if (n.Contains("tire") || n.Contains("tyre")) tire = r;
                    else if (tire == null || (!tire.name.ToLowerInvariant().Contains("tire") && r.bounds.size.sqrMagnitude > tire.bounds.size.sqrMagnitude)) tire = r;
                }
                if (tire) { mid.Add(tire); far.Add(tire); }
            }
            var all = root.GetComponentsInChildren<Renderer>();
            foreach (var r in all)
            {
                string n = r.name.ToLowerInvariant();
                bool hidden = n.Contains("interior") || n.Contains("wiper") || n.Contains("brake") || n.Contains("engine");
                float size = r.bounds.size.magnitude;
                if (hidden || size < 0.6f) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (wheelParts.Contains(r))
                {
                    if (!n.Contains("brake") && !mid.Contains(r)) mid.Add(r);
                    continue;
                }
                if (hidden) continue;
                if (size >= 0.6f) mid.Add(r);
                if (size >= 2.5f) far.Add(r);
            }
            var lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[]
            {
                new LOD(0.3f, all),
                new LOD(0.1f, mid.ToArray()),
                new LOD(0.012f, far.ToArray()),
            });
            lod.RecalculateBounds();
        }

        /// <summary>
        /// Models often ship with the front wheels posed mid-turn. Finds the axle as the horizontal direction of
        /// least spread in the tire's vertices and yaws the wheel so the axle lines up with the car's right axis;
        /// otherwise spinning about that axis makes the wheel wobble in a figure eight.
        /// </summary>
        static void Straighten(Transform wheel, Transform root)
        {
            var pts = new List<Vector3>();
            foreach (var mf in wheel.GetComponentsInChildren<MeshFilter>())
            {
                if (IsStatic(mf.transform) || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                var v = mf.sharedMesh.vertices;
                for (int i = 0; i < v.Length; i += 2) pts.Add(root.InverseTransformPoint(mf.transform.TransformPoint(v[i])));
            }
            if (pts.Count < 16) return;
            var c = Vector3.zero;
            foreach (var p in pts) c += p;
            c /= pts.Count;
            float xx = 0, xz = 0, zz = 0;
            foreach (var p in pts)
            {
                var d = p - c;
                xx += d.x * d.x; xz += d.x * d.z; zz += d.z * d.z;
            }
            // Smallest-variance direction of the 2×2 horizontal covariance.
            float angle = 0.5f * Mathf.Atan2(2f * xz, xx - zz);
            var major = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            var axle = new Vector3(-major.z, 0, major.x);
            float spreadMajor = xx * major.x * major.x + 2 * xz * major.x * major.z + zz * major.z * major.z;
            float spreadAxle = xx * axle.x * axle.x + 2 * xz * axle.x * axle.z + zz * axle.z * axle.z;
            if (spreadMajor < spreadAxle) axle = major;
            if (axle.x < 0) axle = -axle;
            float yaw = Vector3.SignedAngle(axle, Vector3.right, Vector3.up);
            if (Mathf.Abs(yaw) < 0.5f) return;
            wheel.RotateAround(root.TransformPoint(c), root.up, yaw);
        }

        static bool IsWheel(Transform t) => t != null && t.name.ToLowerInvariant().StartsWith("wheel");

        static bool IsStatic(Transform t)
        {
            var n = t.name.ToLowerInvariant();
            return n.Contains("brakepad") || n.Contains("caliper");
        }

        /// <summary>
        /// Wraps a wheel in a pivot at its visual center so it can spin in place, and moves brake calipers
        /// onto a separate knuckle that only steers. Both hang off the unscaled root: the view may be scaled
        /// non-uniformly, which would shear a spinning child.
        /// </summary>
        static Transform Repivot(Transform wheel, Transform root, Vector3 center, out Transform knuckle)
        {
            var pivot = new GameObject(wheel.name + "_pivot").transform;
            pivot.SetParent(root, false);
            pivot.SetPositionAndRotation(center, root.rotation);

            knuckle = null;
            foreach (var t in wheel.GetComponentsInChildren<Transform>())
            {
                if (t == wheel || !IsStatic(t)) continue;
                if (knuckle == null)
                {
                    knuckle = new GameObject(wheel.name + "_knuckle").transform;
                    knuckle.SetParent(root, false);
                    knuckle.SetPositionAndRotation(center, root.rotation);
                }
                t.SetParent(knuckle, true);
            }
            wheel.SetParent(pivot, true);
            return pivot;
        }

        /// <summary>
        /// Tight world bounds of the rotating parts only (calipers would offset the spin axis). Uses vertices
        /// when readable: wheel meshes are often rotated inside their nodes, which inflates renderer AABBs.
        /// </summary>
        static Bounds WorldBounds(Transform t)
        {
            bool any = false;
            var b = new Bounds();
            void Add(Vector3 p)
            {
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                else b.Encapsulate(p);
            }
            foreach (var mf in t.GetComponentsInChildren<MeshFilter>())
            {
                if (IsStatic(mf.transform) || mf.sharedMesh == null) continue;
                var m = mf.sharedMesh;
                if (m.isReadable)
                {
                    var verts = m.vertices;
                    for (int i = 0; i < verts.Length; i += 3) Add(mf.transform.TransformPoint(verts[i]));
                }
                else if (mf.TryGetComponent<Renderer>(out var r))
                {
                    Add(r.bounds.min);
                    Add(r.bounds.max);
                }
            }
            return b;
        }

        static Bounds LocalBounds(Transform space, Renderer[] renderers)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var r in renderers)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, ((i & 4) >> 1) - 1));
                    var p = space.InverseTransformPoint(r.transform.TransformPoint(c));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }
    }
}
