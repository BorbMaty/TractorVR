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
/// hitch sweep, ploughing, mowing (front linkage + PTO), towing the trailer.
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
    static Implement[] implements;
    static SimpleWheels[] wheelSets;
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
            hitch = ctrl.GetComponent<Hitch>();
            implements = Object.FindObjectsByType<Implement>(FindObjectsSortMode.None);
            wheelSets = Object.FindObjectsByType<SimpleWheels>(FindObjectsSortMode.None);
            CallAwake(hitch);
            foreach (var i in implements) CallAwake(i);
            foreach (var w in wheelSets) CallAwake(w);
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

    static void Section(string title)
    {
        log.AppendLine();
        log.AppendLine(title);
        log.AppendLine("     t(s)   km/h   accel  yaw°/s  lat m/s²  pitch°  roll°  power  height");
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
            ctrl.FrameUpdate(dt);
            ctrl.PhysicsStep();
            foreach (var i in implements) i.PhysicsStep();
            foreach (var w in wheelSets) w.PhysicsStep();
            Physics.Simulate(dt);
            time += dt;
            if (t >= next)
            {
                next += logEvery;
                Vector3 e = ctrl.transform.eulerAngles;
                float yawRate = rb.angularVelocity.y * Mathf.Rad2Deg;
                float lat = Vector3.Dot(Vector3.Cross(rb.angularVelocity, rb.linearVelocity), ctrl.transform.right);
                log.AppendLine($"   {t,6:0.0} {ctrl.SpeedKmh,6:0.0} {ctrl.Acceleration,7:0.00} {yawRate,7:0.0} {lat,9:0.00} {Mathf.DeltaAngle(0f, e.x),7:0.0} {Mathf.DeltaAngle(0f, e.z),6:0.0} {ctrl.PowerFraction,6:0.00} {ctrl.transform.position.y,7:0.00}{(extra != null ? "  " + extra() : "")}");
            }
        }
    }

    static Implement Find(string name) => implements.First(i => i.name == name);

    // O. What the ground map says across the world, then 15 km/h on each surface: rolling resistance shows in the power.
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
                ctrl.FrameUpdate(dt); ctrl.PhysicsStep();
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
    static void PlaceAt(Transform mount, Vector3 target)
    {
        Vector3 local = ctrl.transform.InverseTransformPoint(mount.position);
        Place(new Vector3(target.x - local.x, 0f, target.z - local.z), 0f);
    }

    static void RunImplementScenarios()
    {
        Section("I. Rear hitch: lower fully (5 s), raise fully (5 s)");
        Place(new Vector3(30f, 0f, 0f), 0f);
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
        extra = () => $"rear {hitch.RearPosition:0.00}  depth {plough.Depth:0.00}  draft {plough.DraftForce / 1000f:0.0} kN  {(plough.Working ? "WORK" : "")}  {ctrl.GroundLabel}  mass {rb.mass:0} CoM z {rb.centerOfMass.z:0.00}";
        // On the yard concrete the plough can't go in: lowering stops at the surface, no draft.
        input.scriptedHitch = -1f; Simulate(3f, 0f, 0f, 1.5f); input.scriptedHitch = 1f; Simulate(4f, 0f, 0f, 10f); input.scriptedHitch = 0f;
        log.AppendLine("   → carried to the north field (soil):");
        Place(new Vector3(-60f, 0f, 250f), 0f); Simulate(1f, 0f, 0f, 10f);
        input.scriptedHitch = -1f; Simulate(6f, 0f, 0f, 2f);
        Simulate(20f, 0.21f, 0f, 2f);
        input.scriptedHitch = 1f; Simulate(5f, 0.21f, 0f, 1f);
        input.scriptedHitch = 0f;
        log.AppendLine($"   front axle loads {ctrl.wheels[0].load / 1000f:0.0} / {ctrl.wheels[1].load / 1000f:0.0} kN with the plough raised");
        Simulate(1f, 0f, 0f, 10f);
        input.scriptedHitch = -1f; Simulate(6f, 0f, 0f, 10f); input.scriptedHitch = 0f;
        hitch.ToggleAttach();   // detach
        Simulate(2f, 0f, 0f, 10f);
        log.AppendLine($"   plough parked at {plough.transform.position:F2}, upright {Vector3.Angle(plough.transform.up, Vector3.up):0}°, mass back to {rb.mass:0}");

        var mower = Find("Mower");
        Section("K. Mower: lower the front linkage, line up, attach, PTO on, 10 km/h for 15 s");
        input.scriptedFront = true; input.scriptedHitch = -1f;
        Place(new Vector3(30f, 0f, 40f), 0f);
        Simulate(6f, 0f, 0f, 10f);
        input.scriptedHitch = 0f;
        PlaceAt(hitch.frontMount, mower.hitchPoint.position);
        Simulate(0.5f, 0f, 0f, 10f);
        hitch.ToggleAttach();
        Simulate(1.5f, 0f, 0f, 10f);
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
                      $"coupling gap {Vector3.Distance(trailer.hitchPoint.position, hitch.drawbarMount.position) * 100f:0.0} cm  trailer {Vector3.Dot(trb.linearVelocity, trailer.transform.forward) * 3.6f:0.0} km/h";
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
        extra = null;
        CabControlsTest();
    }

    // Fake hands on the steering wheel and the speed lever (VRHands.Hand fields are written directly).
    static void CabControlsTest()
    {
        log.AppendLine();
        log.AppendLine("N. Cab controls with simulated hands");
        var cab = ctrl.GetComponent<CabControls>();
        var vrHands = Object.FindAnyObjectByType<VRHands>();
        cab.GetType().GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(cab, null);
        var update = cab.GetType().GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
        Place(new Vector3(14f, 0f, -20f), 0f);   // open yard (the barn is at x -44..-30)
        input.scriptedSteer = input.scriptedThrottle = 0f;
        float dt = Time.fixedDeltaTime;
        var column = ctrl.steeringWheel.parent;
        var h = vrHands.Left;
        void Step(int frames = 1)
        {
            for (int i = 0; i < frames; i++) { update.Invoke(cab, null); ctrl.FrameUpdate(dt); h.GrabStarted = h.GrabEnded = false; }
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

        // Speed lever: grab the knob and push it 15° forward.
        Vector3 knob = cab.speedLever.TransformPoint(cab.knobLocal);
        h.GrabPoint = knob; h.Grabbing = h.GrabStarted = true; Step();
        Vector3 pivot = cab.speedLever.position;
        h.GrabPoint = pivot + Quaternion.AngleAxis(15f, ctrl.transform.right) * (knob - pivot); Step();
        log.AppendLine($"   lever pushed 15° forward → lever throttle {input.leverThrottle:0.00} (expected +{15f / cab.leverTilt:0.00}), knob moved {(cab.speedLever.TransformPoint(cab.knobLocal) - knob).z * 100f:0.0} cm forward");
        h.Grabbing = false; h.GrabEnded = true; Step();
        log.AppendLine($"   released → lever stays at {input.leverThrottle:0.00}");

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
                update.Invoke(cab, null);
                ctrl.FrameUpdate(dt);
                if (h.GrabEnded) drops++;
                // Shake = frame-to-frame changes of the turning speed of the steering wheel (2nd difference), in wheel degrees.
                float w = -ctrl.SteerAngle * ctrl.steeringRatio;
                float d = w - last;
                if (i > 2) { shake += (d - lastD) * (d - lastD); shakeN++; }
                lastD = d; last = w;
            }
            VRHands.Apply(h, true, h.GrabPoint, false); update.Invoke(cab, null);
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

    static float SlopeUnder()
    {
        Vector3 p = ctrl.transform.position;
        return Vector3.Angle(TerrainGenerator.Normal(p.x, p.z, 2f), Vector3.up);
    }
}
