using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Animates the bones of the official Quest 2 Touch model (set up by ProjectSetup.BuildController):
/// stick tilt, A/B (X/Y) press, trigger and grip, using the ranges of Meta's own press animations.
/// The pose comes from a TrackedPoseDriver on the same object. Hidden while the controller isn't tracked.
/// </summary>
public class ControllerVisual : MonoBehaviour
{
    public bool leftHand;
    public Transform stick, primaryButton, secondaryButton, trigger, grip;
    [Tooltip("Button press depth in the bones' local units.")]
    public float buttonTravel = 0.0018f;

    // From the FBX animation curves (converted to Unity's left-handed axes: Y and Z rotations flip).
    const float StickTilt = 22f;
    const float TriggerAngle = 20f;
    static readonly Vector3 GripAngles = new Vector3(1.34f, -7.36f, -4.12f);

    InputAction stickAction, primaryAction, secondaryAction, triggerAction, gripAction, trackedAction;
    Renderer[] renderers;
    Quaternion stickRest, triggerRest, gripRest;
    Vector3 primaryRest, secondaryRest;
    bool shown = true;

    void Awake()
    {
        string hand = leftHand ? "{LeftHand}" : "{RightHand}";
        stickAction = Make("Stick", $"<XRController>{hand}/{{Primary2DAxis}}");
        primaryAction = Make("Primary", $"<XRController>{hand}/{{PrimaryButton}}");
        secondaryAction = Make("Secondary", $"<XRController>{hand}/{{SecondaryButton}}");
        triggerAction = Make("Trigger", $"<XRController>{hand}/{{Trigger}}");
        gripAction = Make("Grip", $"<XRController>{hand}/{{Grip}}");
        trackedAction = Make("Tracked", $"<XRController>{hand}/isTracked");

        renderers = GetComponentsInChildren<Renderer>(true);
        stickRest = stick.localRotation;
        triggerRest = trigger.localRotation;
        gripRest = grip.localRotation;
        primaryRest = primaryButton.localPosition;
        secondaryRest = secondaryButton.localPosition;
    }

    static InputAction Make(string name, string binding) =>
        new InputAction(name, InputActionType.PassThrough, binding);

    void OnEnable()
    {
        stickAction.Enable(); primaryAction.Enable(); secondaryAction.Enable();
        triggerAction.Enable(); gripAction.Enable(); trackedAction.Enable();
    }

    void OnDisable()
    {
        stickAction.Disable(); primaryAction.Disable(); secondaryAction.Disable();
        triggerAction.Disable(); gripAction.Disable(); trackedAction.Disable();
    }

    void Update()
    {
        bool tracked = trackedAction.ReadValue<float>() > 0.5f;
        if (tracked != shown)
        {
            shown = tracked;
            foreach (var r in renderers) r.enabled = tracked;
        }
        if (!tracked) return;

        Vector2 s = stickAction.ReadValue<Vector2>();
        stick.localRotation = stickRest * Quaternion.Euler(s.y * StickTilt, 0f, -s.x * StickTilt);

        primaryButton.localPosition = primaryRest - Vector3.up * (primaryAction.ReadValue<float>() > 0.5f ? buttonTravel : 0f);
        secondaryButton.localPosition = secondaryRest - Vector3.up * (secondaryAction.ReadValue<float>() > 0.5f ? buttonTravel : 0f);

        trigger.localRotation = triggerRest * Quaternion.Euler(triggerAction.ReadValue<float>() * TriggerAngle, 0f, 0f);
        Vector3 g = GripAngles;
        if (leftHand) { g.y = -g.y; g.z = -g.z; } // the left model is mirrored
        grip.localRotation = gripRest * Quaternion.Euler(g * gripAction.ReadValue<float>());
    }
}
