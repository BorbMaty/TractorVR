using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Plays the real scene in the editor (no headset) for ~25 s with scripted input and reports any runtime error.
/// Everything runs as in the game: Awake / Start / Update / FixedUpdate of every component, the terrain build, the HUD, the cab panel.
/// Script: settle → drive off → steer → stop button → every cab panel button (except reset) → CommandCenter hand throttle
/// → hitch down/up, trailer body tipped (pours its load) → done.
///   Menu: Tractor VR > Run Play Mode Test
///   CLI : Unity.exe -batchmode -projectPath . -executeMethod PlayModeTest.Run   (no -quit: it exits by itself, code 1 on errors)
/// Entering play mode reloads the scripts, so the run state lives in SessionState.
/// XR start-up messages (no headset / runtime) are expected and not counted.
/// </summary>
[InitializeOnLoad]
public static class PlayModeTest
{
    const string Key = "TractorVR.PlayModeTest";
    const string ReportPath = "Logs/PlayModeTest.txt";
    static readonly List<string> errors = new List<string>();
    static readonly StringBuilder log = new StringBuilder();
    static double start;
    static int frames;
    static bool done;
    static readonly HashSet<string> stepsDone = new HashSet<string>();

    static PlayModeTest()
    {
        if (!SessionState.GetBool(Key, false)) return;
        Application.logMessageReceived += OnLog;
        EditorApplication.playModeStateChanged += OnState;
        if (EditorApplication.isPlaying) Begin();
    }

    [MenuItem("Tractor VR/Run Play Mode Test")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/TractorFlat.unity");
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }

    static void OnState(PlayModeStateChange s)
    {
        if (s == PlayModeStateChange.EnteredPlayMode) Begin();
    }

    static void Begin()
    {
        start = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    static void OnLog(string msg, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        // No headset or OpenXR runtime in the editor test: XR start-up errors are expected.
        if (msg.Contains("OpenXR") || msg.Contains("XR") || msg.Contains("xr") || msg.Contains("HMD")) return;
        errors.Add($"{type}: {msg}\n{stack}");
    }

    static void Once(string id, double at, double t, System.Action action)
    {
        if (t < at || stepsDone.Contains(id)) return;
        stepsDone.Add(id);
        try { action(); }
        catch (System.Exception e) { errors.Add($"step {id}: {e}"); }
    }

    static void Tick()
    {
        if (done || !EditorApplication.isPlaying) return;
        frames++;
        double t = EditorApplication.timeSinceStartup - start;
        var tractor = Object.FindAnyObjectByType<TractorController>();
        var input = tractor ? tractor.GetComponent<TractorInput>() : null;
        if (tractor == null || input == null) { if (t > 10) Finish("no tractor in the scene"); return; }
        input.scripted = true;

        Once("drive", 2, t, () => { input.scriptedThrottle = 0.5f; log.AppendLine($"{t:0.0}s drive off"); });
        Once("speed", 8, t, () => log.AppendLine($"{t:0.0}s speed {tractor.SpeedKmh:0.0} km/h on {tractor.GroundLabel}, gear {tractor.GetComponent<Drivetrain>().GearName}, {tractor.GetComponent<Drivetrain>().Rpm:0} rpm"));
        Once("steer", 9, t, () => { input.scriptedSteer = 0.6f; log.AppendLine($"{t:0.0}s steer"); });
        Once("stop", 12, t, () =>
        {
            input.scriptedSteer = 0f; input.scriptedThrottle = 0f;
            tractor.GetComponent<CabControls>().Neutral(); tractor.RequestStop();
            log.AppendLine($"{t:0.0}s STOP at {tractor.SpeedKmh:0.0} km/h");
        });
        Once("stopped", 17, t, () => log.AppendLine($"{t:0.0}s speed {tractor.SpeedKmh:0.0} km/h"));
        Once("panel", 18, t, () =>
        {
            // Press every cab panel button (twice, so toggles end where they started), except reset.
            var panel = Object.FindAnyObjectByType<CabPanel>();
            var list = (System.Collections.IList)typeof(CabPanel).GetField("buttons", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(panel);
            int n = 0;
            foreach (var b in list)
            {
                var bt = b.GetType();
                var press = (System.Action)bt.GetField("press").GetValue(b);
                var label = ((System.Func<string>)bt.GetField("text").GetValue(b))();
                if (label.StartsWith("RESET")) continue;
                press(); press(); n++;
            }
            log.AppendLine($"{t:0.0}s pressed {n} panel buttons twice");
            var cc = Object.FindAnyObjectByType<CommandCenter>();
            cc.Press(1f, 1f);
            var drive = tractor.GetComponent<Drivetrain>();
            log.AppendLine($"{t:0.0}s CommandCenter THROTTLE + for 1 s → hand throttle {drive.HandThrottleRpm:0} rpm");
            cc.Press(-1f, 5f);
        });
        Once("hitchDown", 19, t, () => input.scriptedHitch = -1f);
        Once("hitchUp", 22, t, () => input.scriptedHitch = 1f);
        Once("tip", 20, t, () => Object.FindAnyObjectByType<Tipper>().Move(1f, 7f));   // raise the parked trailer's body straight to the top
        Once("end", 25, t, () =>
        {
            input.scriptedHitch = 0f;
            var hitch = tractor.GetComponent<Hitch>();
            log.AppendLine($"{t:0.0}s hitch at {hitch.RearPosition:0.00}, {frames} frames ({frames / t:0} fps in the editor)");
            var drive = tractor.GetComponent<Drivetrain>();
            var tipper = Object.FindAnyObjectByType<Tipper>();
            log.AppendLine($"{t:0.0}s trailer body {tipper.Angle:0}°, load {tipper.LoadKg:0} kg left, {Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Count(m => m.name == "SoilHeap")} heap(s)");
            log.AppendLine($"{t:0.0}s fuel {drive.FuelLitres:0.0} L ({drive.FuelRateLph:0.0} L/h), coolant {drive.CoolantC:0.0} °C, doors open {tractor.GetComponent<CabControls>().DoorsOpen}");
            Finish(null);
        });
    }

    static void Finish(string problem)
    {
        done = true;
        EditorApplication.update -= Tick;
        if (problem != null) errors.Add(problem);
        var report = new StringBuilder();
        report.AppendLine(errors.Count == 0 ? "PLAY MODE TEST: OK" : $"PLAY MODE TEST: {errors.Count} ERROR(S)");
        report.Append(log);
        foreach (var e in errors.Take(20)) report.AppendLine(e);
        File.WriteAllText(ReportPath, report.ToString());
        Debug.Log("[PlayModeTest]\n" + report);
        SessionState.SetBool(Key, false);
        Application.logMessageReceived -= OnLog;
        EditorApplication.ExitPlaymode();
        if (Application.isBatchMode) EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
    }
}
