using UnityEngine;

/// <summary>
/// Phase 2 driving model: a Rigidbody (7.5 t) on four raycast wheels.
///   Suspension ... per wheel a ray from above the wheel centre down to the ground; a spring-damper (tyre + front axle
///                  suspension) pushes the body up along its own up axis. Preloaded so the body sits at the model's height.
///   Tyres ........ longitudinal (drive / brake) and lateral forces at the contact point, limited by a friction circle (μ·load).
///                  Lateral grip cancels the wheel's sideways sliding, so the tractor follows its Ackermann geometry until grip runs out.
///   Drive ........ the same comfort-limited speed command as in phase 1 (stick = target speed, gentle accel/brake rates).
///                  A speed controller turns it into a drive force, compensating slope and rolling resistance, limited by
///                  engine power (195 hp) and grip. All four wheels drive (MFWD), shared by load. On a steep climb it slows down.
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
        [System.NonSerialized] public bool grounded;
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
    public float grip = 0.8f;                   // μ, tyre on grass / dirt
    public float rollingResistance = 0.04f;     // × weight

    [Header("Engine")]
    public float enginePowerKW = 145f;          // 195 hp
    [System.NonSerialized] public float towedMass;   // trailer on the drawbar (set by Hitch): the drive force accelerates it too
    public float speedTracking = 2.5f;          // 1/s, how hard the drive force pulls the speed onto the command

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
    /// <summary>Share of engine power currently used for driving (0..1).</summary>
    public float PowerFraction { get; private set; }
    public bool Grounded { get; private set; }

    TractorInput input;
    Drivetrain drivetrain;
    Hitch hitch;
    Rigidbody rb;
    float speedCmd;
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
        UpdateSteering(input.Steer, dt);
        AnimateWheels(dt);

        // Rolled over or fell off the world: put it back at the start after a moment.
        upsideDownTime = transform.up.y < 0.3f ? upsideDownTime + dt : 0f;
        if (upsideDownTime > 3f || transform.position.y < -50f) ResetTractor();
    }

    public void ResetTractor()
    {
        rb.position = spawnPos + Vector3.up * 0.2f;
        rb.rotation = spawnRot;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        speedCmd = 0f;
        upsideDownTime = 0f;
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
        float target = 0f;
        if (throttle > dead) target = (throttle - dead) / (1f - dead) * fwdKmh / 3.6f;
        else if (throttle < -dead) target = (throttle + dead) / (1f - dead) * revKmh / 3.6f;

        float rate;
        bool opposite = Mathf.Abs(speedCmd) > 0.05f && Mathf.Sign(target) != Mathf.Sign(speedCmd) && target != 0f;
        if (opposite) rate = braking;                                      // stick against motion: brake first
        else if (Mathf.Abs(target) > Mathf.Abs(speedCmd)) rate = acceleration;
        else rate = target == 0f ? coastDecel : braking * 0.5f;            // release = coast, partial = slow down

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
        int n = Physics.RaycastNonAlloc(origin, dir, rayHits, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
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
        float roll = Mathf.Abs(SpeedMs) > 0.1f ? rollingResistance * g * Mathf.Sign(SpeedMs) : 0f;
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
            float available = Mathf.Max(10f, enginePowerKW - (hitch ? hitch.PtoPowerKW : 0f));   // the PTO takes its share first
            float maxForce = available * 1000f / Mathf.Max(Mathf.Abs(SpeedMs), 2f);
            force = Mathf.Clamp(force, -maxForce, maxForce);
        }
        PowerFraction = driving ? Mathf.Clamp01(Mathf.Abs(force * SpeedMs) / (enginePowerKW * 1000f)) : 0f;

        for (int i = 0; i < wheels.Length; i++)
        {
            var w = wheels[i];
            if (!w.grounded) continue;
            RaycastHit hit = contacts[i];
            Vector3 fwd = Quaternion.AngleAxis(w.angle, transform.up) * transform.forward;
            fwd = Vector3.ProjectOnPlane(fwd, hit.normal).normalized;
            Vector3 side = Vector3.Cross(hit.normal, fwd);

            Vector3 v = rb.GetPointVelocity(hit.point);
            float share = w.load / totalLoad;
            float fLong = force * share;
            // Lateral: cancel this wheel's sideways sliding (its share of the mass, half per step for stability).
            float fLat = -Vector3.Dot(v, side) * (w.load / g) / dt * 0.5f;
            if (holding) fLat -= 20f * Vector3.Dot(rb.position - holdPos, side) * (w.load / g);   // no sideways creep when parked

            // Friction circle.
            float max = grip * w.load, len = Mathf.Sqrt(fLong * fLong + fLat * fLat);
            if (len > max) { fLong *= max / len; fLat *= max / len; }

            rb.AddForceAtPosition(fwd * fLong + side * fLat, hit.point);
        }
    }

    void UpdateSteering(float steer, float dt)
    {
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
        float target = steer * lockAngle;
        SteerAngle = Mathf.MoveTowards(SteerAngle, target, steerRate * dt);

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

            // Spin from the ground speed along the wheel's rolling direction.
            Vector3 fwd = w.steerPivot.forward;
            float rolling = Vector3.Dot(rb.GetPointVelocity(w.steerPivot.position), fwd);
            float spinDeg = rolling * dt / Mathf.Max(0.05f, w.radius) * Mathf.Rad2Deg;
            w.spinPivot.localRotation *= Quaternion.Euler(spinDeg, 0f, 0f);
        }
    }
}
