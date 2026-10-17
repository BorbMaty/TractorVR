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
/// Scenarios: settle, full throttle, coast + hold, steady turn, steepest hill (climb + park), pushing a hay bale.
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
            typeof(TractorController).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(ctrl, null);
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
            ctrl.FrameUpdate(dt);
            ctrl.PhysicsStep();
            Physics.Simulate(dt);
            time += dt;
            if (t >= next)
            {
                next += logEvery;
                Vector3 e = ctrl.transform.eulerAngles;
                float yawRate = rb.angularVelocity.y * Mathf.Rad2Deg;
                float lat = Vector3.Dot(Vector3.Cross(rb.angularVelocity, rb.linearVelocity), ctrl.transform.right);
                log.AppendLine($"   {t,6:0.0} {ctrl.SpeedKmh,6:0.0} {ctrl.Acceleration,7:0.00} {yawRate,7:0.0} {lat,9:0.00} {Mathf.DeltaAngle(0f, e.x),7:0.0} {Mathf.DeltaAngle(0f, e.z),6:0.0} {ctrl.PowerFraction,6:0.00} {ctrl.transform.position.y,7:0.00}");
            }
        }
    }

    static float SlopeUnder()
    {
        Vector3 p = ctrl.transform.position;
        return Vector3.Angle(TerrainGenerator.Normal(p.x, p.z, 2f), Vector3.up);
    }
}
