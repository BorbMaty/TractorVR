using UnityEngine;

/// <summary>
/// Instrument cluster on the steering column: engine speed and road speed dials (needle pivots spin around
/// their local Z, which points away from the driver). Direction + gear sit in the upper half of the rev counter and
/// the gearbox mode in the upper half of the speedometer (the hub hides the middle, the column shroud the bottom).
/// </summary>
public class Dashboard : MonoBehaviour
{
    public TractorController tractor;
    public Drivetrain drivetrain;
    public Transform rpmNeedle, speedNeedle;
    public TextMesh gearText, modeText;
    public Material textMaterial;    // TractorVR/WorldText, shared by all labels

    public float rpmMax = 3000f;
    public float speedMax = 60f;     // km/h
    public float sweep = 270f;       // degrees from 0 to full scale

    Font font;

    void Awake()
    {
        // The labels use a depth-tested text shader. Give it the dynamic font atlas, and again whenever it is rebuilt.
        font = gearText.font;
        textMaterial.mainTexture = font.material.mainTexture;
        Font.textureRebuilt += OnFontRebuilt;
    }

    void OnDestroy() => Font.textureRebuilt -= OnFontRebuilt;

    void OnFontRebuilt(Font f)
    {
        if (f == font) textMaterial.mainTexture = font.material.mainTexture;
    }

    void Update()
    {
        rpmNeedle.localRotation = Needle(drivetrain.Rpm / rpmMax);
        speedNeedle.localRotation = Needle(Mathf.Abs(tractor.SpeedKmh) / speedMax);
        char dir = drivetrain.Direction == Drivetrain.Dir.Forward ? 'F' : drivetrain.Direction == Drivetrain.Dir.Reverse ? 'R' : 'N';
        gearText.text = $"{dir} {drivetrain.GearName}";
        modeText.text = drivetrain.Automatic ? "AUTO" : "MAN";
    }

    // Zero sits at the lower left; the needle sweeps clockwise (negative Z as seen from the driver).
    Quaternion Needle(float t) => Quaternion.Euler(0f, 0f, sweep * 0.5f - Mathf.Clamp01(t) * sweep);
}
