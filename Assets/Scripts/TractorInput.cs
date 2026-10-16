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
/// </summary>
public class TractorInput : MonoBehaviour
{
    InputAction steerAction;
    InputAction throttleAction;
    InputAction switchViewAction;
    InputAction recenterAction;
    InputAction calibrateAction;
    InputAction comfortAction;

    public Vector2 LeftStick => steerAction.ReadValue<Vector2>();
    public Vector2 RightStick => throttleAction.ReadValue<Vector2>();
    public float Steer => Calibrating ? 0f : LeftStick.x;
    public float Throttle => Calibrating ? 0f : RightStick.y;
    public bool SwitchViewPressed => switchViewAction.WasPressedThisFrame();
    public bool RecenterPressed => recenterAction.WasPressedThisFrame();
    public bool Calibrating => calibrateAction.IsPressed();
    public bool ComfortCyclePressed => comfortAction.WasPressedThisFrame();

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
    }

    void OnEnable()
    {
        steerAction.Enable();
        throttleAction.Enable();
        switchViewAction.Enable();
        recenterAction.Enable();
        calibrateAction.Enable();
        comfortAction.Enable();
    }

    void OnDisable()
    {
        steerAction.Disable();
        throttleAction.Disable();
        switchViewAction.Disable();
        recenterAction.Disable();
        calibrateAction.Disable();
        comfortAction.Disable();
    }
}
