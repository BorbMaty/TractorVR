using UnityEngine;

/// <summary>
/// Phase 1 driving model: kinematic bicycle model with Ackermann steering, made for a flat world.
/// The tractor's transform origin is the rear axle centre on the ground, so the body yaws
/// around the rear axle like a real front-steered vehicle.
/// Motion is computed in Update (not physics) so the VR camera that rides on it never jitters.
/// </summary>
[RequireComponent(typeof(TractorInput))]
public class TractorController : MonoBehaviour
{
    [System.Serializable]
    public class Wheel
    {
        public Transform steerPivot;   // rotates around local Y (front wheels only)
        public Transform spinPivot;    // rotates around local X
        public float radius = 0.7f;
        public bool steers;
        public bool leftSide;
    }

    [Header("Geometry (filled by the setup script)")]
    public float wheelbase = 2.8f;
    public float trackWidth = 1.9f;
    public Wheel[] wheels = new Wheel[0];

    [Header("Speed")]
    public float maxForwardKmh = 40f;
    public float maxReverseKmh = 15f;
    public float acceleration = 1.8f;  // m/s²
    public float braking = 5f;         // m/s²
    public float coastDecel = 1.0f;    // m/s² when the stick is released

    [Header("Steering")]
    public float maxSteerAngle = 40f;      // degrees, virtual centre wheel
    public float steerRate = 70f;          // degrees per second
    [Tooltip("Reduce the steering lock at speed for stability (fraction kept at max speed).")]
    public float steerAtMaxSpeed = 0.45f;

    public float SpeedMs { get; private set; }
    public float SpeedKmh => SpeedMs * 3.6f;
    public float SteerAngle { get; private set; }

    TractorInput input;

    void Awake() => input = GetComponent<TractorInput>();

    void Update()
    {
        float dt = Time.deltaTime;
        UpdateSpeed(input.Throttle, dt);
        UpdateSteering(input.Steer, dt);
        Move(dt);
        AnimateWheels(dt);
    }

    void UpdateSpeed(float throttle, float dt)
    {
        const float dead = 0.08f;
        float target = 0f;
        if (throttle > dead) target = (throttle - dead) / (1f - dead) * maxForwardKmh / 3.6f;
        else if (throttle < -dead) target = (throttle + dead) / (1f - dead) * maxReverseKmh / 3.6f;

        float rate;
        bool opposite = Mathf.Abs(SpeedMs) > 0.05f && Mathf.Sign(target) != Mathf.Sign(SpeedMs) && target != 0f;
        if (opposite) rate = braking;                                      // stick against motion: brake first
        else if (Mathf.Abs(target) > Mathf.Abs(SpeedMs)) rate = acceleration;
        else rate = target == 0f ? coastDecel : braking * 0.5f;            // release = coast, partial = slow down

        SpeedMs = Mathf.MoveTowards(SpeedMs, target, rate * dt);
    }

    void UpdateSteering(float steer, float dt)
    {
        float speedFactor = Mathf.Lerp(1f, steerAtMaxSpeed, Mathf.Abs(SpeedKmh) / maxForwardKmh);
        float target = steer * maxSteerAngle * speedFactor;
        SteerAngle = Mathf.MoveTowards(SteerAngle, target, steerRate * dt);
    }

    void Move(float dt)
    {
        float distance = SpeedMs * dt;
        float yawRate = Mathf.Abs(SteerAngle) > 0.01f
            ? SpeedMs / wheelbase * Mathf.Tan(SteerAngle * Mathf.Deg2Rad) * Mathf.Rad2Deg
            : 0f;

        // Integrate around the rear axle using the mid-step heading (stable at any frame rate).
        float halfYaw = yawRate * dt * 0.5f;
        Vector3 dir = Quaternion.Euler(0f, halfYaw, 0f) * transform.forward;
        dir.y = 0f;
        transform.position += dir.normalized * distance;
        transform.rotation = Quaternion.Euler(0f, yawRate * dt, 0f) * transform.rotation;
    }

    void AnimateWheels(float dt)
    {
        float tanSteer = Mathf.Tan(SteerAngle * Mathf.Deg2Rad);
        foreach (var w in wheels)
        {
            if (w == null || w.spinPivot == null) continue;

            float wheelAngle = 0f;
            if (w.steers && Mathf.Abs(tanSteer) > 1e-4f)
            {
                // Ackermann: the inner wheel turns tighter than the outer one.
                float turnRadius = wheelbase / tanSteer;                   // signed, + = right turn
                float lateral = w.leftSide ? -trackWidth * 0.5f : trackWidth * 0.5f;
                wheelAngle = Mathf.Atan(wheelbase / (turnRadius - lateral)) * Mathf.Rad2Deg;
            }
            if (w.steerPivot != null && w.steers)
                w.steerPivot.localRotation = Quaternion.Euler(0f, wheelAngle, 0f);

            // Front wheels travel a slightly longer path than the rear axle when turning.
            float travel = SpeedMs * dt / Mathf.Max(0.2f, Mathf.Cos(wheelAngle * Mathf.Deg2Rad));
            float spinDeg = travel / Mathf.Max(0.05f, w.radius) * Mathf.Rad2Deg;
            w.spinPivot.localRotation *= Quaternion.Euler(spinDeg, 0f, 0f);
        }
    }
}
