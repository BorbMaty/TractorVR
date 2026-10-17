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
        public Vector3 PokeTip;
        public bool Grabbing;
        public bool GrabStarted;   // this frame
        public bool GrabEnded;     // this frame
    }

    public Transform cameraOffset;                  // tracking space
    public Transform leftGrip, rightGrip;           // controller grip poses (TrackedPoseDriver)
    public Transform leftAim, rightAim;             // controller aim poses (the visible controllers)
    public ControllerVisual leftControllerVisual, rightControllerVisual;

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

    void UpdateHand(Hand h, XRHand xr, Transform grip, Transform aim, InputAction gripAction, ControllerVisual controllerVisual)
    {
        bool wasGrabbing = h.Grabbing;
        if (xr.isTracked && Joint(xr, XRHandJointID.Palm, out Pose palm) && Joint(xr, XRHandJointID.IndexTip, out Pose index))
        {
            h.Tracked = h.IsHandTracking = true;
            Vector3 palmPos = cameraOffset.TransformPoint(palm.position);
            Vector3 fingers = cameraOffset.TransformDirection(palm.rotation * Vector3.forward);
            h.GrabPoint = palmPos + fingers * 0.03f;
            h.PokeTip = cameraOffset.TransformPoint(index.position);
            // Fist: average fingertip distance to the palm; close below 6.5 cm, open above 8.5 cm.
            float sum = 0f; int n = 0;
            foreach (var id in new[] { XRHandJointID.IndexTip, XRHandJointID.MiddleTip, XRHandJointID.RingTip })
                if (Joint(xr, id, out Pose tip)) { sum += Vector3.Distance(tip.position, palm.position); n++; }
            float curl = n > 0 ? sum / n : 1f;
            h.Grabbing = h.Grabbing ? curl < 0.085f : curl < 0.065f;
        }
        else
        {
            h.IsHandTracking = false;
            h.Tracked = controllerVisual == null || controllerVisual.Tracked;
            h.GrabPoint = grip.position;
            h.PokeTip = aim.position + aim.forward * 0.02f;
            float g = gripAction.ReadValue<float>();
            h.Grabbing = h.Tracked && (h.Grabbing ? g > 0.35f : g > 0.6f);
        }
        h.GrabStarted = h.Grabbing && !wasGrabbing;
        h.GrabEnded = !h.Grabbing && wasGrabbing;
    }

    static bool Joint(XRHand hand, XRHandJointID id, out Pose pose)
    {
        pose = default;
        return hand.GetJoint(id).TryGetPose(out pose);
    }
}
