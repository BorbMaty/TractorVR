using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Phase 6: the start menu (FREE ROAM / TUTORIAL) and the tutorial. A board at the cab's left front (riding with the rig, like the
/// panel on the right) shows the menu, the lesson list and each step's instruction; its buttons are pressed with a fingertip or a
/// controller tip. On the desktop the same content is drawn on screen by <see cref="DesktopMode"/> (<see cref="Title"/>,
/// <see cref="Body"/>, <see cref="ButtonCount"/> / <see cref="ButtonLabel"/> / <see cref="Press"/>).
///
/// A lesson is a list of steps; each step has a goal and a check that is evaluated every frame (the tractor's, hitch's, lights'
/// own state; nothing is faked), so a step whose goal is already met passes at once: a lesson can be started in any state
/// (e.g. "couple the plough" passes if it is coupled). A pulsing glow sits on the control to use (wheel, lever, panel button,
/// console switch) and a large one on the place to drive to. SKIP / REPEAT / LESSONS on every step.
/// </summary>
public class Tutorial : MonoBehaviour
{
    public static Tutorial Instance { get; private set; }

    public VRCameraRig rig;
    public TractorController tractor;
    public Drivetrain drivetrain;
    public Hitch hitch;
    public TractorLights lights;
    public CabPanel panel;
    public CabControls cab;
    public TractorHud hud;
    public VRHands hands;
    public Material buttonMat, buttonHotMat, panelMat, textMat, haloMat;
    public AudioSource click;

    class Step
    {
        public string text;
        public System.Func<bool> done;
        public System.Action enter;
        public System.Func<string> status;
        public System.Func<Transform> highlight;
        public System.Func<Vector3?> goal;
        public float goalRadius = 6f;
        public bool info;            // NEXT button instead of a check
    }

    class Lesson { public string name, intro; public List<Step> steps = new List<Step>(); }

    enum Page { Start, Lessons, Step, Done, Hidden }

    readonly List<Lesson> lessons = new List<Lesson>();
    Page page = Page.Start;
    int lessonIndex, stepIndex;
    float stepTime, doneDelay = -1f;

    // ---- shared content (board + desktop)
    public string Title { get; private set; } = "";
    public string Body { get; private set; } = "";
    public bool Visible => page != Page.Hidden;
    readonly List<(string label, System.Action press)> actions = new List<(string, System.Action)>();
    public int ButtonCount => actions.Count;
    public string ButtonLabel(int i) => actions[i].label;
    public void Press(int i) { if (i < actions.Count) { if (click) click.Play(); actions[i].press(); } }

    // ---- per-step trackers
    Vector3 lastPos;
    float distance;           // m driven (for steps that count metres)
    int counter, startGear;
    bool flag, flag2;

    // ---- slalom
    readonly List<(Rigidbody rb, Vector3 pos, Quaternion rot)> cones = new List<(Rigidbody, Vector3, Quaternion)>();

    void Awake() => Instance = this;

    void Start()
    {
        foreach (var rb in FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
            if (rb.name == "Cone") cones.Add((rb, rb.position, rb.rotation));
        cones.Sort((a, b) => b.pos.z.CompareTo(a.pos.z));
        BuildLessons();
        BuildBoard();
        lastPos = tractor.transform.position;
        ShowStart();
    }

    // ================================================================== pages
    public void ShowStart()
    {
        page = Page.Start;
        Title = "TRACTOR VR";
        Body = "A John Deere 6195M on a farm near Reghin, Transylvania.\n\n" +
               "FREE ROAM: drive and work as you like.\nTUTORIAL: nine short lessons, from driving off to ploughing.\n\n" +
               "Touch a button with a fingertip or the controller tip" + (DesktopMode.Active ? " (or click it)." : ".");
        actions.Clear();
        actions.Add(("FREE ROAM", () => Hide()));
        actions.Add(("TUTORIAL", ShowLessons));
        Changed();
    }

    public void ShowLessons()
    {
        page = Page.Lessons;
        Title = "TUTORIAL: LESSONS";
        Body = "Pick a lesson. Each step checks itself; a yellow glow marks the control to use or the place to drive to.";
        actions.Clear();
        for (int i = 0; i < lessons.Count; i++)
        {
            int k = i;
            bool done = PlayerPrefs.GetInt("Lesson" + i, 0) == 1;
            actions.Add(($"{i + 1}. {lessons[i].name}{(done ? " (done)" : "")}", () => StartLesson(k)));
        }
        actions.Add(("FREE ROAM", () => Hide()));
        Changed();
    }

    /// <summary>Hides the board (free roam); the panel's TUTORIAL button brings back the lesson list.</summary>
    public void Hide()
    {
        page = Page.Hidden;
        actions.Clear();
        Changed();
    }

    public void StartLesson(int i)
    {
        lessonIndex = i;
        page = Page.Step;
        EnterStep(0);
    }

    void EnterStep(int i)
    {
        stepIndex = i;
        stepTime = 0f;
        doneDelay = -1f;
        distance = 0f; counter = 0; flag = flag2 = false;
        lastPos = tractor.transform.position;
        var s = Current;
        s.enter?.Invoke();
        actions.Clear();
        if (s.info) actions.Add(("NEXT", NextStep));
        else actions.Add(("SKIP STEP", NextStep));
        actions.Add(("REPEAT LESSON", () => StartLesson(lessonIndex)));
        actions.Add(("LESSONS", ShowLessons));
        RefreshStepText();
        Changed();
    }

    void NextStep()
    {
        if (stepIndex + 1 < lessons[lessonIndex].steps.Count) { EnterStep(stepIndex + 1); return; }
        PlayerPrefs.SetInt("Lesson" + lessonIndex, 1);
        page = Page.Done;
        Title = $"LESSON {lessonIndex + 1} COMPLETE";
        Body = lessonIndex + 1 < lessons.Count ? $"Well done!\n\nNext: {lessons[lessonIndex + 1].name}." : "Well done: that was the last lesson.\nThe farm is yours: free roam!";
        actions.Clear();
        if (lessonIndex + 1 < lessons.Count) { int n = lessonIndex + 1; actions.Add(("NEXT LESSON", () => StartLesson(n))); }
        actions.Add(("LESSONS", ShowLessons));
        actions.Add(("FREE ROAM", () => Hide()));
        if (hud) hud.Show("LESSON COMPLETE");
        Changed();
    }

    Step Current => lessons[lessonIndex].steps[stepIndex];

    void RefreshStepText()
    {
        var l = lessons[lessonIndex];
        var s = Current;
        Title = $"{lessonIndex + 1}. {l.name}  ({stepIndex + 1}/{l.steps.Count})";
        string status = s.status?.Invoke();
        var g = s.goal?.Invoke();
        if (g.HasValue && string.IsNullOrEmpty(status))
            status = $"Marker: {Flat(g.Value - tractor.transform.position).magnitude:0} m";
        Body = s.text + (string.IsNullOrEmpty(status) ? "" : "\n\n> " + status);
    }

    // ================================================================== per frame
    void Update()
    {
        Vector3 p = tractor.transform.position;
        float moved = Flat(p - lastPos).magnitude;
        lastPos = p;

        if (page == Page.Step)
        {
            var s = Current;
            stepTime += Time.deltaTime;
            s_moved = moved;
            bool ok = false;
            if (!s.info)
            {
                if (s.done != null) ok = s.done();
                else if (s.goal != null && s.goal() is Vector3 g) ok = Flat(g - p).magnitude < s.goalRadius;
            }
            if (ok && doneDelay < 0f)
            {
                doneDelay = 0.8f;   // a moment to see it worked
                if (click) { click.pitch = 1.6f; click.Play(); }
                if (hud) hud.Show("STEP DONE");
            }
            if (doneDelay >= 0f && (doneDelay -= Time.deltaTime) < 0f)
            {
                if (click) click.pitch = 1f;
                NextStep();
            }
            if (page == Page.Step) RefreshStepText();
        }
        UpdateBoard();
        UpdateHalos();
    }

    float s_moved;   // metres the tractor moved this frame (for the counting steps)

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    // ================================================================== lessons
    Implement FindImpl(string name)
    {
        foreach (var i in FindObjectsByType<Implement>(FindObjectsSortMode.None))
            if (i.displayName == name) return i;
        return null;
    }

    Transform PanelButton(string id)
    {
        if (panel == null) return null;
        var b = panel.Open ? panel.transform.Find("Buttons/Button_" + id) : null;
        return b != null ? b : panel.transform.Find("Button_tab");
    }

    Dictionary<string, Transform> parts;
    /// <summary>A named part anywhere under the tractor (cut-out switches, levers, the stalk).</summary>
    Transform TractorPart(string name)
    {
        if (parts == null)
        {
            parts = new Dictionary<string, Transform>();
            foreach (var t in tractor.GetComponentsInChildren<Transform>(true)) parts.TryAdd(t.name, t);
        }
        return parts.TryGetValue(name, out var p) ? p : null;
    }

    bool InReach(Implement imp) => imp != null && hitch.Nearest == imp && hitch.NearestDistance <= hitch.attachReach;
    Vector3? At(Implement imp) => imp != null ? imp.hitchPoint.position : (Vector3?)null;
    float Speed => Mathf.Abs(tractor.SpeedKmh);

    void Add(Lesson l, string text, System.Func<bool> done = null, System.Func<Transform> highlight = null, System.Func<Vector3?> goal = null,
             float radius = 6f, System.Action enter = null, System.Func<string> status = null, bool info = false)
        => l.steps.Add(new Step { text = text, done = done, highlight = highlight, goal = goal, goalRadius = radius, enter = enter, status = status, info = info });

    Lesson NewLesson(string name) { var l = new Lesson { name = name }; lessons.Add(l); return l; }

    void BuildLessons()
    {
        var plough = FindImpl("PLOUGH");
        var mower = FindImpl("MOWER");
        var trailer = FindImpl("TRAILER");
        Tipper Tip() => hitch.TowedTipper;

        // 1 ---------------------------------------------------------------
        var l = NewLesson("DRIVING AND THE CAB");
        Add(l, "Welcome to the farm! This lesson shows how to drive off, steer, stop and reverse.\n\n" +
               "Look around the cab: the steering wheel, the speed lever by your right hand, the menu panel at the right front.", info: true);
        Add(l, "Drive off: push the RIGHT STICK forward (W), or grab the SPEED LEVER by your right hand and push it forward.\n\nReach 8 km/h.",
            done: () => tractor.SpeedKmh >= 8f, highlight: () => cab ? cab.speedLever : null);
        Add(l, "Steer with the LEFT STICK (A / D), or grab the STEERING WHEEL rim and turn it.\n\nDrive to the yellow marker at the yard gate.",
            goal: () => new Vector3(33f, 0f, 0f), radius: 6f, highlight: () => tractor.steeringWheel);
        Add(l, "Brake: pull the RIGHT STICK back (S) and hold it until the tractor stands still.",
            done: () => Speed < 0.3f);
        Add(l, "Reverse: after stopping, keep the RIGHT STICK pulled back (S). The tractor beeps and backs up.\n\nReverse 5 metres.",
            done: () => (distance += tractor.SpeedMs < -0.1f ? s_moved : 0f) >= 5f, status: () => $"{distance:0.0} / 5 m");
        Add(l, "Stop, then press A (C) to watch the tractor from outside. Press A again to come back into the cab.",
            done: () => { if (rig.view == VRCameraRig.View.ThirdPerson) flag = true; return flag && rig.view == VRCameraRig.View.Cab; });

        // 2 ---------------------------------------------------------------
        l = NewLesson("GEARBOX AND HAND THROTTLE");
        Add(l, "Open the MENU: the MENU button on the left controller (M), or touch the MENU tab at the right front.",
            done: () => panel.Open, highlight: () => PanelButton("tab"));
        Add(l, "The gearbox has 20 gears (A1 to E4) and shifts by itself in AUTO. Switch it to MANUAL: Y button (T), or GEARBOX on the panel.",
            done: () => !drivetrain.Automatic, highlight: () => PanelButton("gearbox"));
        Add(l, "Drive off slowly, then flick the RIGHT STICK to the right (X) to shift up. To the left (Z) shifts down.\n\nShift up two gears.",
            enter: () => startGear = drivetrain.Gear, done: () => drivetrain.Gear >= startGear + 2, status: () => "Gear " + drivetrain.GearName);
        Add(l, "In MANUAL the hand throttle sets the engine speed (for steady work). Raise it to 1500 rpm: THROTTLE + on the panel or the CommandCenter (]).",
            done: () => drivetrain.HandThrottleRpm >= 1450f, highlight: () => PanelButton("thrUp"), status: () => $"{drivetrain.Rpm:0} rpm");
        Add(l, "Now the hand throttle back to idle (THROTTLE -, [) and the gearbox back to AUTO (Y / T).",
            done: () => drivetrain.Automatic && drivetrain.HandThrottleRpm <= drivetrain.idleRpm + 60f, highlight: () => PanelButton("thrDown"));

        // 3 ---------------------------------------------------------------
        l = NewLesson("SLALOM AT THE PRACTICE AREA");
        var slalomStart = new Vector3(46f, 0f, -10f);
        Add(l, "Drive to the PRACTICE AREA east of the yard: past the silos and round the south end of the fence, to the marker at the white START line.",
            goal: () => slalomStart, radius: 5f);
        Add(l, "Slalom: weave through the six cones, passing them on alternate sides, to the FINISH line. Keep below 10 km/h and don't knock any over!",
            enter: ResetSlalom, done: SlalomDone, status: () => flag ? $"Cones passed: {counter} / {WorldLayout.SlalomCones.Length}" : "Drive over the START line");

        // 4 ---------------------------------------------------------------
        l = NewLesson("COUPLING THE PLOUGH");
        Add(l, "Something is on the rear hitch: lower it (LEFT STICK down / E), stop, and press the LEFT STICK (J) or ATTACH/DETACH to park it.",
            done: () => hitch.Rear == null || hitch.Rear == plough);
        Add(l, "Drive to the MACHINE SHED. Turn, and REVERSE slowly up to the PLOUGH bay until the lower links are right at the plough's pins.\n\nLook back over your shoulder, or switch to the outside view (A).",
            done: () => hitch.Rear == plough || InReach(plough), goal: () => At(plough), radius: 0f,
            status: () => hitch.Nearest == plough ? $"Plough: {hitch.NearestDistance:0.0} m (need {hitch.attachReach:0.0} m)" : "");
        Add(l, "Stop and ATTACH: press the LEFT STICK (J) or ATTACH/DETACH on the panel. If it says LOWER THE HITCH, lower it with the LEFT STICK down (E) first.",
            done: () => hitch.Rear == plough, highlight: () => PanelButton("attach"));
        Add(l, "Lift the plough: LEFT STICK up (Q), or grab REMOTE LEVER 1 (behind the speed lever) and pull it back.",
            done: () => hitch.Rear == plough && hitch.RearPosition > 0.9f, highlight: () => TractorPart("RemoteLever_1"));
        Add(l, "Drive out of the shed into the yard with the plough raised.",
            goal: () => new Vector3(0f, 0f, -18f), radius: 7f);

        // 5 ---------------------------------------------------------------
        l = NewLesson("PLOUGHING");
        Add(l, "This lesson needs the plough on the rear hitch (lesson 4). Couple it in the shed, then come back here.",
            done: () => hitch.Rear == plough, goal: () => hitch.Rear == plough ? (Vector3?)null : At(plough), radius: 0f);
        Add(l, "Drive to the WEST FIELD: out of the yard's south-west corner, along the west track past the barn, to the field gate (marker).",
            goal: () => new Vector3(-236f, 0f, -55f), radius: 10f);
        Add(l, "In the field, lower the plough (LEFT STICK down / E, or remote lever 1 forward) and drive on at 5 to 10 km/h.\n\nPlough 50 metres.",
            done: () => (distance += hitch.Rear && hitch.Rear.Working ? s_moved : 0f) >= 50f, status: () => $"{distance:0} / 50 m ploughed",
            highlight: () => TractorPart("RemoteLever_1"));
        Add(l, "Lift the plough at the end of the run (LEFT STICK up / Q) and look back at your furrows.",
            done: () => hitch.RearPosition > 0.9f);

        // 6 ---------------------------------------------------------------
        l = NewLesson("MOWING");
        Add(l, "Park the plough first, best in its shed bay: lower it to the ground (E), stop and DETACH (J).",
            done: () => hitch.Rear == null, goal: () => hitch.Rear == plough ? At(plough) : null, radius: 0f);
        Add(l, "Something is on the front linkage: lower it (hold LEFT TRIGGER + LEFT STICK down / Ctrl+E) and DETACH it (J).",
            done: () => hitch.Front == null || hitch.Front == mower);
        Add(l, "Drive NOSE FIRST into the MOWER bay of the shed, slowly, until the front linkage reaches the mower.",
            done: () => hitch.Front == mower || InReach(mower), goal: () => At(mower), radius: 0f,
            status: () => hitch.Nearest == mower ? $"Mower: {hitch.NearestDistance:0.0} m (need {hitch.attachReach:0.0} m)" : "");
        Add(l, "Stop and ATTACH (J). If it says LOWER THE FRONT LINKAGE: hold LEFT TRIGGER + LEFT STICK down (Ctrl+E) first.",
            done: () => hitch.Front == mower, highlight: () => PanelButton("attach"));
        Add(l, "Lift the mower (LEFT TRIGGER + LEFT STICK up / Ctrl+Q; or remote lever 2), back out, and drive up the north track to the MEADOW gate (marker).",
            goal: () => new Vector3(0f, 0f, 120f), radius: 8f, highlight: () => TractorPart("RemoteLever_2"));
        Add(l, "In the meadow: lower the mower, switch the PTO on (RIGHT STICK click / P, or the PTO button on the console) and drive at 8 to 12 km/h.\n\nMow 50 metres.",
            done: () => (distance += hitch.Front == mower && hitch.Front.Working && hitch.Pto ? s_moved : 0f) >= 50f,
            status: () => $"{distance:0} / 50 m mown" + (hitch.Pto ? "" : "  (PTO is off)"), highlight: () => TractorPart("Switch_pto"));
        Add(l, "PTO off and the mower up. Look back: the cut stubble and the swath of grass behind you.",
            done: () => !hitch.Pto && hitch.FrontPosition > 0.9f);

        // 7 ---------------------------------------------------------------
        l = NewLesson("TRAILER AND TIPPING");
        Add(l, "The trailer couples to the drawbar under the rear hitch. Park any rear implement first (lower it, DETACH).",
            done: () => hitch.Rear == null);
        Add(l, "REVERSE up to the TRAILER in the shed until the drawbar is right under its eye.",
            done: () => hitch.Towed == trailer || InReach(trailer), goal: () => At(trailer), radius: 0f,
            status: () => hitch.Nearest == trailer ? $"Trailer: {hitch.NearestDistance:0.0} m (need {hitch.attachReach:0.0} m)" : "");
        Add(l, "Stop and ATTACH (J).", done: () => hitch.Towed == trailer, highlight: () => PanelButton("attach"));
        Add(l, "Pull the trailer to the PRACTICE AREA (marker). Take the corners wide: the trailer cuts inside.",
            goal: () => new Vector3(58f, 0f, -45f), radius: 8f);
        Add(l, "Reverse the trailer into the BAY between the cones in the south-east corner, until it stands between the yellow lines.\n\n" +
               "Steer the opposite way to where the trailer should go; small corrections.",
            done: TrailerInBay, goal: () => new Vector3(66f, 0f, -70f), radius: 0f, status: TrailerBayStatus);
        Add(l, "Tip the load: TIP UP on the panel (I) or pull REMOTE LEVER 3 back. Raise the body all the way.",
            done: () => Tip() != null && Tip().Angle >= Tip().maxAngle * 0.95f, highlight: () => TractorPart("RemoteLever_3"));
        Add(l, "Lower the body again: TIP DOWN (K) or push remote lever 3 forward.",
            done: () => Tip() != null && Tip().Angle < 1f);

        // 8 ---------------------------------------------------------------
        l = NewLesson("TRACTION: 4WD AND DIFF LOCK");
        Add(l, "The front axle drive (4WD) is a rocker on the right console (or 4WD on the panel, F). Switch it OFF, then ON again.",
            done: () => { if (!tractor.fourWheelDrive) flag = true; return flag && tractor.fourWheelDrive; }, highlight: () => TractorPart("Switch_fourWd"));
        Add(l, "The diff lock makes both wheels of an axle turn together (for mud). Switch it ON: the push button next to 4WD, or DIFF LOCK (L).\n\nIt lets go by itself above 15 km/h or when you steer.",
            done: () => tractor.DiffLock, highlight: () => TractorPart("Switch_diffLock"));
        Add(l, "Drive to the EARTH RAMP north-west of the yard and climb onto its top over the steep 22 degree side (the east side). Slowly!",
            done: () => Flat(tractor.transform.position - new Vector3(WorldLayout.EarthRamp.x, 0f, WorldLayout.EarthRamp.y)).magnitude < 5f &&
                        tractor.transform.position.y - TerrainGenerator.Height(tractor.transform.position.x, tractor.transform.position.z) > 1.0f,
            goal: () => new Vector3(WorldLayout.EarthRamp.x, 0f, WorldLayout.EarthRamp.y), radius: 0f);

        // 9 ---------------------------------------------------------------
        l = NewLesson("LIGHTS AND ROAD DRIVING");
        Add(l, "Evening is coming: set SKY to DUSK on the panel (U).",
            done: () => lights.TimeOfDay != TractorLights.Sky.Day, highlight: () => PanelButton("sky"));
        Add(l, "Headlights on: turn the LIGHT KNOB on the right console to HEAD (two turns), or LIGHTS on the panel (G).",
            done: () => lights.RoadLights == TractorLights.Road.Head, highlight: () => TractorPart("Switch_lights"));
        Add(l, "A tractor on the road shows its amber BEACON: the rocker on the console, or BEACON on the panel (O).",
            done: () => lights.Beacon, highlight: () => TractorPart("Switch_beacon"));
        Add(l, "Drive out through the carved gate to the MAIN ROAD. Before turning LEFT (north) onto it, indicate: grab the stalk left of the wheel and push it DOWN (,).",
            done: () => { if (lights.Indicator < 0) flag = true; return flag && tractor.transform.position.x > 76f && tractor.transform.position.z > 8f; },
            goal: () => new Vector3(80f, 0f, 14f), radius: 0f, highlight: () => flag ? null : TractorPart("IndicatorStalk"),
            status: () => flag ? "Indicator: on" : "Indicator: not used yet");
        Add(l, "Drive north along the main road, between the poplars. Keep right; up to 40 km/h.\n\nDrive 150 metres.",
            done: () => (distance += Mathf.Abs(tractor.transform.position.x - 80f) < 4f && tractor.SpeedMs > 0f ? s_moved : 0f) >= 150f,
            status: () => $"{distance:0} / 150 m");
        Add(l, "That's the tutorial! Turn the beacon off, set the SKY back to DAY when you like, and explore: the fields, the stream and its willows, the village on the hill.",
            info: true);
    }

    // ---- slalom
    void ResetSlalom()
    {
        foreach (var c in cones)
        {
            if (c.pos.x > 50f) continue;   // the reversing bay's cones stay where they are
            c.rb.linearVelocity = Vector3.zero; c.rb.angularVelocity = Vector3.zero;
            c.rb.position = c.pos; c.rb.rotation = c.rot;
            c.rb.transform.SetPositionAndRotation(c.pos, c.rot);
        }
        flag = false; counter = 0; flag2 = false;
    }

    int lastSide;

    bool SlalomDone()
    {
        Vector3 p = tractor.transform.position;
        Vector3 nose = p + tractor.transform.forward * 1.4f;
        if (!flag)
        {
            if (p.x > 38f && p.x < 54f && p.z < -12f && p.z > -18f) { flag = true; counter = 0; }
            return false;
        }
        foreach (var c in cones)
        {
            if (c.pos.x > 50f) continue;
            if (Flat(c.rb.position - c.pos).magnitude > 0.35f || Vector3.Angle(c.rb.transform.up, Vector3.up) > 25f)
            { SlalomFail("A CONE WENT DOWN: BACK TO THE START"); return false; }
        }
        if (Speed > 11f) { SlalomFail("TOO FAST: BACK TO THE START"); return false; }
        var cz = WorldLayout.SlalomCones;
        if (counter < cz.Length && nose.z < cz[counter].y)
        {
            int side = nose.x > cz[counter].x ? 1 : -1;
            if (counter > 0 && side == lastSide) { SlalomFail("PASS THE CONES ON ALTERNATE SIDES: BACK TO THE START"); return false; }
            lastSide = side;
            counter++;
        }
        return counter == cz.Length && p.z < -66f;
    }

    void SlalomFail(string msg)
    {
        if (hud) hud.Show(msg);
        ResetSlalom();
    }

    // ---- reversing bay
    Vector3 TrailerCentre()
    {
        var t = hitch.Towed;
        if (t == null) return Vector3.zero;
        var rb = t.GetComponent<Rigidbody>();
        return rb ? rb.worldCenterOfMass : t.transform.position;
    }

    bool TrailerInBay()
    {
        if (hitch.Towed == null) return false;
        var c = TrailerCentre();
        var g = WorldLayout.ReversingGoal;
        bool inside = c.x > g.x0 + 0.3f && c.x < g.x1 - 0.3f && c.z > g.z0 && c.z < g.z1;
        bool straight = Mathf.Abs(hitch.Towed.transform.forward.z) > 0.94f;
        return inside && straight && Speed < 1f;
    }

    string TrailerBayStatus()
    {
        if (hitch.Towed == null) return "No trailer coupled";
        var c = TrailerCentre();
        var g = WorldLayout.ReversingGoal;
        float off = c.x - (g.x0 + g.x1) * 0.5f;
        return c.z > g.z1 ? $"Trailer {c.z - g.z1:0.0} m before the bay, {Mathf.Abs(off):0.0} m {(off > 0 ? "right" : "left")} of its centre line"
                          : $"In the bay: {Mathf.Abs(off):0.0} m off centre";
    }

    // ================================================================== board (VR)
    Transform board, buttonRoot;
    TextMesh titleText, bodyText;
    MeshRenderer back;
    Font font;
    readonly List<(Transform t, MeshRenderer face, TextMesh label, bool inside)> boardButtons = new List<(Transform, MeshRenderer, TextMesh, bool)>();
    bool dirty = true;
    float cooldown;

    const float BoardW = 0.36f, Margin = 0.012f, BtnH = 0.036f, Gap = 0.006f;

    void Changed() => dirty = true;

    void BuildBoard()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        board = new GameObject("Board").transform;
        board.SetParent(transform, false);
        back = Quad("Back", board, Vector3.zero, Vector2.one, panelMat);
        titleText = Text("Title", board, 0.0135f, new Color(1f, 0.85f, 0.3f));
        bodyText = Text("Body", board, 0.0105f, new Color(0.95f, 0.95f, 0.9f));
        buttonRoot = new GameObject("Buttons").transform;
        buttonRoot.SetParent(board, false);
    }

    TextMesh Text(string name, Transform parent, float height, Color c)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var tm = go.AddComponent<TextMesh>();
        tm.font = font;
        tm.fontSize = 40;
        tm.characterSize = height / (tm.fontSize * 0.1f);
        tm.anchor = TextAnchor.UpperLeft;
        tm.alignment = TextAlignment.Left;
        tm.color = c;
        go.GetComponent<MeshRenderer>().sharedMaterial = textMat;
        return tm;
    }

    MeshRenderer Quad(string name, Transform parent, Vector3 pos, Vector2 size, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return r;
    }

    public static string Wrap(string s, int width)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var para in s.Split('\n'))
        {
            int col = 0;
            foreach (var word in para.Split(' '))
            {
                if (col > 0 && col + 1 + word.Length > width) { sb.Append('\n'); col = 0; }
                else if (col > 0) { sb.Append(' '); col++; }
                sb.Append(word);
                col += word.Length;
            }
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    int lastLines = -1;

    void UpdateBoard()
    {
        bool show = page != Page.Hidden && !DesktopMode.Active;
        board.gameObject.SetActive(show);
        if (!show) return;

        // Line breaks from the font's real glyph widths (a fixed character count let wide letters run off the board).
        string body = WrapToWidth(bodyText, Body, BoardW - 2f * Margin);
        int lines = body.Split('\n').Length;
        titleText.text = Title;
        FitSize(titleText, Title, 0.0135f, BoardW - 2f * Margin);
        bodyText.text = body;
        if (dirty || lines != lastLines)
        {
            dirty = false;
            lastLines = lines;
            Layout(lines);
        }
        for (int i = 0; i < boardButtons.Count && i < actions.Count; i++)
            if (boardLabels[i] != actions[i].label)
            {
                boardLabels[i] = actions[i].label;
                FitButton(boardButtons[i].label, actions[i].label, boardButtonWidth - 0.012f);
            }

        cooldown -= Time.deltaTime;
        for (int i = 0; i < boardButtons.Count; i++)
        {
            var b = boardButtons[i];
            bool inside = false;
            foreach (var h in hands.Both)
            {
                if (!h.Tracked) continue;
                Vector3 q = b.t.InverseTransformPoint(h.PokeTip);
                if (Mathf.Abs(q.x) < 0.5f + 0.02f && Mathf.Abs(q.y) < 0.5f + 0.1f && q.z > -0.4f && q.z < 1.2f)   // in button units (scaled)
                    inside = true;
            }
            b.face.sharedMaterial = inside ? buttonHotMat : buttonMat;
            if (inside && !b.inside && cooldown <= 0f) { cooldown = 0.4f; boardButtons[i] = (b.t, b.face, b.label, true); Press(i); return; }
            boardButtons[i] = (b.t, b.face, b.label, inside);
        }
    }

    // ---- text measuring (TextMesh: one font pixel = characterSize * 0.1 local units)
    float Width(TextMesh tm, string line)
    {
        font.RequestCharactersInTexture(line, tm.fontSize, tm.fontStyle);
        float w = 0f;
        foreach (char c in line)
            if (font.GetCharacterInfo(c, out CharacterInfo ci, tm.fontSize, tm.fontStyle)) w += ci.advance;
        return w * tm.characterSize * 0.1f;
    }

    // Line pitch from the TextMesh size (font.lineHeight is at the font's own size, not tm.fontSize); 1.3 x the size matches the render.
    float LineHeight(TextMesh tm) => tm.fontSize * tm.characterSize * 0.1f * 1.3f * tm.lineSpacing;

    string WrapToWidth(TextMesh tm, string s, float maxW)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var para in s.Split('\n'))
        {
            string line = "";
            foreach (var word in para.Split(' '))
            {
                string tryLine = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && Width(tm, tryLine) > maxW) { sb.Append(line).Append('\n'); line = word; }
                else line = tryLine;
            }
            sb.Append(line).Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    static float CharSize(float height, TextMesh tm) => height / (tm.fontSize * 0.1f);

    void FitSize(TextMesh tm, string text, float height, float maxW)
    {
        tm.characterSize = CharSize(height, tm);
        float w = Width(tm, text);
        if (w > maxW) tm.characterSize *= maxW / w;
    }

    /// <summary>Button label: one line if it fits, else two lines, else smaller.</summary>
    void FitButton(TextMesh tm, string text, float maxW)
    {
        tm.characterSize = CharSize(0.0105f, tm);
        if (Width(tm, text) <= maxW) { tm.text = text; return; }
        tm.characterSize = CharSize(0.0092f, tm);
        string two = WrapToWidth(tm, text, maxW);
        while (two.Split('\n').Length > 2 && tm.characterSize > CharSize(0.006f, tm))
        {
            tm.characterSize *= 0.9f;
            two = WrapToWidth(tm, text, maxW);
        }
        tm.text = two;
    }

    float boardButtonWidth;
    readonly List<string> boardLabels = new List<string>();

    void Layout(int lines)
    {
        foreach (var b in boardButtons) { if (Application.isPlaying) Destroy(b.t.gameObject); else DestroyImmediate(b.t.gameObject); }
        boardButtons.Clear();
        int n = actions.Count;
        int cols = n > 4 ? 2 : Mathf.Max(1, n);
        int rows = (n + cols - 1) / cols;
        float titleH = LineHeight(titleText) + 0.006f;
        float textH = titleH + lines * LineHeight(bodyText) + 0.004f;
        float h = Gap + textH + Gap + rows * (BtnH + Gap) + Gap;
        back.transform.localScale = new Vector3(BoardW, h, 1f);
        back.transform.localPosition = new Vector3(0f, 0f, 0.003f);
        float top = h * 0.5f - Gap;
        titleText.transform.localPosition = new Vector3(-BoardW * 0.5f + Margin, top, -0.001f);
        bodyText.transform.localPosition = new Vector3(-BoardW * 0.5f + Margin, top - titleH, -0.001f);
        float bw = (BoardW - (cols + 1) * Gap) / cols;
        boardButtonWidth = bw;
        boardLabels.Clear();
        float y0 = top - textH - Gap - BtnH * 0.5f;
        for (int i = 0; i < n; i++)
        {
            int c = i % cols, r = i / cols;
            var t = new GameObject("Button_" + i).transform;
            t.SetParent(buttonRoot, false);
            t.localPosition = new Vector3(-BoardW * 0.5f + Gap + bw * 0.5f + c * (bw + Gap), y0 - r * (BtnH + Gap), 0f);
            t.localScale = new Vector3(bw, BtnH, 0.03f);   // poke box in button units: z 1 = 3 cm
            var face = Quad("Face", t, Vector3.zero, Vector2.one, buttonMat);
            var label = Text("Label", t, 0.0105f, new Color(0.95f, 0.95f, 0.9f));
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.transform.localPosition = new Vector3(0f, 0f, -0.07f);
            label.transform.localScale = new Vector3(1f / bw, 1f / BtnH, 1f);
            boardButtons.Add((t, face, label, true));
            boardLabels.Add(null);   // "inside" until the hand leaves: no press on the frame a page opens
        }
    }

    // ================================================================== highlight glows
    Transform controlHalo, goalHalo;
    MaterialPropertyBlock haloBlock;

    void UpdateHalos()
    {
        if (haloMat == null) return;
        if (controlHalo == null)
        {
            haloBlock = new MaterialPropertyBlock();
            controlHalo = Quad("ControlHalo", null, Vector3.zero, Vector2.one, haloMat).transform;
            goalHalo = Quad("GoalHalo", null, Vector3.zero, Vector2.one, haloMat).transform;
        }
        Transform target = null;
        Vector3? goal = null;
        if (page == Page.Step && doneDelay < 0f)
        {
            target = Current.highlight?.Invoke();
            goal = Current.goal?.Invoke();
        }
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 5f);
        controlHalo.gameObject.SetActive(target != null);
        if (target != null)
        {
            controlHalo.position = target.position;
            float s = 0.09f + 0.04f * pulse;
            controlHalo.localScale = new Vector3(s, s, 0.0001f);
            Tint(controlHalo, new Color(1f, 0.8f, 0.1f, 0.9f + 0.6f * pulse));
        }
        goalHalo.gameObject.SetActive(goal.HasValue);
        if (goal.HasValue)
        {
            var g = goal.Value;
            float ground = TerrainGenerator.SurfaceHeight(g.x, g.z);
            var cam = Camera.main;
            float d = cam ? Vector3.Distance(cam.transform.position, g) : 20f;
            goalHalo.position = new Vector3(g.x, ground + 2.5f, g.z);
            float s = Mathf.Max(6f, d * 0.07f) * (0.85f + 0.15f * pulse);
            goalHalo.localScale = new Vector3(s, s, 0.0001f);
            Tint(goalHalo, new Color(1f, 0.75f, 0.1f, 0.7f + 0.4f * pulse));
        }
    }

    void Tint(Transform t, Color c)
    {
        var r = t.GetComponent<MeshRenderer>();
        r.GetPropertyBlock(haloBlock);
        haloBlock.SetColor("_BaseColor", c);
        r.SetPropertyBlock(haloBlock);
    }

    /// <summary>Editor renders: builds the board outside play mode and shows lesson <paramref name="lesson"/>'s step
    /// <paramref name="step"/> (-1 = the start page), with its glows.</summary>
    public void Preview(int lesson, int step)
    {
        if (board == null) { Instance = this; cones.Clear(); lessons.Clear(); BuildLessons(); BuildBoard(); }
        if (lesson == -2) ShowLessons();
        else if (lesson < 0) ShowStart();
        else { lessonIndex = lesson; page = Page.Step; EnterStep(step); }
        UpdateBoard();
        UpdateHalos();
    }

    public void PreviewCleanup()
    {
        if (controlHalo) DestroyImmediate(controlHalo.gameObject);
        if (goalHalo) DestroyImmediate(goalHalo.gameObject);
        if (board) DestroyImmediate(board.gameObject);
    }

    void OnDestroy()
    {
        if (controlHalo) Destroy(controlHalo.gameObject);
        if (goalHalo) Destroy(goalHalo.gameObject);
    }
}
