using UnityEngine;

/// <summary>
/// Grabbable cab controls (controller grip button or a fist with hand tracking).
///   Steering wheel: grab the rim and turn it, with one hand or two, hand over hand. The wheel stays where it was let go
///   (like real hydrostatic steering) until the left stick is used again. The speed-dependent comfort lock still applies,
///   so beyond it the wheel slips in the hand.
///   Speed lever: grab the knob of the lever at the front of the right armrest and tilt it forward / back
///   (forward = drive, back = reverse, ±25°). It stays where it is let go, so the tractor keeps the set speed with both hands
///   on the wheel. Using the right stick puts it back to neutral.
/// </summary>
[DefaultExecutionOrder(-40)]   // after VRHands and TractorInput, before TractorController reads the steering / throttle
public class CabControls : MonoBehaviour
{
    public Transform speedLever;          // hinge at the bottom of the stalk, tilts around local X
    public Vector3 knobLocal;             // knob centre in the hinge's space
    public float leverTilt = 25f;
    public float rimRadius = 0.1985f;
    public float grabReach = 0.07f;       // m from the rim / knob

    VRHands hands;
    TractorController tractor;
    TractorInput input;
    Transform column;
    VRHands.Hand leverHand;
    readonly System.Collections.Generic.List<VRHands.Hand> wheelHands = new System.Collections.Generic.List<VRHands.Hand>();
    readonly System.Collections.Generic.Dictionary<VRHands.Hand, float> lastAngle = new System.Collections.Generic.Dictionary<VRHands.Hand, float>();
    float wheelAngle;                     // degrees the wheel is turned (steering wheel space, + = counter-clockwise for the driver)
    float lever;                          // -1..1

    public bool WheelHeld => wheelHands.Count > 0;
    public bool LeverHeld => leverHand != null;

    void Start()
    {
        hands = FindAnyObjectByType<VRHands>();
        tractor = GetComponent<TractorController>();
        input = GetComponent<TractorInput>();
        column = tractor.steeringWheel.parent;
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
    }

    void TryGrab(VRHands.Hand h)
    {
        if (leverHand == null && Vector3.Distance(h.GrabPoint, speedLever.TransformPoint(knobLocal)) < grabReach + 0.02f)
        {
            leverHand = h;
            return;
        }
        Vector3 p = column.InverseTransformPoint(h.GrabPoint);
        float r = new Vector2(p.x, p.y).magnitude;
        if (Mathf.Abs(r - rimRadius) < grabReach && Mathf.Abs(p.z) < grabReach)
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
            // Tilt = angle of the hand around the hinge in the side view, from the lever's upright rest direction.
            Vector3 rest = knobLocal;
            Vector3 hand = speedLever.parent.InverseTransformPoint(leverHand.GrabPoint) - speedLever.localPosition;
            float a = Vector3.SignedAngle(new Vector3(0f, rest.y, rest.z), new Vector3(0f, hand.y, hand.z), Vector3.right);
            // A positive rotation around X tips the top forward (+Y towards +Z): pushing forward = positive = drive.
            lever = Mathf.Clamp(a / leverTilt, -1f, 1f);
            if (Mathf.Abs(lever) < 0.08f) lever = 0f;   // neutral detent
        }
        speedLever.localRotation = Quaternion.AngleAxis(lever * leverTilt, Vector3.right);
        input.leverThrottle = lever;
    }
}
