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

    void Update()
    {
        float kmh = tractor.SpeedKmh;
        string dir = kmh > 0.3f ? "F" : kmh < -0.3f ? "R" : "N";
        string view = rig.view == VRCameraRig.View.Cab ? "CAB" : "3RD";
        string line = $"{Mathf.Abs(kmh):0} km/h  {dir}   {view}";
        if (input.Calibrating && rig.view == VRCameraRig.View.Cab) line += "\nSEAT ADJUST";
        text.text = line;
    }
}
