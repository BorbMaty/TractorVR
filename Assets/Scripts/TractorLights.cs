using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tractor lights and time of day (phase 3 step 4).
///   Road lights ... rotary knob on the right console: OFF / SIDE (position + tail lights) / HEAD (+ headlights in the grille
///                   and on the cab front). Keyboard G, cab panel LIGHTS.
///   Work lights ... front and rear roof lights, a rocker each (keyboard N cycles off / front / front + rear).
///   Beacon ........ the two amber roof beacons, rotating (keyboard O).
///   Indicators .... left-hand stalk (push up = right, down = left), cancelled when the steering comes back after the turn
///                   (keyboard , / .). Hazards: push button on the console (keyboard Y). They tick while blinking.
///   Brake lights .. on while braking harder than coasting.
///   Time of day ... DAY / DUSK / NIGHT (cab panel SKY, keyboard U): sun, sky, ambient, fog and reflections.
/// The model has no separate lamp parts per function (lens materials are shared), so each lamp gets a generated lens disc
/// that lights up, plus a soft halo that shows at dusk and at night. Real light (spot lights, no shadows) only for the
/// headlights and the two work-light sets, plus a dim cab light at night: the Quest's per-object light budget.
/// </summary>
public class TractorLights : MonoBehaviour
{
    public enum Road { Off, Side, Head }
    public enum Sky { Day, Dusk, Night }

    [Serializable]
    public class Lamp
    {
        public Renderer lens;             // disc on the lamp glass
        public Renderer halo;             // camera-facing glow (TractorVR/Halo)
        public Color color = Color.white;
    }

    [Serializable]
    public class Group
    {
        public string name;
        public Lamp[] lamps = new Lamp[0];
        public Light[] lights = new Light[0];
        [NonSerialized] public float level = -1f;
        [NonSerialized] public float halo = -1f;
    }

    public Group side = new Group(), head = new Group(), workFront = new Group(), workRear = new Group(), beacon = new Group(),
        indicatorLeft = new Group(), indicatorRight = new Group(), brake = new Group();
    public Light cabLight;                // dim light over the console at dusk / night while the road lights are on
    public AudioSource tick;
    public float blinkHz = 1.5f;
    public float beaconRps = 1.5f;        // beacon turns per second

    public Road RoadLights { get; private set; }
    public bool WorkFront { get; private set; }
    public bool WorkRear { get; private set; }
    public bool Beacon { get; private set; }
    public bool Hazards { get; private set; }
    /// <summary>-1 left, 0 off, +1 right.</summary>
    public int Indicator { get; private set; }
    public Sky TimeOfDay { get; private set; }
    /// <summary>Blinking lamps are lit right now (dashboard tell-tales).</summary>
    public bool BlinkOn { get; private set; }
    public event Action<string> Message;

    TractorController tractor;
    TractorInput input;
    CabControls cab;
    MaterialPropertyBlock block;
    static readonly int ColorId = Shader.PropertyToID("_BaseColor");
    bool indicatorArmed;
    float blinkTime;

    // Day settings, captured at start (the scene is built for day).
    Light sun;
    Quaternion daySunRot;
    Color daySunColor, dayFog, daySky, dayEquator, dayGround;
    float daySunIntensity, dayExposure = 1f, dayReflection;
    SphericalHarmonicsL2 dayProbe;
    // The glass is unlit (a faint tint), so it would stay day-bright and lay a grey veil over the view at night: dimmed per
    // material slot with the ambient light.
    readonly System.Collections.Generic.List<(Renderer r, int index, Color color)> glass = new System.Collections.Generic.List<(Renderer, int, Color)>();

    void Awake()
    {
        tractor = GetComponent<TractorController>();
        input = GetComponent<TractorInput>();
        cab = GetComponent<CabControls>();
        block = new MaterialPropertyBlock();
        sun = RenderSettings.sun;
        if (sun)
        {
            daySunRot = sun.transform.rotation;
            daySunColor = sun.color;
            daySunIntensity = sun.intensity;
        }
        dayFog = RenderSettings.fogColor;
        daySky = RenderSettings.ambientSkyColor;
        dayEquator = RenderSettings.ambientEquatorColor;
        dayGround = RenderSettings.ambientGroundColor;
        dayProbe = RenderSettings.ambientProbe;
        dayReflection = RenderSettings.reflectionIntensity;
        if (RenderSettings.skybox && RenderSettings.skybox.HasProperty("_Exposure")) dayExposure = RenderSettings.skybox.GetFloat("_Exposure");
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] && mats[i].name.Contains("Glass") && mats[i].HasProperty(ColorId)) glass.Add((r, i, mats[i].GetColor(ColorId)));
        }
    }

    void OnDestroy()
    {
        // The skybox is a shared asset: put it back so the editor doesn't keep the night sky.
        if (TimeOfDay != Sky.Day) ApplySky(Sky.Day);
    }

    // ------------------------------------------------------------------ switches (console, stalk, panel, keys, tests)
    public void CycleRoadLights() => SetRoadLights((Road)(((int)RoadLights + 1) % 3));
    public void SetRoadLights(Road r) { RoadLights = r; Message?.Invoke(r == Road.Off ? "LIGHTS OFF" : r == Road.Side ? "SIDE LIGHTS" : "HEADLIGHTS"); }
    public void SetWorkFront(bool on) { WorkFront = on; Message?.Invoke(on ? "FRONT WORK LIGHTS ON" : "FRONT WORK LIGHTS OFF"); }
    public void SetWorkRear(bool on) { WorkRear = on; Message?.Invoke(on ? "REAR WORK LIGHTS ON" : "REAR WORK LIGHTS OFF"); }
    /// <summary>Off → front → front + rear → off (keyboard N, cab panel).</summary>
    public void CycleWorkLights()
    {
        if (!WorkFront && !WorkRear) { WorkFront = true; Message?.Invoke("WORK LIGHTS: FRONT"); }
        else if (!WorkRear) { WorkRear = true; Message?.Invoke("WORK LIGHTS: FRONT + REAR"); }
        else { WorkFront = WorkRear = false; Message?.Invoke("WORK LIGHTS OFF"); }
    }
    public void SetBeacon(bool on) { Beacon = on; Message?.Invoke(on ? "BEACON ON" : "BEACON OFF"); }
    public void SetHazards(bool on) { Hazards = on; blinkTime = 0f; Message?.Invoke(on ? "HAZARDS ON" : "HAZARDS OFF"); }
    /// <summary>Same direction again = off (keys, panel); the stalk sets it directly.</summary>
    public void ToggleIndicator(int dir) => SetIndicator(Indicator == dir ? 0 : dir);
    public void SetIndicator(int dir)
    {
        if (dir == Indicator) return;
        Indicator = dir;
        indicatorArmed = false;
        if (dir != 0) blinkTime = 0f;
    }
    public void CycleSky() { TimeOfDay = (Sky)(((int)TimeOfDay + 1) % 3); ApplySky(TimeOfDay); Message?.Invoke(TimeOfDay.ToString().ToUpperInvariant()); }

    void Update() => FrameUpdate(Time.deltaTime);

    /// <summary>Per-frame update (public for the editor tests).</summary>
    public void FrameUpdate(float dt)
    {
        if (input)
        {
            if (input.LightsPressed) CycleRoadLights();
            if (input.WorkLightsPressed) CycleWorkLights();
            if (input.BeaconPressed) SetBeacon(!Beacon);
            if (input.HazardsPressed) SetHazards(!Hazards);
            if (input.IndicatorLeftPressed) ToggleIndicator(-1);
            if (input.IndicatorRightPressed) ToggleIndicator(1);
            if (input.SkyPressed) CycleSky();
        }
        UpdateStalk();

        // Self-cancelling: once the wheels have turned the indicated way (> 10°), it goes off when they come back (< 3°).
        if (Indicator != 0 && tractor)
        {
            float turned = tractor.SteerAngle * Indicator;
            if (turned > 10f) indicatorArmed = true;
            else if (indicatorArmed && turned < 3f) SetIndicator(0);
        }

        bool blinking = Hazards || Indicator != 0;
        bool wasOn = BlinkOn;
        blinkTime = blinking ? blinkTime + dt : 0f;
        BlinkOn = blinking && Mathf.Repeat(blinkTime * blinkHz, 1f) < 0.5f;
        if (BlinkOn != wasOn && tick) { tick.pitch = BlinkOn ? 1.6f : 1.3f; tick.Play(); }

        float halo = TimeOfDay == Sky.Day ? 0f : TimeOfDay == Sky.Dusk ? 0.55f : 1f;
        Set(side, RoadLights >= Road.Side ? 1f : 0f, halo);
        Set(head, RoadLights == Road.Head ? 1f : 0f, halo);
        Set(workFront, WorkFront ? 1f : 0f, halo);
        Set(workRear, WorkRear ? 1f : 0f, halo);
        Set(brake, tractor && tractor.Braking ? 1f : 0f, halo);
        Set(indicatorLeft, BlinkOn && (Hazards || Indicator < 0) ? 1f : 0f, halo);
        Set(indicatorRight, BlinkOn && (Hazards || Indicator > 0) ? 1f : 0f, halo);
        if (cabLight) cabLight.enabled = RoadLights != Road.Off && TimeOfDay != Sky.Day;

        // Beacons: a rotating reflector, seen as a flash each time it faces you; the two run half a turn apart.
        if (Beacon)
        {
            float phase = Time.time * beaconRps * Mathf.PI * 2f;
            for (int i = 0; i < beacon.lamps.Length; i++)
            {
                float c = Mathf.Cos(phase + i * Mathf.PI);
                float level = 0.25f + 0.75f * Mathf.Pow(Mathf.Max(0f, c), 3f);
                Paint(beacon.lamps[i], level, Mathf.Max(halo, 0.35f) * level);
            }
            beacon.level = 1f;
        }
        else Set(beacon, 0f, halo);
    }

    // The stalk follows the indicator (cancel, keys, panel) unless a hand holds it; held, it sets the indicator.
    void UpdateStalk()
    {
        if (!cab || cab.stalk == null || !cab.stalk.hinge) return;
        var s = cab.stalk;
        if (s.Held)
        {
            float edge = s.max * 0.6f;
            int dir = s.angle > edge ? 1 : s.angle < -edge ? -1 : Mathf.Abs(s.angle) < s.max * 0.3f ? 0 : Indicator;
            if (dir != Indicator) { SetIndicator(dir); if (tick) { tick.pitch = 1f; tick.Play(); } }
        }
        else
        {
            float want = Indicator * s.max;
            if (Mathf.Abs(s.angle - want) > 0.1f && (!s.target.HasValue || s.target.Value != want)) s.target = want;
        }
    }

    void Set(Group g, float level, float halo)
    {
        if (g.level == level && g.halo == halo) return;
        bool wasOn = g.level > 0f;
        g.level = level;
        g.halo = halo;
        foreach (var l in g.lamps) Paint(l, level, halo * level);
        if (wasOn != level > 0f) foreach (var light in g.lights) if (light) light.enabled = level > 0f;
    }

    void Paint(Lamp l, float level, float halo)
    {
        if (l.lens)
        {
            l.lens.enabled = level > 0f;
            if (level > 0f)
            {
                block.Clear();
                block.SetColor(ColorId, l.color * Mathf.Lerp(0.35f, 1f, level));
                l.lens.SetPropertyBlock(block);
            }
        }
        if (l.halo)
        {
            l.halo.enabled = halo > 0.01f;
            if (halo > 0.01f)
            {
                block.Clear();
                block.SetColor(ColorId, new Color(l.color.r, l.color.g, l.color.b, halo));
                l.halo.SetPropertyBlock(block);
            }
        }
    }

    // ------------------------------------------------------------------ time of day
    void ApplySky(Sky sky)
    {
        // Light level (sun, ambient) and tint per setting; dusk = sun just over the horizon, night = moonlight.
        float ambient = sky == Sky.Day ? 1f : sky == Sky.Dusk ? 0.4f : 0.07f;
        Color tint = sky == Sky.Day ? Color.white : sky == Sky.Dusk ? new Color(1f, 0.78f, 0.68f) : new Color(0.55f, 0.65f, 1f);
        if (sun)
        {
            sun.transform.rotation = sky == Sky.Day ? daySunRot : sky == Sky.Dusk ? Quaternion.Euler(6f, -35f, 0f) : Quaternion.Euler(40f, 150f, 0f);
            sun.color = sky == Sky.Day ? daySunColor : sky == Sky.Dusk ? new Color(1f, 0.55f, 0.3f) : new Color(0.6f, 0.7f, 1f);
            sun.intensity = sky == Sky.Day ? daySunIntensity : sky == Sky.Dusk ? 0.6f : 0.06f;
        }
        RenderSettings.ambientSkyColor = daySky * tint * ambient;
        RenderSettings.ambientEquatorColor = dayEquator * tint * ambient;
        RenderSettings.ambientGroundColor = dayGround * tint * ambient;
        var probe = dayProbe;
        for (int c = 0; c < 9; c++) { probe[0, c] *= tint.r * ambient; probe[1, c] *= tint.g * ambient; probe[2, c] *= tint.b * ambient; }
        RenderSettings.ambientProbe = probe;
        RenderSettings.fogColor = sky == Sky.Day ? dayFog : sky == Sky.Dusk ? new Color(0.62f, 0.5f, 0.45f) : new Color(0.02f, 0.025f, 0.04f);
        RenderSettings.reflectionIntensity = dayReflection * (sky == Sky.Day ? 1f : sky == Sky.Dusk ? 0.45f : 0.08f);
        var glassBlock = new MaterialPropertyBlock();
        foreach (var (r, index, color) in glass)
        {
            if (sky == Sky.Day) { r.SetPropertyBlock(null, index); continue; }
            glassBlock.SetColor(ColorId, new Color(color.r * ambient, color.g * ambient, color.b * ambient, color.a));
            r.SetPropertyBlock(glassBlock, index);
        }
        if (RenderSettings.skybox && RenderSettings.skybox.HasProperty("_Exposure"))
            RenderSettings.skybox.SetFloat("_Exposure", dayExposure * (sky == Sky.Day ? 1f : sky == Sky.Dusk ? 0.8f : 0.03f));
    }
}
