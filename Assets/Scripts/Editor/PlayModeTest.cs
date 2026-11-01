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
/// Script: settle → drive off → steer → stop button → every cab panel button (except reset) → console switches, lights at night
/// → CommandCenter hand throttle
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

        Once("drive", 2, t, () =>
        {
            input.scriptedThrottle = 0.5f;
            var tier = Object.FindAnyObjectByType<QualityTier>();
            log.AppendLine($"{t:0.0}s quality {QualityTier.Current}: shadow stand-in {(tier.shadowProxy.enabled ? "on" : "off")}, " +
                           $"{tier.modelRenderers.Count(r => r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)} model renderers cast; " +
                           $"desktop mode {(DesktopMode.Active ? "on" : "off")}, {Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Count(c => c.gameObject.layer == DesktopMode.PickLayer)} pick proxies");
            var env = Object.FindObjectsByType<TierVariant>(FindObjectsInactive.Include);
            log.AppendLine($"{t:0.0}s environment variants active: {string.Join(", ", env.Where(v => v.gameObject.activeInHierarchy).Select(v => v.name))} " +
                           $"({env.Count(v => v.gameObject.activeInHierarchy)}/{env.Length}), normal maps {(Shader.IsKeywordEnabled("ENV_NORMALS") ? "on" : "off")}");
            if (env.Any(v => v.gameObject.activeInHierarchy != (v.tier == QualityTier.Current)))
                Debug.LogError("[PlayModeTest] environment variant of the wrong tier is active");
            log.AppendLine($"{t:0.0}s drive off");
        });
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
                if (label.StartsWith("RESET") || label.StartsWith("BENCHMARK")) continue;   // both teleport the tractor
                press(); press(); n++;
            }
            log.AppendLine($"{t:0.0}s pressed {n} panel buttons twice");
            // Console switches (the knob three times round), then lights for the rest of the run: SKY and LIGHTS have three
            // settings, so after the panel's two presses it is night with headlights on.
            var switches = tractor.GetComponent<CabSwitches>();
            foreach (var s in switches.switches)
                for (int k = 0; k < (s.kind == CabSwitches.Kind.Knob ? 3 : 2); k++) switches.Press(s.id);
            var lights = tractor.GetComponent<TractorLights>();
            lights.SetBeacon(true);
            lights.SetWorkFront(true);
            lights.SetIndicator(-1);
            log.AppendLine($"{t:0.0}s pressed {switches.switches.Length} console switches; sky {lights.TimeOfDay}, lights {lights.RoadLights}, beacon, work front, indicator left");
            var cc = Object.FindAnyObjectByType<CommandCenter>();
            cc.Press(1f, 1f);
            var drive = tractor.GetComponent<Drivetrain>();
            log.AppendLine($"{t:0.0}s CommandCenter THROTTLE + for 1 s → hand throttle {drive.HandThrottleRpm:0} rpm");
            cc.Press(-1f, 5f);
        });
        Once("hitchDown", 19, t, () => input.scriptedHitch = -1f);
        Once("hitchUp", 22, t, () => input.scriptedHitch = 1f);
        Once("tip", 20, t, () => Object.FindAnyObjectByType<Tipper>().Move(1f, 7f));   // raise the parked trailer's body straight to the top
        // Phase 6: the start menu is up at start; walk through every lesson's steps (SKIP / NEXT LESSON each frame), so each
        // step's enter, check, status and highlight code runs at least once.
        Once("tutorial", 22.5, t, () =>
        {
            var tut = Tutorial.Instance;
            if (tut == null) { Debug.LogError("[PlayModeTest] no Tutorial in the scene"); return; }
            log.AppendLine($"{t:0.0}s tutorial board: \"{tut.Title}\" with {tut.ButtonCount} buttons");
            tut.ShowLessons();
            log.AppendLine($"{t:0.0}s lessons: {tut.ButtonCount - 1}");
            tut.StartLesson(0);
        });
        if (t > 22.6 && t < 24.6 && Tutorial.Instance != null && Tutorial.Instance.Visible && Tutorial.Instance.ButtonCount > 0)
        {
            var tut = Tutorial.Instance;
            if (tut.Title != lastTutorialTitle) { tutorialPages++; lastTutorialTitle = tut.Title; }
            tut.Press(0);
        }
        Once("tutorialEnd", 24.7, t, () => log.AppendLine($"{t:0.0}s tutorial: {tutorialPages} pages walked, now \"{Tutorial.Instance?.Title}\""));
        Once("end", 25, t, () =>
        {
            input.scriptedHitch = 0f;
            var hitch = tractor.GetComponent<Hitch>();
            log.AppendLine($"{t:0.0}s hitch at {hitch.RearPosition:0.00}, {frames} frames ({frames / t:0} fps in the editor)");
            var lt = tractor.GetComponent<TractorLights>();
            log.AppendLine($"{t:0.0}s lights: sky {lt.TimeOfDay}, road {lt.RoadLights}, indicator {lt.Indicator}, stalk at {tractor.GetComponent<CabControls>().stalk.angle:0}°");
            while (lt.TimeOfDay != TractorLights.Sky.Day) lt.CycleSky();
            var drive = tractor.GetComponent<Drivetrain>();
            var tipper = Object.FindAnyObjectByType<Tipper>();
            log.AppendLine($"{t:0.0}s trailer body {tipper.Angle:0}°, load {tipper.LoadKg:0} kg left, {Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Count(m => m.name == "SoilHeap")} heap(s)");
            log.AppendLine($"{t:0.0}s fuel {drive.FuelLitres:0.0} L ({drive.FuelRateLph:0.0} L/h), coolant {drive.CoolantC:0.0} °C, doors open {tractor.GetComponent<CabControls>().DoorsOpen}");
            Finish(null);
        });
    }

    static int tutorialPages;
    static string lastTutorialTitle;

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
