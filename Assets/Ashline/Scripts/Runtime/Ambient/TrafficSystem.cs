using System.Collections.Generic;
using Ashline.City;
using Ashline.Vehicles;
using UnityEngine;

namespace Ashline.Ambient
{
    /// <summary>Lane position on the road graph: an edge, travel direction along it, lane index and station.</summary>
    public struct LaneCursor
    {
        public RoadEdge edge;
        public int dir;     // +1 along the edge, -1 against it
        public int lane;
        public float d;

        /// <summary>Right-hand traffic: lanes sit to the right of the travel direction.</summary>
        public float Lateral => -dir * (lane + 0.5f) * IslandPlan.LaneWidth;

        public Vector2 Point(float station, out Vector2 heading)
        {
            edge.Sample(station, out var p, out var t);
            heading = t * dir;
            return p + Geo2D.Perp(t) * Lateral;
        }
    }

    /// <summary>Keeps a small pool of AI cars driving around the player, recycling ones left far behind.</summary>
    public class TrafficSystem : MonoBehaviour
    {
        public GameObject carModel;
        public int count = 8;
        public float spawnMin = 70f, spawnMax = 210f, despawn = 260f;

        static readonly Color[] Paints =
        {
            new Color(0.55f, 0.02f, 0.03f), new Color(0.9f, 0.9f, 0.88f), new Color(0.06f, 0.06f, 0.07f), new Color(0.05f, 0.18f, 0.45f),
            new Color(0.35f, 0.37f, 0.4f), new Color(0.75f, 0.55f, 0.12f), new Color(0.08f, 0.3f, 0.2f), new Color(0.6f, 0.62f, 0.65f),
        };

        readonly List<TrafficDriver> drivers = new List<TrafficDriver>();
        readonly Dictionary<(Material, int), Material> paintCache = new Dictionary<(Material, int), Material>();
        RoadNetwork net;
        Transform focus;
        float checkTimer;

        public void Init(RoadNetwork network, Transform player)
        {
            net = network;
            focus = player;
        }

        public void SetFocus(Transform t) => focus = t;

        void Update()
        {
            if (net == null || carModel == null || focus == null) return;
            checkTimer -= Time.deltaTime;
            if (checkTimer > 0) return;
            checkTimer = 0.5f;
            drivers.RemoveAll(d => d == null || !d.enabled);
            for (int i = drivers.Count - 1; i >= 0; i--)
            {
                var d = drivers[i];
                if (Vector3.Distance(d.transform.position, focus.position) > despawn || d.transform.position.y < -3f)
                {
                    if (!Respawn(d.Car)) { Destroy(d.gameObject); drivers.RemoveAt(i); }
                }
            }
            if (drivers.Count < count)
            {
                if (!FindSpot(out var cursor, out var pos, out var rot)) return;
                var car = CarFactory.Create(carModel, pos, rot);
                car.name = "Traffic";
                Repaint(car.gameObject, Random.Range(0, Paints.Length));
                var driver = car.gameObject.AddComponent<TrafficDriver>();
                driver.Begin(net, cursor);
                drivers.Add(driver);
            }
        }

        bool Respawn(CarController car)
        {
            if (!FindSpot(out var cursor, out var pos, out var rot)) return false;
            var rb = car.Body;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = pos + Vector3.up * 0.3f;
            rb.rotation = rot;
            car.transform.SetPositionAndRotation(pos + Vector3.up * 0.3f, rot);
            car.GetComponent<TrafficDriver>().Begin(net, cursor);
            return true;
        }

        bool FindSpot(out LaneCursor cursor, out Vector3 pos, out Quaternion rot)
        {
            var cam = Camera.main;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                var e = net.edges[Random.Range(0, net.edges.Count)];
                if (e.def.kind == RoadKind.Pier || e.Length - e.trimA - e.trimB < 20f) continue;
                cursor = new LaneCursor
                {
                    edge = e,
                    dir = Random.value < 0.5f ? 1 : -1,
                    lane = Random.Range(0, e.def.LanesPerSide),
                    d = Random.Range(e.trimA + 6f, e.Length - e.trimB - 6f),
                };
                var p2 = cursor.Point(cursor.d, out var h);
                pos = p2.X0Z(RoadBuilder.RoadY);
                float dist = Vector3.Distance(pos, focus.position);
                if (dist < spawnMin || dist > spawnMax) continue;
                // Don't pop in on screen when close.
                if (cam != null && dist < 140f)
                {
                    var vp = cam.WorldToViewportPoint(pos);
                    if (vp.z > 0 && vp.x > -0.1f && vp.x < 1.1f && vp.y > -0.1f && vp.y < 1.1f) continue;
                }
                bool crowded = false;
                foreach (var c in CarController.All) if ((c.transform.position - pos).sqrMagnitude < 18f * 18f) { crowded = true; break; }
                if (crowded) continue;
                rot = Quaternion.LookRotation(new Vector3(h.x, 0, h.y));
                return true;
            }
            cursor = default;
            pos = default;
            rot = default;
            return false;
        }

        void Repaint(GameObject car, int color)
        {
            foreach (var r in car.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !m.name.StartsWith("Paint")) continue;
                    if (!paintCache.TryGetValue((m, color), out var clone))
                    {
                        clone = new Material(m) { name = m.name + "_" + color };
                        var c = Paints[color];
                        if (clone.HasProperty("baseColorFactor")) clone.SetColor("baseColorFactor", c);
                        if (clone.HasProperty("_BaseColor")) clone.SetColor("_BaseColor", c);
                        paintCache[(m, color)] = clone;
                    }
                    mats[i] = clone;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }
    }

    /// <summary>
    /// Lane-following driver: pure-pursuit steering on the lane centerline, speed limited by road type,
    /// curvature and upcoming turns, and braking for anything in front (cars, people).
    /// </summary>
    public class TrafficDriver : MonoBehaviour
    {
        public CarController Car { get; private set; }
        LaneCursor cur;
        RoadNetwork net;
        float stuck, reverseTimer;
        RoadEdge nextEdge;
        int nextDir;

        public void Begin(RoadNetwork network, LaneCursor cursor)
        {
            net = network;
            cur = cursor;
            Car = GetComponent<CarController>();
            Car.occupied = true;
            nextEdge = null;
            stuck = 0;
            enabled = true;
        }

        void OnDisable()
        {
            if (Car != null) Car.input = new CarInput { brake = 1f, handbrake = true };
        }

        float Limit(RoadKind k) => k switch
        {
            RoadKind.Highway => 24f,
            RoadKind.Avenue => 15f,
            RoadKind.Street => 11f,
            RoadKind.Lane => 8f,
            _ => 5f,
        };

        void Update()
        {
            if (net == null || Car == null) return;
            var pos = transform.position.XZ();
            var fwd3 = transform.forward;
            var fwd = new Vector2(fwd3.x, fwd3.z).normalized;
            float speed = Car.ForwardSpeed;

            Advance(pos);

            float look = 6f + Mathf.Abs(speed) * 0.55f;
            var target = Ahead(look, out var headingAhead);
            var toTarget = target - pos;
            float angle = Vector2.SignedAngle(toTarget, fwd);
            float steer = Mathf.Clamp(angle / Mathf.Max(8f, Car.maxSteer * 0.8f), -1f, 1f);

            // Desired speed: road limit, then slow for bends and turns ahead.
            float limit = Limit(cur.edge.def.kind);
            Ahead(18f + Mathf.Abs(speed), out var farHeading);
            cur.Point(cur.d, out var hereHeading);
            float bend = Vector2.Angle(hereHeading, farHeading);
            limit = Mathf.Min(limit, Mathf.Lerp(limit, 6f, Mathf.Clamp01((bend - 10f) / 70f)));

            // Obstacles straight ahead.
            float range = 6f + Mathf.Max(0, speed) * 1.6f;
            var origin = transform.position + Vector3.up * 0.8f + fwd3 * 2.4f;
            var dir3 = new Vector3(toTarget.x, 0, toTarget.y).normalized;
            if (Physics.SphereCast(origin, 1.0f, Vector3.Lerp(fwd3, dir3, 0.5f).normalized, out var hit, range, ~0, QueryTriggerInteraction.Collide))
            {
                bool blocker = hit.rigidbody != null && hit.rigidbody != Car.Body || hit.collider is CharacterController || hit.collider.GetComponentInParent<Pedestrian>() != null;
                if (blocker) limit = Mathf.Min(limit, Mathf.Max(0f, (hit.distance - 2.5f) * 0.7f));
            }

            var input = new CarInput { steer = steer };
            float err = limit - speed;
            if (err > 0.5f) input.throttle = Mathf.Clamp01(err * 0.35f);
            else if (err < -0.8f || limit < 0.5f) input.brake = Mathf.Clamp01(-err * 0.3f + (limit < 0.5f ? 0.6f : 0f));
            if (limit < 0.5f && Mathf.Abs(speed) < 0.5f) input.handbrake = true;

            // Wedged against something for a while: back up and swing out.
            if (Mathf.Abs(speed) < 0.6f && limit > 3f) stuck += Time.deltaTime; else stuck = Mathf.Max(0, stuck - Time.deltaTime);
            if (stuck > 2.5f) { reverseTimer = 1.6f; stuck = 0; }
            if (reverseTimer > 0)
            {
                reverseTimer -= Time.deltaTime;
                input = new CarInput { brake = 1f, steer = -steer };
            }
            Car.input = input;
        }

        /// <summary>Moves the cursor station to the point nearest the car, hopping onto the next edge at junctions.</summary>
        void Advance(Vector2 pos)
        {
            float best = float.MaxValue, bestD = cur.d;
            for (float k = -4f; k <= 12f; k += 0.5f)
            {
                float d = cur.d + k * cur.dir;
                if (d < 0 || d > cur.edge.Length) continue;
                float dist = (cur.Point(d, out _) - pos).sqrMagnitude;
                if (dist < best) { best = dist; bestD = d; }
            }
            cur.d = bestD;
            float remaining = cur.dir > 0 ? cur.edge.Length - cur.d : cur.d;
            float trim = cur.dir > 0 ? cur.edge.trimB : cur.edge.trimA;
            if (remaining < Mathf.Max(2f, trim * 0.5f))
            {
                ChooseNext();
                if (nextEdge != null)
                {
                    cur.edge = nextEdge;
                    cur.dir = nextDir;
                    cur.lane = Mathf.Min(cur.lane, cur.edge.def.LanesPerSide - 1);
                    cur.d = cur.dir > 0 ? Mathf.Min(cur.edge.trimA * 0.5f, cur.edge.Length) : Mathf.Max(cur.edge.Length - cur.edge.trimB * 0.5f, 0);
                    nextEdge = null;
                }
            }
        }

        void ChooseNext()
        {
            if (nextEdge != null) return;
            int node = cur.dir > 0 ? cur.edge.b : cur.edge.a;
            if (node < 0)
            {
                // Dead end: turn around.
                nextEdge = cur.edge;
                nextDir = -cur.dir;
                return;
            }
            var n = net.nodes[node];
            cur.Point(cur.dir > 0 ? cur.edge.Length : 0, out var arriving);
            RoadEdge pick = null;
            int pickDir = 0;
            float bestScore = float.MinValue;
            foreach (var end in n.ends)
            {
                var e = net.edges[end.edge];
                if (e == cur.edge && end.atStart == (cur.dir < 0)) continue;
                if (e.def.kind == RoadKind.Pier) continue;
                float straight = Vector2.Dot(arriving.normalized, end.dir);
                float score = straight * 1.2f + Random.value * 1.6f + (e.def.kind == RoadKind.Lane ? -0.8f : 0f);
                if (score > bestScore)
                {
                    bestScore = score;
                    pick = e;
                    pickDir = end.atStart ? 1 : -1;
                }
            }
            if (pick == null) { pick = cur.edge; pickDir = -cur.dir; }
            nextEdge = pick;
            nextDir = pickDir;
        }

        /// <summary>Point a given distance ahead along the lane, continuing onto the chosen next edge.</summary>
        Vector2 Ahead(float distance, out Vector2 heading)
        {
            float d = cur.d + distance * cur.dir;
            if (d >= 0 && d <= cur.edge.Length) return cur.Point(d, out heading);
            ChooseNext();
            float over = cur.dir > 0 ? d - cur.edge.Length : -d;
            var next = new LaneCursor { edge = nextEdge, dir = nextDir, lane = Mathf.Min(cur.lane, nextEdge.def.LanesPerSide - 1) };
            float nd = nextDir > 0 ? Mathf.Min(over, nextEdge.Length) : Mathf.Max(nextEdge.Length - over, 0);
            return next.Point(nd, out heading);
        }
    }
}
