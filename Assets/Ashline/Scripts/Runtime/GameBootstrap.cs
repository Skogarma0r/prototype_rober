using Ashline.Ambient;
using Ashline.Characters;
using Ashline.City;
using Ashline.Gameplay;
using Ashline.UI;
using Ashline.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Ashline
{
    /// <summary>
    /// Spawns the player on foot next to their parked car, the camera rigs, HUD, traffic and
    /// pedestrians, and handles switching between walking and driving.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        public GameObject carModel;
        public Material carPaint;
        public CityGenerator city;
        public GameObject characterModel;
        public AnimationClip idleClip, walkClip, runClip;
        public int trafficCars = 8;
        public int pedestrians = 36;

        /// <summary>Scripted input for automated test drives; null means player control.</summary>
        public static CarInput? InputOverride;
        /// <summary>Start behind the wheel instead of on foot (automated tests).</summary>
        public static bool StartInCar;

        public PlayerCharacter Player { get; private set; }
        public CarController Car { get; private set; }
        public bool Driving => Car != null;
        public FootCamera FootCam => footCam;
        public ChaseCamera ChaseCam => chase;
        public TrafficSystem Traffic => traffic;
        public PedestrianSystem Crowd => crowd;

        CarController ownCar;
        ChaseCamera chase;
        FootCamera footCam;
        TouchControls hud;
        TrafficSystem traffic;
        PedestrianSystem crowd;
        Vector3 safeCarPos, safeFootPos;
        Quaternion safeCarRot;
        float safeTimer, sinkTimer;

        void Start()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            SetupEnvironment();

            // Parked on Ocean Drive in Sunny Beach, player on the sidewalk beside the driver's door.
            var road = city.Plan.roads.Find(r => r.name == "Ocean Drive");
            float lane = road.HalfWidth - IslandPlan.LaneWidth * 0.5f;
            city.RoadPose("Ocean Drive", 0.5f, -lane, out var carPos, out var rot);
            city.RoadPose("Ocean Drive", 0.5f, -(road.HalfWidth + road.Sidewalk * 0.45f), out var footPos, out _);
            ownCar = CarFactory.Create(carModel, carPos, rot, new CarFactory.Options { paint = carPaint });
            ownCar.name = "PlayerCar";
            Player = PlayerCharacter.Create(characterModel, idleClip, walkClip, runClip, footPos - rot * Vector3.forward * 1.5f, rot);
            safeFootPos = Player.transform.position;
            safeCarPos = carPos;
            safeCarRot = rot;

            var cam = Camera.main;
            if (!cam.TryGetComponent(out chase)) chase = cam.gameObject.AddComponent<ChaseCamera>();
            if (!cam.TryGetComponent(out footCam)) footCam = cam.gameObject.AddComponent<FootCamera>();
            footCam.target = Player;
            Player.view = cam.transform;
            hud = TouchControls.Create();

            traffic = new GameObject("Traffic").AddComponent<TrafficSystem>();
            traffic.carModel = carModel;
            traffic.count = trafficCars;
            traffic.Init(city.Network, Player.transform);
            crowd = new GameObject("Pedestrians").AddComponent<PedestrianSystem>();
            crowd.model = characterModel;
            crowd.idle = idleClip;
            crowd.walk = walkClip;
            crowd.run = runClip;
            crowd.count = pedestrians;
            crowd.Init(city.Network, Player.transform);

            if (StartInCar || InputOverride.HasValue) Enter(ownCar);
            else Exit(true);

            GameContext.Bootstrap = this;
            GameContext.City = city;
            GameContext.Player = Player;
            GameContext.Status = Player.gameObject.AddComponent<PlayerStatus>();
            GameContext.Camera = cam;
            GameContext.Hud = hud;
            GameContext.HumanModel = characterModel;
            GameContext.IdleClip = idleClip;
            GameContext.WalkClip = walkClip;
            GameContext.RunClip = runClip;
            gameObject.AddComponent<InteractionDriver>();
            GameEvents.RaiseWorldReady();
        }

        void SetupEnvironment()
        {
            DynamicGI.UpdateEnvironment();
            var probeGo = new GameObject("SkyReflection");
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.cullingMask = 0;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.resolution = 256;
            probe.hdr = true;
            probe.size = Vector3.one * 5000f;
            probe.importance = 0;
            probe.transform.position = Vector3.up * 30f;
            probe.RenderProbe();
        }

        // ------------------------------------------------------------------ enter / exit

        CarController NearestCar(float range)
        {
            CarController best = null;
            float bestD = range;
            var p = Player.transform.position;
            foreach (var c in CarController.All)
            {
                // Distance to the body shell rather than the car's origin, so doors are in reach.
                var col = c.GetComponent<Collider>();
                float d = col != null ? Vector3.Distance(col.ClosestPoint(p + Vector3.up * 0.8f), p + Vector3.up * 0.8f) : Vector3.Distance(c.transform.position, p) - 1.5f;
                if (d < bestD && c.Body.linearVelocity.magnitude < 6f) { bestD = d; best = c; }
            }
            return best;
        }

        void Enter(CarController c)
        {
            var ai = c.GetComponent<TrafficDriver>();
            if (ai != null) Destroy(ai);
            Car = c;
            Car.occupied = true;
            Player.gameObject.SetActive(false);
            footCam.enabled = false;
            chase.enabled = true;
            chase.target = Car;
            chase.Snap();
            hud.SetMode(true);
            traffic.SetFocus(Car.transform);
            crowd.SetFocus(Car.transform);
        }

        void Exit(bool initial = false)
        {
            if (!initial && Car != null)
            {
                var t = Car.transform;
                Vector3 pos = t.position - t.right * 1.5f + Vector3.up * 0.1f;
                if (Physics.CheckCapsule(pos + Vector3.up * 0.4f, pos + Vector3.up * 1.5f, 0.3f, ~0, QueryTriggerInteraction.Ignore))
                    pos = t.position + t.right * 1.5f + Vector3.up * 0.1f;
                if (Physics.Raycast(pos + Vector3.up * 1.5f, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore)) pos.y = hit.point.y;
                Player.gameObject.SetActive(true);
                Player.Teleport(pos, Quaternion.LookRotation(Vector3.ProjectOnPlane(-t.right, Vector3.up)));
                Car.input = new CarInput { handbrake = true };
                Car.occupied = false;
            }
            float heading = Car != null ? Car.transform.eulerAngles.y : Player.transform.eulerAngles.y;
            Car = null;
            chase.enabled = false;
            footCam.enabled = true;
            footCam.Snap(heading);
            hud.SetMode(false);
            traffic.SetFocus(Player.transform);
            crowd.SetFocus(Player.transform);
        }

        // ------------------------------------------------------------------ input

        void Update()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool action = (kb != null && kb.fKey.wasPressedThisFrame) || (pad != null && pad.buttonNorth.wasPressedThisFrame) || (hud != null && hud.action.Consume());

            if (Driving) DriveUpdate(kb, pad, action);
            else FootUpdate(kb, pad, action);
            Rescue();
        }

        void FootUpdate(Keyboard kb, Gamepad pad, bool action)
        {
            var near = NearestCar(1.8f);
            hud.SetAction(near != null ? "ENTER" : null);
            if (action && near != null)
            {
                Enter(near);
                return;
            }
            var move = Vector2.zero;
            bool sprint = false, jump = false;
            var look = Vector2.zero;
            if (kb != null)
            {
                move.x = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
                move.y = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
                sprint = kb.leftShiftKey.isPressed;
                jump = kb.spaceKey.wasPressedThisFrame;
                look.x += ((kb.cKey.isPressed ? 1f : 0f) - (kb.zKey.isPressed ? 1f : 0f)) * 120f * Time.deltaTime;
            }
            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed) look += mouse.delta.ReadValue() * 0.15f;
            if (pad != null)
            {
                var s = pad.leftStick.ReadValue();
                if (s.sqrMagnitude > move.sqrMagnitude) move = s;
                look += pad.rightStick.ReadValue() * 160f * Time.deltaTime;
                sprint |= pad.leftStickButton.isPressed || pad.rightTrigger.ReadValue() > 0.5f;
                jump |= pad.buttonSouth.wasPressedThisFrame;
            }
            if (hud != null)
            {
                if (hud.stick.Value.sqrMagnitude > move.sqrMagnitude) move = hud.stick.Value;
                sprint |= hud.sprint.Held || hud.stick.Value.magnitude > 0.95f;
                jump |= hud.jump.Consume();
                look += hud.look.Consume() * 0.18f;
            }
            Player.move = move;
            Player.sprint = sprint;
            Player.jump |= jump;
            footCam.look += look;
        }

        void DriveUpdate(Keyboard kb, Gamepad pad, bool action)
        {
            hud.SetAction("EXIT");
            if (action && Car.SpeedKmh < 25f)
            {
                Exit();
                return;
            }
            var input = new CarInput();
            if (kb != null)
            {
                input.throttle = kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f;
                input.brake = kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f;
                input.steer = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
                input.handbrake = kb.spaceKey.isPressed;
                input.nitro = kb.leftShiftKey.isPressed;
            }
            if (pad != null)
            {
                input.throttle = Mathf.Max(input.throttle, pad.rightTrigger.ReadValue());
                input.brake = Mathf.Max(input.brake, pad.leftTrigger.ReadValue());
                float s = pad.leftStick.x.ReadValue();
                if (Mathf.Abs(s) > Mathf.Abs(input.steer)) input.steer = s;
                input.handbrake |= pad.buttonSouth.isPressed;
                input.nitro |= pad.buttonEast.isPressed;
            }
            if (hud != null)
            {
                if (hud.gas.Held) input.throttle = 1f;
                if (hud.brake.Held) input.brake = 1f;
                if (hud.left.Held) input.steer -= 1f;
                if (hud.right.Held) input.steer += 1f;
                input.steer = Mathf.Clamp(input.steer, -1f, 1f);
                input.handbrake |= hud.handbrake.Held;
                input.nitro |= hud.nitro.Held;
                hud.Show(Car.SpeedKmh, Car.Gear, Car.Nitro / Car.nitroCapacity);
            }
            Car.input = InputOverride ?? input;
        }

        /// <summary>Remembers the last dry position and puts the player back there after falling into the sea.</summary>
        void Rescue()
        {
            safeTimer -= Time.deltaTime;
            if (Driving)
            {
                var t = Car.transform;
                if (safeTimer <= 0 && Car.Grounded && t.position.y > -0.2f && Vector3.Dot(t.up, Vector3.up) > 0.9f)
                {
                    safeTimer = 1f;
                    safeCarPos = t.position;
                    safeCarRot = Quaternion.LookRotation(Vector3.ProjectOnPlane(t.forward, Vector3.up));
                }
                sinkTimer = t.position.y < IslandPlan.WaterLevel - 0.4f ? sinkTimer + Time.deltaTime : 0f;
                if (sinkTimer > 1.5f)
                {
                    sinkTimer = 0;
                    var rb = Car.Body;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.position = safeCarPos + Vector3.up * 0.5f;
                    rb.rotation = safeCarRot;
                    t.SetPositionAndRotation(safeCarPos + Vector3.up * 0.5f, safeCarRot);
                    chase.Snap();
                }
            }
            else
            {
                var p = Player.transform.position;
                if (safeTimer <= 0 && Player.Grounded && p.y > -0.4f)
                {
                    safeTimer = 0.5f;
                    safeFootPos = p;
                }
                if (p.y < IslandPlan.WaterLevel - 0.5f) Player.Teleport(safeFootPos + Vector3.up * 0.2f, Player.transform.rotation);
            }
        }
    }
}
