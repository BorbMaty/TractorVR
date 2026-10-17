using UnityEngine;

/// <summary>
/// Engine + gearbox on top of the kinematic driving model, loosely after the 6195M's AutoQuad Plus 20/20:
/// 5 ranges (A–E) × 4 powershift steps = 20 forward / 20 reverse gears, 50 km/h top speed at rated engine speed.
///
/// The speed itself still comes from <see cref="TractorController"/> (comfort-limited speed command + physics). The drivetrain
/// decides which gear is in, what the engine is doing (RPM, load) and, in manual mode, how fast the selected gear can go.
///   Auto ..... shifts up and down by engine speed, like AutoQuad's automatic mode.
///   Manual ... right stick flick right/left = shift up/down; the gear caps the speed (stick = share of the gear's top speed).
/// </summary>
[RequireComponent(typeof(TractorController), typeof(TractorInput))]
public class Drivetrain : MonoBehaviour
{
    public enum Dir { Neutral, Forward, Reverse }

    [Header("Engine (John Deere 6195M: 6.8 L PowerTech PVX, 195 hp at 2100 rpm)")]
    public float idleRpm = 850f;
    public float ratedRpm = 2100f;
    public float maxRpm = 2250f;            // high idle; manual downshifts that would exceed it are refused
    public float rpmResponse = 0.15f;       // s, engine speed smoothing

    [Header("Gearbox")]
    [Tooltip("Road speed (km/h) of each gear at rated engine speed. Generated geometrically when empty.")]
    public float[] gearKmh = new float[0];
    public float firstGearKmh = 2.4f;
    public float topGearKmh = 50f;
    public int gearCount = 20;
    public int startGear = 5;               // B2 — the gear AUTO pulls away in
    public float upshiftRpm = 1950f;
    public float downshiftRpm = 1300f;
    public float minShiftInterval = 0.5f;   // s

    public bool Automatic { get; private set; } = true;
    public int Gear { get; private set; }
    public float Rpm { get; private set; }
    public float Load { get; private set; }        // 0..1, for sound
    public Dir Direction { get; private set; }
    public string GearName => $"{(char)('A' + Gear / 4)}{Gear % 4 + 1}";

    /// <summary>Raised on every gear change (sound).</summary>
    public event System.Action Shifted;
    /// <summary>Raised when the mode changes or a manual shift is refused (HUD message).</summary>
    public event System.Action<string> Message;

    TractorController tractor;
    TractorInput input;
    float rpmVel, lastShift = -10f;

    void Awake()
    {
        tractor = GetComponent<TractorController>();
        input = GetComponent<TractorInput>();
        if (gearKmh == null || gearKmh.Length == 0)
        {
            gearKmh = new float[gearCount];
            float step = Mathf.Pow(topGearKmh / firstGearKmh, 1f / (gearCount - 1));
            for (int i = 0; i < gearCount; i++) gearKmh[i] = firstGearKmh * Mathf.Pow(step, i);
        }
        Gear = Mathf.Clamp(startGear, 0, gearKmh.Length - 1);
        Rpm = idleRpm;
        Automatic = PlayerPrefs.GetInt("TransmissionAuto", 1) == 1;
    }

    /// <summary>Speed cap the controller may drive at (km/h). Auto: the full range; manual: the selected gear.</summary>
    public float SpeedLimitKmh(bool forward, float maxForward, float maxReverse)
    {
        float cap = forward ? maxForward : maxReverse;
        return Automatic ? cap : Mathf.Min(cap, gearKmh[Gear]);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        float kmh = Mathf.Abs(tractor.SpeedKmh);
        float throttle = input.Throttle;

        if (input.TransmissionModePressed)
        {
            Automatic = !Automatic;
            PlayerPrefs.SetInt("TransmissionAuto", Automatic ? 1 : 0);
            Message?.Invoke(Automatic ? "AUTO" : "MANUAL");
        }
        int manualShift = input.ShiftUpPressed ? 1 : input.ShiftDownPressed ? -1 : 0;
        if (manualShift != 0)
        {
            if (Automatic) { Automatic = false; PlayerPrefs.SetInt("TransmissionAuto", 0); Message?.Invoke("MANUAL"); }
            int g = Mathf.Clamp(Gear + manualShift, 0, gearKmh.Length - 1);
            if (g != Gear && GearedRpm(kmh, g) > maxRpm) Message?.Invoke("TOO FAST TO SHIFT DOWN");
            else SetGear(g);
        }

        // Direction (shuttle): follows the motion, or the stick when standing still.
        if (tractor.SpeedMs > 0.05f) Direction = Dir.Forward;
        else if (tractor.SpeedMs < -0.05f) Direction = Dir.Reverse;
        else Direction = throttle > 0.08f ? Dir.Forward : throttle < -0.08f ? Dir.Reverse : Dir.Neutral;

        if (Automatic) AutoShift(kmh);

        // Engine speed: locked to the wheels when the clutch is in, idling (+ a bit of slip) otherwise.
        bool sameDir = Mathf.Abs(tractor.SpeedMs) < 0.05f || Mathf.Sign(throttle) == Mathf.Sign(tractor.SpeedMs);
        float demand = Mathf.Abs(throttle) > 0.08f && sameDir ? Mathf.Abs(throttle) : 0f;
        float geared = GearedRpm(kmh, Gear);
        float target = Direction == Dir.Neutral ? idleRpm : Mathf.Max(geared, idleRpm + demand * 250f);
        target = Mathf.Min(target, maxRpm);
        Rpm = Mathf.SmoothDamp(Rpm, target, ref rpmVel, rpmResponse);

        // Load = share of engine power the wheels are using (physics), with a floor while the throttle is open.
        float loadTarget = demand > 0f ? Mathf.Max(0.15f, tractor.PowerFraction) : 0.05f;
        Load = Mathf.MoveTowards(Load, Mathf.Clamp01(loadTarget), 2f * dt);
    }

    void AutoShift(float kmh)
    {
        if (Time.time - lastShift < minShiftInterval) return;
        if (kmh < 0.5f) { if (Gear != startGear) SetGear(startGear, silent: true); return; }

        float rpm = GearedRpm(kmh, Gear);
        if (rpm > upshiftRpm && Gear < gearKmh.Length - 1) SetGear(Gear + 1);
        else if (rpm < downshiftRpm && Gear > startGear) SetGear(Gear - 1);
    }

    void SetGear(int g, bool silent = false)
    {
        if (g == Gear) return;
        Gear = g;
        lastShift = Time.time;
        if (!silent) Shifted?.Invoke();
    }

    float GearedRpm(float kmh, int gear) => kmh / gearKmh[gear] * ratedRpm;
}
