using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Ashline.UI
{
    /// <summary>Button that reports whether a finger is currently on it (multi-touch safe).</summary>
    public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public bool Held => pointers > 0;
        int pointers;
        Image image;
        Color idle;

        void Awake()
        {
            image = GetComponent<Image>();
            idle = image.color;
        }

        bool pressed;

        public void OnPointerDown(PointerEventData e) { pointers++; pressed = true; Refresh(); }
        public void OnPointerUp(PointerEventData e) { pointers = Mathf.Max(0, pointers - 1); Refresh(); }

        /// <summary>True once per press, for buttons that act on tap as well as hold.</summary>
        public bool Consume()
        {
            bool p = pressed;
            pressed = false;
            return p;
        }

        /// <summary>Sets the resting colour (the pressed highlight is derived from it).</summary>
        public void Tint(Color c)
        {
            idle = c;
            if (image != null) image.color = c;
        }

        void OnDisable() { pointers = 0; pressed = false; Refresh(); }

        void Refresh()
        {
            if (image == null) return;
            image.color = Held ? new Color(1f, 1f, 1f, Mathf.Min(1f, idle.a + 0.35f)) : idle;
            transform.localScale = Held ? Vector3.one * 0.94f : Vector3.one;
        }
    }

    /// <summary>Button that reports single taps; Consume() returns true once per tap.</summary>
    public class TapButton : MonoBehaviour, IPointerDownHandler
    {
        bool tapped;
        public void OnPointerDown(PointerEventData e) => tapped = true;
        public bool Consume()
        {
            bool t = tapped;
            tapped = false;
            return t;
        }
        void OnDisable() => tapped = false;
    }

    /// <summary>Floating thumbstick: appears where the finger lands inside its zone.</summary>
    public class VirtualStick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform knob, ring;
        public float radius = 110f;
        public Vector2 Value { get; private set; }
        int pointer = -99;
        Vector2 origin;

        public void OnPointerDown(PointerEventData e)
        {
            if (pointer != -99) return;
            pointer = e.pointerId;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, e.position, e.pressEventCamera, out origin);
            ring.anchoredPosition = origin;
            knob.anchoredPosition = origin;
            ring.gameObject.SetActive(true);
            knob.gameObject.SetActive(true);
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != pointer) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, e.position, e.pressEventCamera, out var p);
            var d = Vector2.ClampMagnitude(p - origin, radius);
            knob.anchoredPosition = origin + d;
            Value = d / radius;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != pointer) return;
            pointer = -99;
            Value = Vector2.zero;
            ring.gameObject.SetActive(false);
            knob.gameObject.SetActive(false);
        }

        void OnDisable()
        {
            pointer = -99;
            Value = Vector2.zero;
            if (ring != null) ring.gameObject.SetActive(false);
            if (knob != null) knob.gameObject.SetActive(false);
        }
    }

    /// <summary>Invisible drag area for orbiting the camera; Delta accumulates until read.</summary>
    public class LookPad : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        Vector2 delta;
        int pointer = -99;
        public void OnPointerDown(PointerEventData e) { if (pointer == -99) pointer = e.pointerId; }
        public void OnDrag(PointerEventData e) { if (e.pointerId == pointer) delta += e.delta; }
        public void OnPointerUp(PointerEventData e) { if (e.pointerId == pointer) pointer = -99; }
        public Vector2 Consume()
        {
            var d = delta;
            delta = Vector2.zero;
            return d;
        }
        void OnDisable() { pointer = -99; delta = Vector2.zero; }
    }

    /// <summary>Builds the in-game HUD: driving controls (steer arrows, pedals, handbrake, nitro, speedometer)
    /// and on-foot controls (stick, look pad, sprint, jump), plus the contextual enter/exit button.</summary>
    public class TouchControls : MonoBehaviour
    {
        public HoldButton left, right, gas, brake, handbrake, nitro;
        public Text speed, gear;
        public Image nitroBar;
        public VirtualStick stick;
        public LookPad look;
        public HoldButton sprint;
        public TapButton jump, action;
        public Text actionLabel;
        /// <summary>Contextual interaction (loot, pick lock…): tap, or hold while <see cref="useFill"/> fills.</summary>
        public HoldButton use;
        public Text useLabel;
        public Image useFill;
        RectTransform drivePanel, footPanel;

        /// <summary>Safe-area root; modules add their own widgets here (see HUD layout in Gameplay/README.md).</summary>
        public RectTransform Root { get; private set; }
        /// <summary>Visible only on foot.</summary>
        public RectTransform FootPanel => footPanel;
        /// <summary>Visible only while driving.</summary>
        public RectTransform DrivePanel => drivePanel;
        public static Font Font => font;

        static Font font;

        public static TouchControls Create()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                DontDestroyOnLoad(es);
            }
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var go = new GameObject("TouchControls", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2000, 920);
            scaler.matchWidthOrHeight = 1f;

            var tc = go.AddComponent<TouchControls>();
            var root = Panel("Safe", go.transform);
            go.AddComponent<SafeArea>().target = root;
            tc.Root = root;

            // On-foot layer: look pad underneath everything, stick zone on the left.
            var foot = tc.footPanel = Panel("Foot", root);
            var lookRt = Panel("Look", foot);
            lookRt.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            tc.look = lookRt.gameObject.AddComponent<LookPad>();
            var zone = Panel("StickZone", foot);
            zone.anchorMin = Vector2.zero;
            zone.anchorMax = new Vector2(0.42f, 0.75f);
            zone.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            tc.stick = zone.gameObject.AddComponent<VirtualStick>();
            var ring = Panel("Ring", zone);
            Anchor(ring, Vector2.zero, Vector2.zero, new Vector2(240, 240));
            ring.pivot = new Vector2(0.5f, 0.5f);
            ring.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0.12f);
            var knob = Panel("Knob", zone);
            Anchor(knob, Vector2.zero, Vector2.zero, new Vector2(110, 110));
            knob.pivot = new Vector2(0.5f, 0.5f);
            var knobImg = knob.gameObject.AddComponent<Image>();
            knobImg.color = new Color(1, 1, 1, 0.3f);
            knobImg.raycastTarget = false;
            tc.stick.ring = ring;
            tc.stick.knob = knob;
            ring.GetComponent<Image>().raycastTarget = false;
            ring.gameObject.SetActive(false);
            knob.gameObject.SetActive(false);
            tc.sprint = Button(foot, "RUN", new Vector2(1, 0), new Vector2(-300, 70), new Vector2(180, 180));
            tc.jump = Button(foot, "JUMP", new Vector2(1, 0), new Vector2(-100, 70), new Vector2(170, 170)).gameObject.AddComponent<TapButton>();
            Object.Destroy(tc.jump.GetComponent<HoldButton>());

            tc.use = Button(foot, "USE", new Vector2(1, 0), new Vector2(-200, 290), new Vector2(210, 150));
            tc.use.Tint(new Color(0.3f, 0.9f, 0.5f, 0.35f));
            tc.useLabel = tc.use.GetComponentInChildren<Text>();
            tc.useLabel.fontSize = 34;
            var fill = Panel("Fill", tc.use.transform);
            fill.SetAsFirstSibling();
            tc.useFill = fill.gameObject.AddComponent<Image>();
            tc.useFill.color = new Color(0.3f, 1f, 0.5f, 0.45f);
            tc.useFill.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);
            tc.useFill.type = Image.Type.Filled;
            tc.useFill.fillMethod = Image.FillMethod.Horizontal;
            tc.useFill.fillAmount = 0;
            tc.useFill.raycastTarget = false;
            tc.use.gameObject.SetActive(false);

            var drive = tc.drivePanel = Panel("Drive", root);
            tc.left = Button(drive, "<", new Vector2(0, 0), new Vector2(110, 70), new Vector2(190, 190));
            tc.right = Button(drive, ">", new Vector2(0, 0), new Vector2(330, 70), new Vector2(190, 190));
            tc.gas = Button(drive, "GAS", new Vector2(1, 0), new Vector2(-110, 70), new Vector2(170, 260));
            tc.brake = Button(drive, "BRK", new Vector2(1, 0), new Vector2(-310, 70), new Vector2(170, 170));
            tc.handbrake = Button(drive, "DRIFT", new Vector2(1, 0), new Vector2(-310, 270), new Vector2(170, 120));
            tc.nitro = Button(drive, "NOS", new Vector2(1, 0), new Vector2(-110, 360), new Vector2(170, 120));
            tc.nitro.Tint(new Color(0.2f, 0.55f, 1f, 0.35f));

            tc.speed = Label(drive, "0", new Vector2(0.5f, 0), new Vector2(0, 70), 96, new Vector2(400, 110));
            Label(drive, "KM/H", new Vector2(0.5f, 0), new Vector2(0, 40), 28, new Vector2(200, 40)).color = new Color(1, 1, 1, 0.6f);
            tc.gear = Label(drive, "1", new Vector2(0.5f, 0), new Vector2(150, 90), 44, new Vector2(80, 60));

            // Contextual enter/exit, top right, shared by both layers.
            var act = Button(root, "ENTER", new Vector2(1, 1), new Vector2(-110, -150), new Vector2(200, 110));
            act.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 0f);
            act.GetComponent<Image>().color = new Color(1f, 0.8f, 0.2f, 0.4f);
            tc.actionLabel = act.GetComponentInChildren<Text>();
            tc.action = act.gameObject.AddComponent<TapButton>();
            Object.Destroy(act);

            var barBg = Panel("NitroBg", drive);
            Anchor(barBg, new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(260, 10));
            barBg.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0.15f);
            var bar = Panel("Nitro", barBg);
            bar.anchorMin = Vector2.zero; bar.anchorMax = Vector2.one;
            bar.offsetMin = bar.offsetMax = Vector2.zero;
            tc.nitroBar = bar.gameObject.AddComponent<Image>();
            tc.nitroBar.color = new Color(0.35f, 0.7f, 1f, 0.9f);
            tc.nitroBar.type = Image.Type.Filled;
            tc.nitroBar.fillMethod = Image.FillMethod.Horizontal;
            tc.nitroBar.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);
            return tc;
        }

        public void SetMode(bool driving)
        {
            drivePanel.gameObject.SetActive(driving);
            footPanel.gameObject.SetActive(!driving);
        }

        /// <summary>Shows or hides the contextual button with the given caption.</summary>
        public void SetAction(string caption)
        {
            bool on = !string.IsNullOrEmpty(caption);
            if (action.gameObject.activeSelf != on) action.gameObject.SetActive(on);
            if (on && actionLabel.text != caption) actionLabel.text = caption;
        }

        /// <summary>Shows the USE button with a caption and hold progress (0..1); null hides it.</summary>
        public void SetUse(string caption, float progress)
        {
            bool on = !string.IsNullOrEmpty(caption);
            if (use.gameObject.activeSelf != on) use.gameObject.SetActive(on);
            if (!on) return;
            if (useLabel.text != caption) useLabel.text = caption;
            useFill.fillAmount = progress;
        }

        public void Show(float kmh, int gearIndex, float nitro01)
        {
            speed.text = Mathf.RoundToInt(kmh).ToString();
            gear.text = gearIndex == 0 ? "R" : gearIndex.ToString();
            nitroBar.fillAmount = nitro01;
        }

        static RectTransform Panel(string name, Transform parent)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(anchor.x, 0f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        static HoldButton Button(Transform parent, string label, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var rt = Panel(label, parent);
            Anchor(rt, anchor, pos, size);
            rt.pivot = new Vector2(0.5f, 0f);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.16f);
            var t = Label(rt, label, new Vector2(0.5f, 0.5f), Vector2.zero, 40, size);
            t.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            t.raycastTarget = false;
            return rt.gameObject.AddComponent<HoldButton>();
        }

        static Text Label(Transform parent, string text, Vector2 anchor, Vector2 pos, int fontSize, Vector2 size)
        {
            var rt = Panel("Label", parent);
            Anchor(rt, anchor, pos, size);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = fontSize;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.raycastTarget = false;
            return t;
        }
    }

    /// <summary>Keeps HUD inside the iPhone safe area (Dynamic Island, home indicator).</summary>
    public class SafeArea : MonoBehaviour
    {
        public RectTransform target;
        Rect last;

        void Update()
        {
            var sa = Screen.safeArea;
            if (sa == last || target == null) return;
            last = sa;
            target.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
            target.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
        }
    }
}
