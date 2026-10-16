using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Animates the stand-in Touch controller model built by ProjectSetup (primitives in grip-pose space:
/// +Z along the handle toward the face plate, -Y = trigger side). The pose itself comes from a
/// TrackedPoseDriver on the same object. Shows stick tilt and button / trigger / grip presses,
/// and hides the model while the controller isn't tracked.
/// </summary>
public class ControllerVisual : MonoBehaviour
{
    public bool leftHand;
    public Transform stick;                         // pivot, tilts with the thumbstick
    public Renderer primaryButton, secondaryButton; // A/B on the right, X/Y on the left
    public Transform trigger, grip;
    public Material idle, pressed;

    const float StickTilt = 18f;      // degrees at full deflection
    const float ButtonTravel = 0.0015f;
    const float TriggerTravel = 0.006f;

    InputAction stickAction, primaryAction, secondaryAction, triggerAction, gripAction, trackedAction;
    Renderer[] renderers;
    Vector3 primaryHome, secondaryHome, triggerHome, gripHome;
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
        primaryHome = primaryButton.transform.localPosition;
        secondaryHome = secondaryButton.transform.localPosition;
        triggerHome = trigger.localPosition;
        gripHome = grip.localPosition;
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
        stick.localRotation = Quaternion.Euler(s.y * StickTilt, 0f, -s.x * StickTilt);

        Press(primaryButton, primaryHome, primaryAction.ReadValue<float>() > 0.5f);
        Press(secondaryButton, secondaryHome, secondaryAction.ReadValue<float>() > 0.5f);

        // Both move into the handle: the trigger along +Y, the grip button (on the palm side) along ±X.
        trigger.localPosition = triggerHome + Vector3.up * (triggerAction.ReadValue<float>() * TriggerTravel);
        float side = leftHand ? -1f : 1f;
        grip.localPosition = gripHome + Vector3.right * (side * gripAction.ReadValue<float>() * TriggerTravel * 0.5f);
    }

    void Press(Renderer button, Vector3 home, bool down)
    {
        button.transform.localPosition = home - Vector3.up * (down ? ButtonTravel : 0f);
        button.sharedMaterial = down ? pressed : idle;
    }
}
