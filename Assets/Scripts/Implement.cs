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

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        colliders = GetComponentsInChildren<Collider>();
        if (trail) trail.emitting = false;
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
        foreach (var s in stands) s.enabled = true;
        if (joint) Destroy(joint);
        transform.SetParent(null, true);
        rb.isKinematic = false;
        rb.linearVelocity = tractorRb.GetPointVelocity(transform.position);
        hitch = null;
        Working = false;
        DraftForce = 0f;
        if (trail) trail.emitting = false;
        // Collisions with the tractor come back once it has driven clear (see FrameUpdate).
    }

    void SetTractorCollisions(bool on)
    {
        if (tractorColliders == null) return;
        foreach (var a in colliders)
        foreach (var b in tractorColliders)
            Physics.IgnoreCollision(a, b, !on);
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
        if (!Attached && tractorColliders != null && tractorRb != null &&
            Vector3.Distance(tractorRb.position, transform.position) > 8f)
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

        float speed = Attached && tractorRb ? Vector3.Dot(tractorRb.linearVelocity, tractorRb.transform.forward) : 0f;
        bool pto = Attached && hitch && hitch.Pto;
        Working = Attached && Depth > (workingDepth > 0f ? 0.03f : -0.06f) && speed > 0.3f && (ptoKW <= 0f || pto);

        if (spinners.Length > 0 && hitch)
        {
            spinAngle += hitch.PtoSpeed / 60f * 360f * spinRatio * dt;
            foreach (var s in spinners) s.localRotation = Quaternion.Euler(0f, spinAngle, 0f);
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
        joint.enableCollision = false;
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
        DraftForce = draftKN * 1000f * Mathf.Clamp01(Depth / Mathf.Max(0.01f, workingDepth)) * (0.8f + 0.03f * kmh);
        tractorRb.AddForceAtPosition(-v.normalized * DraftForce, mountPoint.position);
    }
}
