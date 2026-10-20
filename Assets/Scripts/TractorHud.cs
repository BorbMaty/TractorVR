using UnityEngine;

/// <summary>
/// Small world-space readout attached to the rig (not to the head), below the line of sight.
/// </summary>
public class TractorHud : MonoBehaviour
{
    public TractorController tractor;
    public VRCameraRig rig;
    public TractorInput input;
    public TextMesh text;
    public ComfortVignette comfort;
    public Drivetrain drivetrain;
    public Hitch hitch;
    public CabControls cab;

    /// <summary>Frame-time readout (cab panel), remembered.</summary>
    public static bool ShowPerf
    {
        get => PlayerPrefs.GetInt("ShowPerf", 0) == 1;
        set => PlayerPrefs.SetInt("ShowPerf", value ? 1 : 0);
    }

    float messageUntil;
    float frameAvg = 1f / 72f, frameWorst, worstWindow, worstShown;
    string message;

    void Start()
    {
        comfort.LevelChanged += level => Show("COMFORT " + level.ToString().ToUpperInvariant());
        drivetrain.Message += Show;
        hitch.Message += Show;
    }

    // Coupled implements with their linkage position, the PTO, or the distance to the nearest implement while lining up.
    string Implements()
    {
        var s = new System.Text.StringBuilder();
        if (hitch.Front) s.Append($"{hitch.Front.displayName} {hitch.FrontPosition * 100f:0}%  ");
        if (hitch.Rear) s.Append($"{hitch.Rear.displayName} {hitch.RearPosition * 100f:0}%{(hitch.Rear.Working ? " WORK" : "")}  ");
        var tipper = hitch.TowedTipper;
        if (hitch.Towed) s.Append(tipper ? $"{hitch.Towed.displayName} {tipper.LoadKg / 1000f:0.0} t{(tipper.Angle > 0.5f ? $" TIP {tipper.Share * 100f:0}%" : "")}  " : $"{hitch.Towed.displayName}  ");
        if (hitch.Pto) s.Append($"PTO {hitch.PtoSpeed:0}");
        if (hitch.Nearest && hitch.NearestDistance < 6f) s.Append($"  {hitch.Nearest.displayName} {hitch.NearestDistance:0.0} m");
        return s.ToString().Trim();
    }

    void Show(string msg)
    {
        message = msg;
        messageUntil = Time.time + 2.5f;
    }

    void Update()
    {
        float kmh = tractor.SpeedKmh;
        string dir = drivetrain.Direction == Drivetrain.Dir.Forward ? "F" : drivetrain.Direction == Drivetrain.Dir.Reverse ? "R" : "N";
        string speed = CabPanel.Mph ? $"{Mathf.Abs(kmh) * 0.6214f:0} mph" : $"{Mathf.Abs(kmh):0} km/h";
        string line = $"{speed}  {dir} {drivetrain.GearName}  {drivetrain.Rpm:0} rpm";
        if (input.Calibrating && rig.view == VRCameraRig.View.Cab) line += "\nSEAT ADJUST";
        string work = Implements();
        if (work.Length > 0) line += "\n" + work;
        string lever = cab ? cab.Status : "";
        if (lever.Length > 0) line += "\n" + lever;
        if (Time.time < messageUntil) line += "\n" + message;
        if (ShowPerf)
        {
            // Smoothed frame time + the worst frame of the last second (a hitch shows there).
            float dt = Time.unscaledDeltaTime;
            frameAvg = Mathf.Lerp(frameAvg, dt, 0.05f);
            frameWorst = Mathf.Max(frameWorst, dt);
            worstWindow += dt;
            if (worstWindow > 1f) { worstShown = frameWorst; frameWorst = 0f; worstWindow = 0f; }
            line += $"\n{1f / frameAvg:0} fps  {frameAvg * 1000f:0.0} ms  worst {worstShown * 1000f:0} ms";
        }
        text.text = line;
    }
}
