using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Hands;

/// <summary>
/// One "hand" per side, from whichever is active: a tracked hand (XR Hands) or a Touch controller.
///   Grab point ... controller: OpenXR grip pose (centre of the hand on the handle); hand: palm, nudged towards the fingers.
///   Grabbing ..... controller: grip button; hand: a fist (index, middle and ring tips close to the palm), with hysteresis.
///   Poke tip ..... controller: the front of the controller; hand: the index fingertip. Used for the cab panel buttons.
/// Hand joint poses are in tracking space, so they go through the rig's Camera Offset to world space.
/// </summary>
[DefaultExecutionOrder(-90)]   // after TractorInput, before everything that grabs or pokes
public class VRHands : MonoBehaviour
{
    public class Hand
    {
        public bool Tracked;
        public bool IsHandTracking;
        public Vector3 GrabPoint;
        public Vector3 RawGrabPoint;   // unfiltered (where the visible hand is)
        public Vector3 PokeTip;
        public bool Grabbing;
        public bool GrabStarted;   // this frame
        public bool GrabEnded;     // this frame
    }

    public Transform cameraOffset;                  // tracking space
    public Transform leftGrip, rightGrip;           // controller grip poses (TrackedPoseDriver)
    public Transform leftAim, rightAim;             // controller aim poses (the visible controllers)
    public ControllerVisual leftControllerVisual, rightControllerVisual;
    public XRHandSkeletonDriver leftDriver, rightDriver;   // tracked hand meshes (for snapping onto what they hold)

    public Hand Left { get; } = new Hand();
    public Hand Right { get; } = new Hand();
    public IEnumerable<Hand> Both { get { yield return Left; yield return Right; } }

    InputAction leftGripAction, rightGripAction;
    XRHandSubsystem hands;
    readonly List<XRHandSubsystem> found = new List<XRHandSubsystem>();

    void Awake()
    {
        leftGripAction = new InputAction("LeftGrab", InputActionType.Value, "<XRController>{LeftHand}/{Grip}", expectedControlType: "Axis");
        rightGripAction = new InputAction("RightGrab", InputActionType.Value, "<XRController>{RightHand}/{Grip}", expectedControlType: "Axis");
    }

    void OnEnable() { leftGripAction.Enable(); rightGripAction.Enable(); }
    void OnDisable() { leftGripAction.Disable(); rightGripAction.Disable(); }

    void Update()
    {
        if (hands == null || !hands.running)
        {
            SubsystemManager.GetSubsystems(found);
            hands = found.Find(s => s.running);
        }
        UpdateHand(Left, hands != null ? hands.leftHand : default, leftGrip, leftAim, leftGripAction, leftControllerVisual);
        UpdateHand(Right, hands != null ? hands.rightHand : default, rightGrip, rightAim, rightGripAction, rightControllerVisual);
    }

    readonly HandSignal leftSignal = new HandSignal(), rightSignal = new HandSignal();

    void UpdateHand(Hand h, XRHand xr, Transform grip, Transform aim, InputAction gripAction, ControllerVisual controllerVisual)
    {
        var sig = h == Left ? leftSignal : rightSignal;
        float dt = Time.deltaTime;
        if (xr.isTracked && Joint(xr, XRHandJointID.Palm, out Pose palm) && Joint(xr, XRHandJointID.IndexTip, out Pose index)
            && Joint(xr, XRHandJointID.MiddleProximal, out Pose knuckle) && Joint(xr, XRHandJointID.ThumbTip, out Pose thumb))
        {
            // Grab point: between the palm and the middle knuckle, which stay steady in a fist (the fingertips are hidden then).
            Vector3 raw = cameraOffset.TransformPoint((palm.position + knuckle.position) * 0.5f);
            h.RawGrabPoint = raw;
            // Fist: average fingertip distance to the palm (close < 6.5 cm, open > 8.5 cm). Pinch: thumb to index tip (< 2.5 / > 4.5 cm).
            float sum = 0f; int n = 0;
            foreach (var id in new[] { XRHandJointID.IndexTip, XRHandJointID.MiddleTip, XRHandJointID.RingTip })
                if (Joint(xr, id, out Pose tip)) { sum += Vector3.Distance(tip.position, palm.position); n++; }
            float curl = n > 0 ? sum / n : 1f;
            sig.fist = sig.fist ? curl < 0.085f : curl < 0.065f;
            float pinch = Vector3.Distance(thumb.position, index.position);
            sig.pinch = sig.pinch ? pinch < 0.045f : pinch < 0.025f;
            sig.Step(true, raw, sig.fist || sig.pinch, dt);
            h.IsHandTracking = true;
            h.PokeTip = cameraOffset.TransformPoint(index.position);
            Apply(h, true, sig.Point, sig.Grabbing);
            return;
        }
        sig.Step(false, Vector3.zero, false, dt);
        if (sig.Active)
        {
            // Hand briefly lost while holding (a fist hides the fingers): keep holding at the last point.
            Apply(h, true, sig.Point, true);
            return;
        }
        h.IsHandTracking = false;
        h.PokeTip = aim.position + aim.forward * 0.02f;
        bool tracked = controllerVisual == null || controllerVisual.Tracked;
        h.RawGrabPoint = grip.position;
        float g = gripAction.ReadValue<float>();
        Apply(h, tracked, grip.position, tracked && (h.Grabbing ? g > 0.35f : g > 0.6f));
    }

    /// <summary>
    /// Shows a tracked hand at <paramref name="target"/> (its grab point moved there) while it holds something, so the
    /// wheel rim or lever knob stays in the hand and the tracking jitter doesn't show; null puts it back. Controllers are left alone.
    /// </summary>
    public void SnapVisual(Hand h, Vector3? target)
    {
        var driver = h == Left ? leftDriver : rightDriver;
        if (driver == null) return;
        if (target.HasValue && h.IsHandTracking) driver.ApplyRootPoseOffset(target.Value - h.RawGrabPoint);
        else driver.ResetRootPoseOffset();
    }

    /// <summary>Sets a hand's state and its grab edges for this frame.</summary>
    public static void Apply(Hand h, bool tracked, Vector3 grabPoint, bool grabbing)
    {
        bool was = h.Grabbing;
        h.Tracked = tracked;
        h.GrabPoint = grabPoint;
        h.Grabbing = grabbing;
        h.GrabStarted = grabbing && !was;
        h.GrabEnded = !grabbing && was;
    }

    /// <summary>
    /// Steadies a tracked hand for grabbing: a one-euro filter on the grab point (strong smoothing while still, little lag
    /// when moving), a release only after the hand has been open for <see cref="releaseDelay"/> (the point is frozen meanwhile,
    /// so opening the hand doesn't drag the wheel or lever), and a grace period when tracking drops out while holding.
    /// </summary>
    public class HandSignal
    {
        public float minCutoff = 1f;      // Hz while still
        public float beta = 4f;           // extra cutoff per m/s of hand speed
        public float releaseDelay = 0.2f; // s
        public float lostGrace = 0.4f;    // s
        public bool fist, pinch;          // hysteresis state of the raw gestures (kept here per hand)

        public bool Active { get; private set; }
        public bool Grabbing { get; private set; }
        public Vector3 Point { get; private set; }

        OneEuro x = new OneEuro(), y = new OneEuro(), z = new OneEuro();
        float openTime, lostTime;

        public void Step(bool tracked, Vector3 raw, bool rawGrab, float dt)
        {
            if (!tracked)
            {
                lostTime += dt;
                Active = Grabbing && lostTime < lostGrace;
                if (!Active) { Grabbing = false; x.Reset(); y.Reset(); z.Reset(); }
                return;
            }
            lostTime = 0f;
            Active = true;
            Vector3 filtered = new Vector3(x.Filter(raw.x, dt, minCutoff, beta), y.Filter(raw.y, dt, minCutoff, beta), z.Filter(raw.z, dt, minCutoff, beta));
            if (rawGrab) { Grabbing = true; openTime = 0f; Point = filtered; }
            else if (Grabbing)
            {
                openTime += dt;                               // keep the last held point until the release is certain
                if (openTime > releaseDelay) { Grabbing = false; Point = filtered; }
            }
            else Point = filtered;
        }

        class OneEuro
        {
            bool init;
            float value, speed;
            public void Reset() => init = false;
            static float Alpha(float cutoff, float dt) => 1f / (1f + 1f / (2f * Mathf.PI * cutoff * Mathf.Max(dt, 1e-4f)));
            public float Filter(float v, float dt, float minCutoff, float beta)
            {
                if (!init) { init = true; value = v; speed = 0f; return v; }
                speed += Alpha(1f, dt) * ((v - value) / Mathf.Max(dt, 1e-4f) - speed);
                value += Alpha(minCutoff + beta * Mathf.Abs(speed), dt) * (v - value);
                return value;
            }
        }
    }

    static bool Joint(XRHand hand, XRHandJointID id, out Pose pose)
    {
        pose = default;
        return hand.GetJoint(id).TryGetPose(out pose);
    }
}
