using System;
using UnityEngine;

/// <summary>
/// Pressable switches on the right-hand console (phase 3 step 4), cut out of the cab model at setup: rockers, push buttons and
/// the rotary road-light knob. A fingertip / controller tip pushes them like the cab panel buttons: each push toggles (or steps
/// the knob round), with a click. The switch shows its state (rocker tipped, knob turned) and its small lamp lights while
/// the function is on. Functions: road lights (knob), front / rear work lights, beacon, hazards, 4WD, diff lock, PTO.
/// The cab panel keeps the same functions as the easy way to reach everything.
/// </summary>
public class CabSwitches : MonoBehaviour
{
    public enum Kind { Rocker, Push, Knob }

    [Serializable]
    public class Switch
    {
        public string id;
        public Kind kind;
        public Transform hinge;           // rocker: tips around `axis`; knob: turns around `axis`; push: moves along -`axis`
        public Vector3 axis = Vector3.forward;
        public float travel = 8f;         // rocker: degrees each way; knob: degrees per step; push: mm
        public Bounds pokeBox;            // hinge space
        public Renderer lamp;             // small indicator lamp in the switch (none on the knob)
        public Color lampColor = new Color(0.3f, 1f, 0.3f);

        [NonSerialized] public Quaternion restRot;
        [NonSerialized] public Vector3 restPos;
        [NonSerialized] public bool inside;
        [NonSerialized] public float shown;   // animated position: rocker -1..1, knob step, push 0..1
        [NonSerialized] public float lampLevel = -1f;
    }

    public Switch[] switches = new Switch[0];
    public AudioSource click;
    public Color lampOff = new Color(0.04f, 0.04f, 0.04f);

    VRHands hands;
    TractorLights lights;
    TractorController tractor;
    Hitch hitch;
    MaterialPropertyBlock block;
    static readonly int ColorId = Shader.PropertyToID("_BaseColor");

    void Start()
    {
        hands = FindAnyObjectByType<VRHands>();
        lights = GetComponent<TractorLights>();
        tractor = GetComponent<TractorController>();
        hitch = GetComponent<Hitch>();
        block = new MaterialPropertyBlock();
        foreach (var s in switches)
        {
            s.restRot = s.hinge.localRotation;
            s.restPos = s.hinge.localPosition;
        }
    }

    /// <summary>State of a function: 0 = off, 1 = on (the knob: 0 off, 1 side lights, 2 headlights).</summary>
    public int State(string id)
    {
        switch (id)
        {
            case "lights": return lights ? (int)lights.RoadLights : 0;
            case "workFront": return lights && lights.WorkFront ? 1 : 0;
            case "workRear": return lights && lights.WorkRear ? 1 : 0;
            case "beacon": return lights && lights.Beacon ? 1 : 0;
            case "hazards": return lights && lights.Hazards ? 1 : 0;
            case "fourWd": return tractor.fourWheelDrive ? 1 : 0;
            case "diffLock": return tractor.DiffLock ? 1 : 0;
            case "pto": return hitch && hitch.Pto ? 1 : 0;
        }
        return 0;
    }

    /// <summary>Push a switch (a finger, or the play-mode test).</summary>
    public void Press(string id)
    {
        switch (id)
        {
            case "lights": lights.CycleRoadLights(); break;
            case "workFront": lights.SetWorkFront(!lights.WorkFront); break;
            case "workRear": lights.SetWorkRear(!lights.WorkRear); break;
            case "beacon": lights.SetBeacon(!lights.Beacon); break;
            case "hazards": lights.SetHazards(!lights.Hazards); break;
            case "fourWd": tractor.SetFourWheelDrive(!tractor.fourWheelDrive); break;
            case "diffLock": tractor.SetDiffLock(!tractor.DiffLock); break;
            case "pto": hitch.SetPto(!hitch.Pto); break;
        }
        if (click) click.Play();
    }

    void Update() => FrameUpdate(Time.deltaTime);

    public void FrameUpdate(float dt)
    {
        foreach (var s in switches)
        {
            // Each hand pokes only the switch whose box its tip is deepest in (they sit 2-3 cm apart). Once pushed, the tip
            // has to move 3 cm clear before the next push, so a shaky fingertip at the edge doesn't flip it twice.
            bool inside = false;
            if (hands)
                foreach (var h in hands.Both)
                    if (h.Tracked && (s.inside ? InBox(s, h.PokeTip, 0.03f) : Nearest(h.PokeTip) == s)) inside = true;
            if (inside && !s.inside) Press(s.id);
            s.inside = inside;

            int state = State(s.id);
            float target = s.kind == Kind.Rocker ? (state > 0 ? 1f : -1f) : s.kind == Kind.Knob ? state : inside ? 1f : 0f;
            s.shown = Mathf.MoveTowards(s.shown, target, dt * 12f);
            if (s.kind == Kind.Push) s.hinge.localPosition = s.restPos - s.restRot * s.axis * (s.travel * 0.001f * s.shown);
            else s.hinge.localRotation = s.restRot * Quaternion.AngleAxis(s.shown * s.travel, s.axis);

            if (s.lamp)
            {
                float level = state > 0 ? (s.id == "hazards" && lights && !lights.BlinkOn ? 0.25f : 1f) : 0f;
                if (level != s.lampLevel)
                {
                    s.lampLevel = level;
                    block.Clear();
                    block.SetColor(ColorId, Color.Lerp(lampOff, s.lampColor, level));
                    s.lamp.SetPropertyBlock(block);
                }
            }
        }
    }

    /// <summary>A fingertip / controller tip is on (or within 1.5 cm of) a switch.</summary>
    public bool Touching(Vector3 tip)
    {
        foreach (var s in switches) if (InBox(s, tip, 0.03f)) return true;
        return false;
    }

    // The switch whose poke box (grown by 6 mm each way) holds the point, nearest to its centre; null if none.
    Switch Nearest(Vector3 world)
    {
        Switch best = null;
        float bestD = float.MaxValue;
        foreach (var s in switches)
        {
            if (!InBox(s, world, 0.012f)) continue;
            float d = (Local(s, world) - s.pokeBox.center).sqrMagnitude;
            if (d < bestD) { best = s; bestD = d; }
        }
        return best;
    }

    // In the switch's rest hinge space (a tipped rocker or pushed button keeps its box).
    static Vector3 Local(Switch s, Vector3 world) => Quaternion.Inverse(s.restRot) * (s.hinge.parent.InverseTransformPoint(world) - s.restPos);

    static bool InBox(Switch s, Vector3 world, float grow)
    {
        var box = s.pokeBox;
        box.Expand(grow);
        return box.Contains(Local(s, world));
    }
}
