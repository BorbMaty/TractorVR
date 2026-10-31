using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Drives the tractor without a headset: steps TractorController + Unity physics by hand in the editor (batch mode works)
/// with scripted stick input, and writes what happened to Logs/PhysicsTest.txt.
/// Scenarios: settle, full throttle, coast + hold, steady turn, steepest hill (climb + park), pushing a hay bale,
/// hitch sweep, ploughing, mowing (front linkage + PTO), towing the trailer, torque curve / hand throttle / fuel / coolant,
/// cab controls with simulated hands (wheel, speed lever, remote levers, doors), top link pitch, front weight, trailer tipping.
///   Menu: Tractor VR > Run Physics Test
///   CLI : Unity.exe -batchmode -quit -projectPath . -executeMethod PhysicsTest.Run
/// The scene is not saved.
/// </summary>
public static class PhysicsTest
{
    static TractorController ctrl;
    static TractorInput input;
    static Rigidbody rb;
    static StringBuilder log;
    static float time;
    static Hitch hitch;
    static Drivetrain drive;
    static Implement[] implements;
    static SimpleWheels[] wheelSets;
    static Tipper[] tippers;
    static System.Func<string> extra;

    static void CallAwake(Object o) =>
        o.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(o, null);

    [MenuItem("Tractor VR/Run Physics Test")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/TractorFlat.unity");
        Object.FindAnyObjectByType<TerrainGenerator>().Build();
        var previousMode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        log = new StringBuilder();
        try
        {
            ctrl = Object.FindAnyObjectByType<TractorController>();
            input = ctrl.GetComponent<TractorInput>();
            rb = ctrl.GetComponent<Rigidbody>();
            input.scripted = true;
            CallAwake(input);
            CallAwake(ctrl);
            drive = ctrl.GetComponent<Drivetrain>();
            CallAwake(drive);
            if (!drive.Automatic) drive.ToggleMode();   // the saved gearbox mode could be manual
            hitch = ctrl.GetComponent<Hitch>();
            implements = Object.FindObjectsByType<Implement>(FindObjectsSortMode.None);
            wheelSets = Object.FindObjectsByType<SimpleWheels>(FindObjectsSortMode.None);
            CallAwake(hitch);
            foreach (var i in implements) CallAwake(i);
            foreach (var w in wheelSets) CallAwake(w);
            tippers = Object.FindObjectsByType<Tipper>(FindObjectsSortMode.None);
            foreach (var tp in tippers) CallAwake(tp);
            hitch.Init();
            hitch.Message += m => log.AppendLine("   [HUD] " + m);
            float dt = Time.fixedDeltaTime;
            log.AppendLine($"Physics step {dt * 1000f:0.##} ms ({1f / dt:0} Hz), mass {rb.mass} kg, CoM {rb.centerOfMass}");

            Section("A. Settle on flat ground, 3 s");
            Place(Vector3.zero, 0f);
            Simulate(3f, 0f, 0f, 1f);
            log.AppendLine($"   rest height {ctrl.transform.position.y:0.000} m, compressions {string.Join(" ", ctrl.wheels.Select(w => w.compression.ToString("0.000")))}");

            Section("B. Full throttle straight, 18 s");
            Simulate(18f, 1f, 0f, 1f);

            Section("C. Release: coast to a stop, then hold, 20 s");
            Simulate(20f, 0f, 0f, 2f);

            Section("D. 15 km/h then full right steering, 12 s");
            Place(Vector3.zero, 0f);
            Simulate(1f, 0f, 0f, 10f);
            Simulate(17f, 0.38f, 0f, 3f);
            Simulate(12f, 0.38f, 1f, 1f);

            Section("E. Full throttle at 30 km/h then full left steering, 8 s");
            Place(Vector3.zero, 0f);
            Simulate(1f, 0f, 0f, 10f);
            Simulate(9f, 0.62f, 0f, 3f);
            Simulate(8f, 0.62f, -1f, 1f);

            // Steepest place within 700 m (sampled every 10 m).
            Vector3 best = Vector3.zero; float bestSlope = 0f;
            for (float x = -700f; x <= 700f; x += 10f)
            for (float z = -700f; z <= 700f; z += 10f)
            {
                float s = Vector3.Angle(TerrainGenerator.Normal(x, z, 2f), Vector3.up);
                if (s > bestSlope) { bestSlope = s; best = new Vector3(x, 0f, z); }
            }
            Vector3 n = TerrainGenerator.Normal(best.x, best.z, 2f);
            float uphill = Mathf.Atan2(-n.x, -n.z) * Mathf.Rad2Deg;
            Section($"F. Steepest slope {bestSlope:0.0}° at ({best.x:0}, {best.z:0}): start 40 m below, full throttle uphill, 25 s");
            Vector3 dir = Quaternion.Euler(0f, uphill, 0f) * Vector3.forward;
            Place(best - dir * 40f, uphill);
            Simulate(1f, 0f, 0f, 10f);
            Simulate(25f, 1f, 0f, 2f);

            Section("G. Park on the slope: release (coast to a stop and hold), 30 s");
            Simulate(30f, 0f, 0f, 3f);
            Vector3 parked = ctrl.transform.position;
            Simulate(5f, 0f, 0f, 5f);
            log.AppendLine($"   creep over the last 5 s: {Vector3.Distance(parked, ctrl.transform.position) * 100f:0.0} cm on {SlopeUnder():0.0}°");

            Section("H. Push a hay bale at 8 km/h, 12 s");
            var bale = GameObject.Find("HayBales").transform.GetChild(5);
            Vector3 baleStart = bale.position;
            Place(new Vector3(baleStart.x, 0f, baleStart.z - 12f), 0f);
            Simulate(1f, 0f, 0f, 10f);
            Simulate(12f, 0.2f, 0f, 2f);
            log.AppendLine($"   bale moved {Vector3.Distance(baleStart, bale.position):0.00} m, now at {bale.position:F2}");
            log.AppendLine($"   wheels (grounded/load kN): {string.Join("  ", ctrl.wheels.Select(w => $"{w.grounded}/{w.load / 1000f:0.0}"))}, sleeping {rb.IsSleeping()}, " +
                           $"speed command {typeof(TractorController).GetField("speedCmd", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ctrl)}");
            RunImplementScenarios();
            SurfaceScenario();
            SpeedBumpScenario();
            SteepSlopeScenario();

            Section("P. STOP button at 20 km/h (vs. letting go of the stick: coasting at 0.8 m/s²)");
            Place(new Vector3(32f, 0f, 0f), 90f);
            Simulate(1f, 0f, 0f, 10f);
            Simulate(7f, 0.45f, 0f, 10f);
            float v0 = ctrl.SpeedKmh;
            input.scriptedThrottle = 0f;
            ctrl.RequestStop();
            float tStop = 0f;
            while (ctrl.SpeedKmh > 0.2f && tStop < 20f) { StepQuiet(); tStop += Time.fixedDeltaTime; }
            Simulate(3f, 0f, 0f, 10f);
            EngineScenario();
            log.AppendLine($"   from {v0:0.0} km/h: stopped in {tStop:0.0} s (≈{v0 / 3.6f / ctrl.braking:0.0} s at {ctrl.braking} m/s²; coasting would take {v0 / 3.6f / ctrl.coastDecel:0.0} s), then {ctrl.SpeedKmh:0.0} km/h");
        }
        catch (System.Exception e)
        {
            log.AppendLine("EXCEPTION " + e);
        }
        finally
        {
            Physics.simulationMode = previousMode;
            File.WriteAllText("Logs/PhysicsTest.txt", log.ToString());
            Debug.Log("[PhysicsTest]\n" + log);
        }
    }

    // Q. Torque curve, hand throttle (manual: drives at the gear's speed for that engine speed; auto: holds the engine there),
    // fuel use and coolant warm-up.
    static void EngineScenario()
    {
        log.AppendLine();
        log.AppendLine("Q. Engine: torque curve");
        var curve = new StringBuilder("   ");
        for (float r = 850f; r <= 2250f; r += 100f) curve.Append($"{r:0}: {drive.TorqueAt(r):0} Nm {drive.PowerAt(r):0} kW | ");
        log.AppendLine(curve.ToString());
        float peak = 0f, peakRpm = 0f;
        for (float r = 850f; r <= 2250f; r += 10f) if (drive.PowerAt(r) > peak) { peak = drive.PowerAt(r); peakRpm = r; }
        log.AppendLine($"   rated {drive.PowerAt(drive.ratedRpm):0} kW at {drive.ratedRpm:0} rpm, peak {peak:0} kW at {peakRpm:0} rpm, torque rise {(drive.TorqueAt(1550f) / drive.TorqueAt(drive.ratedRpm) - 1f) * 100f:0}%");

        Section("Q2. Manual, gear C1, hand throttle 1500 rpm, lever just out of neutral (0.15), 15 s");
        Place(new Vector3(32f, 0f, 0f), 90f);
        Simulate(1f, 0f, 0f, 10f);
        drive.ToggleMode();   // → manual
        typeof(Drivetrain).GetMethod("SetGear", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(drive, new object[] { 8, true });
        drive.MoveHandThrottle(1f, (1500f - drive.idleRpm) / drive.handThrottleRate);
        extra = () => $"hand {drive.HandThrottleRpm:0}  fuel {drive.FuelRateLph:0.0} L/h";
        Simulate(15f, 0.15f, 0f, 3f);
        log.AppendLine($"   {drive.GearName} at {drive.HandThrottleRpm:0} rpm: {ctrl.SpeedKmh:0.0} km/h (expected {drive.gearKmh[8] * drive.HandThrottleRpm / drive.ratedRpm:0.0}), engine {drive.Rpm:0} rpm");
        input.scriptedThrottle = 0f;
        ctrl.RequestStop();
        Simulate(4f, 0f, 0f, 10f);
        drive.ToggleMode();   // → auto
        Section("Q3. Auto, hand throttle 1500 rpm, lever 0.3, 10 s: the engine stays near 1500");
        Simulate(10f, 0.3f, 0f, 2f);
        drive.MoveHandThrottle(-1f, 10f);
        log.AppendLine($"   hand throttle back to {drive.HandThrottleRpm:0} (off)");
        ctrl.RequestStop();
        Simulate(4f, 0f, 0f, 10f);
        extra = null;

        log.AppendLine("Q4. Fuel and coolant (drivetrain only, standing)");
        drive.Refill();
        float startFuel = drive.FuelLitres;
        var warm = new StringBuilder("   coolant at idle: ");
        float dt = Time.fixedDeltaTime, reached = -1f;
        for (float t = 0f; t <= 600f; t += dt)
        {
            drive.FrameUpdate(dt);
            if (reached < 0f && drive.CoolantC >= drive.thermostatC - 0.5f) reached = t;
            if (Mathf.Repeat(t, 60f) < dt) warm.Append($"{t:0}s {drive.CoolantC:0}°C  ");
        }
        log.AppendLine(warm.ToString());
        log.AppendLine($"   thermostat temperature reached after {reached:0} s; idle use {drive.FuelRateLph:0.0} L/h ({(startFuel - drive.FuelLitres) * 1000f:0} mL in 10 min); " +
                       $"at full power {drive.idleLitresPerHour * drive.ratedRpm / drive.idleRpm + drive.litresPerKWh * ctrl.enginePowerKW:0} L/h (6195M: ~40 L/h)");
    }

    static void Section(string title)
    {
        log.AppendLine();
        log.AppendLine(title);
        log.AppendLine("     t(s)   km/h   accel  yaw°/s  lat m/s²  pitch°  roll°  power  height  gear   rpm  avail kW");
    }

    static void Place(Vector3 xz, float yaw)
    {
        Vector3 p = new Vector3(xz.x, TerrainGenerator.Height(xz.x, xz.z) + 0.1f, xz.z);
        var rot = Quaternion.FromToRotation(Vector3.up, TerrainGenerator.Normal(xz.x, xz.z, 2f)) * Quaternion.Euler(0f, yaw, 0f);
        ctrl.transform.SetPositionAndRotation(p, rot);
        rb.position = p;
        rb.rotation = rot;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        input.scriptedThrottle = input.scriptedSteer = 0f;
        ctrl.ResetSuspension();
        // Clear the speed command (private) along with the motion.
        typeof(TractorController).GetField("speedCmd", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ctrl, 0f);
    }

    static void Simulate(float seconds, float throttle, float steer, float logEvery)
    {
        float dt = Time.fixedDeltaTime, next = 0f;
        input.scriptedThrottle = throttle;
        input.scriptedSteer = steer;
        for (float t = 0f; t < seconds; t += dt)
        {
            hitch.FrameUpdate(dt);
            foreach (var i in implements) i.FrameUpdate(dt);
            drive.FrameUpdate(dt);
            ctrl.FrameUpdate(dt);
            ctrl.PhysicsStep();
            foreach (var i in implements) i.PhysicsStep();
            foreach (var w in wheelSets) w.PhysicsStep();
            foreach (var tp in tippers) tp.FrameUpdate(dt);
            Physics.Simulate(dt);
            time += dt;
            if (logEvery > 0f && t >= next)
            {
                next += logEvery;
                Vector3 e = ctrl.transform.eulerAngles;
                float yawRate = rb.angularVelocity.y * Mathf.Rad2Deg;
                float lat = Vector3.Dot(Vector3.Cross(rb.angularVelocity, rb.linearVelocity), ctrl.transform.right);
                log.AppendLine($"   {t,6:0.0} {ctrl.SpeedKmh,6:0.0} {ctrl.Acceleration,7:0.00} {yawRate,7:0.0} {lat,9:0.00} {Mathf.DeltaAngle(0f, e.x),7:0.0} {Mathf.DeltaAngle(0f, e.z),6:0.0} {ctrl.PowerFraction,6:0.00} {ctrl.transform.position.y,7:0.00}  {drive.GearName,4} {drive.Rpm,5:0} {drive.AvailableKW,6:0}{(extra != null ? "  " + extra() : "")}");
            }
        }
    }

    static Implement Find(string name) => implements.First(i => i.name == name);

    // O. What the ground map says across the world, then 15 km/h on each surface: rolling resistance shows in the power.
    // Over the two speed bumps on the west track at 10 km/h, then 20 km/h: suspension travel and the front axle.
    static void SpeedBumpScenario()
    {
        Vector2 a = new Vector2(-25f, -30f), d = (new Vector2(-60f, -60f) - a).normalized, start = a - d * 12f;   // in the yard, so 20 km/h is reached before the first bump
        float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
        foreach (float kmh in new[] { 10f, 20f })
        {
            Section($"W. Speed bumps (10 cm) on the west track, throttle for ~{kmh:0} km/h");
            Place(new Vector3(start.x, 0f, start.y), yaw);
            Simulate(1f, 0f, 0f, 10f);
            float maxRise = 0f, maxRoll = 0f, maxPitch = 0f, maxComp = 0f, minComp = float.MaxValue, maxKmh = 0f;
            extra = () =>
            {
                maxRise = Mathf.Max(maxRise, Mathf.Abs(ctrl.FrontAxleRise)); maxRoll = Mathf.Max(maxRoll, Mathf.Abs(ctrl.FrontAxleRoll));
                maxPitch = Mathf.Max(maxPitch, Mathf.Abs(Mathf.DeltaAngle(0f, ctrl.transform.eulerAngles.x))); maxKmh = Mathf.Max(maxKmh, ctrl.SpeedKmh);
                foreach (var w in ctrl.wheels) { maxComp = Mathf.Max(maxComp, w.compression); minComp = Mathf.Min(minComp, w.compression); }
                Vector2 p = new Vector2(ctrl.transform.position.x, ctrl.transform.position.z);
                return $"along {Vector2.Dot(p - a, d):0.0} m  front axle rise {ctrl.FrontAxleRise * 100f:0.0} cm  swing {ctrl.FrontAxleRoll:0.0}°  " +
                       $"springs {string.Join(" ", ctrl.wheels.Select(w => (w.compression * 100f).ToString("0")))} cm";
            };
            float throttle = kmh / 45f;
            Simulate(4f + 42f / (kmh / 3.6f), throttle, 0f, 0.1f);
            extra = null;
            log.AppendLine($"   at up to {maxKmh:0.0} km/h: max front axle rise {maxRise * 100f:0.0} cm, swing {maxRoll:0.0}°, body pitch {maxPitch:0.0}°, " +
                           $"spring compression {minComp * 100f:0} … {maxComp * 100f:0} cm (static {ctrl.travel * 100f:0})");
            input.scriptedThrottle = 0f;
            Simulate(3f, -0.3f, 0f, 0f);
        }
    }

    // The steep practice slopes: through the silage clamp (1:3), over the earth ramp (up 15°, down 22°) and back up its 22° side.
    static void SteepSlopeScenario()
    {
        var c = WorldLayout.SilageClamp; var r = WorldLayout.EarthRamp;
        var runs = new[]
        {
            ("X. Silage clamp, 1:3 (18°) ramps: through it northwards at ~8 km/h", new Vector3(c.x, 0f, c.y - 28f), 0f, 26f),
            ("X2. Earth ramp: up the 15° side, down the 22° side, ~8 km/h", new Vector3(r.x - 20f, 0f, r.y), 90f, 26f),
            ("X3. Earth ramp: up the 22° side from standstill, ~8 km/h", new Vector3(r.x + 18f, 0f, r.y), -90f, 22f),
        };
        foreach (var (title, at, yaw, seconds) in runs)
        {
            Section(title);
            Place(at, yaw);
            Simulate(1f, 0f, 0f, 10f);
            float maxPitch = 0f, minPitch = 0f, maxSlip = 0f, minKmhOnSlope = float.MaxValue, maxPower = 0f;
            extra = () =>
            {
                float pitch = -Mathf.DeltaAngle(0f, ctrl.transform.eulerAngles.x);   // + = nose up
                maxPitch = Mathf.Max(maxPitch, pitch); minPitch = Mathf.Min(minPitch, pitch);
                maxSlip = Mathf.Max(maxSlip, ctrl.Slip); maxPower = Mathf.Max(maxPower, ctrl.PowerFraction);
                if (pitch > 10f) minKmhOnSlope = Mathf.Min(minKmhOnSlope, ctrl.SpeedKmh);
                return $"slip {ctrl.Slip * 100f:0}%  {ctrl.GroundLabel}";
            };
            Simulate(seconds, 8f / 45f * 1.3f, 0f, 1f);
            extra = null;
            log.AppendLine($"   pitch {minPitch:0.0}° … +{maxPitch:0.0}°, slowest {(minKmhOnSlope < 1000f ? minKmhOnSlope.ToString("0.0") : "-")} km/h above 10° " +
                           $"nose up, max slip {maxSlip * 100f:0}%, max power {maxPower * 100f:0}%, height now {ctrl.transform.position.y:0.00} m");
            input.scriptedThrottle = 0f;
            Simulate(3f, -0.3f, 0f, 0f);
        }
    }

    static void SurfaceScenario()
    {
        var counts = new int[System.Enum.GetValues(typeof(Ground)).Length];
        for (int j = 0; j < GroundMap.Size; j += 4)
        for (int i = 0; i < GroundMap.Size; i += 4)
            counts[(int)GroundMap.At(i - GroundMap.Half, j - GroundMap.Half)]++;
        int total = counts.Sum();
        log.AppendLine();
        log.AppendLine("O. Surfaces: share of the world " + string.Join(", ", System.Enum.GetValues(typeof(Ground)).Cast<Ground>()
            .Select(g => $"{g} {100f * counts[(int)g] / total:0.0}%")));
        // Short runs (~12 m at 6.5 km/h) that stay on one surface. Drive force = power / speed; minus the slope share,
        // what's left is the rolling resistance (weight 73.6 kN × the surface's coefficient).
        // A gently uphill stretch of the road (downhill the engine brakes and there's no drive force to measure),
        // and a patch of mud big enough for the whole run (8 × 24 m heading north).
        float roadZ = 0f;
        for (float z = -800f; z < 800f; z += 10f)
        {
            float rise = TerrainGenerator.Height(80f, z + 15f) - TerrainGenerator.Height(80f, z);
            if (rise > 0.02f && rise < 0.4f && z > 200f) { roadZ = z; break; }
        }
        Vector3 mud = Vector3.zero;
        for (float z = -900f; z < 900f && mud == Vector3.zero; z += 8f)
        for (float x = -900f; x < 900f; x += 8f)
        {
            bool all = true;
            for (float dz = -4f; dz <= 24f && all; dz += 2f)
            for (float dx = -4f; dx <= 4f && all; dx += 2f)
                all = GroundMap.At(x + dx, z + dz) == Ground.Mud;
            if (all) { mud = new Vector3(x, 0f, z); break; }
        }
        var spots = new (string name, Vector3 at, float yaw)[]
        {
            ("asphalt road (uphill)", new Vector3(80f, 0f, roadZ), 0f), ("gravel track", new Vector3(32f, 0f, 0f), 90f),
            ("yard concrete", new Vector3(14f, 0f, -20f), 0f), ("pasture", new Vector3(-150f, 0f, 500f), 0f),
            ("north field soil", new Vector3(60f, 0f, 300f), 0f), ($"mud at ({mud.x:0}, {mud.z:0})", mud, 0f),
        };
        foreach (var (name, at, yaw) in spots)
        {
            Place(at, yaw);
            Simulate(1f, 0f, 0f, 10f);
            Simulate(3f, 0.2f, 0f, 10f);   // up to ~6.5 km/h
            float force = 0f, slope = 0f; int n = 0; string label = "";
            float dt = Time.fixedDeltaTime;
            for (int k = 0; k < 90; k++)
            {
                hitch.FrameUpdate(dt);
                foreach (var i in implements) i.FrameUpdate(dt);
                drive.FrameUpdate(dt); ctrl.FrameUpdate(dt); ctrl.PhysicsStep();
                foreach (var i in implements) i.PhysicsStep();
                foreach (var ws in wheelSets) ws.PhysicsStep();
                Physics.Simulate(dt);
                force += ctrl.PowerFraction * ctrl.enginePowerKW / Mathf.Max(0.5f, Mathf.Abs(ctrl.SpeedMs));
                slope += Vector3.Dot(-Physics.gravity, ctrl.transform.forward) * rb.mass / 1000f;   // uphill share of the weight, kN
                n++; label = ctrl.GroundLabel;
            }
            var w = ctrl.wheels[2];
            log.AppendLine($"   {name,-22} rear wheels on {label,-8} grip {w.surface.grip:0.00} rolling {w.surface.rolling:0.000}  {ctrl.SpeedKmh:0.0} km/h  " +
                           $"drive {force / n:0.0} kN = slope {slope / n:0.0} + rolling {force / n - slope / n:0.0} kN (expected {w.surface.rolling * 73.6f:0.0})");
        }
    }

    // Put the tractor (heading +Z) so that the given mount sits right over a coupling point.
    static void PlaceAt(Transform mount, Vector3 target, float yaw = 0f)
    {
        Vector3 local = Quaternion.Euler(0f, yaw, 0f) * ctrl.transform.InverseTransformPoint(mount.position);
        Place(new Vector3(target.x - local.x, 0f, target.z - local.z), yaw);
    }

    static float FrontAxleKN => (ctrl.wheels[0].load + ctrl.wheels[1].load) / 1000f;
    static float RearAxleKN => (ctrl.wheels[2].load + ctrl.wheels[3].load) / 1000f;

    static void RunImplementScenarios()
    {
        Section("I. Rear hitch: lower fully (5 s), raise fully (5 s)");
        Place(new Vector3(14f, 0f, -20f), 0f);   // open yard (the garden fence runs along z 4.2 east of the gate)
        extra = () => $"rear {hitch.RearPosition:0.00}  link ends y {hitch.rearMount.localPosition.y:0.00} z {hitch.rearMount.localPosition.z:0.00}  " +
                      $"lift arm {hitch.liftArms.localEulerAngles.x:0}° lower links {hitch.lowerLinks.localEulerAngles.x:0}°";
        input.scriptedHitch = -1f; Simulate(5f, 0f, 0f, 1f);
        input.scriptedHitch = 1f; Simulate(5f, 0f, 0f, 1f);
        input.scriptedHitch = 0f;

        var plough = Find("Plough");
        Section("J. Plough: line up, attach, lower into work, 7 km/h for 20 s, raise, 5 s");
        input.scriptedHitch = -1f; Simulate(2.5f, 0f, 0f, 10f); input.scriptedHitch = 0f;   // links down to the pins' height
        PlaceAt(hitch.rearMount, plough.hitchPoint.position);
        Simulate(0.5f, 0f, 0f, 10f);
        hitch.ToggleAttach();
        Simulate(1.5f, 0f, 0f, 10f);
        extra = () => $"rear {hitch.RearPosition:0.00}  pitch {hitch.RearPitch:0.0}°  depth {plough.Depth:0.00}  draft {plough.DraftForce / 1000f:0.0} kN  {(plough.Working ? "WORK" : "")}  {ctrl.GroundLabel}  mass {rb.mass:0} CoM z {rb.centerOfMass.z:0.00}";
        log.AppendLine($"   top link set to {hitch.TopLinkLength * 100f:0} cm, plough pitch {hitch.RearPitch:0.0}° at the coupling height");
        // On the yard concrete the plough can't go in: lowering stops at the surface, no draft.
        input.scriptedHitch = -1f; Simulate(3f, 0f, 0f, 1.5f); input.scriptedHitch = 1f; Simulate(4f, 0f, 0f, 10f); input.scriptedHitch = 0f;
        log.AppendLine("   → carried to the north field (soil):");
        Place(new Vector3(-60f, 0f, 250f), 0f); Simulate(1f, 0f, 0f, 10f);
        input.scriptedHitch = -1f; Simulate(6f, 0f, 0f, 2f);
        Simulate(20f, 0.21f, 0f, 2f);
        TractionScenario(plough);
        input.scriptedHitch = 1f; Simulate(5f, 0.21f, 0f, 1f);
        input.scriptedHitch = 0f;
        log.AppendLine($"   front axle loads {ctrl.wheels[0].load / 1000f:0.0} / {ctrl.wheels[1].load / 1000f:0.0} kN with the plough raised");
        Simulate(1f, 0f, 0f, 10f);
        input.scriptedHitch = -1f; Simulate(6f, 0f, 0f, 10f); input.scriptedHitch = 0f;
        hitch.ToggleAttach();   // detach
        Simulate(2f, 0f, 0f, 10f);
        log.AppendLine($"   plough parked at {plough.transform.position:F2}, upright {Vector3.Angle(plough.transform.up, Vector3.up):0}°, mass back to {rb.mass:0}");

        var mower = Find("Mower");
        Section("K. Mower: lower the front linkage, line up in its shed bay (nose in), attach, carry out, PTO on, 10 km/h for 15 s");
        input.scriptedFront = true; input.scriptedHitch = -1f;
        Place(new Vector3(30f, 0f, 40f), 0f);
        Simulate(6f, 0f, 0f, 10f);
        input.scriptedHitch = 0f;
        PlaceAt(hitch.frontMount, mower.hitchPoint.position, WorldLayout.ShedBayOf("Mower").yaw);
        Simulate(0.5f, 0f, 0f, 10f);
        hitch.ToggleAttach();
        Simulate(1.5f, 0f, 0f, 10f);
        log.AppendLine($"   coupled in the shed bay: {(mower.Attached ? "attached" : "NOT ATTACHED")}");
        Place(new Vector3(30f, 0f, 40f), 0f);   // carried out to the open (the shed faces the yard, the test drives north)
        Simulate(1f, 0f, 0f, 10f);
        hitch.SetPto(true);
        extra = () => $"front {hitch.FrontPosition:0.00}  depth {mower.Depth:0.00}  {(mower.Working ? "WORK" : "")}  PTO {hitch.PtoPowerKW:0} kW";
        input.scriptedHitch = -1f; Simulate(3f, 0f, 0f, 1f); input.scriptedHitch = 0f;
        Simulate(15f, 0.25f, 0f, 3f);
        hitch.SetPto(false);
        input.scriptedHitch = 1f; Simulate(4f, 0f, 0f, 2f);
        input.scriptedHitch = -1f; Simulate(5f, 0f, 0f, 10f); input.scriptedHitch = 0f;
        input.scriptedFront = false;
        Simulate(3f, 0f, 0f, 10f);
        hitch.ToggleAttach();
        Simulate(2f, 0f, 0f, 10f);

        var weight = Find("FrontWeight");
        Section("R. Front weight 900 kg: axle loads without / with it, front linkage raised");
        Place(new Vector3(6f, 0f, -20f), 0f);   // west of the trailer bay's lane (x 13.5): test L drives north there
        Simulate(2f, 0f, 0f, 10f);
        float front0 = FrontAxleKN, rear0 = RearAxleKN;
        input.scriptedFront = true; input.scriptedHitch = -1f; Simulate(6f, 0f, 0f, 10f); input.scriptedHitch = 0f;
        PlaceAt(hitch.frontMount, weight.hitchPoint.position, WorldLayout.ShedBayOf("FrontWeight").yaw);   // its shed bay, nose in
        Simulate(0.5f, 0f, 0f, 10f);
        hitch.ToggleAttach();
        Simulate(1.5f, 0f, 0f, 10f);
        Place(new Vector3(6f, 0f, -20f), 0f);    // back to where the axle loads without it were taken (the weight stays here)
        Simulate(1f, 0f, 0f, 10f);
        input.scriptedHitch = 1f; Simulate(5f, 0f, 0f, 10f); input.scriptedHitch = 0f;
        input.scriptedFront = false;
        Simulate(3f, 0f, 0f, 10f);
        float zw = ctrl.transform.InverseTransformPoint(hitch.frontMount.position + ctrl.transform.rotation * weight.centreOfMassOffset).z;
        float gain = weight.mass * 9.81f * zw / ctrl.wheelbase / 1000f;
        log.AppendLine($"   front axle {front0:0.0} → {FrontAxleKN:0.0} kN (expected +{gain:0.0}), rear axle {rear0:0.0} → {RearAxleKN:0.0} kN " +
                       $"(expected {weight.mass * 9.81f / 1000f - gain:+0.0;-0.0}), weight at {zw:0.00} m ahead of the rear axle, mass {rb.mass:0}");
        Simulate(6f, 0.3f, 0f, 2f);
        input.scriptedFront = true; input.scriptedHitch = -1f; Simulate(6f, 0f, 0f, 10f); input.scriptedHitch = 0f; input.scriptedFront = false;
        hitch.ToggleAttach();
        Simulate(2f, 0f, 0f, 10f);
        log.AppendLine($"   detached: mass back to {rb.mass:0}");

        var trailer = Find("Trailer");
        var trb = trailer.GetComponent<Rigidbody>();
        Section("L. Trailer: settle, line up, attach, 30 km/h, full right lock, brake to a stop");
        Simulate(2f, 0f, 0f, 10f);
        log.AppendLine($"   parked trailer at {trailer.transform.position:F2}, eye height {trailer.hitchPoint.position.y:0.00} (drawbar {hitch.drawbarMount.position.y:0.00})");
        PlaceAt(hitch.drawbarMount, trailer.hitchPoint.position);
        Simulate(0.5f, 0f, 0f, 10f);
        hitch.ToggleAttach();
        Simulate(2f, 0f, 0f, 10f);
        extra = () => $"trailer angle {Vector3.SignedAngle(ctrl.transform.forward, trailer.transform.forward, Vector3.up):0.0}°  " +
                      $"coupling gap {Vector3.Distance(trailer.hitchPoint.position, hitch.drawbarMount.position) * 100f:0.0} cm  trailer {Vector3.Dot(trb.linearVelocity, trailer.transform.forward) * 3.6f:0.0} km/h  " +
                      $"at ({ctrl.transform.position.x:0}, {ctrl.transform.position.z:0})";
        Simulate(16f, 0.62f, 0f, 2f);
        Simulate(8f, 0.62f, 1f, 1f);
        Simulate(4f, 0.62f, 0f, 1f);
        Simulate(14f, -0.3f, 0f, 2f);

        Section("M. Trailer vs tractor body: jackknife in reverse, then reverse into the uncoupled trailer");
        Simulate(3f, 0f, 0f, 10f);
        float worst = 0f;
        extra = () =>
        {
            float p = Penetration(trailer);
            worst = Mathf.Max(worst, p);
            return $"trailer angle {Vector3.SignedAngle(ctrl.transform.forward, trailer.transform.forward, Vector3.up):0.0}°  overlap {p * 100f:0.0} cm";
        };
        Simulate(20f, -0.3f, 1f, 2f);
        log.AppendLine($"   jackknife: deepest overlap {worst * 100f:0.0} cm");
        Simulate(4f, 0f, 0f, 10f);
        hitch.ToggleAttach();             // refused at this angle
        Simulate(14f, 0.15f, 0f, 3f);     // pull forward to straighten
        Simulate(4f, 0f, 0f, 10f);
        hitch.ToggleAttach();   // detach
        Simulate(2f, 0f, 0f, 10f);
        Vector3 parked = trailer.transform.position;
        Simulate(4f, 0.12f, 0f, 10f);     // pull 2–3 m away
        Simulate(3f, 0f, 0f, 10f);
        worst = 0f;
        Simulate(12f, -0.25f, 0f, 2f);    // and back into it
        log.AppendLine($"   reversing into it: deepest overlap {worst * 100f:0.0} cm, trailer pushed {Vector3.Distance(parked, trailer.transform.position):0.00} m");

        var tipper = trailer.GetComponent<Tipper>();
        Section("S. Tipping: couple the loaded trailer, raise the body fully (remote 3 / TIP UP), hold, lower");
        Simulate(2f, 0f, 0f, 10f);
        PlaceAt(hitch.drawbarMount, trailer.hitchPoint.position, trailer.transform.eulerAngles.y);
        Simulate(0.5f, 0f, 0f, 10f);
        hitch.ToggleAttach();
        Simulate(2f, 0f, 0f, 10f);
        extra = () => $"tip {tipper.Angle:0.0}°  load {tipper.LoadKg:0} kg  trailer mass {trb.mass:0}  CoM {trb.centerOfMass.y:0.00}/{trb.centerOfMass.z:0.00}  towed {ctrl.towedMass:0}";
        input.scriptedTip = 1f; Simulate(9f, 0f, 0f, 1f);
        input.scriptedTip = 0f; Simulate(2f, 0f, 0f, 1f);
        input.scriptedTip = -1f; Simulate(5f, 0f, 0f, 1f);
        input.scriptedTip = 0f;
        var heaps = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Where(m => m.name == "SoilHeap").ToArray();
        log.AppendLine($"   {heaps.Length} heap(s) on the ground: " + string.Join(", ", heaps.Select(h =>
            $"radius {h.transform.localScale.x:0.00} m at {Vector3.Distance(Flat(h.transform.position), Flat(trailer.transform.position)):0.0} m behind the eye")));
        Simulate(8f, 0.3f, 0f, 2f);   // drive off with the empty trailer

        Section("T. RESET TRACTOR while driving with the trailer coupled: the trailer comes along and can be detached");
        Vector3 before = trailer.transform.position;
        ctrl.ResetTractor();
        extra = () => $"trailer angle {Vector3.SignedAngle(ctrl.transform.forward, trailer.transform.forward, Vector3.up):0.0}°  " +
                      $"coupling gap {Vector3.Distance(trailer.hitchPoint.position, hitch.drawbarMount.position) * 100f:0.0} cm  trailer {trb.linearVelocity.magnitude * 3.6f:0.0} km/h";
        Simulate(4f, 0f, 0f, 1f);
        log.AppendLine($"   trailer moved {Vector3.Distance(before, trailer.transform.position):0.0} m with the tractor, upright {Vector3.Angle(trailer.transform.up, Vector3.up):0}°, " +
                       $"{(hitch.Towed == trailer ? "coupled" : "NOT COUPLED")}");
        Simulate(6f, 0.2f, 0f, 2f);   // still pulls it
        input.scriptedThrottle = 0f; ctrl.RequestStop();
        Simulate(4f, 0f, 0f, 10f);
        hitch.ToggleAttach();         // uncouple before the next scenarios teleport the tractor
        Simulate(2f, 0f, 0f, 10f);
        log.AppendLine($"   trailer {(hitch.Towed ? "STILL COUPLED" : "uncoupled")}, mass {trb.mass:0} kg");
        extra = null;
        BrakeScenario();
        CabControlsTest();
    }

    static string Wheels() => string.Join(" ", ctrl.wheels.Select(w =>
        $"{(w.steers ? "F" : "R")}{(w.leftSide ? "L" : "R")} {w.surface.label.PadRight(4).Substring(0, 4)} {w.wheelSpeed * 3.6f:0.0}{(w.spinning ? "*" : w.locked ? "#" : "")}"));

    // U. With the plough in: 4WD, 2WD, 2WD + diff lock (slip and speed), then the left side in mud (open diff: that wheel spins).
    static void TractionScenario(Implement plough)
    {
        Section("U. Traction with the plough in (km/h per tyre, * = spinning): 4WD, 2WD, 2WD + diff lock; then left side in mud");
        extra = () => $"slip {ctrl.Slip * 100f:0}%  short {ctrl.Shortfall * 100f:0}%  draft {plough.DraftForce / 1000f:0.0} kN  {Wheels()}";
        Simulate(6f, 0.21f, 0f, 2f);
        log.AppendLine($"   4WD: slip {ctrl.Slip * 100f:0.0}%, {ctrl.SpeedKmh:0.0} km/h");
        ctrl.SetFourWheelDrive(false); Simulate(8f, 0.21f, 0f, 2f);
        log.AppendLine($"   2WD: slip {ctrl.Slip * 100f:0.0}%, {ctrl.SpeedKmh:0.0} km/h");
        ctrl.SetDiffLock(true); Simulate(8f, 0.21f, 0f, 2f);
        log.AppendLine($"   2WD + diff lock: slip {ctrl.Slip * 100f:0.0}%, {ctrl.SpeedKmh:0.0} km/h, diff lock {ctrl.DiffLock}");
        ctrl.SetDiffLock(false);

        // Mud gate at (0, 116) r 8: tractor at x 8 heading north = left wheels in the mud, right wheels on grass / crop.
        foreach (var (fwd, lockDiff) in new[] { (false, false), (false, true), (true, false), (true, true) })
        {
            Place(new Vector3(8f, 0f, 112.5f), 0f);
            ctrl.SetFourWheelDrive(fwd);
            Simulate(0.5f, 0f, 0f, 10f);
            if (lockDiff) ctrl.SetDiffLock(true);
            log.AppendLine($"   split grip, {(fwd ? "4WD" : "2WD")}{(lockDiff ? " + diff lock" : ", open diffs")}:");
            Simulate(8f, 0.21f, 0f, 2f);
            ctrl.SetDiffLock(false);
        }
        ctrl.SetFourWheelDrive(true);
        Place(new Vector3(-60f, 0f, 250f), 0f); Simulate(1f, 0f, 0f, 10f);
        extra = null;
    }

    // V. Braking into the mud at the north field gate: all-wheel brakes vs rear brakes only (# = wheel locked).
    static void BrakeScenario()
    {
        Section("V. 20 km/h on the north track, STOP as the rear wheels reach the mud: all-wheel vs rear-only brakes");
        extra = () => $"{ctrl.GroundLabel}  {Wheels()}";
        foreach (bool all in new[] { true, false })
        {
            ctrl.SetAllWheelBrakes(all);
            ctrl.SetFourWheelDrive(all);   // 4WD engaged also brakes the front axle
            Place(new Vector3(0f, 0f, 70f), 0f);
            Simulate(1f, 0f, 0f, 10f);
            input.scriptedThrottle = 0.42f;
            for (float t = 0f; t < 20f && ctrl.GroundLabel != "MUD"; t += 0.1f) Simulate(0.1f, 0.42f, 0f, 0f);
            float v0 = ctrl.SpeedKmh;
            Vector3 p0 = ctrl.transform.position;
            ctrl.RequestStop();
            log.AppendLine($"   {(all ? "all wheels" : "rear only")}: STOP at {v0:0.0} km/h");
            int locked = 0;
            for (float t = 0f; t < 6f; t += 0.1f) { Simulate(0.1f, 0f, 0f, t % 0.5f < 0.05f ? 1f : 0f); if (ctrl.wheels.Any(w => w.locked)) locked++; }
            log.AppendLine($"   stopped in {Vector3.Distance(p0, ctrl.transform.position):0.0} m, a wheel locked for {locked * 0.1f:0.0} s");
        }
        ctrl.SetAllWheelBrakes(true);
        ctrl.SetFourWheelDrive(true);
        extra = null;
    }

    // Fake hands on the steering wheel and the speed lever (VRHands.Hand fields are written directly).
    static void CabControlsTest()
    {
        log.AppendLine();
        log.AppendLine("N. Cab controls with simulated hands");
        var cab = ctrl.GetComponent<CabControls>();
        var vrHands = Object.FindAnyObjectByType<VRHands>();
        cab.GetType().GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(cab, null);
        Place(new Vector3(14f, 0f, -20f), 0f);   // open yard (the barn is at x -44..-30)
        input.scriptedSteer = input.scriptedThrottle = 0f;
        float dt = Time.fixedDeltaTime;
        var column = ctrl.steeringWheel.parent;
        var h = vrHands.Left;
        void Step(int frames = 1)
        {
            for (int i = 0; i < frames; i++) { cab.FrameUpdate(dt); ctrl.FrameUpdate(dt); h.GrabStarted = h.GrabEnded = false; }
        }
        Vector3 Rim(float deg) => column.TransformPoint(new Vector3(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad), 0f) * cab.rimRadius);

        // Grab at 12 o'clock and turn 60° clockwise as the driver sees it (to 2 o'clock): a right turn of 60/13 = 4.6°.
        h.Tracked = true; h.GrabPoint = Rim(90f); h.Grabbing = h.GrabStarted = true;
        Step();
        for (int i = 1; i <= 30; i++) { h.GrabPoint = Rim(90f - 2f * i); Step(); }
        Step(60);
        log.AppendLine($"   wheel turned 60° clockwise → steer {ctrl.SteerAngle:0.00}° (expected +{60f / ctrl.steeringRatio:0.00}), wheel held {cab.WheelHeld}");
        h.Grabbing = false; h.GrabEnded = true; Step();
        Step(90);
        log.AppendLine($"   released, 1 s later → steer {ctrl.SteerAngle:0.00}° (stays), wheel held {cab.WheelHeld}");
        input.scriptedSteer = -0.6f; Step(90); input.scriptedSteer = 0f; Step(180);
        log.AppendLine($"   stick used → steer {ctrl.SteerAngle:0.00}° (back under stick control)");

        // Speed lever: the hand swings around the lever's pivot (side view); the lever follows, speed is 0 in the middle.
        Vector3 knob = cab.speedLever.TransformPoint(cab.knobLocal);
        Vector3 pivot = cab.speedLever.position;
        Vector3 Swing(float deg) => pivot + Quaternion.AngleAxis(deg, ctrl.transform.right) * (knob - pivot);
        float Expected(float deg) { float l = Mathf.Clamp(deg / cab.leverTilt, -1f, 1f); return Mathf.Abs(l) <= cab.neutralNotch ? 0f : Mathf.Sign(l) * (Mathf.Abs(l) - cab.neutralNotch) / (1f - cab.neutralNotch); }
        h.GrabPoint = knob; h.Grabbing = h.GrabStarted = true; Step();
        foreach (float deg in new[] { 5f, 15f, 25f, 0f, -15f })
        {
            h.GrabPoint = Swing(deg); Step();
            log.AppendLine($"   hand swung {deg,4:0}° → lever {cab.Lever:0.00}, speed command {input.leverThrottle:0.00} (expected {Expected(deg):0.00}), " +
                           $"knob {Vector3.Distance(cab.speedLever.TransformPoint(cab.knobLocal), h.GrabPoint) * 100f:0.0} cm from the hand, status \"{cab.LeverStatus}\"");
        }
        h.Grabbing = false; h.GrabEnded = true; Step();
        log.AppendLine($"   released at -15° → speed command stays {input.leverThrottle:0.00}");
        // Grab again (no jump), swing back into the neutral zone and let go: snaps upright.
        h.GrabPoint = Swing(-15f); h.Grabbing = h.GrabStarted = true; Step();
        log.AppendLine($"   grabbed again → lever {cab.Lever:0.00} (no jump)");
        h.GrabPoint = Swing(-3f); Step();
        h.Grabbing = false; h.GrabEnded = true; Step();
        log.AppendLine($"   let go at -3° (neutral zone) → lever {cab.Lever:0.00}, speed command {input.leverThrottle:0.00}");

        // Remote lever 1 (rear hitch): push forward = lower, pull back = raise, let go = springs back to the middle.
        {
            var r1 = cab.remotes[0];
            Vector3 rk = r1.hinge.TransformPoint(r1.grabBox.center), rp = r1.hinge.position;
            Vector3 RSwing(float deg) => rp + Quaternion.AngleAxis(deg, ctrl.transform.right) * (rk - rp);
            void Run(int frames) { for (int i = 0; i < frames; i++) { Step(); hitch.FrameUpdate(dt); } }
            Run(1);
            float start = hitch.RearPosition;
            h.GrabPoint = rk; h.Grabbing = h.GrabStarted = true; Step();
            h.GrabPoint = RSwing(18f); Run(180);
            log.AppendLine($"   remote 1 pushed 18° for 2 s → {cab.Status.Replace("\n", " / ")}; hitch {start:0.00} → {hitch.RearPosition:0.00} (lowers)");
            float low = hitch.RearPosition;
            h.GrabPoint = RSwing(-10f); Run(180);
            log.AppendLine($"   pulled back 10° for 2 s → hitch {low:0.00} → {hitch.RearPosition:0.00} (raises, slower)");
            h.Grabbing = false; h.GrabEnded = true; Step();
            Run(30);
            log.AppendLine($"   let go → lever {r1.angle:0.0}° (springs back), hitch stays at {hitch.RearPosition:0.00}");
            var r2 = cab.remotes[1];
            float front = hitch.FrontPosition;
            h.GrabPoint = r2.hinge.TransformPoint(r2.grabBox.center); h.Grabbing = h.GrabStarted = true; Step();
            h.GrabPoint = r2.hinge.position + Quaternion.AngleAxis(-20f, ctrl.transform.right) * (h.GrabPoint - r2.hinge.position); Run(90);
            h.Grabbing = false; h.GrabEnded = true; Step(); Run(30);
            log.AppendLine($"   remote 2 pulled back 20° for 1 s → front linkage {front:0.00} → {hitch.FrontPosition:0.00} (raises)");
        }
        // Doors: grab the left door and swing it 40° open, let go (stays), then the panel button closes / opens both.
        {
            var door = cab.doors[0];
            Vector3 hp = door.hinge.position, ax = door.hinge.TransformDirection(door.axis);
            Vector3 dp = door.hinge.TransformPoint(door.grabBox.center);
            h.GrabPoint = dp; h.Grabbing = h.GrabStarted = true; Step();
            for (int i = 1; i <= 40; i++) { h.GrabPoint = hp + Quaternion.AngleAxis(i, ax) * (dp - hp); Step(); }
            log.AppendLine($"   left door swung 40° by hand → {door.angle:0}° ({cab.Status})");
            h.Grabbing = false; h.GrabEnded = true; Step(); Step(90);
            log.AppendLine($"   let go → stays at {door.angle:0}°, doors open {cab.DoorsOpen}");
            cab.ToggleDoors(); Step(120);
            log.AppendLine($"   DOORS button → left {cab.doors[0].angle:0}°, right {cab.doors[1].angle:0}° (closed)");
            cab.ToggleDoors(); Step(120);
            log.AppendLine($"   DOORS button → left {cab.doors[0].angle:0}°, right {cab.doors[1].angle:0}° (open, limit {door.max:0}°)");
            cab.ToggleDoors(); Step(120);
        }

        // Hand-tracking replay on the lever: forward to +20°, back to -20°, 4 s, 4 mm noise, through VRHands.HandSignal.
        {
            var rnd = new System.Random(9);
            float N() => ((float)rnd.NextDouble() + (float)rnd.NextDouble() + (float)rnd.NextDouble() - 1.5f) * 0.0047f;
            var sig = new VRHands.HandSignal();
            int frames = (int)(4f / dt), wrong = 0;
            var marks = new System.Text.StringBuilder();
            for (int i = 0; i < frames; i++)
            {
                float t = i * dt;
                float deg = t < 1f ? 20f * t : t < 3f ? 20f - 20f * (t - 1f) : -20f;
                sig.Step(true, Swing(deg) + new Vector3(N(), N(), N()), true, dt);
                VRHands.Apply(h, true, sig.Point, sig.Grabbing);
                cab.FrameUpdate(dt);
                ctrl.FrameUpdate(dt);
                if (Mathf.Abs(Expected(deg)) > 0.15f && Mathf.Sign(input.leverThrottle) != Mathf.Sign(Expected(deg))) wrong++;
                if (i % (int)(0.5f / dt) == 0) marks.Append($"{deg:0}°→{input.leverThrottle:0.00}  ");
            }
            VRHands.Apply(h, true, h.GrabPoint, false); cab.FrameUpdate(dt);
            log.AppendLine($"   noisy hand replay: {marks}| frames with the wrong direction: {wrong}");
        }

        // Hand-tracking replay: turn the wheel 60° clockwise over 2 s, hold 1 s, with tracking noise (~4 mm), the fist reading
        // "open" on 12 % of the frames, and two 5-frame tracking dropouts. Raw (the first version) vs through VRHands.HandSignal.
        log.AppendLine("   hand tracking replay (noise 4 mm, 12 % grab flicker, 2 dropouts), 60° turn:");
        foreach (bool filtered in new[] { false, true })
        {
            var rnd = new System.Random(5);
            float Noise() => ((float)rnd.NextDouble() + (float)rnd.NextDouble() + (float)rnd.NextDouble() - 1.5f) * 0.0047f;
            input.scriptedSteer = -1f; Step(30); input.scriptedSteer = 0f; Step(200);   // straight ahead, stick control
            var sig = new VRHands.HandSignal();
            VRHands.Apply(h, false, Rim(90f), false); Step();
            int drops = 0; float last = 0f, lastD = 0f, shake = 0f; int shakeN = 0;
            int frames = (int)(3f / dt);
            for (int i = 0; i < frames; i++)
            {
                float t = i * dt;
                float deg = 90f - 60f * Mathf.Clamp01(t / 2f);
                Vector3 raw = Rim(deg) + new Vector3(Noise(), Noise(), Noise());
                bool tracked = !((i >= 70 && i < 75) || (i >= 140 && i < 145));
                bool grab = rnd.NextDouble() > 0.12;
                if (filtered)
                {
                    sig.Step(tracked, raw, grab, dt);
                    if (tracked) VRHands.Apply(h, true, sig.Point, sig.Grabbing);
                    else VRHands.Apply(h, sig.Active, sig.Point, sig.Active);
                }
                else VRHands.Apply(h, tracked, raw, tracked && grab);
                cab.FrameUpdate(dt);
                ctrl.FrameUpdate(dt);
                if (h.GrabEnded) drops++;
                // Shake = frame-to-frame changes of the turning speed of the steering wheel (2nd difference), in wheel degrees.
                float w = -ctrl.SteerAngle * ctrl.steeringRatio;
                float d = w - last;
                if (i > 2) { shake += (d - lastD) * (d - lastD); shakeN++; }
                lastD = d; last = w;
            }
            VRHands.Apply(h, true, h.GrabPoint, false); cab.FrameUpdate(dt);
            log.AppendLine($"     {(filtered ? "filtered" : "raw     ")}: grab drops {drops}, wheel shake {Mathf.Sqrt(shake / Mathf.Max(1, shakeN)):0.00}°/frame², " +
                           $"steer {ctrl.SteerAngle:0.00}° (expected {60f / ctrl.steeringRatio:0.00})");
        }
    }

    // Deepest intersection between the implement's colliders and the tractor's (contacts sit within a few mm).
    static float Penetration(Implement imp)
    {
        float worst = 0f;
        foreach (var a in imp.GetComponentsInChildren<Collider>())
        foreach (var b in ctrl.GetComponentsInChildren<Collider>())
            if (a.enabled && Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, b, b.transform.position, b.transform.rotation, out _, out float d))
                worst = Mathf.Max(worst, d);
        return worst;
    }

    // One physics step of everything, without logging.
    static void StepQuiet()
    {
        float dt = Time.fixedDeltaTime;
        hitch.FrameUpdate(dt);
        foreach (var i in implements) i.FrameUpdate(dt);
        drive.FrameUpdate(dt);
        ctrl.FrameUpdate(dt);
        ctrl.PhysicsStep();
        foreach (var i in implements) i.PhysicsStep();
        foreach (var w in wheelSets) w.PhysicsStep();
        foreach (var tp in tippers) tp.FrameUpdate(dt);
        Physics.Simulate(dt);
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    static float SlopeUnder()
    {
        Vector3 p = ctrl.transform.position;
        return Vector3.Angle(TerrainGenerator.Normal(p.x, p.z, 2f), Vector3.up);
    }
}
