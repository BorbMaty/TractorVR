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

    float messageUntil;
    string message;

    void Start()
    {
        comfort.LevelChanged += level => Show("COMFORT " + level.ToString().ToUpperInvariant());
        drivetrain.Message += Show;
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
        string line = $"{Mathf.Abs(kmh):0} km/h  {dir} {drivetrain.GearName}  {drivetrain.Rpm:0} rpm";
        if (input.Calibrating && rig.view == VRCameraRig.View.Cab) line += "\nSEAT ADJUST";
        if (Time.time < messageUntil) line += "\n" + message;
        text.text = line;
    }
}
