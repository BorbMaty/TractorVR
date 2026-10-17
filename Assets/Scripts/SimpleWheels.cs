using UnityEngine;

/// <summary>
/// Undriven raycast wheels for trailed implements (same idea as the tractor's, much simpler):
/// preloaded spring-damper, sideways grip that cancels sliding, rolling resistance, and a parking brake while uncoupled.
/// Rays ignore anything with a Rigidbody (the trailer itself, the tractor, bales).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class SimpleWheels : MonoBehaviour
{
    [System.Serializable]
    public class Wheel
    {
        public Transform pivot;      // wheel centre as modelled; the mesh child spins around local X
        public float radius = 0.5f;
        [System.NonSerialized] public Vector3 rest;
        [System.NonSerialized] public float compression, last, load;
    }

    public Wheel[] wheels = new Wheel[0];
    public float travel = 0.08f;
    public float stiffness = 250000f;
    public float dampingRatio = 0.5f;
    public float staticShare = 0.45f;          // share of the weight on each wheel at rest (the rest is on the drawbar / jack)
    public float grip = 0.8f;
    public float rollingResistance = 0.04f;
    public Implement implement;                // parked brake while not attached

    Rigidbody rb;
    readonly RaycastHit[] hits = new RaycastHit[8];

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        foreach (var w in wheels) { w.rest = w.pivot.localPosition; w.compression = w.last = travel; }
    }

    void FixedUpdate() => PhysicsStep();

    public void PhysicsStep()
    {
        if (rb.isKinematic) return;
        float dt = Time.fixedDeltaTime, g = -Physics.gravity.y;
        float staticLoad = rb.mass * g * staticShare;
        bool parked = implement != null && !implement.Attached;
        foreach (var w in wheels)
        {
            Vector3 up = transform.up;
            Vector3 top = transform.TransformPoint(w.rest) + up * travel;
            float len = 2f * travel + w.radius;
            w.last = w.compression;
            if (!Ray(top, -up, len, out RaycastHit hit)) { w.compression = 0f; w.load = 0f; continue; }
            w.compression = len - hit.distance;
            float c = 2f * dampingRatio * Mathf.Sqrt(stiffness * staticLoad / g);
            w.load = Mathf.Clamp(staticLoad + stiffness * (w.compression - travel) + c * (w.compression - w.last) / dt, 0f, 6f * staticLoad);
            rb.AddForceAtPosition(up * w.load, hit.point);

            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, hit.normal).normalized;
            Vector3 side = Vector3.Cross(hit.normal, fwd);
            Vector3 v = rb.GetPointVelocity(hit.point);
            float vLong = Vector3.Dot(v, fwd);
            float fLat = -Vector3.Dot(v, side) * (w.load / g) / dt * 0.5f;
            float fLong = parked ? -vLong * (w.load / g) / dt * 0.5f
                                 : -Mathf.Clamp(vLong * 10f, -1f, 1f) * rollingResistance * w.load;
            float max = grip * w.load, l = Mathf.Sqrt(fLat * fLat + fLong * fLong);
            if (l > max) { fLat *= max / l; fLong *= max / l; }
            rb.AddForceAtPosition(side * fLat + fwd * fLong, hit.point);
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        foreach (var w in wheels)
        {
            w.pivot.localPosition = w.rest + Vector3.up * (w.compression - travel);
            float roll = Vector3.Dot(rb.GetPointVelocity(w.pivot.position), transform.forward);
            // The "Spin" child carries the tyre and rim.
            if (w.pivot.childCount > 0) w.pivot.GetChild(0).localRotation *= Quaternion.Euler(roll * dt / w.radius * Mathf.Rad2Deg, 0f, 0f);
        }
    }

    bool Ray(Vector3 o, Vector3 d, float len, out RaycastHit best)
    {
        best = default;
        int n = Physics.RaycastNonAlloc(o, d, hits, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float bd = float.MaxValue;
        for (int i = 0; i < n; i++)
            if (hits[i].rigidbody == null && hits[i].distance < bd) { best = hits[i]; bd = best.distance; }
        return bd < float.MaxValue;
    }
}
