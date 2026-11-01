using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Settings / work panel in the cab's right front corner (where the 6M has its CommandCenter display), riding with the rig.
/// Buttons are pressed by touching them with a fingertip (hand tracking) or the front of a controller.
/// Open / close: left controller menu button (keyboard M) or the small MENU tab, which stays visible.
/// Everything here also has a button or stick on the controllers, except seat adjust, which only lives here
/// (the grips are now for grabbing the wheel and the lever).
/// </summary>
public class CabPanel : MonoBehaviour
{
    public VRCameraRig rig;
    public TractorController tractor;
    public Drivetrain drivetrain;
    public Hitch hitch;
    public ComfortVignette comfort;
    public TractorInput input;
    public VRHands hands;
    public PerfBenchmark benchmark;
    public Material buttonMat, buttonHotMat, panelMat, textMat;
    public AudioSource click;

    public static bool Mph { get; private set; }

    class Button
    {
        public Transform t;
        public Vector2 size;
        public TextMesh label;
        public System.Func<string> text;
        public System.Action press;
        public bool repeat;
        public bool inside;
        public MeshRenderer face;
    }

    readonly List<Button> buttons = new List<Button>();
    Button tab;
    Transform body;
    bool open, hidden;
    float cooldown;
    Font font;

    const float W = 0.105f, H = 0.042f, Gap = 0.008f;
    const int Cols = 3;   // 33 buttons: three columns keep the panel about as tall as the seat-to-knee distance

    void Start()
    {
        Mph = PlayerPrefs.GetInt("UnitsMph", 0) == 1;
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        body = new GameObject("Buttons").transform;
        body.SetParent(transform, false);

        var lights = tractor.GetComponent<TractorLights>();
        var items = new (string id, System.Func<string> text, System.Action press, bool repeat)[]
        {
            ("tutorial", () => "TUTORIAL", () => { if (Tutorial.Instance) Tutorial.Instance.ShowLessons(); }, false),
            ("view",     () => rig.view == VRCameraRig.View.Cab ? "VIEW: CAB" : "VIEW: 3RD", () => rig.ToggleView(), false),
            ("recenter", () => "RECENTER", () => rig.Recenter(), false),
            ("comfort",  () => "COMFORT: " + comfort.CurrentLevel.ToString().ToUpperInvariant(), () => comfort.Cycle(), false),
            ("gearbox",  () => drivetrain.Automatic ? "GEARBOX: AUTO" : "GEARBOX: MAN", () => drivetrain.ToggleMode(), false),
            ("hitchUp",  () => "HITCH UP", () => hitch.MenuLift(false, 1f), true),
            ("hitchDn",  () => "HITCH DOWN", () => hitch.MenuLift(false, -1f), true),
            ("frontUp",  () => "FRONT UP", () => hitch.MenuLift(true, 1f), true),
            ("frontDn",  () => "FRONT DOWN", () => hitch.MenuLift(true, -1f), true),
            ("pto",      () => hitch.Pto ? "PTO: ON" : "PTO: OFF", () => hitch.SetPto(!hitch.Pto), false),
            ("attach",   () => "ATTACH/DETACH", () => hitch.ToggleAttach(), false),
            ("seat",     () => input.seatAdjust ? "SEAT ADJ: ON" : "SEAT ADJ: OFF", () => input.seatAdjust = !input.seatAdjust, false),
            ("seatReset",() => "SEAT RESET", () => rig.ResetSeat(), false),
            ("units",    () => Mph ? "UNITS: MPH" : "UNITS: KM/H", ToggleUnits, false),
            ("reset",    () => "RESET TRACTOR", () => tractor.ResetTractor(), false),
            ("stop",     () => "STOP / NEUTRAL", () => { tractor.GetComponent<CabControls>().Neutral(); tractor.RequestStop(); }, false),
            ("foveation",() => "FOVEATION: " + Foveation.Names[Foveation.Level], () => Foveation.Level = (Foveation.Level + 1) % 4, false),
            ("perf",     () => TractorHud.ShowPerf ? "FRAME TIME: ON" : "FRAME TIME: OFF", () => TractorHud.ShowPerf = !TractorHud.ShowPerf, false),
            ("doors",    () => tractor.GetComponent<CabControls>().DoorsOpen ? "DOORS: OPEN" : "DOORS: CLOSED", () => tractor.GetComponent<CabControls>().ToggleDoors(), false),
            ("thrDown",  () => "THROTTLE -", () => drivetrain.MoveHandThrottle(-1f, Time.deltaTime), true),
            ("thrUp",    () => "THROTTLE +", () => drivetrain.MoveHandThrottle(1f, Time.deltaTime), true),
            ("tipUp",    () => "TIP UP", () => hitch.MenuTip(1f), true),
            ("tipDn",    () => "TIP DOWN", () => hitch.MenuTip(-1f), true),
            ("fourWd",   () => tractor.fourWheelDrive ? "4WD: ON" : "4WD: OFF", () => tractor.SetFourWheelDrive(!tractor.fourWheelDrive), false),
            ("diffLock", () => tractor.DiffLock ? "DIFF LOCK: ON" : "DIFF LOCK: OFF", () => tractor.SetDiffLock(!tractor.DiffLock), false),
            ("brakes",   () => tractor.allWheelBrakes ? "BRAKES: ALL" : "BRAKES: REAR", () => tractor.SetAllWheelBrakes(!tractor.allWheelBrakes), false),
            ("lights",   () => "LIGHTS: " + (lights.RoadLights == TractorLights.Road.Off ? "OFF" : lights.RoadLights == TractorLights.Road.Side ? "SIDE" : "HEAD"), () => lights.CycleRoadLights(), false),
            ("work",     () => "WORK: " + (lights.WorkRear ? "FRONT+REAR" : lights.WorkFront ? "FRONT" : "OFF"), () => lights.CycleWorkLights(), false),
            ("beacon",   () => lights.Beacon ? "BEACON: ON" : "BEACON: OFF", () => lights.SetBeacon(!lights.Beacon), false),
            ("hazards",  () => lights.Hazards ? "HAZARDS: ON" : "HAZARDS: OFF", () => lights.SetHazards(!lights.Hazards), false),
            ("indLeft",  () => lights.Indicator < 0 ? "< INDICATOR *" : "< INDICATOR", () => lights.ToggleIndicator(-1), false),
            ("indRight", () => lights.Indicator > 0 ? "* INDICATOR >" : "INDICATOR >", () => lights.ToggleIndicator(1), false),
            ("sky",      () => "SKY: " + lights.TimeOfDay.ToString().ToUpperInvariant(), () => lights.CycleSky(), false),
            ("bench",    () => PerfBenchmark.Running ? "BENCHMARK..." : "BENCHMARK", () => benchmark.Begin(), false),
        };
        int rows = (items.Length + Cols - 1) / Cols;
        var bg = Quad("Background", transform, new Vector3(0f, 0f, 0.004f), new Vector2(Cols * W + (Cols + 1) * Gap, rows * (H + Gap) + Gap), panelMat);
        bg.transform.SetParent(body, true);
        for (int i = 0; i < items.Length; i++)
        {
            int col = i % Cols, row = i / Cols;
            var pos = new Vector3((col - (Cols - 1) * 0.5f) * (W + Gap), ((rows - 1) * 0.5f - row) * (H + Gap), 0f);
            buttons.Add(MakeButton(items[i].id, body, pos, new Vector2(W, H), items[i].text, items[i].press, items[i].repeat, 0.011f));
        }
        // The tab sits just above the panel and is always shown.
        float top = rows * 0.5f * (H + Gap) + Gap;
        tab = MakeButton("tab", transform, new Vector3((Cols - 1) * 0.5f * (W + Gap), top + 0.018f, 0f), new Vector2(0.07f, 0.03f),
            () => open ? "CLOSE" : "MENU", Toggle, false, 0.009f);
        SetOpen(false);
        SetHidden(hidden);
    }

    // ---- desktop version (DesktopMode): the same buttons as an on-screen menu, the in-cab panel hidden
    public bool Open => open;
    public void ToggleOpen() => Toggle();
    public int EntryCount => buttons.Count;
    public (string label, bool repeat) Entry(int i) => (buttons[i].text(), buttons[i].repeat);

    public void PressEntry(int i)
    {
        if (!buttons[i].repeat && click) click.Play();
        buttons[i].press();
    }

    public void SetHidden(bool h)
    {
        hidden = h;
        if (body == null) return;   // before Start: applied there
        body.gameObject.SetActive(open && !hidden);
        tab.t.gameObject.SetActive(!hidden);
    }

    void ToggleUnits()
    {
        Mph = !Mph;
        PlayerPrefs.SetInt("UnitsMph", Mph ? 1 : 0);
    }

    void Toggle() => SetOpen(!open);

    /// <summary>Builds and opens the panel outside play mode (editor preview renders).</summary>
    public void PreviewOpen()
    {
        if (tab == null) Start();
        SetOpen(true);
        foreach (var b in buttons) b.label.text = b.text();
        tab.label.text = tab.text();
    }

    void SetOpen(bool o)
    {
        open = o;
        body.gameObject.SetActive(o && !hidden);
    }

    Button MakeButton(string id, Transform parent, Vector3 pos, Vector2 size, System.Func<string> text, System.Action press, bool repeat, float textHeight)
    {
        var t = new GameObject("Button_" + id).transform;
        t.SetParent(parent, false);
        t.localPosition = pos;
        var face = Quad("Face", t, Vector3.zero, size, buttonMat);
        var go = new GameObject("Label");
        go.transform.SetParent(t, false);
        go.transform.localPosition = new Vector3(0f, 0f, -0.002f);
        var tm = go.AddComponent<TextMesh>();
        tm.font = font;
        tm.fontSize = 40;
        tm.characterSize = textHeight / (tm.fontSize * 0.1f);
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = new Color(0.95f, 0.95f, 0.9f);
        go.GetComponent<MeshRenderer>().sharedMaterial = textMat;
        return new Button { t = t, size = size, label = tm, text = text, press = press, repeat = repeat, face = face };
    }

    MeshRenderer Quad(string name, Transform parent, Vector3 pos, Vector2 size, Material mat)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = name;
        if (Application.isPlaying) Destroy(q.GetComponent<Collider>()); else DestroyImmediate(q.GetComponent<Collider>());
        q.transform.SetParent(parent, false);
        q.transform.localPosition = pos;
        q.transform.localScale = new Vector3(size.x, size.y, 1f);
        var r = q.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return r;
    }

    void Update()
    {
        if (tab == null) return;
        if (input.MenuPressed) Toggle();
        if (hidden) return;
        cooldown -= Time.deltaTime;
        Poke(tab);
        if (open) foreach (var b in buttons) Poke(b);
        if (open) foreach (var b in buttons) b.label.text = b.text();
        tab.label.text = tab.text();
    }

    // A button is touched when a fingertip / controller tip is within its face (+5 mm) and from 1.5 cm in front to 4 cm behind it.
    void Poke(Button b)
    {
        bool inside = false;
        foreach (var h in hands.Both)
        {
            if (!h.Tracked) continue;
            Vector3 p = b.t.InverseTransformPoint(h.PokeTip);
            if (Mathf.Abs(p.x) < b.size.x * 0.5f + 0.005f && Mathf.Abs(p.y) < b.size.y * 0.5f + 0.005f && p.z > -0.015f && p.z < 0.04f)
                inside = true;
        }
        if (inside && (b.repeat || (!b.inside && cooldown <= 0f)))
        {
            if (!b.inside && click) click.Play();
            b.press();
            if (!b.repeat) cooldown = 0.3f;
        }
        b.inside = inside;
        b.face.sharedMaterial = inside ? buttonHotMat : buttonMat;
    }
}
