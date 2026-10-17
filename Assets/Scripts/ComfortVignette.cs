using UnityEngine;

/// <summary>
/// Comfort vignette: darkens the edge of the view while the viewpoint turns or changes speed,
/// the two kinds of motion the inner ear notices most (so they cause most of the sickness).
/// Head-locked quad 1 m in front of the camera (child of Main Camera), drawn last with ZTest Always
/// by the TractorVR/ComfortVignette shader. Far enough away that both eyes see it centred.
/// Level cycles Off / Low / High with X on the left controller and is stored in PlayerPrefs.
/// </summary>
[DefaultExecutionOrder(100)] // after VRCameraRig.LateUpdate has moved the rig this frame
public class ComfortVignette : MonoBehaviour
{
    public enum Level { Off, Low, High }

    [System.Serializable]
    public struct Profile
    {
        public float yawFull;     // deg/s of view rotation that gives full vignette
        public float accelFull;   // m/s² of speed change that gives full vignette
        public float innerDeg;    // clear radius at full vignette
        public float outerDeg;    // fully dark beyond this at full vignette
    }

    public TractorController tractor;
    public TractorInput input;
    public Transform rig;
    public Renderer quad;
    [Tooltip("tan of the half angle the quad covers (half size / distance).")]
    public float quadTanHalf = 2.5f;

    public Profile low = new Profile { yawFull = 35f, accelFull = 1.6f, innerDeg = 38f, outerDeg = 52f };
    public Profile high = new Profile { yawFull = 15f, accelFull = 0.8f, innerDeg = 24f, outerDeg = 40f };
    public float idleInnerDeg = 60f, idleOuterDeg = 75f;   // outside the Quest 2 field of view
    public float yawDeadZone = 2f;                         // deg/s, ignores tiny corrections
    public float fadeIn = 4f, fadeOut = 1.2f;              // intensity per second

    public Level CurrentLevel { get; private set; }
    public float Intensity { get; private set; }
    public event System.Action<Level> LevelChanged;

    const string PrefKey = "ComfortLevel";
    static readonly int InnerId = Shader.PropertyToID("_InnerDeg");
    static readonly int OuterId = Shader.PropertyToID("_OuterDeg");
    static readonly int TanHalfId = Shader.PropertyToID("_TanHalf");

    Material mat;
    Quaternion lastRot;

    void Start()
    {
        CurrentLevel = (Level)PlayerPrefs.GetInt(PrefKey, (int)Level.High);
        mat = quad.material;
        mat.SetFloat(TanHalfId, quadTanHalf);
        lastRot = rig.rotation;
        quad.enabled = false;
    }

    void Update()
    {
        if (input.ComfortCyclePressed) Cycle();
    }

    public void Cycle()
    {
        CurrentLevel = (Level)(((int)CurrentLevel + 1) % 3);
        PlayerPrefs.SetInt(PrefKey, (int)CurrentLevel);
        PlayerPrefs.Save();
        LevelChanged?.Invoke(CurrentLevel);
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // All rotation of the viewpoint (yaw, and on hills pitch and roll in the cab) + the tractor's smoothed acceleration.
        float yawRate = Quaternion.Angle(lastRot, rig.rotation) / dt;
        float accel = Mathf.Abs(tractor.Acceleration);
        lastRot = rig.rotation;

        float target = 0f;
        Profile p = CurrentLevel == Level.High ? high : low;
        if (CurrentLevel != Level.Off)
        {
            float turn = Mathf.Clamp01((yawRate - yawDeadZone) / (p.yawFull - yawDeadZone));
            float speedChange = Mathf.Clamp01(accel / p.accelFull);
            target = Mathf.Max(turn, speedChange);
        }
        Intensity = Mathf.MoveTowards(Intensity, target, (target > Intensity ? fadeIn : fadeOut) * dt);

        quad.enabled = Intensity > 0.01f;
        if (!quad.enabled) return;
        mat.SetFloat(InnerId, Mathf.Lerp(idleInnerDeg, p.innerDeg, Intensity));
        mat.SetFloat(OuterId, Mathf.Lerp(idleOuterDeg, p.outerDeg, Intensity));
    }
}
