using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Grabbable cab controls (controller grip button, or a fist / pinch with hand tracking).
///   Steering wheel: grab the rim and turn it, with one hand or two, hand over hand. The wheel stays where it was let go
///   (like real hydrostatic steering) until the left stick is used again. The speed-dependent comfort lock still applies,
///   so beyond it the wheel slips in the hand.
///   Speed lever: grab the knob of the lever at the front of the right armrest and swing it forward / back. The lever follows
///   the hand around its pivot (the knob stays in the hand), relative to where it was grabbed so it never jumps. Speed is 0
///   in the middle (±25 % of the swing, a click when passing through) and rises smoothly beyond it; let go in the middle and it
///   snaps upright. It stays where it is let go (cruise); the right stick or the panel's STOP button puts it back to neutral.
///   While held, the HUD shows what it does (N / F % / R %).
///   Hydraulic remote levers (three, behind the speed lever): spring-centred, pull back = raise, push forward = lower,
///   speed proportional to the deflection. 1 = rear hitch, 2 = front linkage, 3 = tipping trailer body.
///   Doors (rear-hinged): grab anywhere on the inside and swing them open / closed; they stay where they are let go.
///   The cab panel's DOORS button opens / closes both.
///   Indicator stalk (left of the column): grab it and push it up (right) / down (left); TractorLights reads it and moves it
///   back to the middle when the indicator cancels. It sits next to the rim, so the rim wins when the hand is closer to it.
/// Feedback: the rim / knob lights up softly when a hand is in reach and brighter while held, and a tracked hand is shown
/// snapped onto what it holds (hides tracking jitter).
/// </summary>
[DefaultExecutionOrder(-40)]   // after VRHands and TractorInput, before TractorController reads the steering / throttle
public class CabControls : MonoBehaviour
{
    public Transform speedLever;          // hinge at the bottom of the stem, tilts around local X
    public Vector3 knobLocal;             // knob centre in the hinge's space
    public float leverTilt = 25f;         // degrees each way
    public float neutralNotch = 0.25f;    // middle share of the swing where the speed is 0
    public float rimRadius = 0.1985f;
    public float grabReach = 0.07f;       // m from the rim / knob (controllers)
    public float handReach = 0.09f;       // m, tracked hands (the palm sits further from a thin rim than a controller's grip)
    public AudioSource click;
    public Swing[] remotes = new Swing[0];
    public Swing[] doors = new Swing[0];
    public Swing stalk;                   // indicator stalk (+ = up = right); no hinge = none
    public float doorSpeed = 90f;         // degrees per second when opened / closed from the panel
    public Color hoverGlow = new Color(0.04f, 0.10f, 0.04f), heldGlow = new Color(0.08f, 0.22f, 0.08f);

    /// <summary>A part that swings around a hinge axis (remote lever, door), grabbed anywhere in its grab box.</summary>
    [System.Serializable]
    public class Swing
    {
        public string name;
        public Transform hinge;           // rotates around `axis` (hinge space) from its rest rotation
        public Vector3 axis = Vector3.right;
        public Bounds grabBox;            // hinge space: where a hand can take hold
        public float min = -20f, max = 20f;   // degrees
        public bool springBack;           // returns to 0 when let go (remote levers)
        public AudioSource sound;         // a door clicks when it shuts and when it comes off the latch
        public float reach;               // > 0: grab distance from the grab box instead of the usual reach (small parts)

        [System.NonSerialized] public float angle, offset;
        [System.NonSerialized] public Vector3 grabLocal;   // where the hand took hold (hinge space), for the hand snap
        [System.NonSerialized] public float? target;   // panel open / close, spring return
        [System.NonSerialized] public VRHands.Hand hand;
        [System.NonSerialized] public Quaternion rest;
        [System.NonSerialized] public Renderer[] renderers;
        public bool Held => hand != null;
        /// <summary>-1..1 with a small dead band (remote levers: + = pushed forward).</summary>
        public float Output => Mathf.Abs(angle) < 0.15f * max ? 0f : angle / max;
    }

    VRHands hands;
    CabSwitches switches;
    TractorController tractor;
    TractorInput input;
    Hitch hitch;
    Transform column;
    VRHands.Hand leverHand;
    readonly List<VRHands.Hand> wheelHands = new List<VRHands.Hand>();
    readonly Dictionary<VRHands.Hand, float> lastAngle = new Dictionary<VRHands.Hand, float>();
    float wheelAngle;                     // degrees the wheel is turned (steering wheel space, + = counter-clockwise for the driver)
    float lever;                          // lever position -1..1 (what you see)
    float leverAngleOffset;               // lever angle minus hand angle at the grab (keeps it from jumping)
    Renderer[] wheelRenderers, leverRenderers;
    MaterialPropertyBlock block;
    static readonly int HighlightId = Shader.PropertyToID("_Highlight");

    public bool WheelHeld => wheelHands.Count > 0;
    public bool LeverHeld => leverHand != null;
    public bool DoorsOpen => doors.Length > 0 && doors[0].angle > 1f;
    public float Lever => lever;
    /// <summary>Speed command from the lever (-1..1): 0 in the neutral zone, smooth beyond it.</summary>
    public float LeverOutput => Mathf.Abs(lever) <= neutralNotch ? 0f : Mathf.Sign(lever) * (Mathf.Abs(lever) - neutralNotch) / (1f - neutralNotch);
    /// <summary>"N", "F 35%", "R 20%" while the lever is held (HUD), else empty.</summary>
    public string LeverStatus => !LeverHeld ? "" : LeverOutput == 0f ? "LEVER: N" : $"LEVER: {(LeverOutput > 0f ? "F" : "R")} {Mathf.Abs(LeverOutput) * 100f:0}%";
    static readonly string[] RemoteJobs = { "HITCH", "FRONT", "TIPPER" };
    /// <summary>Everything held right now, for the HUD (speed lever, remote levers, doors).</summary>
    public string Status
    {
        get
        {
            var lines = new List<string>();
            if (LeverHeld) lines.Add(LeverStatus);
            for (int i = 0; i < remotes.Length; i++)
            {
                if (!remotes[i].Held) continue;
                float o = remotes[i].Output;
                lines.Add($"REMOTE {i + 1} {RemoteJobs[Mathf.Min(i, 2)]}: {(o == 0f ? "HOLD" : o < 0f ? $"RAISE {-o * 100f:0}%" : $"LOWER {o * 100f:0}%")}");
            }
            foreach (var d in doors)
                if (d.Held) lines.Add($"{d.name}: {d.angle / d.max * 100f:0}% OPEN");
            return string.Join("\n", lines);
        }
    }

    void Start()
    {
        hands = FindAnyObjectByType<VRHands>();
        tractor = GetComponent<TractorController>();
        input = GetComponent<TractorInput>();
        column = tractor.steeringWheel.parent;
        wheelRenderers = tractor.steeringWheel.GetComponentsInChildren<Renderer>();
        leverRenderers = speedLever.GetComponentsInChildren<Renderer>();
        block = new MaterialPropertyBlock();
        hitch = GetComponent<Hitch>();
        switches = GetComponent<CabSwitches>();
        foreach (var sw in Swings())
        {
            sw.rest = sw.hinge.localRotation;
            sw.renderers = sw.hinge.GetComponentsInChildren<Renderer>();
        }
    }

    IEnumerable<Swing> Swings()
    {
        foreach (var r in remotes) yield return r;
        foreach (var d in doors) yield return d;
        if (stalk != null && stalk.hinge) yield return stalk;
    }

    /// <summary>Open both doors if they're closed, else close them (cab panel).</summary>
    public void ToggleDoors()
    {
        bool open = !DoorsOpen;
        foreach (var d in doors) if (!d.Held) d.target = open ? d.max : 0f;
    }

    void Update() => FrameUpdate(Time.deltaTime);

    /// <summary>Per-frame update (public for the editor physics test).</summary>
    public void FrameUpdate(float dt)
    {
        if (hands == null) return;
        foreach (var h in hands.Both)
        {
            if (h.GrabStarted) TryGrab(h);
            if (h.GrabEnded || !h.Tracked) Release(h);
        }
        UpdateWheel();
        UpdateLever();
        UpdateSwings(dt);
        UpdateFeedback();
    }

    /// <summary>Lever to neutral (cab panel STOP).</summary>
    public void Neutral()
    {
        lever = 0f;
        if (leverHand != null) leverAngleOffset = -HandLeverAngle(leverHand);
    }

    float Reach(VRHands.Hand h) => h.IsHandTracking ? handReach : grabReach;

    bool NearKnob(VRHands.Hand h) => Vector3.Distance(h.GrabPoint, speedLever.TransformPoint(knobLocal)) < Reach(h) + 0.02f;

    float RimDistance(VRHands.Hand h)
    {
        Vector3 p = column.InverseTransformPoint(h.GrabPoint);
        return new Vector2(new Vector2(p.x, p.y).magnitude - rimRadius, p.z).magnitude;
    }

    bool NearRim(VRHands.Hand h)
    {
        Vector3 p = column.InverseTransformPoint(h.GrabPoint);
        return Mathf.Abs(new Vector2(p.x, p.y).magnitude - rimRadius) < Reach(h) && Mathf.Abs(p.z) < Reach(h);
    }

    // Angle of the hand around the lever's pivot in its swing plane (side view of the tractor), from the lever's rest
    // direction; positive = forward (the same sense as the lever's tilt).
    float HandLeverAngle(VRHands.Hand h)
    {
        Vector3 hand = transform.InverseTransformPoint(h.GrabPoint) - speedLever.localPosition;
        return Vector3.SignedAngle(new Vector3(0f, knobLocal.y, knobLocal.z), new Vector3(0f, hand.y, hand.z), Vector3.right);
    }

    bool NearSwing(VRHands.Hand h, Swing sw)
    {
        var box = sw.grabBox;
        box.Expand(2f * (sw.reach > 0f ? sw.reach : Reach(h)));
        return box.Contains(sw.hinge.InverseTransformPoint(h.GrabPoint));
    }

    // Angle of the hand around a swing's hinge axis from the swing's rest pose (degrees, + = the swing's positive sense).
    float HandSwingAngle(VRHands.Hand h, Swing sw)
    {
        Vector3 axis = sw.rest * sw.axis;                                            // in the parent's space
        Vector3 hand = sw.hinge.parent.InverseTransformPoint(h.GrabPoint) - sw.hinge.localPosition;
        Vector3 rest = sw.rest * sw.grabBox.center;
        return Vector3.SignedAngle(Vector3.ProjectOnPlane(rest, axis), Vector3.ProjectOnPlane(hand, axis), axis);
    }

    void TryGrab(VRHands.Hand h)
    {
        // A fingertip on a console switch is pressing it: no grab (the remote levers stand right next to the switches).
        if (switches && switches.Touching(h.PokeTip)) return;
        // The nearest remote lever / door in reach; the speed lever wins if its knob is closer (they sit side by side).
        Swing best = null;
        float bestDistance = float.MaxValue;
        foreach (var sw in Swings())
        {
            if (sw.Held || !NearSwing(h, sw)) continue;
            Vector3 local = sw.hinge.InverseTransformPoint(h.GrabPoint);
            float d = Vector3.Distance(sw.grabBox.ClosestPoint(local), local);
            if (sw.reach > 0f && NearRim(h) && RimDistance(h) < d) continue;   // the stalk sits right behind the rim
            if (d < bestDistance) { best = sw; bestDistance = d; }
        }
        float knobDistance = leverHand == null && NearKnob(h) ? Vector3.Distance(h.GrabPoint, speedLever.TransformPoint(knobLocal)) : float.MaxValue;
        if (best != null && bestDistance <= knobDistance)
        {
            best.hand = h;
            best.target = null;
            best.offset = best.angle - HandSwingAngle(h, best);
            best.grabLocal = best.hinge.InverseTransformPoint(h.GrabPoint);
            Debug.Log($"[Cab] {best.name} grabbed ({(h.IsHandTracking ? (h.PinchGrab ? "hand, pinch" : "hand, fist") : "controller")}) at {best.angle:0}°");
            return;
        }
        if (leverHand == null && NearKnob(h))
        {
            leverHand = h;
            leverAngleOffset = lever * leverTilt - HandLeverAngle(h);
            Debug.Log($"[Cab] lever grabbed ({(h.IsHandTracking ? (h.PinchGrab ? "hand, pinch" : "hand, fist") : "controller")}) at {lever:0.00}");
            return;
        }
        if (NearRim(h))
        {
            Debug.Log($"[Cab] wheel grabbed ({(h.IsHandTracking ? (h.PinchGrab ? "hand, pinch" : "hand, fist") : "controller")})");
            if (wheelHands.Count == 0) wheelAngle = -tractor.SteerAngle * tractor.steeringRatio;
            wheelHands.Add(h);
            lastAngle[h] = HandAngle(h);
        }
    }

    void Release(VRHands.Hand h)
    {
        if (leverHand == h)
        {
            leverHand = null;
            if (LeverOutput == 0f) lever = 0f;   // let go in the middle: snaps upright
            Debug.Log($"[Cab] lever released at {lever:0.00} (speed command {LeverOutput:0.00})");
        }
        if (wheelHands.Remove(h) && wheelHands.Count == 0) tractor.ReleaseHeldSteer();
        foreach (var sw in Swings())
        {
            if (sw.hand != h) continue;
            sw.hand = null;
            if (sw.springBack) sw.target = 0f;
            Debug.Log($"[Cab] {sw.name} released at {sw.angle:0}°");
        }
        hands.SnapVisual(h, null);
    }

    float HandAngle(VRHands.Hand h)
    {
        Vector3 p = column.InverseTransformPoint(h.GrabPoint);
        return Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
    }

    void UpdateWheel()
    {
        if (wheelHands.Count == 0) return;
        // Average turn of the holding hands since last frame (each hand's own angle, so hand over hand works).
        float delta = 0f;
        foreach (var h in wheelHands)
        {
            float a = HandAngle(h);
            delta += Mathf.DeltaAngle(lastAngle[h], a);
            lastAngle[h] = a;
        }
        wheelAngle += delta / wheelHands.Count;
        // Steering wheel space is counter-clockwise positive; a right turn (positive SteerAngle) is clockwise.
        float limit = tractor.LockAngle * tractor.steeringRatio;
        wheelAngle = Mathf.Clamp(wheelAngle, -limit, limit);
        tractor.HoldSteer(-wheelAngle / tractor.steeringRatio);
    }

    void UpdateLever()
    {
        if (Mathf.Abs(input.RightStick.y) > 0.1f && leverHand == null) lever = 0f;   // the stick takes over
        if (leverHand != null)
        {
            float before = LeverOutput;
            lever = Mathf.Clamp((HandLeverAngle(leverHand) + leverAngleOffset) / leverTilt, -1f, 1f);
            if ((before == 0f) != (LeverOutput == 0f) && click) click.Play();   // passing the edge of neutral
        }
        speedLever.localRotation = Quaternion.AngleAxis(lever * leverTilt, Vector3.right);
        input.leverThrottle = LeverOutput;
    }

    void UpdateSwings(float dt)
    {
        foreach (var sw in Swings())
        {
            float before = sw.angle;
            if (sw.Held) sw.angle = Mathf.Clamp(HandSwingAngle(sw.hand, sw) + sw.offset, sw.min, sw.max);
            else if (sw.target.HasValue)
            {
                // Spring-centred levers snap back quickly; doors swing at a steady pace.
                sw.angle = Mathf.MoveTowards(sw.angle, sw.target.Value, (sw.springBack ? 400f : doorSpeed) * dt);
                if (sw.angle == sw.target.Value) sw.target = null;
            }
            sw.hinge.localRotation = sw.rest * Quaternion.AngleAxis(sw.angle, sw.axis);
            if (sw.sound && (before > 0.5f) != (sw.angle > 0.5f)) sw.sound.Play();
        }
        // Remote levers: 1 rear hitch, 2 front linkage, 3 tipping trailer (pull back = raise).
        if (hitch)
        {
            if (remotes.Length > 0 && remotes[0].Output != 0f) hitch.MenuLift(false, -remotes[0].Output);
            if (remotes.Length > 1 && remotes[1].Output != 0f) hitch.MenuLift(true, -remotes[1].Output);
            if (remotes.Length > 2 && remotes[2].Output != 0f) hitch.MenuTip(-remotes[2].Output);
        }
    }

    void UpdateFeedback()
    {
        bool wheelNear = false, leverNear = false;
        foreach (var h in hands.Both)
        {
            if (!h.Tracked || h.Grabbing) continue;
            wheelNear |= NearRim(h);
            leverNear |= NearKnob(h);
        }
        Glow(wheelRenderers, WheelHeld ? heldGlow : wheelNear ? hoverGlow : Color.black);
        Glow(leverRenderers, LeverHeld ? heldGlow : leverNear ? hoverGlow : Color.black);
        foreach (var sw in Swings())
        {
            bool near = false;
            foreach (var h in hands.Both) near |= h.Tracked && !h.Grabbing && NearSwing(h, sw);
            Glow(sw.renderers, sw.Held ? heldGlow : near ? hoverGlow : Color.black);
            if (sw.Held) hands.SnapVisual(sw.hand, sw.hinge.TransformPoint(sw.grabLocal));
        }

        // The visible tracked hand sits on the rim at its own angle, or on the knob.
        foreach (var h in wheelHands)
        {
            Vector3 p = column.InverseTransformPoint(h.GrabPoint);
            Vector2 dir = new Vector2(p.x, p.y).normalized;
            hands.SnapVisual(h, column.TransformPoint(new Vector3(dir.x, dir.y, 0f) * rimRadius));
        }
        if (leverHand != null) hands.SnapVisual(leverHand, speedLever.TransformPoint(knobLocal));
    }

    void Glow(Renderer[] renderers, Color c)
    {
        foreach (var r in renderers)
        {
            r.GetPropertyBlock(block);
            if (block.GetColor(HighlightId) == c) continue;
            block.SetColor(HighlightId, c);
            r.SetPropertyBlock(block);
        }
    }
}
