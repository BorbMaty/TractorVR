using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Moves the XR rig between the two viewpoints:
///   Cab ........... locked to the driver's seat, rides with the tractor.
///   Third person .. behind and above the tractor, follows its heading with yaw smoothing
///                   (yaw only — no pitch/roll on the rig, for comfort).
/// Hierarchy: Rig (this) > CameraOffset > Main Camera (TrackedPoseDriver).
/// CameraOffset cancels the headset's tracked pose at recenter time, so the eye lands exactly on
/// the anchor regardless of floor/device tracking origin or where the user is sitting.
/// </summary>
public class VRCameraRig : MonoBehaviour
{
    public enum View { Cab, ThirdPerson }

    public TractorController tractor;
    public TractorInput input;
    public Transform seatAnchor;
    public Transform cameraOffset;
    public Transform head;

    public View view = View.Cab;
    /// <summary>Start menu (phase 6): the rig stands at <see cref="menuAnchor"/> instead of the seat / behind the tractor.</summary>
    [System.NonSerialized] public bool InMenu;
    [System.NonSerialized] public Transform menuAnchor;
    public Vector3 thirdPersonOffset = new Vector3(0f, 4.2f, -9.5f);
    public float thirdPersonYawSmoothing = 0.35f;

    [Header("Seat calibration (hold left grip)")]
    public float calibrateSpeed = 0.25f;  // m/s

    public event System.Action<View> ViewChanged;

    float followYaw, followYawVel;
    float recenterTimer = 0.6f;
    bool recentered;
    Vector3 seatHome;
    readonly List<XRInputSubsystem> subsystems = new List<XRInputSubsystem>();

    const string SeatPrefKey = "SeatOffset";

    void Start()
    {
        seatHome = seatAnchor.localPosition;
        seatAnchor.localPosition = seatHome + LoadSeatOffset();
        followYaw = tractor.transform.eulerAngles.y;

        SubsystemManager.GetSubsystems(subsystems);
        foreach (var s in subsystems) s.trackingOriginUpdated += OnTrackingOriginUpdated;
    }

    void OnDestroy()
    {
        foreach (var s in subsystems) s.trackingOriginUpdated -= OnTrackingOriginUpdated;
    }

    // Quest system recenter (holding the Meta button) moves the tracking space — follow it.
    void OnTrackingOriginUpdated(XRInputSubsystem _) => recenterTimer = 0.1f;

    void Update()
    {
        if (input.SwitchViewPressed && !InMenu) ToggleView();
        if (input.RecenterPressed) Recenter();

        // Auto recenter once the headset has delivered its first poses.
        if (!recentered || recenterTimer > 0f)
        {
            recenterTimer -= Time.deltaTime;
            if (recenterTimer <= 0f) { Recenter(); recentered = true; }
        }

        if (input.Calibrating && view == View.Cab && !InMenu) CalibrateSeat();
    }

    void LateUpdate()
    {
        Transform t = tractor.transform;
        if (InMenu && menuAnchor != null)
        {
            transform.SetPositionAndRotation(menuAnchor.position, menuAnchor.rotation);
            return;
        }
        if (view == View.Cab)
        {
            transform.SetPositionAndRotation(seatAnchor.position, seatAnchor.rotation);
        }
        else
        {
            followYaw = Mathf.SmoothDampAngle(followYaw, t.eulerAngles.y, ref followYawVel, thirdPersonYawSmoothing);
            Quaternion yaw = Quaternion.Euler(0f, followYaw, 0f);
            transform.SetPositionAndRotation(t.position + yaw * thirdPersonOffset, yaw);
        }
    }

    public void ToggleView()
    {
        view = view == View.Cab ? View.ThirdPerson : View.Cab;
        followYaw = tractor.transform.eulerAngles.y;
        followYawVel = 0f;
        ViewChanged?.Invoke(view);
    }

    /// <summary>Third person: jump to behind the tractor's current heading (after a teleport) instead of swinging round.</summary>
    public void SnapFollow()
    {
        followYaw = tractor.transform.eulerAngles.y;
        followYawVel = 0f;
    }

    public void ResetSeat()
    {
        seatAnchor.localPosition = seatHome;
        SaveSeatOffset(Vector3.zero);
    }

    public void Recenter()
    {
        if (DesktopMode.Active) return;   // no headset pose; the desktop camera has its own "look ahead" (R)
        Vector3 p = head.localPosition;
        float headYaw = head.localEulerAngles.y;
        Quaternion inv = Quaternion.Euler(0f, -headYaw, 0f);
        cameraOffset.localRotation = inv;
        cameraOffset.localPosition = -(inv * p);
    }

    void CalibrateSeat()
    {
        // Left stick Y: up/down, right stick Y: forward/back, right stick X: left/right.
        Vector3 delta = new Vector3(input.RightStick.x, input.LeftStick.y, input.RightStick.y);
        if (delta.sqrMagnitude < 0.01f) return;
        seatAnchor.localPosition += delta * calibrateSpeed * Time.deltaTime;
        SaveSeatOffset(seatAnchor.localPosition - seatHome);
    }

    static Vector3 LoadSeatOffset() => new Vector3(
        PlayerPrefs.GetFloat(SeatPrefKey + "X", 0f),
        PlayerPrefs.GetFloat(SeatPrefKey + "Y", 0f),
        PlayerPrefs.GetFloat(SeatPrefKey + "Z", 0f));

    static void SaveSeatOffset(Vector3 o)
    {
        PlayerPrefs.SetFloat(SeatPrefKey + "X", o.x);
        PlayerPrefs.SetFloat(SeatPrefKey + "Y", o.y);
        PlayerPrefs.SetFloat(SeatPrefKey + "Z", o.z);
    }
}
