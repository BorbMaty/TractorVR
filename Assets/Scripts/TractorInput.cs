using System.Collections.Generic;
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
///   Seat adjust ......... toggled in the cab panel: the sticks move the seat instead of driving (keyboard: hold Left Shift)
///   Left menu button .... cab panel open / close (keyboard M)
///   Grip buttons ........ grab the steering wheel / speed lever (CabControls, VRHands)
///   X (left) ............ cycle comfort vignette: off / low / high
///   Right stick flick → / ← ... shift up / down (switches the gearbox to manual)
///   Y (left) ............ gearbox mode: auto / manual
///   Right trigger ....... horn
///   Left stick ↑ / ↓ .... raise / lower the rear hitch (hold left trigger: front linkage)
///   Right stick click ... PTO on / off
///   Left stick click .... attach / detach implement
///   Hand throttle ....... cab panel / CommandCenter buttons (keyboard ] / [, gamepad d-pad up / down)
///   Trailer tipping ..... remote lever 3 / cab panel TIP buttons (keyboard I / K)
///   Lights .............. console switches / cab panel (keyboard G road lights, N work lights, O beacon, Y hazards,
///                         , / . indicators, U day / dusk / night)
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
    InputAction frontLinkageAction, ptoAction, attachAction, menuAction, handThrottleAction, tipAction;
    InputAction fourWdAction, diffLockAction, brakeModeAction;
    InputAction lightsAction, workLightsAction, beaconAction, hazardsAction, indicatorLeftAction, indicatorRightAction, skyAction;
    int flick;          // -1 / 0 / +1: where the right stick X was held last frame

    public Vector2 LeftStick => steerAction.ReadValue<Vector2>();
    public Vector2 RightStick => throttleAction.ReadValue<Vector2>();
    public float Steer => scripted ? scriptedSteer : Calibrating ? 0f : LeftStick.x;
    public float Throttle => scripted ? scriptedThrottle : Calibrating ? 0f : Mathf.Abs(RightStick.y) > 0.08f ? RightStick.y : leverThrottle;
    /// <summary>Speed lever position from the cab (CabControls), used while the right stick is idle.</summary>
    [System.NonSerialized] public float leverThrottle;
    /// <summary>Seat adjust mode, switched in the cab panel.</summary>
    [System.NonSerialized] public bool seatAdjust;
    public bool MenuPressed => menuAction.WasPressedThisFrame();
    /// <summary>Hand throttle up (+1) / down (-1) while held.</summary>
    public float HandThrottle => scripted ? 0f : handThrottleAction.ReadValue<float>();
    /// <summary>Tipping trailer body up (+1) / down (-1) while held.</summary>
    public float TipLever => scripted ? scriptedTip : tipAction.ReadValue<float>();

    /// <summary>Test autopilot (editor physics test): replaces the sticks for steering and throttle.</summary>
    [System.NonSerialized] public bool scripted;
    [System.NonSerialized] public float scriptedSteer, scriptedThrottle, scriptedHitch, scriptedTip;
    [System.NonSerialized] public bool scriptedFront;
    public bool SwitchViewPressed => switchViewAction.WasPressedThisFrame();
    public bool RecenterPressed => recenterAction.WasPressedThisFrame();
    public bool Calibrating => seatAdjust || calibrateAction.IsPressed();
    public bool ComfortCyclePressed => comfortAction.WasPressedThisFrame();
    public bool TransmissionModePressed => modeAction.WasPressedThisFrame();
    public bool Horn => hornAction.IsPressed();
    /// <summary>Hitch lever from the left stick's vertical axis (-1 lower .. +1 raise), only when it's clearly up/down, not steering.</summary>
    public float HitchLever
    {
        get
        {
            if (scripted) return scriptedHitch;
            if (Calibrating) return 0f;
            Vector2 s = LeftStick;
            if (Mathf.Abs(s.y) < 0.6f || Mathf.Abs(s.y) < Mathf.Abs(s.x)) return 0f;
            return Mathf.Sign(s.y) * Mathf.InverseLerp(0.6f, 0.95f, Mathf.Abs(s.y));
        }
    }
    public bool FrontLinkageHeld => scripted ? scriptedFront : frontLinkageAction.IsPressed();
    public bool PtoPressed => !scripted && ptoAction.WasPressedThisFrame();
    public bool AttachPressed => !scripted && attachAction.WasPressedThisFrame();
    public bool FourWheelDrivePressed => !scripted && fourWdAction.WasPressedThisFrame();
    public bool DiffLockPressed => !scripted && diffLockAction.WasPressedThisFrame();
    public bool BrakeModePressed => !scripted && brakeModeAction.WasPressedThisFrame();
    public bool LightsPressed => !scripted && lightsAction.WasPressedThisFrame();
    public bool WorkLightsPressed => !scripted && workLightsAction.WasPressedThisFrame();
    public bool BeaconPressed => !scripted && beaconAction.WasPressedThisFrame();
    public bool HazardsPressed => !scripted && hazardsAction.WasPressedThisFrame();
    public bool IndicatorLeftPressed => !scripted && indicatorLeftAction.WasPressedThisFrame();
    public bool IndicatorRightPressed => !scripted && indicatorRightAction.WasPressedThisFrame();
    public bool SkyPressed => !scripted && skyAction.WasPressedThisFrame();
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

        frontLinkageAction = new InputAction("FrontLinkage", InputActionType.Button);
        frontLinkageAction.AddBinding("<XRController>{LeftHand}/{TriggerButton}");
        frontLinkageAction.AddBinding("<Gamepad>/leftTrigger");
        frontLinkageAction.AddBinding("<Keyboard>/leftCtrl");

        ptoAction = new InputAction("Pto", InputActionType.Button);
        ptoAction.AddBinding("<XRController>{RightHand}/{Primary2DAxisClick}");
        ptoAction.AddBinding("<Gamepad>/rightStickPress");
        ptoAction.AddBinding("<Keyboard>/p");

        attachAction = new InputAction("Attach", InputActionType.Button);
        attachAction.AddBinding("<XRController>{LeftHand}/{Primary2DAxisClick}");
        attachAction.AddBinding("<Gamepad>/leftStickPress");
        attachAction.AddBinding("<Keyboard>/j");

        menuAction = new InputAction("Menu", InputActionType.Button);
        menuAction.AddBinding("<XRController>{LeftHand}/{MenuButton}");
        menuAction.AddBinding("<Gamepad>/start");
        menuAction.AddBinding("<Keyboard>/m");

        handThrottleAction = new InputAction("HandThrottle", InputActionType.Value, expectedControlType: "Axis");
        handThrottleAction.AddCompositeBinding("1DAxis").With("Positive", "<Keyboard>/rightBracket").With("Negative", "<Keyboard>/leftBracket");
        handThrottleAction.AddCompositeBinding("1DAxis").With("Positive", "<Gamepad>/dpad/up").With("Negative", "<Gamepad>/dpad/down");

        tipAction = new InputAction("Tip", InputActionType.Value, expectedControlType: "Axis");
        tipAction.AddCompositeBinding("1DAxis").With("Positive", "<Keyboard>/i").With("Negative", "<Keyboard>/k");

        // Traction (keyboard only; in the headset: cab panel buttons).
        fourWdAction = new InputAction("FourWD", InputActionType.Button, "<Keyboard>/f");
        diffLockAction = new InputAction("DiffLock", InputActionType.Button, "<Keyboard>/l");
        brakeModeAction = new InputAction("BrakeMode", InputActionType.Button, "<Keyboard>/b");

        // Lights + time of day (keyboard only; in the headset: console switches, indicator stalk, cab panel).
        lightsAction = new InputAction("Lights", InputActionType.Button, "<Keyboard>/g");
        workLightsAction = new InputAction("WorkLights", InputActionType.Button, "<Keyboard>/n");
        beaconAction = new InputAction("Beacon", InputActionType.Button, "<Keyboard>/o");
        hazardsAction = new InputAction("Hazards", InputActionType.Button, "<Keyboard>/y");
        indicatorLeftAction = new InputAction("IndicatorLeft", InputActionType.Button, "<Keyboard>/comma");
        indicatorRightAction = new InputAction("IndicatorRight", InputActionType.Button, "<Keyboard>/period");
        skyAction = new InputAction("Sky", InputActionType.Button, "<Keyboard>/u");
    }

    IEnumerable<InputAction> LightActions()
    {
        yield return lightsAction; yield return workLightsAction; yield return beaconAction; yield return hazardsAction;
        yield return indicatorLeftAction; yield return indicatorRightAction; yield return skyAction;
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
        frontLinkageAction.Enable();
        ptoAction.Enable();
        attachAction.Enable();
        menuAction.Enable();
        handThrottleAction.Enable();
        tipAction.Enable();
        fourWdAction.Enable();
        diffLockAction.Enable();
        brakeModeAction.Enable();
        foreach (var a in LightActions()) a.Enable();
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
        frontLinkageAction.Disable();
        ptoAction.Disable();
        attachAction.Disable();
        menuAction.Disable();
        handThrottleAction.Disable();
        tipAction.Disable();
        fourWdAction.Disable();
        diffLockAction.Disable();
        brakeModeAction.Disable();
        foreach (var a in LightActions()) a.Disable();
    }
}
