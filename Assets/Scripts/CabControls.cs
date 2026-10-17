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
    public Color hoverGlow = new Color(0.04f, 0.10f, 0.04f), heldGlow = new Color(0.08f, 0.22f, 0.08f);

    VRHands hands;
    TractorController tractor;
    TractorInput input;
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
    public float Lever => lever;
    /// <summary>Speed command from the lever (-1..1): 0 in the neutral zone, smooth beyond it.</summary>
    public float LeverOutput => Mathf.Abs(lever) <= neutralNotch ? 0f : Mathf.Sign(lever) * (Mathf.Abs(lever) - neutralNotch) / (1f - neutralNotch);
    /// <summary>"N", "F 35%", "R 20%" while the lever is held (HUD), else empty.</summary>
    public string LeverStatus => !LeverHeld ? "" : LeverOutput == 0f ? "LEVER: N" : $"LEVER: {(LeverOutput > 0f ? "F" : "R")} {Mathf.Abs(LeverOutput) * 100f:0}%";

    void Start()
    {
        hands = FindAnyObjectByType<VRHands>();
        tractor = GetComponent<TractorController>();
        input = GetComponent<TractorInput>();
        column = tractor.steeringWheel.parent;
        wheelRenderers = tractor.steeringWheel.GetComponentsInChildren<Renderer>();
        leverRenderers = speedLever.GetComponentsInChildren<Renderer>();
        block = new MaterialPropertyBlock();
    }

    void Update()
    {
        if (hands == null) return;
        foreach (var h in hands.Both)
        {
            if (h.GrabStarted) TryGrab(h);
            if (h.GrabEnded || !h.Tracked) Release(h);
        }
        UpdateWheel();
        UpdateLever();
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

    void TryGrab(VRHands.Hand h)
    {
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
