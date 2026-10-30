using UnityEngine;

/// <summary>
/// Phase 2 driving model: a Rigidbody (7.5 t) on four raycast wheels.
///   Suspension ... per wheel a ray from above the wheel centre down to the ground; a spring-damper (tyre + front axle
///                  suspension) pushes the body up along its own up axis. Preloaded so the body sits at the model's height.
///   Tyres ........ longitudinal (drive / brake) and lateral forces at the contact point, limited by a friction circle (μ·load).
///                  Lateral grip cancels the wheel's sideways sliding, so the tractor follows its Ackermann geometry until grip runs out.
///   Drive ........ the same comfort-limited speed command as in phase 1 (stick = target speed, gentle accel/brake rates).
///                  A speed controller turns it into a drive force, compensating slope and rolling resistance, limited by
///                  the engine power at the current engine speed (Drivetrain torque curve, 195 hp rated) and grip. On a steep climb it slows down.
///   Traction ..... (phase 3) the force goes to the driven wheels (rear, + front with 4WD) through open or locked differentials;
///                  each tyre's slip follows a traction curve for the ground under it, and a wheel spins up when the ground
///                  can't take its force. Brakes on all wheels or the rear only; a wheel locks when the ground can't take the braking.
///   Steering ..... same comfort limits as phase 1 (yaw rate, lateral acceleration); Ackermann angle per front wheel.
/// Physics runs at 90 Hz with Rigidbody interpolation, so the VR camera riding on the body moves smoothly every frame.
/// The tractor's colliders are on the Ignore Raycast layer so the wheel rays don't hit their own body.
/// </summary>
[RequireComponent(typeof(TractorInput), typeof(Rigidbody))]
public class TractorController : MonoBehaviour
{
    [System.Serializable]
    public class Wheel
    {
        public Transform steerPivot;   // rotates around local Y (front wheels only); moved up/down with the suspension
        public Transform spinPivot;    // rotates around local X
        public float radius = 0.7f;
        public bool steers;
        public bool leftSide;

        [System.NonSerialized] public Vector3 restLocal;      // pivot position as modelled (static load)
        [System.NonSerialized] public float staticLoad, compression, lastCompression, load, angle;
        [System.NonSerialized] public GroundMap.Surface surface;   // what this wheel stands on
        [System.NonSerialized] public bool grounded;
        [System.NonSerialized] public float wheelSpeed;  // tyre surface speed (m/s, + = rolling forward): spins up / locks with slip
        [System.NonSerialized] public float slip;        // 0..1 while driving: 1 - ground speed / wheel speed
        [System.NonSerialized] public bool spinning, locked;
        [System.NonSerialized] public float force;       // longitudinal tyre force this step (N)
    }

    [Header("Geometry (filled by the setup script)")]
    public float wheelbase = 2.8f;
    public float trackWidth = 1.9f;
    public Wheel[] wheels = new Wheel[0];

    [Header("Mass (6195M: ~7.5 t unballasted, ~40 % on the front axle)")]
    public float mass = 7500f;
    public Vector3 centerOfMass = new Vector3(0f, 1.05f, 1.12f);

    [Header("Suspension and tyres")]
    public float travel = 0.15f;                // m each way from the static position
    public float frontStiffness = 300000f;      // N/m per wheel (TLS front axle + tyre)
    public float rearStiffness = 500000f;       // N/m per wheel (tyre only, rigid rear axle)
    public float dampingRatio = 0.45f;
    [Tooltip("Multiplier on the ground's tyre grip (GroundMap: asphalt 0.9 … mud 0.35).")]
    public float grip = 1f;
    [Tooltip("Multiplier on the ground's rolling resistance (GroundMap: asphalt 0.015 … mud 0.15).")]
    public float rollingResistance = 1f;

    [Header("Engine")]
    public float enginePowerKW = 145f;          // 195 hp rated; the power at other engine speeds comes from Drivetrain's torque curve
    [System.NonSerialized] public float towedMass;   // trailer on the drawbar (set by Hitch): the drive force accelerates it too
    public float speedTracking = 2.5f;          // 1/s, how hard the drive force pulls the speed onto the command

    [Header("Traction")]
    [Tooltip("Front axle drive (MFWD). Off: only the rear wheels drive.")]
    public bool fourWheelDrive = true;
    [Tooltip("Brakes on all four wheels (the 6195M engages the front axle to brake). Off: rear brakes only.")]
    public bool allWheelBrakes = true;
    [Tooltip("The diff lock lets go above this steering angle (degrees) or speed (km/h), like the real one.")]
    public float diffLockMaxSteer = 12f, diffLockMaxKmh = 15f;
    [Tooltip("Particle material for the clods a spinning wheel throws (none = no clods).")]
    public Material dirtMaterial;

    [Header("Speed")]
    public float maxForwardKmh = 50f;      // 6195M top speed
    public float maxReverseKmh = 15f;
    // Gentle values on purpose: changes in speed are a main cause of VR motion sickness.
    public float acceleration = 1.0f;  // m/s²
    public float braking = 2.5f;       // m/s²
    public float coastDecel = 0.8f;    // m/s² when the stick is released

    [Header("Steering")]
    public float maxSteerAngle = 40f;      // degrees, virtual centre wheel
    public float steerRate = 35f;          // degrees per second
    [Tooltip("Comfort: cap on how fast the tractor (and the cab view) can yaw.")]
    public float maxYawRate = 25f;         // degrees per second
    [Tooltip("Lateral acceleration limit (v² / R); also keeps high-speed turns realistic.")]
    public float maxLateralAccel = 2.5f;   // m/s²

    [Header("Cab")]
    public Transform steeringWheel;        // pivot; spins around its local Z (the steering column)
    [Tooltip("Steering wheel degrees per degree of road-wheel angle: 40° lock × 13 ≈ 1.45 turns each way.")]
    public float steeringRatio = 13f;

    /// <summary>Measured forward speed (m/s, negative in reverse).</summary>
    public float SpeedMs { get; private set; }
    public float SpeedKmh => SpeedMs * 3.6f;
    /// <summary>Smoothed forward acceleration (m/s²), steady per frame even though physics steps at its own rate.</summary>
    public float Acceleration { get; private set; }
    public float SteerAngle { get; private set; }
    /// <summary>Current steering limit (degrees) from the comfort rules at this speed.</summary>
    public float LockAngle { get; private set; } = 40f;
    /// <summary>Share of engine power currently used for driving (0..1).</summary>
    public float PowerFraction { get; private set; }
    /// <summary>The drive force is capped by the engine's power right now (the drivetrain shifts down when it lugs).</summary>
    public bool PowerLimited { get; private set; }
    /// <summary>Braking harder than coasting (brake lights).</summary>
    public bool Braking { get; private set; }
    public bool Grounded { get; private set; }
    /// <summary>Surface under the rear wheels (HUD, tests).</summary>
    public string GroundLabel { get; private set; } = "";
    /// <summary>Wheel slip of the driven wheels while driving (0..1, smoothed), as the CommandCenter shows it.</summary>
    public float Slip { get; private set; }
    /// <summary>Speed the rear wheels' tyres turn at (m/s): ground speed plus slip. The engine is geared to this.</summary>
    public float WheelSpeedMs { get; private set; }
    public float WheelSpeedKmh => WheelSpeedMs * 3.6f;
    public bool DiffLock { get; private set; }
    /// <summary>Share of the wanted drive / brake force the tyres could not put down this step (0 = all of it went to the ground).</summary>
    public float Shortfall { get; private set; }
    public event System.Action<string> Message;

    public void SetFourWheelDrive(bool on) { fourWheelDrive = on; Message?.Invoke(on ? "4WD ON" : "4WD OFF"); }
    public void SetAllWheelBrakes(bool on) { allWheelBrakes = on; Message?.Invoke(on ? "BRAKES: ALL WHEELS" : "BRAKES: REAR ONLY"); }
    public void SetDiffLock(bool on)
    {
        if (on && (Mathf.Abs(SteerAngle) > diffLockMaxSteer || Mathf.Abs(SpeedKmh) > diffLockMaxKmh))
        {
            Message?.Invoke(Mathf.Abs(SpeedKmh) > diffLockMaxKmh ? $"DIFF LOCK: BELOW {diffLockMaxKmh:0} KM/H ONLY" : "DIFF LOCK: STRAIGHTEN UP FIRST");
            return;
        }
        DiffLock = on;
        Message?.Invoke(on ? "DIFF LOCK ON" : "DIFF LOCK OFF");
    }

    TractorInput input;
    Drivetrain drivetrain;
    Hitch hitch;
    Rigidbody rb;
    float speedCmd;
    float? heldSteer;      // steering angle from the hands on the wheel (CabControls)
    bool steerLatched;     // wheel was let go: keep its angle until the stick is used

    bool stopRequested;
    /// <summary>Brake to a stop (and then hold), as if the stick were pulled back to neutral firmly.</summary>
    public void RequestStop() => stopRequested = true;

    /// <summary>The wheel is in someone's hands: steer to this angle (clamped to the lock).</summary>
    public void HoldSteer(float angle) => heldSteer = angle;
    public void ReleaseHeldSteer() { heldSteer = null; steerLatched = true; }
    Vector3 spawnPos;
    Quaternion spawnRot;
    float upsideDownTime;
    Vector3 holdPos;
    bool holdSet;

    void Awake()
    {
        input = GetComponent<TractorInput>();
        drivetrain = GetComponent<Drivetrain>();
        hitch = GetComponent<Hitch>();
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.mass = mass;
        rb.centerOfMass = centerOfMass;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.linearDamping = 0f;
        rb.angularDamping = 0.3f;
        spawnPos = transform.position;
        spawnRot = transform.rotation;

        // Static load per wheel from the centre of mass position along the wheelbase.
        float frontShare = Mathf.Clamp01(centerOfMass.z / wheelbase);
        float weight = mass * -Physics.gravity.y;
        foreach (var w in wheels)
        {
            w.restLocal = w.steerPivot.localPosition;
            w.staticLoad = weight * (w.steers ? frontShare : 1f - frontShare) * 0.5f;
            w.compression = w.lastCompression = travel;
        }
    }

    void Update() => FrameUpdate(Time.deltaTime);

    /// <summary>Per-frame part (steering, wheel visuals, reset). Public so the editor physics test can step it.</summary>
    public void FrameUpdate(float dt)
    {
        if (input.FourWheelDrivePressed) SetFourWheelDrive(!fourWheelDrive);
        if (input.DiffLockPressed) SetDiffLock(!DiffLock);
        if (input.BrakeModePressed) SetAllWheelBrakes(!allWheelBrakes);
        UpdateSteering(input.Steer, dt);
        // The diff lock lets go by itself when steering or going fast (it would push the tractor straight on).
        if (DiffLock && (Mathf.Abs(SteerAngle) > diffLockMaxSteer || Mathf.Abs(SpeedKmh) > diffLockMaxKmh))
        {
            DiffLock = false;
            Message?.Invoke("DIFF LOCK OFF");
        }
        AnimateWheels(dt);
        UpdateDirt();

        // Rolled over or fell off the world: put it back at the start after a moment.
        upsideDownTime = transform.up.y < 0.3f ? upsideDownTime + dt : 0f;
        if (upsideDownTime > 3f || transform.position.y < -50f) ResetTractor();
    }

    public Vector3 SpawnPosition => spawnPos;

    public void ResetTractor() => TeleportTo(spawnPos, spawnRot);

    /// <summary>Puts the tractor at rest at <paramref name="groundPos"/> (spawn height if it is on the flat yard), like a reset.</summary>
    public void TeleportTo(Vector3 groundPos, Quaternion rotation)
    {
        // The transform too, so the mounts are in their new place for a coupled trailer right away.
        transform.SetPositionAndRotation(groundPos + Vector3.up * 0.2f, rotation);
        rb.position = transform.position;
        rb.rotation = rotation;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        speedCmd = 0f;
        upsideDownTime = 0f;
        ResetSuspension();
        if (drivetrain) drivetrain.Refill();
        // A trailer left on its joint far away got yanked across the map and lost (and then couldn't be detached).
        if (hitch) hitch.AfterTeleport();
    }

    /// <summary>After a teleport: forget the last spring positions, or the dampers see a huge spring speed on the next step;
    /// and straighten the wheels (a wheel left turned by hand would otherwise stay latched).</summary>
    public void ResetSuspension()
    {
        foreach (var w in wheels) { w.compression = w.lastCompression = travel; w.load = w.staticLoad; w.wheelSpeed = 0f; w.slip = 0f; w.spinning = w.locked = false; }
        Slip = 0f;
        holdSet = false;   // the parking brake must not pull towards where it stood before the teleport
        SteerAngle = 0f;
        heldSteer = null;
        steerLatched = false;
    }

    void FixedUpdate() => PhysicsStep();

    /// <summary>One physics step (called from FixedUpdate; public for the editor physics test).</summary>
    public void PhysicsStep()
    {
        float dt = Time.fixedDeltaTime;
        float lastSpeed = SpeedMs;
        SpeedMs = Vector3.Dot(rb.linearVelocity, transform.forward);
        Acceleration = Mathf.Lerp(Acceleration, (SpeedMs - lastSpeed) / dt, 1f - Mathf.Exp(-dt / 0.15f));

        float accelCmd = UpdateSpeedCommand(input.Throttle, dt);
        Suspension(dt);
        Drive(accelCmd, dt);
    }

    // Comfort-limited speed command, same rules as phase 1. Returns its rate of change (m/s²).
    float UpdateSpeedCommand(float throttle, float dt)
    {
        const float dead = 0.08f;
        float fwdKmh = drivetrain ? drivetrain.SpeedLimitKmh(true, maxForwardKmh, maxReverseKmh) : maxForwardKmh;
        float revKmh = drivetrain ? drivetrain.SpeedLimitKmh(false, maxForwardKmh, maxReverseKmh) : maxReverseKmh;
        // Manual with the hand throttle set: any lever / stick input drives at least at the gear's speed for that engine speed.
        float hand = drivetrain ? drivetrain.HandThrottleShare : 0f;
        float target = 0f;
        if (throttle > dead) target = Mathf.Max((throttle - dead) / (1f - dead), hand) * fwdKmh / 3.6f;
        else if (throttle < -dead) target = -Mathf.Max((-throttle - dead) / (1f - dead), hand) * revKmh / 3.6f;

        // STOP (cab panel): brake to a standstill at the comfort braking rate; any stick / lever input cancels it.
        if (stopRequested && (target != 0f || Mathf.Abs(SpeedMs) < 0.05f)) stopRequested = false;
        float rate;
        bool opposite = Mathf.Abs(speedCmd) > 0.05f && Mathf.Sign(target) != Mathf.Sign(speedCmd) && target != 0f;
        if (opposite) rate = braking;                                      // stick against motion: brake first
        else if (Mathf.Abs(target) > Mathf.Abs(speedCmd)) rate = acceleration;
        else rate = target == 0f ? coastDecel : braking * 0.5f;            // release = coast, partial = slow down

        if (stopRequested) rate = braking;
        float before = speedCmd;
        speedCmd = Mathf.MoveTowards(speedCmd, target, rate * dt);
        // Anti wind-up: when power or grip can't keep up (steep climb), the command waits for the tractor.
        speedCmd = Mathf.Clamp(speedCmd, SpeedMs - 1.5f, SpeedMs + 1.5f);
        return (speedCmd - before) / dt;
    }

    void Suspension(float dt)
    {
        Vector3 up = transform.up;
        Grounded = false;
        for (int i = 0; i < wheels.Length; i++)
        {
            var w = wheels[i];
            Vector3 top = transform.TransformPoint(w.restLocal + Vector3.up * travel);
            float len = 2f * travel + w.radius;   // compression = travel when the wheel sits where it was modelled
            w.lastCompression = w.compression;
            if (GroundRay(top, -up, len, out RaycastHit hit))
            {
                w.grounded = Grounded = true;
                w.surface = GroundMap.SurfaceAt(hit.point);
                w.compression = len - hit.distance;
                float k = w.steers ? frontStiffness : rearStiffness;
                float c = 2f * dampingRatio * Mathf.Sqrt(k * w.staticLoad / -Physics.gravity.y);
                float load = w.staticLoad + k * (w.compression - travel) + c * (w.compression - w.lastCompression) / dt;
                if (w.compression > 1.8f * travel) load += 10f * k * (w.compression - 1.8f * travel);   // bump stop
                w.load = Mathf.Clamp(load, 0f, 6f * w.staticLoad);
                rb.AddForceAtPosition(up * w.load, hit.point);
                contacts[i] = hit;
            }
            else
            {
                w.grounded = false;
                w.compression = 0f;
                w.load = 0f;
            }
        }
    }

    readonly RaycastHit[] contacts = new RaycastHit[8];
    readonly RaycastHit[] rayHits = new RaycastHit[8];

    // Nearest static hit: the wheels roll on the ground only. Movable things (hay bales, later implements) are met by
    // the wheel and body colliders instead; a wheel ray riding up onto a bale would launch the tractor.
    bool GroundRay(Vector3 origin, Vector3 dir, float length, out RaycastHit best)
    {
        best = default;
        int n = Physics.RaycastNonAlloc(origin, dir, rayHits, length, Physics.DefaultRaycastLayers & ~(1 << DesktopMode.PickLayer), QueryTriggerInteraction.Ignore);
        float bestDist = float.MaxValue;
        for (int i = 0; i < n; i++)
            if (rayHits[i].rigidbody == null && rayHits[i].distance < bestDist) { best = rayHits[i]; bestDist = best.distance; }
        return bestDist < float.MaxValue;
    }

    void Drive(float accelCmd, float dt)
    {
        float totalLoad = 0f;
        foreach (var w in wheels) totalLoad += w.load;
        if (totalLoad <= 0f) { PowerFraction = 0f; return; }

        // Force along the tractor that gives the commanded acceleration, with slope and rolling resistance compensated.
        float g = -Physics.gravity.y;
        float slope = Vector3.Dot(Physics.gravity, transform.forward);            // < 0 uphill
        // Rolling resistance from the ground under the wheels (load-weighted).
        float surfaceRoll = 0f;
        foreach (var w in wheels) if (w.grounded) surfaceRoll += w.surface.rolling * w.load / totalLoad;
        foreach (var w in wheels) if (w.grounded && !w.steers) GroundLabel = w.surface.label;
        float roll = Mathf.Abs(SpeedMs) > 0.1f ? rollingResistance * surfaceRoll * g * Mathf.Sign(SpeedMs) : 0f;
        rb.AddForce(-transform.forward * rb.mass * roll);                        // the real resistance (soil, tyres)...
        float wanted = accelCmd + speedTracking * (speedCmd - SpeedMs);
        bool holding = speedCmd == 0f && Mathf.Abs(SpeedMs) < 0.3f;
        // Parking brake: stop, then hold the spot where it stopped (a pure velocity brake creeps on a slope).
        if (!holding) holdSet = false;
        else
        {
            if (!holdSet) { holdPos = rb.position; holdSet = true; }
            float drift = Vector3.Dot(rb.position - holdPos, transform.forward);
            wanted = -SpeedMs / dt * 0.5f - 20f * drift;
        }
        float m = rb.mass + towedMass;
        float force = m * (wanted - slope) + m * roll;               // ...which the drive force overcomes
        // Implement draft (plough) is known, so pull against it directly instead of leaving it to the speed error.
        if (hitch && Mathf.Abs(SpeedMs) > 0.05f) force += Mathf.Sign(SpeedMs) * hitch.DraftForce;

        // Engine power limit while driving (not while braking).
        bool driving = !holding && force * (Mathf.Abs(SpeedMs) > 0.1f ? SpeedMs : speedCmd) > 0f;
        if (driving)
        {
            float engineKW = drivetrain ? drivetrain.AvailableKW : enginePowerKW;                  // torque curve at the current engine speed
            float available = Mathf.Max(10f, engineKW - (hitch ? hitch.PtoPowerKW : 0f));          // the PTO takes its share first
            float maxForce = available * 1000f / Mathf.Max(Mathf.Abs(SpeedMs), 2f);
            PowerLimited = Mathf.Abs(force) > maxForce * 0.98f;
            force = Mathf.Clamp(force, -maxForce, maxForce);
        }
        else PowerLimited = false;
        PowerFraction = driving ? Mathf.Clamp01(Mathf.Abs(force * SpeedMs) / (enginePowerKW * 1000f)) : 0f;

        // Lateral grip first: a tractor sliding sideways is the worst thing for comfort, so the drive / brake force
        // gets what is left of each tyre's friction circle.
        bool braking = !driving && !holding;
        Braking = braking && wanted < -1.2f;   // coasting is 0.8 m/s²
        // Parked: park lock + front axle engaged, all four hold. Braking: all wheels with all-wheel brakes (or 4WD engaged).
        bool frontTakes = holding || fourWheelDrive || (braking && allWheelBrakes);
        float takeLoad = 0f;
        for (int i = 0; i < wheels.Length; i++)
        {
            var w = wheels[i];
            takes[i] = w.grounded && (!w.steers || frontTakes);
            room[i] = lat[i] = rollSpeed[i] = 0f;
            if (!w.grounded) continue;
            RaycastHit hit = contacts[i];
            Vector3 fwd = Quaternion.AngleAxis(w.angle, transform.up) * transform.forward;
            fwd = Vector3.ProjectOnPlane(fwd, hit.normal).normalized;
            Vector3 side = Vector3.Cross(hit.normal, fwd);
            fwds[i] = fwd;
            sides[i] = side;
            Vector3 v = rb.GetPointVelocity(hit.point);
            rollSpeed[i] = Vector3.Dot(v, fwd);
            // Lateral: cancel this wheel's sideways sliding (its share of the mass, half per step for stability).
            float fLat = -Vector3.Dot(v, side) * (w.load / g) / dt * 0.5f;
            if (holding) fLat -= 20f * Vector3.Dot(rb.position - holdPos, side) * (w.load / g);   // no sideways creep when parked
            float max = grip * w.surface.grip * w.load;
            if (Mathf.Abs(fLat) > max) fLat = Mathf.Sign(fLat) * max;
            lat[i] = fLat;
            room[i] = Mathf.Sqrt(Mathf.Max(0f, max * max - fLat * fLat));
            if (takes[i]) takeLoad += w.load;
        }

        Distribute(Mathf.Abs(force), takeLoad, braking || holding);
        float sign = Mathf.Sign(force);
        for (int i = 0; i < wheels.Length; i++)
        {
            var w = wheels[i];
            w.force = takes[i] ? sign * got[i] : 0f;
            if (w.grounded) rb.AddForceAtPosition(fwds[i] * w.force + sides[i] * lat[i], contacts[i].point);
        }
        WheelSlip(braking, holding, dt);
    }

    // Per-wheel scratch for one physics step.
    readonly bool[] takes = new bool[8];
    readonly float[] room = new float[8], lat = new float[8], want = new float[8], got = new float[8], rollSpeed = new float[8];
    readonly Vector3[] fwds = new Vector3[8], sides = new Vector3[8];

    /// <summary>
    /// Shares the force out (magnitudes, into got[]). Each axle's differential decides what the axle can put down:
    /// open = the same force on both wheels, so the wheel with less grip sets the limit (2 × its grip) and spins;
    /// locked = each wheel up to its own grip. With 4WD the axles are coupled rigidly (no centre differential),
    /// so what one axle can't put down goes to the other. Brakes act on each wheel by itself.
    /// </summary>
    void Distribute(float total, float takeLoad, bool perWheel)
    {
        for (int i = 0; i < wheels.Length; i++) { got[i] = 0f; want[i] = takes[i] && takeLoad > 0f ? total * wheels[i].load / takeLoad : 0f; }
        Shortfall = 0f;
        if (total <= 0f || takeLoad <= 0f) return;

        // Axle 0 = the steering pair, axle 1 = the rear pair. Pass 0 gives each axle its share, pass 1 the rest to whoever can take it.
        float leftover = total;
        for (int pass = 0; pass < 2 && leftover > 1f; pass++)
        {
            for (int a = 0; a < 2; a++)
            {
                int l = -1, r = -1;
                for (int i = 0; i < wheels.Length; i++)
                    if (wheels[i].steers == (a == 0) && takes[i]) { if (l < 0) l = i; else r = i; }
                if (l < 0) continue;
                bool locked = perWheel || DiffLock || r < 0;
                float have = got[l] + (r >= 0 ? got[r] : 0f);
                float cap = locked ? room[l] + (r >= 0 ? room[r] : 0f) : 2f * Mathf.Min(room[l], room[r]);
                float asked = pass == 0 ? want[l] + (r >= 0 ? want[r] : 0f) : have + leftover;
                float axle = Mathf.Max(have, Mathf.Min(asked, cap));
                leftover -= axle - have;
                if (r < 0) { got[l] = axle; continue; }
                if (!locked) { got[l] = got[r] = axle * 0.5f; continue; }
                // Locked / brakes: by load, and the wheel with grip to spare takes what the other can't.
                float share = want[l] + want[r] > 0f ? want[l] / (want[l] + want[r]) : 0.5f;
                got[l] = Mathf.Min(axle * share, room[l]);
                got[r] = Mathf.Min(axle - got[l], room[r]);
                got[l] = Mathf.Min(axle - got[r], room[l]);
            }
        }
        Shortfall = Mathf.Clamp01(leftover / total);
    }

    // Slip of each tyre from the traction curve (how much of its grip it uses) and its own speed: a driven wheel the ground
    // can't hold spins up, a braked one locks. The engine follows the rear wheels' speed.
    void WheelSlip(bool braking, bool holding, float dt)
    {
        float driveSlip = 0f, driven = 0f, rear = 0f, rearN = 0f;
        float k = 1f - Mathf.Exp(-dt / 0.08f);
        for (int i = 0; i < wheels.Length; i++)
        {
            var w = wheels[i];
            float target;
            w.spinning = w.locked = false;
            if (!w.grounded) target = w.wheelSpeed * (1f - 0.5f * dt);       // in the air: runs on, slowly
            else if (!takes[i] || got[i] <= 0f || holding) target = rollSpeed[i];
            else
            {
                float use = room[i] > 1f ? got[i] / room[i] : 1f;
                bool limit = use > 0.995f && Shortfall > 0.01f;
                float s = -Mathf.Log(1f - Mathf.Min(use, 0.97f)) / Mathf.Max(1f, w.surface.slipK);
                float v = rollSpeed[i];
                if (braking)
                {
                    w.locked = limit;
                    target = limit ? 0f : v * (1f - Mathf.Min(s, 1f));
                }
                else
                {
                    float dir = Mathf.Abs(v) > 0.1f ? Mathf.Sign(v) : Mathf.Sign(w.force);
                    target = dir * Mathf.Abs(v) / Mathf.Max(0.1f, 1f - Mathf.Min(s, 0.9f));
                    if (limit)
                    {
                        // Spinning: runs up with how much force is missing; through an open diff twice as fast (its partner stands).
                        w.spinning = true;
                        target = dir * (Mathf.Abs(v) + Mathf.Lerp(0.6f, 3.5f, Shortfall) * (DiffLock ? 1f : 2f));
                    }
                }
            }
            w.wheelSpeed = Mathf.Lerp(w.wheelSpeed, target, k);
            if (w.grounded && !braking && takes[i] && got[i] > 0f && Mathf.Abs(w.wheelSpeed) > 0.05f)
                w.slip = Mathf.Clamp01(1f - Mathf.Abs(rollSpeed[i]) / Mathf.Abs(w.wheelSpeed));
            else w.slip = 0f;
            if (!w.steers || fourWheelDrive) { driveSlip += w.slip; driven++; }
            if (!w.steers) { rear += w.wheelSpeed; rearN++; }
        }
        float shown = driven > 0f ? driveSlip / driven : 0f;
        Slip = Mathf.Lerp(Slip, shown, 1f - Mathf.Exp(-dt / 0.3f));
        WheelSpeedMs = rearN > 0f ? rear / rearN : SpeedMs;
    }

    // ------------------------------------------------------------------ clods from spinning wheels
    ParticleSystem[] dirt;

    void Start() => BuildDirt();

    // One small particle system per wheel, at the back of its contact patch; cubes of soil, a few dozen at most (Quest budget).
    void BuildDirt()
    {
        if (dirtMaterial == null || dirt != null) return;
        dirt = new ParticleSystem[wheels.Length];
        var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        for (int i = 0; i < wheels.Length; i++)
        {
            var w = wheels[i];
            var go = new GameObject("Dirt_" + i);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = w.restLocal + new Vector3(0f, -w.radius + 0.1f, -w.radius * 0.45f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 1f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            main.startRotation3D = true;
            main.startRotationX = main.startRotationY = main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;
            main.playOnAwake = false;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 20f;
            shape.radius = 0.15f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = cube;
            r.sharedMaterial = dirtMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            dirt[i] = ps;
            ps.Play();
        }
    }

    // Thrown backwards and up (forwards when reversing) while a wheel slips hard on soft ground.
    void UpdateDirt()
    {
        if (dirt == null) return;
        for (int i = 0; i < wheels.Length; i++)
        {
            var w = wheels[i];
            var ps = dirt[i];
            float rate = 0f;
            if (w.grounded && w.surface.dirt.a > 0f && Mathf.Abs(w.wheelSpeed) > 0.4f)
                rate = 70f * Mathf.Clamp01((w.slip - 0.2f) / 0.4f);
            var emission = ps.emission;
            emission.rateOverTime = rate;
            if (rate <= 0f) continue;
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(w.surface.dirt, w.surface.dirt * 0.75f);
            float back = w.wheelSpeed > 0f ? -1f : 1f;
            var t = ps.transform;
            t.localPosition = w.steerPivot.localPosition + new Vector3(0f, -w.radius + 0.1f, back * w.radius * 0.45f);
            t.localRotation = Quaternion.LookRotation(new Vector3(0f, 0.8f, back));
        }
    }

    void UpdateSteering(float steer, float dt)
    {
        float raw = steer;
        // Squared response: fine control around the centre, full lock still reachable.
        steer = Mathf.Sign(steer) * steer * steer;

        // Limit the steering angle so the yaw rate stays below maxYawRate and v·ω below maxLateralAccel.
        float v = Mathf.Abs(SpeedMs);
        float lockAngle = maxSteerAngle;
        if (v > 0.1f)
        {
            float maxOmega = Mathf.Min(maxYawRate * Mathf.Deg2Rad, maxLateralAccel / v);
            lockAngle = Mathf.Min(lockAngle, Mathf.Atan(maxOmega * wheelbase / v) * Mathf.Rad2Deg);
        }
        LockAngle = lockAngle;
        float target = steer * lockAngle, rate = steerRate;
        if (heldSteer.HasValue)
        {
            target = heldSteer.Value;
            rate = steerRate * 6f;                       // the hands turn the wheel directly
        }
        else if (steerLatched && Mathf.Abs(raw) < 0.15f) target = SteerAngle;   // let go: the wheel stays put
        else steerLatched = false;
        target = Mathf.Clamp(target, -lockAngle, lockAngle);
        SteerAngle = Mathf.MoveTowards(SteerAngle, target, rate * dt);

        // Ackermann: the inner wheel turns tighter than the outer one.
        float tanSteer = Mathf.Tan(SteerAngle * Mathf.Deg2Rad);
        foreach (var w in wheels)
        {
            w.angle = 0f;
            if (!w.steers || Mathf.Abs(tanSteer) < 1e-4f) continue;
            float turnRadius = wheelbase / tanSteer;                   // signed, + = right turn
            float lateral = w.leftSide ? -trackWidth * 0.5f : trackWidth * 0.5f;
            w.angle = Mathf.Atan(wheelbase / (turnRadius - lateral)) * Mathf.Rad2Deg;
        }

        // The pivot's Z points away from the driver, so a positive Z rotation looks counter-clockwise to them:
        // a right turn (positive SteerAngle) needs a negative one.
        if (steeringWheel) steeringWheel.localRotation = Quaternion.Euler(0f, 0f, -SteerAngle * steeringRatio);
    }

    void AnimateWheels(float dt)
    {
        foreach (var w in wheels)
        {
            if (w == null || w.spinPivot == null) continue;
            if (w.steers) w.steerPivot.localRotation = Quaternion.Euler(0f, w.angle, 0f);
            // Suspension: the wheel hangs lower when the spring extends.
            w.steerPivot.localPosition = w.restLocal + Vector3.up * (w.compression - travel);

            // Spin from the tyre's own speed (ground speed + slip; faster when spinning, still when locked).
            float spinDeg = w.wheelSpeed * dt / Mathf.Max(0.05f, w.radius) * Mathf.Rad2Deg;
            w.spinPivot.localRotation *= Quaternion.Euler(spinDeg, 0f, 0f);
        }
    }
}
