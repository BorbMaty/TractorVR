using UnityEngine;

/// <summary>
/// CommandCenter-style display on the right front corner post (fixed to the tractor, not the rig): engine speed, hand throttle,
/// fuel level and use, coolant temperature, engine power, linkage positions, PTO, engine hours. Two touch buttons move the
/// hand throttle (fingertip / controller tip, like the cab panel). Built at runtime from the materials set by the setup script.
/// </summary>
public class CommandCenter : MonoBehaviour
{
    public Drivetrain drivetrain;
    public TractorController tractor;
    public Hitch hitch;
    public VRHands hands;
    public Material screenMat, bezelMat, barMat, buttonMat, buttonHotMat, textMat;
    public AudioSource click;

    public const float W = 0.24f, H = 0.15f;   // screen size (m), about a 10" display

    TractorLights lights;
    TextMesh rpmText, handText, fuelText, tempText, useText, powerText, linkText, hoursText, slipText, driveText;
    Transform fuelBar, tempBar;
    MeshRenderer fuelFill, tempFill;
    MaterialPropertyBlock block;
    Font font;
    bool built;

    class Button { public Transform t; public Vector2 size; public MeshRenderer face; public float dir; public bool inside; }
    Button down, up;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static Mesh quadMesh;
    static Mesh QuadMesh => quadMesh ? quadMesh : quadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

    void Start() => Build();

    /// <summary>Builds the screen (also outside play mode, for the preview renders).</summary>
    public void Build()
    {
        if (built) return;
        built = true;
        block = new MaterialPropertyBlock();
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Quad("Bezel", transform, new Vector3(0f, 0f, 0.003f), new Vector2(W + 0.016f, H + 0.016f), bezelMat);
        Quad("Screen", transform, Vector3.zero, new Vector2(W, H), screenMat);

        float left = -W * 0.5f + 0.012f, right = W * 0.5f - 0.012f, row = 0.0155f, top = H * 0.5f - 0.016f;
        var white = new Color(0.92f, 0.94f, 0.9f);
        var grey = new Color(0.6f, 0.65f, 0.6f);
        var yellow = new Color(1f, 0.85f, 0.3f);
        Label("COMMANDCENTER", new Vector3(left, top, 0f), 0.0065f, grey, TextAnchor.MiddleLeft);
        hoursText = Label("", new Vector3(right, top, 0f), 0.0065f, grey, TextAnchor.MiddleRight);
        driveText = Label("", new Vector3(0.012f, top, 0f), 0.0065f, yellow, TextAnchor.MiddleCenter);
        rpmText = Label("", new Vector3(left, top - row * 1.3f, 0f), 0.012f, yellow, TextAnchor.MiddleLeft);
        handText = Label("", new Vector3(right, top - row * 1.3f, 0f), 0.009f, white, TextAnchor.MiddleRight);

        float barLeft = left + 0.042f, barLen = 0.12f;
        Label("FUEL", new Vector3(left, top - row * 2.6f, 0f), 0.0085f, grey, TextAnchor.MiddleLeft);
        fuelBar = Bar("FuelBar", new Vector3(barLeft, top - row * 2.6f, 0f), barLen, out fuelFill);
        fuelText = Label("", new Vector3(right, top - row * 2.6f, 0f), 0.0085f, white, TextAnchor.MiddleRight);
        Label("TEMP", new Vector3(left, top - row * 3.6f, 0f), 0.0085f, grey, TextAnchor.MiddleLeft);
        tempBar = Bar("TempBar", new Vector3(barLeft, top - row * 3.6f, 0f), barLen, out tempFill);
        tempText = Label("", new Vector3(right, top - row * 3.6f, 0f), 0.0085f, white, TextAnchor.MiddleRight);

        useText = Label("", new Vector3(left, top - row * 4.7f, 0f), 0.0085f, white, TextAnchor.MiddleLeft);
        slipText = Label("", new Vector3(right, top - row * 4.7f, 0f), 0.0085f, white, TextAnchor.MiddleRight);
        powerText = Label("", new Vector3(left, top - row * 5.6f, 0f), 0.0085f, white, TextAnchor.MiddleLeft);
        linkText = Label("", new Vector3(left, top - row * 6.5f, 0f), 0.0085f, white, TextAnchor.MiddleLeft);

        var size = new Vector2(0.095f, 0.022f);
        float by = -H * 0.5f + 0.006f + size.y * 0.5f;
        down = MakeButton("ThrottleDown", new Vector3(-0.053f, by, -0.001f), size, "THROTTLE -", -1f);
        up = MakeButton("ThrottleUp", new Vector3(0.053f, by, -0.001f), size, "THROTTLE +", 1f);
        Refresh();
    }

    MeshRenderer Quad(string name, Transform parent, Vector3 pos, Vector2 size, Material mat)
    {
        // No CreatePrimitive: its MeshCollider would sit on the tractor's Rigidbody until the deferred Destroy (physics error).
        var q = new GameObject(name);
        q.AddComponent<MeshFilter>().sharedMesh = QuadMesh;
        q.transform.SetParent(parent, false);
        q.transform.localPosition = pos;
        q.transform.localScale = new Vector3(size.x, size.y, 1f);
        var r = q.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return r;
    }

    TextMesh Label(string text, Vector3 pos, float height, Color color, TextAnchor anchor, Transform parent = null)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent ? parent : transform, false);
        go.transform.localPosition = pos + new Vector3(0f, 0f, -0.0015f);
        var tm = go.AddComponent<TextMesh>();
        tm.font = font;
        tm.fontSize = 48;
        tm.characterSize = height / (tm.fontSize * 0.1f);   // TextMesh glyph height ≈ characterSize × fontSize / 10
        tm.anchor = anchor;
        tm.alignment = anchor == TextAnchor.MiddleRight ? TextAlignment.Right : anchor == TextAnchor.MiddleLeft ? TextAlignment.Left : TextAlignment.Center;
        tm.color = color;
        tm.text = text;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = textMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return tm;
    }

    // A dark track with a coloured fill growing from its left end (the pivot's X scale = share).
    Transform Bar(string name, Vector3 leftEnd, float length, out MeshRenderer fill)
    {
        Quad(name + "Track", transform, leftEnd + new Vector3(length * 0.5f, 0f, -0.0005f), new Vector2(length, 0.008f), buttonMat);
        var pivot = new GameObject(name).transform;
        pivot.SetParent(transform, false);
        pivot.localPosition = leftEnd + new Vector3(0f, 0f, -0.001f);
        fill = Quad("Fill", pivot, new Vector3(0.5f * length, 0f, 0f), new Vector2(length, 0.008f), barMat);
        return pivot;
    }

    Button MakeButton(string name, Vector3 pos, Vector2 size, string text, float dir)
    {
        var t = new GameObject("Button_" + name).transform;
        t.SetParent(transform, false);
        t.localPosition = pos;
        var face = Quad("Face", t, Vector3.zero, size, buttonMat);
        Label(text, Vector3.zero, 0.009f, new Color(0.95f, 0.95f, 0.9f), TextAnchor.MiddleCenter, t);
        return new Button { t = t, size = size, face = face, dir = dir };
    }

    void Update()
    {
        if (!built) return;
        Poke(down);
        Poke(up);
        Refresh();
    }

    // Touched like the cab panel: a fingertip / controller tip within the face (+5 mm), 1.5 cm in front to 4 cm behind it.
    // Held down = the hand throttle keeps moving.
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
        if (inside)
        {
            if (!b.inside && click) click.Play();
            Press(b.dir, Time.deltaTime);
        }
        b.inside = inside;
        b.face.sharedMaterial = inside ? buttonHotMat : buttonMat;
    }

    /// <summary>Hand throttle button held for dt (also used by the play-mode test).</summary>
    public void Press(float dir, float dt) => drivetrain.MoveHandThrottle(dir, dt);

    void Refresh()
    {
        if (drivetrain == null) return;
        rpmText.text = $"{drivetrain.Rpm:0} rpm";
        handText.text = drivetrain.HandThrottleRpm > drivetrain.idleRpm + 1f ? $"HAND {drivetrain.HandThrottleRpm:0}" : "HAND OFF";
        float fuel = drivetrain.FuelShare;
        fuelText.text = $"{fuel * 100f:0}%  {drivetrain.FuelLitres:0} L";
        SetBar(fuelBar, fuelFill, fuel, fuel < 0.1f ? new Color(0.9f, 0.15f, 0.1f) : fuel < 0.2f ? new Color(1f, 0.7f, 0.1f) : new Color(0.35f, 0.75f, 0.25f));
        float c = drivetrain.CoolantC;
        tempText.text = $"{c:0} °C";
        SetBar(tempBar, tempFill, Mathf.InverseLerp(40f, 120f, c),
            c < 60f ? new Color(0.3f, 0.55f, 0.95f) : c > 105f ? new Color(0.9f, 0.15f, 0.1f) : new Color(0.35f, 0.75f, 0.25f));
        useText.text = $"FUEL USE {drivetrain.FuelRateLph:0.0} L/h";
        powerText.text = $"POWER {drivetrain.OutputKW:0} kW of {drivetrain.AvailableKW:0} kW  ({drivetrain.TorqueAt(drivetrain.Rpm):0} Nm)";
        var tipper = hitch ? hitch.TowedTipper : null;
        linkText.text = hitch ? $"HITCH {hitch.RearPosition * 100f:0}%   FRONT {hitch.FrontPosition * 100f:0}%   PTO {(hitch.Pto ? $"{hitch.PtoSpeed:0}" : "OFF")}" +
                                (tipper ? $"   TIP {tipper.Share * 100f:0}%" : "") : "";
        hoursText.text = $"{drivetrain.Hours:0.00} h";
        if (tractor)
        {
            // Wheel slip as the real display shows it (radar vs. wheel speed); red above 20 %.
            slipText.text = $"SLIP {tractor.Slip * 100f:0}%";
            slipText.color = tractor.Slip > 0.2f ? new Color(1f, 0.35f, 0.25f) : new Color(0.92f, 0.94f, 0.9f);
            // Indicator tell-tales blink either side of the drive state.
            if (!lights) lights = tractor.GetComponent<TractorLights>();
            bool left = lights && lights.BlinkOn && (lights.Hazards || lights.Indicator < 0), right = lights && lights.BlinkOn && (lights.Hazards || lights.Indicator > 0);
            driveText.text = (left ? "<<  " : "") + (tractor.fourWheelDrive ? "4WD" : "2WD") + (tractor.DiffLock ? "  DIFF LOCK" : "") + (tractor.allWheelBrakes ? "" : "  REAR BRAKES") + (right ? "  >>" : "");
        }
    }

    void SetBar(Transform pivot, MeshRenderer fill, float share, Color color)
    {
        pivot.localScale = new Vector3(Mathf.Clamp01(share), 1f, 1f);
        fill.GetPropertyBlock(block);
        block.SetColor(BaseColorId, color);
        fill.SetPropertyBlock(block);
    }
}
