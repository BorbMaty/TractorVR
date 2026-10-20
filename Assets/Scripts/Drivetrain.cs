using UnityEngine;

/// <summary>
/// Engine + gearbox on top of the kinematic driving model, loosely after the 6195M's AutoQuad Plus 20/20:
/// 5 ranges (A–E) × 4 powershift steps = 20 forward / 20 reverse gears, 50 km/h top speed at rated engine speed.
///
/// The speed itself still comes from <see cref="TractorController"/> (comfort-limited speed command + physics). The drivetrain
/// decides which gear is in, what the engine is doing (RPM, load) and, in manual mode, how fast the selected gear can go.
///   Auto ..... shifts up and down by engine speed, like AutoQuad's automatic mode.
///   Manual ... right stick flick right/left = shift up/down; the gear caps the speed (stick = share of the gear's top speed).
/// Engine: a torque curve (6.8 L PowerTech, ~33 % torque rise) gives the power available at the current engine speed, which
/// limits the drive force. The hand throttle sets a minimum engine speed: AUTO keeps the engine there (like PTO work), and in
/// manual the tractor drives at that engine speed in the selected gear as soon as the lever / stick asks for motion.
/// Fuel use follows the engine power (about 0.24 L per kWh + idle losses) and the coolant warms up to its thermostat temperature.
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
    [Tooltip("Engine speed for 1000 rpm at the PTO. With the PTO on, AUTO keeps the engine near it.")]
    public float ptoEngineRpm = 1900f;
    [Tooltip("Torque curve (Nm) at the engine speeds in curveRpm; zero at high idle (governor).")]
    public float[] curveRpm = { 800f, 1000f, 1200f, 1400f, 1500f, 1600f, 1800f, 1900f, 2100f, 2250f };
    public float[] curveNm = { 520f, 640f, 760f, 850f, 880f, 880f, 800f, 745f, 659f, 0f };
    public float peakTorqueRpm = 1550f;     // the engine is brought up to this when the drive force is power-limited (pulling away under load)
    public float handThrottleRate = 400f;   // rpm per second while a hand throttle button is held

    [Header("Fuel and cooling")]
    public float tankLitres = 340f;
    public float startFuel = 0.85f;         // share of the tank at the start / after a reset
    public float litresPerKWh = 0.24f;      // about 205 g/kWh of diesel (+ the idle losses below: ~41 L/h at full power)
    public float idleLitresPerHour = 2.5f;  // at idle speed, grows with engine speed
    public float ambientC = 20f, startCoolantC = 35f, thermostatC = 85f;
    public float warmUpTime = 300f;         // s to warm up at light load (faster under load)

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
    /// <summary>Engine speed set by the hand throttle (idle = off).</summary>
    public float HandThrottleRpm { get; private set; }
    /// <summary>Power the engine can give at its current speed (kW, torque curve).</summary>
    public float AvailableKW => PowerAt(Rpm);
    /// <summary>Power the engine is giving now (kW): wheels + PTO.</summary>
    public float OutputKW { get; private set; }
    public float FuelLitres => (float)fuel;
    public float FuelShare => FuelLitres / tankLitres;
    public float FuelRateLph { get; private set; }
    public float CoolantC { get; private set; }
    /// <summary>Engine hours this session.</summary>
    public float Hours => (float)(seconds / 3600.0);
    /// <summary>Manual mode with the hand throttle set: the least share of the gear's speed the lever / stick gives (else 0).</summary>
    public float HandThrottleShare => !Automatic && HandThrottleRpm > idleRpm + 1f ? HandThrottleRpm / ratedRpm : 0f;

    /// <summary>Raised on every gear change (sound).</summary>
    public event System.Action Shifted;
    /// <summary>Raised when the mode changes or a manual shift is refused (HUD message).</summary>
    public event System.Action<string> Message;

    TractorController tractor;
    TractorInput input;
    Hitch hitch;
    float rpmVel, lastShift = -10f, clock, pullUntil;   // clock: own time base, so the editor test can step it
    double fuel, seconds;                     // double: a 90 Hz step of idle fuel use is below float precision at 300 L

    void Awake()
    {
        tractor = GetComponent<TractorController>();
        input = GetComponent<TractorInput>();
        hitch = GetComponent<Hitch>();
        if (gearKmh == null || gearKmh.Length == 0)
        {
            gearKmh = new float[gearCount];
            float step = Mathf.Pow(topGearKmh / firstGearKmh, 1f / (gearCount - 1));
            for (int i = 0; i < gearCount; i++) gearKmh[i] = firstGearKmh * Mathf.Pow(step, i);
        }
        Gear = Mathf.Clamp(startGear, 0, gearKmh.Length - 1);
        Rpm = idleRpm;
        HandThrottleRpm = idleRpm;
        Refill();
        Automatic = PlayerPrefs.GetInt("TransmissionAuto", 1) == 1;
    }

    public void ToggleMode()
    {
        Automatic = !Automatic;
        PlayerPrefs.SetInt("TransmissionAuto", Automatic ? 1 : 0);
        Message?.Invoke(Automatic ? "AUTO" : "MANUAL");
    }

    /// <summary>Tank back to the start level, engine half warm (start, reset).</summary>
    public void Refill()
    {
        fuel = tankLitres * startFuel;
        CoolantC = startCoolantC;
    }

    /// <summary>Hand throttle up (+1) / down (-1) while held (cab panel, CommandCenter, keyboard).</summary>
    public void MoveHandThrottle(float direction, float dt)
    {
        float before = HandThrottleRpm;
        HandThrottleRpm = Mathf.Clamp(HandThrottleRpm + direction * handThrottleRate * dt, idleRpm, ratedRpm);
        if (HandThrottleRpm != before) Message?.Invoke(HandThrottleRpm <= idleRpm ? "HAND THROTTLE OFF" : $"HAND THROTTLE {HandThrottleRpm:0} rpm");
    }

    /// <summary>Engine torque (Nm) at an engine speed.</summary>
    public float TorqueAt(float rpm)
    {
        if (rpm <= curveRpm[0]) return curveNm[0];
        for (int i = 1; i < curveRpm.Length; i++)
            if (rpm <= curveRpm[i]) return Mathf.Lerp(curveNm[i - 1], curveNm[i], (rpm - curveRpm[i - 1]) / (curveRpm[i] - curveRpm[i - 1]));
        return 0f;
    }

    public float PowerAt(float rpm) => TorqueAt(rpm) * rpm * (2f * Mathf.PI / 60f) / 1000f;

    /// <summary>Speed cap the controller may drive at (km/h). Auto: the full range; manual: the selected gear.</summary>
    public float SpeedLimitKmh(bool forward, float maxForward, float maxReverse)
    {
        float cap = forward ? maxForward : maxReverse;
        return Automatic ? cap : Mathf.Min(cap, gearKmh[Gear]);
    }

    void Update() => FrameUpdate(Time.deltaTime);

    /// <summary>Per-frame update (public for the editor physics test).</summary>
    public void FrameUpdate(float dt)
    {
        clock += dt;
        float kmh = Mathf.Abs(tractor.SpeedKmh);
        float throttle = input.Throttle;

        if (input.TransmissionModePressed) ToggleMode();
        if (input.HandThrottle != 0f) MoveHandThrottle(input.HandThrottle, dt);
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
        // PTO work or hand throttle: the engine is held at that speed (the clutch slips below it).
        target = Mathf.Max(target, HeldRpm);
        // Pulling against a heavy load (e.g. a plough already in the ground) at low engine speed: rev up to peak torque
        // and let the clutch slip, as a driver would. Held for a second, or the engine would hunt between the two.
        if (tractor.PowerLimited) pullUntil = clock + 1f;
        if (clock < pullUntil && Direction != Dir.Neutral) target = Mathf.Max(target, peakTorqueRpm);
        target = Mathf.Min(target, maxRpm);
        Rpm = Mathf.SmoothDamp(Rpm, target, ref rpmVel, rpmResponse, Mathf.Infinity, dt);

        // Load = share of engine power the wheels are using (physics), with a floor while the throttle is open.
        float loadTarget = demand > 0f ? Mathf.Max(0.15f, tractor.PowerFraction) : 0.05f;
        bool pto = hitch && hitch.Pto;
        if (pto) loadTarget = Mathf.Max(loadTarget, 0.1f) + hitch.PtoPowerKW / tractor.enginePowerKW;
        Load = Mathf.MoveTowards(Load, Mathf.Clamp01(loadTarget), 2f * dt);

        // Fuel and coolant.
        OutputKW = tractor.PowerFraction * tractor.enginePowerKW + (pto ? hitch.PtoPowerKW : 0f);
        FuelRateLph = fuel > 0.0 ? idleLitresPerHour * Rpm / idleRpm + litresPerKWh * OutputKW : 0f;
        fuel = System.Math.Max(0.0, fuel - FuelRateLph * (double)dt / 3600.0);
        float hot = thermostatC + 8f * Load;                       // the thermostat holds it, a bit higher under load
        float warmRate = (thermostatC - ambientC) / warmUpTime * (0.5f + 2f * Load);
        CoolantC = Mathf.MoveTowards(CoolantC, hot, warmRate * dt);
        seconds += dt;
    }

    // Engine speed the engine is held at: PTO speed while the PTO runs, or the hand throttle (whichever is higher; 0 = none).
    float HeldRpm => Mathf.Max(hitch && hitch.Pto ? ptoEngineRpm : 0f, HandThrottleRpm > idleRpm + 1f ? HandThrottleRpm : 0f);

    void AutoShift(float kmh)
    {
        if (clock - lastShift < minShiftInterval) return;
        if (kmh < 0.5f) { if (Gear != startGear) SetGear(startGear, silent: true); return; }

        float rpm = GearedRpm(kmh, Gear);
        // With the PTO on or the hand throttle set, the engine must stay near that speed, so shift to keep it there.
        float held = HeldRpm;
        float up = held > 0f ? Mathf.Min(held + 150f, maxRpm - 100f) : upshiftRpm, down = held > 0f ? held - 200f : downshiftRpm;
        // Pulling hard at low engine speed: shift down to where the torque curve gives more power.
        bool lugging = tractor.PowerLimited && rpm < 1600f;
        int lowest = held > 0f || lugging ? 0 : startGear;
        if (rpm > up && Gear < gearKmh.Length - 1)
        {
            // Skip-shift: straight to the first gear that brings the engine back under the upshift speed.
            int g = Gear + 1;
            while (g < gearKmh.Length - 1 && GearedRpm(kmh, g) > up) g++;
            SetGear(g);
        }
        else if ((rpm < down || lugging) && Gear > lowest && GearedRpm(kmh, Gear - 1) < maxRpm - 100f) SetGear(Gear - 1);
    }

    void SetGear(int g, bool silent = false)
    {
        if (g == Gear) return;
        Gear = g;
        lastShift = clock;
        if (!silent) Shifted?.Invoke();
    }

    float GearedRpm(float kmh, int gear) => kmh / gearKmh[gear] * ratedRpm;
}
