using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hydraulics and implement couplings.
///   Rear three-point linkage: the lift arms turn on the rockshaft; the lift rods hang from the arm tips and carry the
///   lower links. This four-bar linkage is solved from the model's own pivot points (set up by ProjectSetup), and the
///   lift cylinders follow. Position 0 = lowest, 1 = highest.
///   Front linkage: the lower links fold from forward-down (0) up to the stowed, upright position of the model (1).
///   Drawbar: fixed coupling point for trailed implements.
///   PTO: on/off; the engine holds PTO speed (see Drivetrain), implements report the power they take.
/// Controls: left stick ↑↓ = rear hitch (hold left trigger = front linkage), right stick click = PTO, left stick click = attach / detach.
/// Attaching: back up (or drive up, for the front) until the implement's coupling point is within reach, then press attach.
/// </summary>
[DefaultExecutionOrder(-50)]  // before TractorController and the implements read positions / forces
public class Hitch : MonoBehaviour
{
    [Header("Rear three-point linkage (filled by setup)")]
    public Transform liftArms;          // hinge on the rockshaft
    public Transform lowerLinks;        // hinge at the lower link pivots
    public Transform[] liftRods = new Transform[0];     // hinges at the arm tips
    public Transform[] stabilisers = new Transform[0];  // follow the lower links
    public Transform cylinderBarrel, cylinderRod;       // barrel hinge at its base, rod hinge on the arm
    public Vector3 rockshaftPivot, armTip, rodBottom, lowerPivot, linkEnd, cylinderBase, cylinderTop;
    public Transform rearMount;         // follows the lower link ends (x = 0)

    [Header("Front linkage (filled by setup)")]
    public Transform[] frontLinks = new Transform[0];
    public Vector3 frontPivot, frontTip;
    public Transform frontMount;

    [Header("Drawbar")]
    public Transform drawbarMount;

    [Header("Operation")]
    public float liftTime = 4f;         // s for a full stroke
    public float attachReach = 1.5f;    // m between coupling points
    public float attachAngle = 30f;     // degrees of heading difference
    public float ptoRpm = 1000f;

    float menuRear, menuFront;
    /// <summary>Cab panel buttons: move the rear hitch / front linkage this frame (-1 lower, +1 raise).</summary>
    public void MenuLift(bool frontLinkage, float direction) { if (frontLinkage) menuFront = direction; else menuRear = direction; }

    public float RearPosition { get; private set; }
    public float FrontPosition { get; private set; } = 1f;
    public bool Pto { get; private set; }
    public float PtoSpeed { get; private set; }          // rpm at the shaft
    public Implement Rear { get; private set; }
    public Implement Front { get; private set; }
    public Implement Towed { get; private set; }
    /// <summary>Closest implement that could be attached, and how far its coupling point is (for the HUD).</summary>
    public Implement Nearest { get; private set; }
    public float NearestDistance { get; private set; }

    public event System.Action<string> Message;

    TractorController tractor;
    TractorInput input;
    Drivetrain drivetrain;
    Rigidbody rb;
    float baseMass;
    Vector3 baseCom;
    float rearSign = 1f, frontSign = 1f;
    readonly List<Vector2> rearTable = new List<Vector2>();   // (arm angle θ, lower link angle φ) for position 0..1
    float frontLow;                                          // front link angle at position 0
    float rodLength;
    float restPosition;
    Implement[] all = new Implement[0];

    void Awake()
    {
        tractor = GetComponent<TractorController>();
        input = GetComponent<TractorInput>();
        drivetrain = GetComponent<Drivetrain>();
        rb = GetComponent<Rigidbody>();
        BuildRearTable();
        BuildFront();
        RearPosition = restPosition;
        ApplyRear(RearPosition);
        ApplyFront(FrontPosition);
    }

    void Start()
    {
        baseMass = rb.mass;
        baseCom = rb.centerOfMass;
        all = FindObjectsByType<Implement>(FindObjectsSortMode.None);
    }

    /// <summary>Poses the linkages without running (editor preview renders).</summary>
    public void PreviewPose(float rear, float front)
    {
        if (rearTable.Count == 0) { BuildRearTable(); BuildFront(); }
        ApplyRear(rear);
        ApplyFront(front);
    }

    /// <summary>Re-reads the scene's implements (the editor test calls this after Awake).</summary>
    public void Init()
    {
        Start();
    }

    // ------------------------------------------------------------------ kinematics
    static Vector3 Rot(float deg, Vector3 v, Vector3 pivot) => Quaternion.AngleAxis(deg, Vector3.right) * (v - pivot) + pivot;
    static Vector3 YZ(Vector3 v) => new Vector3(0f, v.y, v.z);

    void BuildRearTable()
    {
        rodLength = Vector3.Distance(YZ(armTip), YZ(rodBottom));
        // Which way of turning the rockshaft raises the links?
        rearSign = 1f;
        if (SolveLower(2f, 0f, out float up) && LinkEndY(up) < LinkEndY(0f)) rearSign = -1f;

        // Walk the arm angle both ways from the modelled pose while the linkage closes and the link ends stay in range.
        var down = new List<Vector2>();
        var upList = new List<Vector2>();
        float phi = 0f;
        for (float t = 0f; t >= -60f; t -= 0.5f)
        {
            if (!SolveLower(t, phi, out phi) || LinkEndY(phi) < 0.18f) break;
            down.Add(new Vector2(t, phi));
        }
        phi = 0f;
        for (float t = 0.5f; t <= 60f; t += 0.5f)
        {
            if (!SolveLower(t, phi, out phi) || LinkEndY(phi) > 1.15f) break;
            upList.Add(new Vector2(t, phi));
        }
        down.Reverse();
        rearTable.Clear();
        rearTable.AddRange(down);
        rearTable.AddRange(upList);
        restPosition = rearTable.Count > 1 ? (down.Count - 1) / (float)(rearTable.Count - 1) : 0f;
    }

    // Lower link angle that puts the rod's bottom end at rod length from the arm tip (bisection near the previous answer).
    bool SolveLower(float theta, float guess, out float phi)
    {
        Vector3 a = YZ(Rot(rearSign * theta, armTip, rockshaftPivot));
        float F(float p) => Vector3.Distance(a, YZ(Rot(p, rodBottom, lowerPivot))) - rodLength;
        phi = guess;
        for (float step = 1f; step <= 60f; step += 1f)
        {
            foreach (float s in new[] { guess - step, guess + step })
            {
                float lo = Mathf.Min(guess, s), hi = Mathf.Max(guess, s);
                if (F(lo) * F(hi) > 0f) continue;
                for (int i = 0; i < 30; i++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (F(lo) * F(mid) <= 0f) hi = mid; else lo = mid;
                }
                phi = 0.5f * (lo + hi);
                return true;
            }
        }
        return Mathf.Abs(F(guess)) < 1e-3f;
    }

    float LinkEndY(float phi) => Rot(phi, linkEnd, lowerPivot).y;

    void ApplyRear(float p)
    {
        if (rearTable.Count == 0 || liftArms == null) return;
        float f = Mathf.Clamp01(p) * (rearTable.Count - 1);
        int i = Mathf.Min((int)f, rearTable.Count - 2);
        Vector2 ang = rearTable.Count > 1 ? Vector2.Lerp(rearTable[i], rearTable[i + 1], f - i) : rearTable[0];
        float theta = rearSign * ang.x, phi = ang.y;

        liftArms.localRotation = Quaternion.AngleAxis(theta, Vector3.right);
        lowerLinks.localRotation = Quaternion.AngleAxis(phi, Vector3.right);
        foreach (var s in stabilisers) s.localRotation = Quaternion.AngleAxis(phi, Vector3.right);

        // Lift rods: from the arm tip to the point on the lower link.
        Vector3 a = Rot(theta, armTip, rockshaftPivot), b = Rot(phi, rodBottom, lowerPivot);
        float rodAngle = Vector3.SignedAngle(YZ(rodBottom - armTip), YZ(b - a), Vector3.right);
        foreach (var r in liftRods)
        {
            r.localPosition = new Vector3(r.localPosition.x, a.y, a.z);
            r.localRotation = Quaternion.AngleAxis(rodAngle, Vector3.right);
        }

        // Lift cylinders: base fixed on the axle housing, top pinned to the arm.
        Vector3 top = Rot(theta, cylinderTop, rockshaftPivot);
        float cylAngle = Vector3.SignedAngle(YZ(cylinderTop - cylinderBase), YZ(top - cylinderBase), Vector3.right);
        if (cylinderBarrel) cylinderBarrel.localRotation = Quaternion.AngleAxis(cylAngle, Vector3.right);
        if (cylinderRod)
        {
            cylinderRod.localPosition = new Vector3(cylinderRod.localPosition.x, top.y, top.z);
            cylinderRod.localRotation = Quaternion.AngleAxis(cylAngle, Vector3.right);
        }

        Vector3 end = Rot(phi, linkEnd, lowerPivot);
        rearMount.localPosition = new Vector3(0f, end.y, end.z);
    }

    void BuildFront()
    {
        if (frontLinks.Length == 0) return;
        // Folding forward = the tip moves to +Z.
        frontSign = Rot(2f, frontTip, frontPivot).z > frontTip.z ? 1f : -1f;
        // Lowest working position: tip 0.42 m above the ground, in front of the pivot.
        frontLow = 0f;
        for (float a = 0f; a <= 170f; a += 0.5f)
        {
            Vector3 t = Rot(frontSign * a, frontTip, frontPivot);
            frontLow = a;
            if (t.y <= 0.42f && t.z > frontPivot.z) break;
        }
    }

    void ApplyFront(float p)
    {
        if (frontLinks.Length == 0) return;
        float a = frontSign * Mathf.Lerp(frontLow, 0f, Mathf.Clamp01(p));
        foreach (var l in frontLinks) l.localRotation = Quaternion.AngleAxis(a, Vector3.right);
        Vector3 tip = Rot(a, frontTip, frontPivot);
        frontMount.localPosition = new Vector3(0f, tip.y, tip.z);
    }

    // ------------------------------------------------------------------ operation
    void Update() => FrameUpdate(Time.deltaTime);

    public void FrameUpdate(float dt)
    {
        float lever = menuRear != 0f ? menuRear : input.HitchLever;
        bool front = input.FrontLinkageHeld;
        if (menuFront != 0f) { lever = menuFront; front = true; }
        menuRear = menuFront = 0f;
        if (input.PtoPressed) SetPto(!Pto);
        if (input.AttachPressed) ToggleAttach();

        float rate = dt / liftTime;
        if (front) FrontPosition = MoveLinkage(FrontPosition, lever, rate, Front);
        else RearPosition = MoveLinkage(RearPosition, lever, rate, Rear);
        ApplyRear(RearPosition);
        ApplyFront(FrontPosition);

        PtoSpeed = Mathf.MoveTowards(PtoSpeed, Pto ? ptoRpm * drivetrain.Rpm / drivetrain.ptoEngineRpm : 0f, ptoRpm * dt);
        FindNearest();
    }

    // Lever + position control: an implement in the ground can't be pushed deeper than its working depth,
    // and is lifted back to it (like draft control) when the ground rises.
    float MoveLinkage(float pos, float lever, float rate, Implement imp)
    {
        if (imp != null && imp.Depth > imp.workingDepth + 0.05f) return Mathf.Clamp01(pos + 2f * rate);
        if (lever < 0f && imp != null && imp.Depth >= imp.workingDepth) lever = 0f;
        return Mathf.Clamp01(pos + lever * rate);
    }

    public void SetPto(bool on)
    {
        if (on == Pto) return;
        Pto = on;
        Message?.Invoke(on ? "PTO ON" : "PTO OFF");
    }

    /// <summary>Draft of the implements working in the ground (N, against the motion).</summary>
    public float DraftForce
    {
        get
        {
            float f = 0f;
            foreach (var imp in new[] { Rear, Front, Towed })
                if (imp != null) f += imp.DraftForce;
            return f;
        }
    }

    /// <summary>Total power the attached implements take through the PTO (kW).</summary>
    public float PtoPowerKW
    {
        get
        {
            float kw = 0f;
            foreach (var imp in new[] { Rear, Front, Towed })
                if (imp != null && imp.ptoKW > 0f && Pto) kw += imp.ptoKW * (imp.Working ? 1f : 0.2f);
            return kw;
        }
    }

    Transform MountFor(Implement imp) => imp.mount == Implement.Mount.Rear ? rearMount : imp.mount == Implement.Mount.Front ? frontMount : drawbarMount;

    void FindNearest()
    {
        Nearest = null;
        NearestDistance = float.MaxValue;
        foreach (var imp in all)
        {
            if (imp == null || imp.Attached || Occupied(imp.mount)) continue;
            float d = Vector3.Distance(MountFor(imp).position, imp.hitchPoint.position);
            if (d < NearestDistance) { NearestDistance = d; Nearest = imp; }
        }
    }

    bool Occupied(Implement.Mount m) => m == Implement.Mount.Rear ? Rear != null : m == Implement.Mount.Front ? Front != null : Towed != null;

    public void ToggleAttach()
    {
        // Detach first if something is coupled at the end we're nearest to... simplest rule: detach the most recent.
        var coupled = new[] { Rear, Front, Towed };
        if (Nearest == null || NearestDistance > attachReach)
        {
            foreach (var imp in coupled)
                if (imp != null) { TryDetach(imp); return; }
            Message?.Invoke(Nearest != null ? $"{Nearest.displayName}: {NearestDistance:0.0} m, get closer" : "NOTHING TO ATTACH");
            return;
        }
        var target = Nearest;
        float heading = Vector3.Angle(Vector3.ProjectOnPlane(transform.forward, Vector3.up), Vector3.ProjectOnPlane(target.transform.forward, Vector3.up));
        if (heading > attachAngle) { Message?.Invoke($"{target.displayName}: line up straighter"); return; }
        if (Mathf.Abs(tractor.SpeedKmh) > 2f) { Message?.Invoke("STOP TO ATTACH"); return; }
        float dy = MountFor(target).position.y - target.hitchPoint.position.y;
        if (target.mount != Implement.Mount.Drawbar && Mathf.Abs(dy) > 0.35f)
        {
            string linkage = target.mount == Implement.Mount.Front ? "FRONT LINKAGE" : "HITCH";
            Message?.Invoke(dy > 0f ? "LOWER THE " + linkage : "RAISE THE " + linkage);
            return;
        }
        target.Attach(this, MountFor(target), rb);
        if (target.mount == Implement.Mount.Rear) Rear = target;
        else if (target.mount == Implement.Mount.Front) Front = target;
        else Towed = target;
        UpdateMass();
        Message?.Invoke(target.displayName + " ATTACHED");
    }

    void TryDetach(Implement imp)
    {
        if (Mathf.Abs(tractor.SpeedKmh) > 2f) { Message?.Invoke("STOP TO DETACH"); return; }
        // A trailer parked at a sharp angle would drop its jack into the tractor's rear.
        if (imp.mount == Implement.Mount.Drawbar &&
            Vector3.Angle(Vector3.ProjectOnPlane(transform.forward, Vector3.up), Vector3.ProjectOnPlane(imp.transform.forward, Vector3.up)) > 25f)
        {
            Message?.Invoke("STRAIGHTEN UP TO DETACH");
            return;
        }
        if (imp.mount != Implement.Mount.Drawbar && imp.Depth < -0.08f)
        {
            Message?.Invoke("LOWER " + (imp.mount == Implement.Mount.Front ? "FRONT LINKAGE" : "HITCH") + " FIRST");
            return;
        }
        imp.Detach();
        if (imp == Rear) Rear = null;
        if (imp == Front) Front = null;
        if (imp == Towed) Towed = null;
        UpdateMass();
        Message?.Invoke(imp.displayName + " DETACHED");
    }

    // Mounted implements ride on the tractor: their mass moves the centre of mass (a plough lightens the front axle).
    void UpdateMass()
    {
        float m = baseMass;
        Vector3 moment = baseCom * baseMass;
        foreach (var imp in new[] { Rear, Front })
        {
            if (imp == null) continue;
            m += imp.mass;
            moment += imp.mass * transform.InverseTransformPoint(MountFor(imp).position + transform.rotation * imp.centreOfMassOffset);
        }
        rb.mass = m;
        rb.centerOfMass = moment / m;
        tractor.towedMass = Towed != null ? Towed.mass : 0f;
    }
}
