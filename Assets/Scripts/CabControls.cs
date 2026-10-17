using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Grabbable cab controls (controller grip button, or a fist / pinch with hand tracking).
///   Steering wheel: grab the rim and turn it, with one hand or two, hand over hand. The wheel stays where it was let go
///   (like real hydrostatic steering) until the left stick is used again. The speed-dependent comfort lock still applies,
///   so beyond it the wheel slips in the hand.
///   Speed lever: grab the knob of the lever at the front of the right armrest and move the hand forward / back.
///   It is geared down: 20 cm of hand travel from neutral to full (relative to where it was grabbed, so it never jumps).
///   A notchy neutral (±20 %) snaps it upright with a click. It stays where it is let go (cruise); the right stick or the
///   panel's STOP button puts it back to neutral.
/// Feedback: the rim / knob lights up softly when a hand is in reach and brighter while held, and a tracked hand is shown
/// snapped onto what it holds (hides tracking jitter).
/// </summary>
[DefaultExecutionOrder(-40)]   // after VRHands and TractorInput, before TractorController reads the steering / throttle
public class CabControls : MonoBehaviour
{
    public Transform speedLever;          // hinge at the bottom of the stem, tilts around local X
    public Vector3 knobLocal;             // knob centre in the hinge's space
    public float leverTilt = 25f;
    public float leverTravel = 0.20f;     // m of hand travel from neutral to full
    public float neutralNotch = 0.2f;     // share of the range that snaps to neutral
    public float rimRadius = 0.1985f;
    public float grabReach = 0.07f;       // m from the rim / knob
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
    float lever;                          // -1..1
    float leverAtGrab, handAtGrab;
    Renderer[] wheelRenderers, leverRenderers;
    MaterialPropertyBlock block;
    static readonly int HighlightId = Shader.PropertyToID("_Highlight");

    public bool WheelHeld => wheelHands.Count > 0;
    public bool LeverHeld => leverHand != null;
    public float Lever => lever;

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
        if (leverHand != null) { leverAtGrab = 0f; handAtGrab = HandForward(leverHand); }
    }

    bool NearKnob(VRHands.Hand h) => Vector3.Distance(h.GrabPoint, speedLever.TransformPoint(knobLocal)) < grabReach + 0.02f;

    bool NearRim(VRHands.Hand h)
    {
        Vector3 p = column.InverseTransformPoint(h.GrabPoint);
        return Mathf.Abs(new Vector2(p.x, p.y).magnitude - rimRadius) < grabReach && Mathf.Abs(p.z) < grabReach;
    }

    float HandForward(VRHands.Hand h) => Vector3.Dot(h.GrabPoint, transform.forward);

    void TryGrab(VRHands.Hand h)
    {
        if (leverHand == null && NearKnob(h))
        {
            leverHand = h;
            leverAtGrab = lever;
            handAtGrab = HandForward(h);
            return;
        }
        if (NearRim(h))
        {
            if (wheelHands.Count == 0) wheelAngle = -tractor.SteerAngle * tractor.steeringRatio;
            wheelHands.Add(h);
            lastAngle[h] = HandAngle(h);
        }
    }

    void Release(VRHands.Hand h)
    {
        if (leverHand == h) leverHand = null;
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
            float raw = Mathf.Clamp(leverAtGrab + (HandForward(leverHand) - handAtGrab) / leverTravel, -1f, 1f);
            float next = Mathf.Abs(raw) < neutralNotch ? 0f : raw;   // notch: snaps upright
            if (next == 0f && lever != 0f && click) click.Play();
            lever = next;
        }
        speedLever.localRotation = Quaternion.AngleAxis(lever * leverTilt, Vector3.right);
        input.leverThrottle = lever;
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
