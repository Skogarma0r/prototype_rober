using System.Collections.Generic;
using Ashline.Characters;
using Ashline.City;
using Ashline.Vehicles;
using UnityEngine;

namespace Ashline.Ambient
{
    /// <summary>
    /// Sidewalk walker: follows the sidewalk of its road edge, picks a new street at each corner,
    /// pauses now and then, jumps out of the way of speeding cars and gets knocked down if hit.
    /// </summary>
    public class Pedestrian : MonoBehaviour
    {
        RoadNetwork net;
        RoadEdge edge;
        int dir;
        float side, d, speed, pause, panic, down, groundY;
        Vector2 panicDir;
        Vector3 fall;
        CharacterAnimator anim;
        Transform model;
        float groundTimer;

        public bool Down => down > 0;

        public void Init(CharacterAnimator a, Transform view)
        {
            anim = a;
            model = view;
        }

        public void Begin(RoadNetwork network, RoadEdge e, int direction, float station, float lateral)
        {
            net = network;
            edge = e;
            dir = direction;
            d = station;
            side = lateral;
            speed = Random.Range(1.15f, 1.6f);
            pause = 0;
            panic = 0;
            down = 0;
            if (model != null) model.localRotation = Quaternion.Euler(0, 180, 0);
            var p = Point(d, out var h);
            groundY = RoadBuilder.CurbTop;
            transform.SetPositionAndRotation(p.X0Z(groundY), Quaternion.LookRotation(new Vector3(h.x, 0, h.y)));
            GetComponent<Collider>().enabled = true;
        }

        Vector2 Point(float station, out Vector2 heading)
        {
            edge.Sample(station, out var p, out var t);
            heading = t * dir;
            return p + Geo2D.Perp(t) * side;
        }

        void Update()
        {
            if (edge == null) return;
            float dt = Time.deltaTime;
            if (down > 0)
            {
                down -= dt;
                fall.y -= 18f * dt;
                var np = transform.position + fall * dt;
                if (np.y < groundY) { np.y = groundY; fall *= 0.0f; }
                transform.position = np;
                if (model != null) model.localRotation = Quaternion.Slerp(model.localRotation, Quaternion.Euler(-88f, 180f, 0), dt * 8f);
                anim?.SetSpeed(0);
                return;
            }

            React();
            var pos = transform.position.XZ();
            float v = 0;
            if (panic > 0)
            {
                panic -= dt;
                v = 4.8f;
                pos += panicDir * v * dt;
                Face(panicDir, dt * 10f);
                if (panic <= 0) Resync(pos);
            }
            else if (pause > 0)
            {
                pause -= dt;
            }
            else
            {
                d += speed * dt * dir;
                float L = edge.Length;
                if (d > L || d < 0) NextEdge();
                var target = Point(d + dir * 1.5f, out var h);
                var step = Vector2.ClampMagnitude(target - pos, speed * dt * 1.6f);
                pos += step;
                v = speed;
                Face(step.sqrMagnitude > 1e-6f ? step.normalized : h, dt * 6f);
                if (Random.value < dt * 0.02f) pause = Random.Range(1.5f, 5f);
            }
            groundTimer -= dt;
            if (groundTimer <= 0)
            {
                groundTimer = 0.25f;
                if (Physics.Raycast(new Vector3(pos.x, transform.position.y + 1.2f, pos.y), Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore))
                    groundY = hit.point.y;
            }
            var y = Mathf.MoveTowards(transform.position.y, groundY, dt * 2.5f);
            transform.position = new Vector3(pos.x, y, pos.y);
            anim?.SetSpeed(v);
        }

        void Face(Vector2 dir2, float k)
        {
            if (dir2.sqrMagnitude < 1e-6f) return;
            var want = Quaternion.LookRotation(new Vector3(dir2.x, 0, dir2.y));
            transform.rotation = Quaternion.Slerp(transform.rotation, want, Mathf.Clamp01(k));
        }

        void NextEdge()
        {
            int node = dir > 0 ? edge.b : edge.a;
            if (node < 0)
            {
                dir = -dir;
                d = Mathf.Clamp(d, 0, edge.Length);
                side = -side;
                return;
            }
            var n = net.nodes[node];
            var options = new List<NodeEnd>();
            foreach (var end in n.ends)
            {
                var e = net.edges[end.edge];
                if (e.Sidewalk <= 0 || (e == edge && end.atStart == (dir < 0))) continue;
                options.Add(end);
            }
            if (options.Count == 0)
            {
                dir = -dir;
                d = Mathf.Clamp(d, 0, edge.Length);
                return;
            }
            var pick = options[Random.Range(0, options.Count)];
            float keepSide = Mathf.Abs(side);
            var prev = edge;
            edge = net.edges[pick.edge];
            dir = pick.atStart ? 1 : -1;
            d = dir > 0 ? 0.5f : edge.Length - 0.5f;
            float s = keepSide - prev.Outer + edge.Outer;
            side = Mathf.Clamp(s, edge.HalfWidth + 0.6f, edge.Outer - 0.5f) * (Random.value < 0.5f ? 1 : -1);
        }

        void Resync(Vector2 pos)
        {
            float best = float.MaxValue;
            for (float k = 0; k <= edge.Length; k += 1f)
            {
                float dist = (Point(k, out _) - pos).sqrMagnitude;
                if (dist < best) { best = dist; d = k; }
            }
        }

        void React()
        {
            var me = transform.position;
            foreach (var car in CarController.All)
            {
                var rel = me - car.transform.position;
                rel.y = 0;
                float dist = rel.magnitude;
                if (dist > 14f) continue;
                var vel = car.Body.linearVelocity;
                vel.y = 0;
                float spd = vel.magnitude;
                if (dist < 1.9f && spd > 3.5f)
                {
                    // Hit: thrown along the car's travel.
                    down = 7f;
                    fall = vel * 0.55f + Vector3.up * Mathf.Min(6f, spd * 0.35f);
                    GetComponent<Collider>().enabled = false;
                    return;
                }
                if (panic > 0 || spd < 6f) continue;
                var heading = vel / spd;
                float ahead = Vector3.Dot(rel, heading);
                float lateral = Vector3.Cross(heading, rel).y;
                if (ahead > 0 && ahead < spd * 1.3f + 4f && Mathf.Abs(lateral) < 3f)
                {
                    // Dive sideways, away from the car's path.
                    var away = Vector3.Cross(Vector3.up, heading) * (lateral >= 0 ? -1f : 1f);
                    panicDir = new Vector2(away.x, away.z).normalized;
                    panic = Random.Range(1.0f, 1.8f);
                    pause = 0;
                }
            }
        }
    }

    /// <summary>Keeps a crowd of pedestrians on the sidewalks around the player.</summary>
    public class PedestrianSystem : MonoBehaviour
    {
        public GameObject model;
        public AnimationClip idle, walk, run;
        public int count = 36;
        public float spawnMin = 25f, spawnMax = 130f, despawn = 170f;

        static readonly Color[] Tints =
        {
            Color.white, new Color(1f, 0.82f, 0.7f), new Color(0.75f, 0.85f, 1f), new Color(0.85f, 1f, 0.8f),
            new Color(1f, 0.75f, 0.8f), new Color(0.7f, 0.7f, 0.72f), new Color(1f, 0.95f, 0.7f),
        };

        readonly List<Pedestrian> crowd = new List<Pedestrian>();
        readonly Dictionary<(Material, int), Material> tintCache = new Dictionary<(Material, int), Material>();
        RoadNetwork net;
        Transform focus;
        float timer;

        public void Init(RoadNetwork network, Transform player)
        {
            net = network;
            focus = player;
        }

        public void SetFocus(Transform t) => focus = t;

        void Update()
        {
            if (net == null || model == null || focus == null) return;
            timer -= Time.deltaTime;
            if (timer > 0) return;
            timer = 0.3f;
            foreach (var p in crowd)
            {
                float dist = Vector3.Distance(p.transform.position, focus.position);
                if (dist > despawn || (p.Down && dist > 40f)) Place(p);
            }
            if (crowd.Count < count)
            {
                var go = new GameObject("Pedestrian");
                go.transform.SetParent(transform, false);
                var view = Instantiate(model, go.transform);
                view.transform.localRotation = Quaternion.Euler(0, 180, 0);
                Tint(view, Random.Range(0, Tints.Length));
                foreach (var r in view.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    r.updateWhenOffscreen = false;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
                var lod = go.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(0.025f, view.GetComponentsInChildren<Renderer>()) });
                lod.RecalculateBounds();
                var anim = go.AddComponent<CharacterAnimator>();
                anim.Init(view.GetComponentInChildren<Animator>(), idle, walk, run);
                var col = go.AddComponent<CapsuleCollider>();
                col.isTrigger = true;
                col.center = Vector3.up * 0.9f;
                col.height = 1.8f;
                col.radius = 0.3f;
                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                var ped = go.AddComponent<Pedestrian>();
                ped.Init(anim, view.transform);
                crowd.Add(ped);
                Place(ped);
            }
        }

        void Place(Pedestrian p)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                var e = net.edges[Random.Range(0, net.edges.Count)];
                if (e.Sidewalk <= 0) continue;
                float d = Random.Range(0, e.Length);
                float side = (e.HalfWidth + 0.6f + Random.value * (e.Sidewalk - 1.1f)) * (Random.value < 0.5f ? 1 : -1);
                e.Sample(d, out var pt, out var t);
                var q = (pt + Geo2D.Perp(t) * side).X0Z(RoadBuilder.CurbTop);
                float dist = Vector3.Distance(q, focus.position);
                if (dist < spawnMin || dist > spawnMax) continue;
                p.Begin(net, e, Random.value < 0.5f ? 1 : -1, d, side);
                return;
            }
        }

        void Tint(GameObject view, int k)
        {
            if (k == 0) return;
            foreach (var r in view.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !m.name.Contains("Body")) continue;
                    if (!tintCache.TryGetValue((m, k), out var clone))
                    {
                        clone = new Material(m);
                        if (clone.HasProperty("baseColorFactor")) clone.SetColor("baseColorFactor", Tints[k]);
                        if (clone.HasProperty("_BaseColor")) clone.SetColor("_BaseColor", Tints[k]);
                        tintCache[(m, k)] = clone;
                    }
                    mats[i] = clone;
                }
                r.sharedMaterials = mats;
            }
        }
    }
}
