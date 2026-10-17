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

    float messageUntil;
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
        if (hitch.Towed) s.Append($"{hitch.Towed.displayName}  ");
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
        if (Time.time < messageUntil) line += "\n" + message;
        text.text = line;
    }
}
