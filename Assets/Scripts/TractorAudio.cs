using UnityEngine;

/// <summary>
/// Engine and cab sounds. The clips are synthesised by the setup script (Assets/Generated/Audio):
/// two engine loops recorded at 1000 rpm (light and heavy load) are pitched with the engine speed and
/// cross-faded by load. Plus a shift clunk, the reverse beeper and the horn.
/// In the cab view the engine is low-pass filtered (the cab is insulated).
/// </summary>
public class TractorAudio : MonoBehaviour
{
    public Drivetrain drivetrain;
    public TractorInput input;
    public VRCameraRig rig;

    public AudioSource engineLight, engineHeavy, beeper, horn, shift;
    public AudioLowPassFilter lightFilter, heavyFilter;

    public float clipRpm = 1000f;
    public float engineVolume = 0.7f;
    public float cabCutoff = 2200f;

    float hornVol;

    void Start()
    {
        foreach (var s in new[] { engineLight, engineHeavy }) { s.volume = 0f; s.Play(); }
        drivetrain.Shifted += () => shift.PlayOneShot(shift.clip, 0.8f);
    }

    void Update()
    {
        float pitch = drivetrain.Rpm / clipRpm;
        float load = drivetrain.Load;
        float level = engineVolume * Mathf.Lerp(0.55f, 1f, Mathf.InverseLerp(drivetrain.idleRpm, drivetrain.ratedRpm, drivetrain.Rpm));
        engineLight.pitch = engineHeavy.pitch = pitch;
        engineLight.volume = level * Mathf.Cos(load * Mathf.PI * 0.5f);
        engineHeavy.volume = level * Mathf.Sin(load * Mathf.PI * 0.5f);

        float cutoff = rig.view == VRCameraRig.View.Cab ? cabCutoff : 22000f;
        lightFilter.cutoffFrequency = heavyFilter.cutoffFrequency = cutoff;

        bool reversing = drivetrain.Direction == Drivetrain.Dir.Reverse;
        if (reversing && !beeper.isPlaying) beeper.Play();
        else if (!reversing && beeper.isPlaying) beeper.Stop();

        // Short fades so the horn loop doesn't click on and off.
        hornVol = Mathf.MoveTowards(hornVol, input.Horn ? 1f : 0f, Time.deltaTime / 0.04f);
        horn.volume = hornVol;
        if (hornVol > 0f && !horn.isPlaying) horn.Play();
        else if (hornVol == 0f && horn.isPlaying) horn.Stop();
    }
}
