using UnityEngine;

/// <summary>
/// An implement that couples to the tractor's rear three-point linkage, front linkage or drawbar.
///   Detached ... a free Rigidbody standing on the ground (stands / its own wheels).
///   Mounted .... (rear / front) kinematic, carried by the linkage mount; its mass moves the tractor's centre of mass.
///   Trailed .... (drawbar) stays a Rigidbody, coupled by a ball joint at the drawbar eye.
/// Work: <see cref="Depth"/> is how far the ground points (plough shares, cutter bar) are below the ground.
///   Plough: draft force against the motion, growing with depth and speed, applied to the tractor at the hitch.
///   PTO implements: parts spin with the PTO; power is taken from the engine (Hitch.PtoPowerKW).
///   A trail marks worked ground (furrows / mown strip).
/// </summary>
public class Implement : MonoBehaviour
{
    public enum Mount { Rear, Front, Drawbar }

    public string displayName = "IMPLEMENT";
    public Mount mount;
    public Transform hitchPoint;            // the implement's coupling point (lower link pins midpoint / drawbar eye)
    public Transform topPin;                // rear three-point implements: where the top link connects (null = no top link)
    public float mass = 1000f;
    public Vector3 centreOfMassOffset;      // from the coupling point, implement space
    public Collider[] stands = new Collider[0];   // parking stands / jack: switched off while coupled

    [Header("Work")]
    public Transform[] groundPoints = new Transform[0];
    public float workingDepth = 0f;         // m below the ground at full work (plough 0.25, mower 0 = floats)
    public float draftKN = 0f;              // draft at working depth and low speed
    public float ptoKW = 0f;
    public Transform[] spinners = new Transform[0];   // spin around their local Y with the PTO
    public float spinRatio = 1f;
    public TrailRenderer trail;             // child "marker", kept on the ground; emits while working

    public bool Attached { get; private set; }
    public float Depth { get; private set; } = -1f;
    public bool Working { get; private set; }
    public float DraftForce { get; private set; }
    public bool HardGround { get; private set; }
    /// <summary>How deep this implement may go here (0 on hard ground).</summary>
    public float WorkingDepthHere => HardGround ? 0f : workingDepth;

    Hitch hitch;
    Transform mountPoint;
    Rigidbody tractorRb, rb;
    ConfigurableJoint joint;
    float snap = 1f;
    Vector3 snapFromPos;
    Quaternion snapFromRot;
    Collider[] colliders;
    Collider[] tractorColliders;
    float spinAngle;
    float parkedPinHeight;                  // coupling point above the ground when standing on its own

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        colliders = GetComponentsInChildren<Collider>();
        if (trail) trail.emitting = false;
        parkedPinHeight = hitchPoint ? hitchPoint.localPosition.y : 0f;
    }

    public void Attach(Hitch h, Transform mountT, Rigidbody tractorBody)
    {
        hitch = h;
        mountPoint = mountT;
        tractorRb = tractorBody;
        Attached = true;
        foreach (var s in stands) s.enabled = false;
        tractorColliders = tractorBody.GetComponentsInChildren<Collider>();
        SetTractorCollisions(false);

        // Slide onto the coupling over a short time (kinematic), then couple.
        rb.isKinematic = true;
        snapFromPos = transform.position;
        snapFromRot = transform.rotation;
        snap = 0f;
        if (mount != Mount.Drawbar) transform.SetParent(mountT, true);
    }

    public void Detach()
    {
        Attached = false;
        SetTractorCollisions(false);   // back on once nothing overlaps (FrameUpdate), e.g. the jack coming down
        foreach (var s in stands) s.enabled = true;
        if (joint) { if (Application.isPlaying) Destroy(joint); else DestroyImmediate(joint); }   // edit mode: tests
        transform.SetParent(null, true);
        rb.isKinematic = false;
        rb.linearVelocity = tractorRb.GetPointVelocity(transform.position);
        hitch = null;
        Working = false;
        DraftForce = 0f;
        if (trail) trail.emitting = false;
        // Collisions with the tractor come back once nothing overlaps (see FrameUpdate).
    }

    /// <summary>True while any of our colliders intersects one of the tractor's.</summary>
    public bool OverlapsTractor()
    {
        if (tractorRb == null) return false;
        var tc = tractorColliders ?? tractorRb.GetComponentsInChildren<Collider>();
        foreach (var a in colliders)
        {
            if (!a.enabled) continue;
            foreach (var b in tc)
                if (Physics.ComputePenetration(a, a.transform.position, a.transform.rotation,
                                               b, b.transform.position, b.transform.rotation, out _, out _))
                    return true;
        }
        return false;
    }

    void SetTractorCollisions(bool on)
    {
        if (tractorColliders == null) return;
        foreach (var a in colliders)
        foreach (var b in tractorColliders)
            Physics.IgnoreCollision(a, b, !on);
    }

    /// <summary>Put a coupled implement exactly on its mount, at rest (after the tractor was teleported).</summary>
    public void SnapToMount()
    {
        if (!Attached) return;
        TargetPose(out var p, out var r);
        transform.SetPositionAndRotation(p, r);
        snapFromPos = p;
        snapFromRot = r;
        if (rb.isKinematic) return;
        rb.position = p;
        rb.rotation = r;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        var wheels = GetComponent<SimpleWheels>();
        if (wheels) wheels.ResetSuspension();
    }

    // Pose that puts our coupling point on the tractor's mount, facing the tractor's way.
    void TargetPose(out Vector3 pos, out Quaternion rot)
    {
        rot = mountPoint.rotation;
        pos = mountPoint.position - rot * hitchPoint.localPosition;
    }

    void Update() => FrameUpdate(Time.deltaTime);

    public void FrameUpdate(float dt)
    {
        // After detaching: collide with the tractor again as soon as nothing overlaps (re-enabling inside an overlap
        // would shove the two apart).
        if (!Attached && tractorColliders != null && !OverlapsTractor())
        {
            SetTractorCollisions(true);
            tractorColliders = null;
        }

        if (Attached && snap < 1f)
        {
            snap = Mathf.Min(1f, snap + dt / 0.8f);
            TargetPose(out var p, out var r);
            float s = Mathf.SmoothStep(0f, 1f, snap);
            transform.SetPositionAndRotation(Vector3.Lerp(snapFromPos, p, s), Quaternion.Slerp(snapFromRot, r, s));
            if (snap >= 1f && mount == Mount.Drawbar) Couple();
        }
        else if (Attached && mount != Mount.Drawbar)
        {
            TargetPose(out var p, out var r);
            transform.SetPositionAndRotation(p, r);
        }

        // Depth of the deepest ground point (positive = in the ground).
        Depth = -1f;
        foreach (var g in groundPoints)
            Depth = Mathf.Max(Depth, TerrainGenerator.Height(g.position.x, g.position.z) - g.position.y);
        // No working parts (front weight): how far the coupling point is below its parked height (0 = standing on its feet).
        if (groundPoints.Length == 0 && hitchPoint)
            Depth = TerrainGenerator.Height(hitchPoint.position.x, hitchPoint.position.z) + parkedPinHeight - hitchPoint.position.y;
        // Concrete, asphalt and gravel (draft 0 in GroundMap): the shares can't enter, so the working depth there is 0.
        HardGround = workingDepth > 0f && groundPoints.Length > 0 && GroundMap.SurfaceAt(groundPoints[0].position).draft <= 0f;

        float speed = Attached && tractorRb ? Vector3.Dot(tractorRb.linearVelocity, tractorRb.transform.forward) : 0f;
        bool pto = Attached && hitch && hitch.Pto;
        Working = Attached && Depth > (workingDepth > 0f ? 0.03f : -0.06f) && speed > 0.3f && (ptoKW <= 0f || pto);

        if (spinners.Length > 0 && hitch)
        {
            spinAngle += hitch.PtoSpeed / 60f * 360f * spinRatio * dt;
            foreach (var s in spinners) s.localRotation = Quaternion.Euler(0f, spinAngle, 0f);
        }

        // Ploughing turns the ground over: lower grip, more rolling resistance, less draft on the next pass.
        if (Working && draftKN > 0f && trail)
        {
            Vector3 c = trail.transform.position, right = transform.right;
            float half = trail.widthMultiplier * 0.5f;
            for (float o = -half; o <= half; o += 0.5f)
            {
                Vector3 q = c + right * o;
                var here = GroundMap.At(q.x, q.z);
                if (here == Ground.Grass || here == Ground.Soil || here == Ground.Crop || here == Ground.Mud) GroundMap.Set(q.x, q.z, Ground.Ploughed);
            }
        }

        if (trail)
        {
            trail.emitting = Working;
            Vector3 m = trail.transform.position;
            m.y = TerrainGenerator.Height(m.x, m.z) + 0.04f;
            trail.transform.position = m;
            trail.transform.rotation = Quaternion.LookRotation(Vector3.up, transform.forward);  // trail lies flat (Transform Z alignment)
        }
    }

    // Trailed: ball joint at the drawbar eye; free to swing, yaw limited so it can't fold into the tractor.
    void Couple()
    {
        rb.isKinematic = false;
        joint = gameObject.AddComponent<ConfigurableJoint>();
        joint.connectedBody = tractorRb;
        joint.autoConfigureConnectedAnchor = false;
        joint.anchor = transform.InverseTransformPoint(hitchPoint.position);
        joint.connectedAnchor = tractorRb.transform.InverseTransformPoint(mountPoint.position);
        joint.axis = Vector3.right;
        joint.secondaryAxis = Vector3.up;
        joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
        joint.angularXMotion = ConfigurableJointMotion.Free;
        joint.angularYMotion = ConfigurableJointMotion.Limited;
        joint.angularYLimit = new SoftJointLimit { limit = 70f };
        joint.angularZMotion = ConfigurableJointMotion.Limited;
        joint.angularZLimit = new SoftJointLimit { limit = 25f };
        joint.projectionMode = JointProjectionMode.PositionAndRotation;
        // The trailer body must still hit the tractor (jackknifing, reversing into it): it sits 0.24 m behind the
        // tractor when straight, so there is no contact at the coupling itself.
        joint.enableCollision = true;
        SetTractorCollisions(true);
    }

    void FixedUpdate() => PhysicsStep();

    public void PhysicsStep()
    {
        DraftForce = 0f;
        if (!Attached || draftKN <= 0f || Depth <= 0f || tractorRb == null) return;
        Vector3 v = Vector3.ProjectOnPlane(tractorRb.GetPointVelocity(mountPoint.position), Vector3.up);
        if (v.magnitude < 0.05f) return;
        // Draft grows with depth and (a little) with speed; it always acts against the motion.
        float kmh = v.magnitude * 3.6f;
        // Soil decides too: grass sod is tough, ploughed ground easy, roads and concrete very hard (GroundMap).
        float soil = groundPoints.Length > 0 ? GroundMap.SurfaceAt(groundPoints[0].position).draft : 1f;
        if (soil <= 0f) return;   // hard ground: resting on it, no draft
        DraftForce = draftKN * 1000f * soil * Mathf.Clamp01(Depth / Mathf.Max(0.01f, workingDepth)) * (0.8f + 0.03f * kmh);
        tractorRb.AddForceAtPosition(-v.normalized * DraftForce, mountPoint.position);
    }
}
