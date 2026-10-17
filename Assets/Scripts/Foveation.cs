using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Fixed foveated rendering on the Quest (OpenXR "Foveated Rendering" feature, SRP foveation API): the edges of the view are
/// rendered at a lower shading rate, which the eye doesn't notice through the lenses. Biggest single GPU saving on the Quest 2.
/// Level Off / Low / Medium / High from the cab panel, remembered (default High).
/// </summary>
public class Foveation : MonoBehaviour
{
    public static readonly string[] Names = { "OFF", "LOW", "MEDIUM", "HIGH" };
    static readonly float[] Strength = { 0f, 0.33f, 0.66f, 1f };

    public static int Level
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt("Foveation", 3), 0, 3);
        set => PlayerPrefs.SetInt("Foveation", Mathf.Clamp(value, 0, 3));
    }

    readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
    int applied = -1;

    void Update()
    {
        if (applied == Level) return;
        SubsystemManager.GetSubsystems(displays);
        if (displays.Count == 0) return;
        foreach (var d in displays)
        {
            d.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.None;   // fixed: no eye tracking on the Quest 2
            d.foveatedRenderingLevel = Strength[Level];
        }
        applied = Level;
    }
}
