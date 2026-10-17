using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// All player input in one place. Quest 2 Touch controllers are the primary device;
/// keyboard and gamepad bindings exist so the scene can be tested in the editor without a headset.
///
///   Left stick X ........ steering
///   Right stick Y ....... throttle (forward = drive, back = brake / reverse)
///   A (right) ........... switch camera: cab / third person
///   B (right) ........... recenter view
///   Left grip (hold) .... seat calibration mode (sticks move the seat instead of driving)
///   X (left) ............ cycle comfort vignette: off / low / high
///   Right stick flick → / ← ... shift up / down (switches the gearbox to manual)
///   Y (left) ............ gearbox mode: auto / manual
///   Right trigger ....... horn
/// Runs before the other tractor scripts so the per-frame flick events are ready when they read them.
/// </summary>
[DefaultExecutionOrder(-100)]
public class TractorInput : MonoBehaviour
{
    InputAction steerAction;
    InputAction throttleAction;
    InputAction switchViewAction;
    InputAction recenterAction;
    InputAction calibrateAction;
    InputAction comfortAction;
    InputAction modeAction;
    InputAction hornAction;
    int flick;          // -1 / 0 / +1: where the right stick X was held last frame

    public Vector2 LeftStick => steerAction.ReadValue<Vector2>();
    public Vector2 RightStick => throttleAction.ReadValue<Vector2>();
    public float Steer => Calibrating ? 0f : LeftStick.x;
    public float Throttle => Calibrating ? 0f : RightStick.y;
    public bool SwitchViewPressed => switchViewAction.WasPressedThisFrame();
    public bool RecenterPressed => recenterAction.WasPressedThisFrame();
    public bool Calibrating => calibrateAction.IsPressed();
    public bool ComfortCyclePressed => comfortAction.WasPressedThisFrame();
    public bool TransmissionModePressed => modeAction.WasPressedThisFrame();
    public bool Horn => hornAction.IsPressed();
    public bool ShiftUpPressed { get; private set; }
    public bool ShiftDownPressed { get; private set; }

    void Awake()
    {
        steerAction = new InputAction("Steer", InputActionType.Value, expectedControlType: "Vector2");
        steerAction.AddBinding("<XRController>{LeftHand}/{Primary2DAxis}");
        steerAction.AddBinding("<Gamepad>/leftStick");
        steerAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/q").With("Down", "<Keyboard>/e")
            .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");

        throttleAction = new InputAction("Throttle", InputActionType.Value, expectedControlType: "Vector2");
        throttleAction.AddBinding("<XRController>{RightHand}/{Primary2DAxis}");
        throttleAction.AddBinding("<Gamepad>/rightStick");
        throttleAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/z").With("Right", "<Keyboard>/x");

        switchViewAction = new InputAction("SwitchView", InputActionType.Button);
        switchViewAction.AddBinding("<XRController>{RightHand}/{PrimaryButton}");
        switchViewAction.AddBinding("<Gamepad>/buttonSouth");
        switchViewAction.AddBinding("<Keyboard>/c");

        recenterAction = new InputAction("Recenter", InputActionType.Button);
        recenterAction.AddBinding("<XRController>{RightHand}/{SecondaryButton}");
        recenterAction.AddBinding("<Gamepad>/buttonEast");
        recenterAction.AddBinding("<Keyboard>/r");

        calibrateAction = new InputAction("Calibrate", InputActionType.Button);
        calibrateAction.AddBinding("<XRController>{LeftHand}/{GripButton}");
        calibrateAction.AddBinding("<Gamepad>/leftShoulder");
        calibrateAction.AddBinding("<Keyboard>/leftShift");

        comfortAction = new InputAction("Comfort", InputActionType.Button);
        comfortAction.AddBinding("<XRController>{LeftHand}/{PrimaryButton}");
        comfortAction.AddBinding("<Gamepad>/buttonWest");
        comfortAction.AddBinding("<Keyboard>/v");

        modeAction = new InputAction("TransmissionMode", InputActionType.Button);
        modeAction.AddBinding("<XRController>{LeftHand}/{SecondaryButton}");
        modeAction.AddBinding("<Gamepad>/buttonNorth");
        modeAction.AddBinding("<Keyboard>/t");

        hornAction = new InputAction("Horn", InputActionType.Button);
        hornAction.AddBinding("<XRController>{RightHand}/{TriggerButton}");
        hornAction.AddBinding("<Gamepad>/rightTrigger");
        hornAction.AddBinding("<Keyboard>/h");
    }

    void Update()
    {
        // Shift = a sideways flick of the right stick (the stick has to come back towards the centre before the next one).
        Vector2 r = Calibrating ? Vector2.zero : RightStick;
        int now = flick;
        if (Mathf.Abs(r.x) < 0.4f) now = 0;
        else if (Mathf.Abs(r.x) > 0.7f && Mathf.Abs(r.x) > Mathf.Abs(r.y)) now = r.x > 0f ? 1 : -1;
        ShiftUpPressed = now == 1 && flick != 1;
        ShiftDownPressed = now == -1 && flick != -1;
        flick = now;
    }

    void OnEnable()
    {
        steerAction.Enable();
        throttleAction.Enable();
        switchViewAction.Enable();
        recenterAction.Enable();
        calibrateAction.Enable();
        comfortAction.Enable();
        modeAction.Enable();
        hornAction.Enable();
    }

    void OnDisable()
    {
        steerAction.Disable();
        throttleAction.Disable();
        switchViewAction.Disable();
        recenterAction.Disable();
        calibrateAction.Disable();
        comfortAction.Disable();
        modeAction.Disable();
        hornAction.Disable();
    }
}
