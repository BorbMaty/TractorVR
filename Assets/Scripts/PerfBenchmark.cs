using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using XRStats = UnityEngine.XR.Provider.XRStats;

/// <summary>
/// Benchmark for the phase 4 comparison of the three versions (Quest standalone, PC VR over Link, PC desktop): the same scripted drive
/// every time, every frame logged.
///   Start ... cab panel / screen menu BENCHMARK, F8, or <c>-benchmark</c> on the command line (starts once the mode is decided, quits at the end).
///   Run ..... the tractor is reset to the start (automatic gearbox, hand throttle idle, PTO off), day, cab view; 3 s settle, then 30 s driving straight out of the yard in the cab view
///             ("cab"), then 30 s in the third-person view with a gentle left turn ("third"). In VR: look straight ahead and keep still.
///             Then (phase 5), standing in the yard facing the barn in the third-person view (the drive leaves the yard northwards and
///             never shows it): 10 s with the environment assets ("env"), 10 s with them hidden ("noenv"); the difference is their cost.
///             Then the same looking east across the yard from its west side ("yard" / "noyard": shed, silos, tank, house, gate).
///             "all" = the drive only, comparable with the phase 4 runs.
///   Data .... per frame: frame time (unscaled delta), Unity FrameTimingManager CPU / GPU time, and in VR the runtime's own counters
///             (XR_META_performance_metrics through XRStats: app CPU / GPU, compositor GPU, dropped frames).
///   Output .. Application.persistentDataPath/Benchmark/&lt;version&gt;_&lt;time&gt;/: summary.txt, frames.csv, cab.png, third.png, env.png
///             (the stills come from a separate 1600×900 camera at a 60° field of view, so all versions are framed alike; taken
///             outside the timed drive: cab standing in the yard before it, third person where it ends).
/// </summary>
public class PerfBenchmark : MonoBehaviour
{
    public TractorController tractor;
    public TractorInput input;
    public VRCameraRig rig;
    public DesktopMode desktop;
    public TractorHud hud;
    public Camera cam;

    public const float Settle = 3f, PhaseLength = 30f, EnvLength = 10f;
    /// <summary>Env phase spot: yard, 16 m east of the barn's doors, facing them (west).</summary>
    static readonly Vector3 EnvSpot = new Vector3(WorldLayout.Barn.x1 + 16f, 0f, WorldLayout.Barn.Centre.y);
    /// <summary>Second env view: west side of the yard facing east-south-east, so the shed, silos, diesel tank, house and gate are in view.</summary>
    static readonly Vector3 YardSpot = new Vector3(-18f, 0f, -8f);
    const float YardYaw = 95f;
    /// <summary>Third view (phase 5 step 3): in the meadow, facing across it and the wheat field; "nomeadow" switches the vegetation off.</summary>
    static readonly Vector3 MeadowSpot = new Vector3(-30f, 0f, 200f);
    const float MeadowYaw = 75f;

    public static bool Running { get; private set; }
    /// <summary>Folder of the last finished run (null before).</summary>
    public static string LastOutput { get; private set; }

    struct Frame { public float t, frameMs, cpuMs, gpuMs, xrAppCpu, xrAppGpu, xrCompGpu, kmh; public int dropped; public string phase; }
    readonly List<Frame> frames = new List<Frame>();
    readonly FrameTiming[] timing = new FrameTiming[1];
    readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();

    IEnumerator Start()
    {
        if (!System.Environment.GetCommandLineArgs().Contains("-benchmark")) yield break;
        yield return new WaitForSeconds(1f);
        while (DesktopMode.Waiting) yield return null;
        yield return new WaitForSeconds(3f);
        yield return Run();
        Application.Quit();
    }

    void Update()
    {
        if (!Running && Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame) Begin();
    }

    /// <summary>Starts a run (cab panel / menu button).</summary>
    public void Begin()
    {
        if (!Running) StartCoroutine(Run());
    }

    XRDisplaySubsystem Display()
    {
        SubsystemManager.GetSubsystems(displays);
        return displays.FirstOrDefault(d => d.running);
    }

    static string Version()
    {
        if (Application.platform == RuntimePlatform.Android) return "QuestStandalone";
        return DesktopMode.Active ? "PCDesktop" : "PCVR";
    }

    IEnumerator Run()
    {
        Running = true;
        frames.Clear();
        string dir = Path.Combine(Application.persistentDataPath, "Benchmark", $"{Version()}_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}");
        Directory.CreateDirectory(dir);

        var lights = tractor.GetComponent<TractorLights>();
        if (lights) while (lights.TimeOfDay != TractorLights.Sky.Day) lights.CycleSky();
        if (rig.view != VRCameraRig.View.Cab) rig.ToggleView();
        if (desktop && DesktopMode.Active) desktop.ResetLook();
        tractor.ResetTractor();
        // Same machine state every run: the gearbox mode is remembered between sessions (manual held a run at 3 km/h in B2).
        var drive = tractor.GetComponent<Drivetrain>();
        if (!drive.Automatic) drive.ToggleMode();
        drive.MoveHandThrottle(-1f, 10f);
        var hitch = tractor.GetComponent<Hitch>();
        if (hitch.Pto) hitch.SetPto(false);
        input.scripted = true;
        input.scriptedSteer = 0f; input.scriptedThrottle = 0f;
        hud.Show("BENCHMARK: SETTLING, LOOK AHEAD");
        yield return new WaitForSeconds(Settle);
        // Stills outside the timed drive: on the Quest one costs ~20 frames. Cab: standing in the yard; third person: where the drive ends.
        Still(Path.Combine(dir, "cab.png"));
        yield return new WaitForSeconds(1f);

        string summaryHeader = Header();
        var display = Display();
        int droppedStart = DroppedFrames(display);
        bool switched = false;
        int skip = 1;   // the first frame is not counted
        float start = Time.unscaledTime;
        input.scriptedThrottle = 0.55f;
        hud.Show("BENCHMARK: CAB");
        while (true)
        {
            float t = Time.unscaledTime - start;
            if (t >= 2f * PhaseLength) break;
            string phase = t < PhaseLength ? "cab" : "third";
            if (phase == "third" && !switched)
            {
                switched = true;
                rig.ToggleView();
                if (desktop && DesktopMode.Active) desktop.ResetLook();
                input.scriptedSteer = -0.3f;
                hud.Show("BENCHMARK: THIRD PERSON");
            }
            yield return null;
            if (skip > 0) skip--;
            else frames.Add(Sample(t, phase, display, droppedStart));
        }

        Still(Path.Combine(dir, "third.png"));
        input.scriptedThrottle = 0f; input.scriptedSteer = 0f;

        // Env: parked in the yard, third person (the rig is still in it), with and without the environment assets.
        // First facing the barn (as in the step 0 runs), then across the yard (shed, silos, tank, house, gate).
        var variants = FindObjectsByType<TierVariant>(FindObjectsInactive.Include);
        float envStart = Time.unscaledTime;
        var vegetation = FindAnyObjectByType<Vegetation>();
        foreach (var (on, off, spot, yaw) in new[] { ("env", "noenv", EnvSpot, -90f), ("yard", "noyard", YardSpot, YardYaw),
                                                     ("meadow", "nomeadow", MeadowSpot, MeadowYaw) })
        {
            QualityTier.ApplyEnvironment(QualityTier.Current);
            hud.Show("BENCHMARK: FARMYARD, LOOK AHEAD");
            // On the ground there: the meadow lies 0.9 m above the yard, and a teleport at the yard's height put the tractor
            // inside the hill (run 2026-11-01 02:39: ~20 ms GPU there with or without the grass).
            float ground = TerrainGenerator.SurfaceHeight(spot.x, spot.z) - TerrainGenerator.SurfaceHeight(tractor.SpawnPosition.x, tractor.SpawnPosition.z);
            tractor.TeleportTo(new Vector3(spot.x, tractor.SpawnPosition.y + ground, spot.z), Quaternion.Euler(0f, yaw, 0f));
            tractor.RequestStop();
            rig.SnapFollow();
            if (desktop && DesktopMode.Active) desktop.ResetLook();
            yield return new WaitForSeconds(1.5f);
            foreach (var phase in new[] { on, off })
            {
                if (phase == off)
                {
                    Still(Path.Combine(dir, on + ".png"));
                    if (on == "meadow") { if (vegetation) vegetation.enabled = false; }   // grass and wheat only
                    else foreach (var v in variants) v.gameObject.SetActive(false);
                    yield return new WaitForSeconds(1f);
                }
                float phaseStart = Time.unscaledTime;
                skip = 1;
                while (Time.unscaledTime - phaseStart < EnvLength)
                {
                    yield return null;
                    if (skip > 0) skip--;
                    else frames.Add(Sample(2f * PhaseLength + Time.unscaledTime - envStart, phase, display, droppedStart));
                }
            }
        }
        QualityTier.ApplyEnvironment(QualityTier.Current);
        if (vegetation) vegetation.enabled = true;
        tractor.ResetTractor();
        tractor.GetComponent<CabControls>().Neutral();
        tractor.RequestStop();
        input.scripted = false;
        if (rig.view != VRCameraRig.View.Cab) rig.ToggleView();

        File.WriteAllText(Path.Combine(dir, "frames.csv"), Csv());
        string summary = summaryHeader + Summary();
        File.WriteAllText(Path.Combine(dir, "summary.txt"), summary);
        Debug.Log("[Benchmark] " + dir + "\n" + summary);
        LastOutput = dir;
        var all = frames.Select(f => f.frameMs).ToList();
        hud.Show($"BENCHMARK DONE: {1000f / all.Average():0} FPS, {Percentile(all, 0.95f):0.0} MS P95");
        Running = false;
    }

    Frame Sample(float t, string phase, XRDisplaySubsystem display, int droppedStart)
    {
        var f = new Frame { t = t, phase = phase, frameMs = Time.unscaledDeltaTime * 1000f, kmh = tractor.SpeedKmh, cpuMs = -1f, gpuMs = -1f, xrAppCpu = -1f, xrAppGpu = -1f, xrCompGpu = -1f, dropped = -1 };
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
        {
            f.cpuMs = (float)timing[0].cpuFrameTime;
            f.gpuMs = (float)timing[0].gpuFrameTime;
        }
        if (display != null)
        {
            if (XRStats.TryGetStat(display, "perfmetrics.appcputime", out float v)) f.xrAppCpu = v;
            if (XRStats.TryGetStat(display, "perfmetrics.appgputime", out v)) f.xrAppGpu = v;
            if (XRStats.TryGetStat(display, "perfmetrics.compositorgputime", out v)) f.xrCompGpu = v;
            int d = DroppedFrames(display);
            if (d >= 0 && droppedStart >= 0) f.dropped = d - droppedStart;
        }
        return f;
    }

    static int DroppedFrames(XRDisplaySubsystem display)
    {
        if (display != null && XRStats.TryGetStat(display, "appstats.compositordroppedframes", out float v)) return Mathf.RoundToInt(v);
        return -1;
    }

    float RefreshRate()
    {
        var d = Display();
        if (d != null && d.TryGetDisplayRefreshRate(out float hz) && hz > 0f) return hz;
        return (float)Screen.currentResolution.refreshRateRatio.value;
    }

    string Header()
    {
        var urp = UniversalRenderPipeline.asset;
        var d = Display();
        float hz = RefreshRate();
        string res = d != null ? $"{XRSettings.eyeTextureWidth}×{XRSettings.eyeTextureHeight} per eye" : $"{Screen.width}×{Screen.height}";
        var s = new StringBuilder();
        s.AppendLine($"Tractor VR benchmark  {System.DateTime.Now:yyyy-MM-dd HH:mm}");
        s.AppendLine($"Version        {Version()}  (quality level {QualitySettings.names[QualitySettings.GetQualityLevel()]})");
        s.AppendLine($"Device         {SystemInfo.deviceModel} / {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}) / {SystemInfo.processorType}");
        s.AppendLine($"Display        {hz:0.#} Hz, frame budget {1000f / hz:0.0} ms, render target {res}");
        s.AppendLine($"Rendering      render scale {(urp ? urp.renderScale : 1f):0.0#}, MSAA {(urp ? urp.msaaSampleCount : 1)}×, shadows {(urp ? urp.mainLightShadowmapResolution : 0)} px / {(urp ? urp.shadowDistance : 0):0} m, " +
                      $"mesh LOD threshold {QualitySettings.meshLodThreshold:0.0}, foveation {(Application.platform == RuntimePlatform.Android ? Foveation.Names[Foveation.Level] : "n/a")}");
        s.AppendLine($"Drive          {Settle:0} s settle, {PhaseLength:0} s cab view straight out of the yard, {PhaseLength:0} s third person with a gentle left turn; env / noenv: {EnvLength:0} s each parked facing the barn, with / without the environment assets");
        return s.ToString();
    }

    string Summary()
    {
        float budget = 1000f / RefreshRate();
        var s = new StringBuilder();
        s.AppendLine();
        s.AppendLine("Phase     frames   fps    frame ms avg / p95 / p99 / max   over budget   CPU ms avg / p95   GPU ms avg / p95   XR app GPU ms   XR compositor GPU ms   dropped");
        foreach (var phase in new[] { "cab", "third", "all", "env", "noenv", "yard", "noyard", "meadow", "nomeadow" })
        {
            var fs = frames.Where(f => phase == "all" ? f.phase == "cab" || f.phase == "third" : f.phase == phase).ToList();
            if (fs.Count == 0) continue;
            var ft = fs.Select(f => f.frameMs).ToList();
            string Pair(IEnumerable<float> xs) { var l = xs.Where(x => x >= 0f).ToList(); return l.Count == 0 ? "n/a" : $"{l.Average():0.0} / {Percentile(l, 0.95f):0.0}"; }
            string Avg(IEnumerable<float> xs) { var l = xs.Where(x => x > 0f).ToList(); return l.Count == 0 ? "n/a" : $"{l.Average():0.0}"; }
            int dropped = (phase.EndsWith("env") || phase.EndsWith("yard") || phase.EndsWith("meadow")) && fs.Min(f => f.dropped) >= 0 ? fs.Max(f => f.dropped) - fs.Min(f => f.dropped) : fs.Max(f => f.dropped);
            float over = 100f * ft.Count(x => x > budget * 1.1f) / ft.Count;
            s.AppendLine($"{phase,-8} {fs.Count,7} {1000f / ft.Average(),6:0.0}    {ft.Average(),5:0.0} / {Percentile(ft, 0.95f),5:0.0} / {Percentile(ft, 0.99f),5:0.0} / {ft.Max(),6:0.0}   {over,8:0.0} %   " +
                         $"{Pair(fs.Select(f => f.cpuMs)),-17}  {Pair(fs.Select(f => f.gpuMs)),-17}  {Avg(fs.Select(f => f.xrAppGpu)),-14}  {Avg(fs.Select(f => f.xrCompGpu)),-21}  {(dropped >= 0 ? dropped.ToString() : "n/a")}");
        }
        s.AppendLine();
        s.AppendLine($"over budget = frames longer than 110 % of the {budget:0.0} ms budget. CPU / GPU = Unity FrameTimingManager (-1 / n/a where the platform doesn't report it).");
        s.AppendLine("XR values come from the runtime (XR_META_performance_metrics); dropped = frames the compositor dropped during the run.");
        s.AppendLine("all = the drive (cab + third), comparable with runs before phase 5; env / noenv = standing in the yard facing the barn with / without the phase 5 assets; " +
                     "yard / noyard = the same, looking east across the yard (shed, silos, tank, house, gate); " +
                     "meadow / nomeadow = in the meadow with / without the standing grass and wheat.");
        return s.ToString();
    }

    string Csv()
    {
        var s = new StringBuilder("t_s,phase,frame_ms,cpu_ms,gpu_ms,xr_app_cpu_ms,xr_app_gpu_ms,xr_compositor_gpu_ms,dropped,speed_kmh\n");
        var c = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var f in frames)
            s.AppendLine(string.Join(",", f.t.ToString("0.000", c), f.phase, f.frameMs.ToString("0.00", c), f.cpuMs.ToString("0.00", c), f.gpuMs.ToString("0.00", c),
                f.xrAppCpu.ToString("0.00", c), f.xrAppGpu.ToString("0.00", c), f.xrCompGpu.ToString("0.00", c), f.dropped, f.kmh.ToString("0.0", c)));
        return s.ToString();
    }

    static float Percentile(List<float> xs, float p)
    {
        var sorted = xs.OrderBy(x => x).ToList();
        return sorted[Mathf.Clamp(Mathf.CeilToInt(p * sorted.Count) - 1, 0, sorted.Count - 1)];
    }

    // A still from the view camera's pose through a separate mono camera (same pipeline and quality level), 1600×900, 60° vertical.
    void Still(string path)
    {
        var go = new GameObject("BenchmarkStill");
        var c = go.AddComponent<Camera>();
        c.CopyFrom(cam);
        c.stereoTargetEye = StereoTargetEyeMask.None;
        c.fieldOfView = 60f;
        c.transform.SetPositionAndRotation(cam.transform.position, cam.transform.rotation);
        var data = go.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = false;
        // Single-sampled target: URP still renders with the pipeline's MSAA and resolves into it. Reading back a multisampled
        // target came out black on the Quest (Vulkan), though it worked on the PC.
        var rt = new RenderTexture(1600, 900, 24);
        c.targetTexture = rt;
        c.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        c.targetTexture = null;
        Destroy(tex); Destroy(rt); Destroy(go);
    }
}
